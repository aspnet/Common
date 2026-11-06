// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.Diagnostics;

using FunctionInvocationResult = Microsoft.Extensions.AI.FunctionInvokingChatClient.FunctionInvocationResult;
using FunctionInvocationStatus = Microsoft.Extensions.AI.FunctionInvokingChatClient.FunctionInvocationStatus;

#pragma warning disable CA2213 // Disposable fields should be disposed
#pragma warning disable S2219 // Runtime type checking should be simplified
#pragma warning disable S3353 // Unchanged local variables should be "const"

namespace Microsoft.Extensions.AI;

/// <summary>
/// A delegating realtime session that invokes functions defined on <see cref="CreateResponseRealtimeClientMessage"/>.
/// Include this in a realtime session pipeline to resolve function calls automatically.
/// </summary>
/// <remarks>
/// <para>
/// When this session receives a <see cref="FunctionCallContent"/> in a realtime server message from its inner
/// <see cref="IRealtimeClientSession"/>, it responds by invoking the corresponding <see cref="AIFunction"/> defined
/// in <see cref="CreateResponseRealtimeClientMessage.Tools"/> (or in <see cref="AdditionalTools"/>), producing a <see cref="FunctionResultContent"/>
/// that it sends back to the inner session. This loop is repeated until there are no more function calls to make, or until
/// another stop condition is met, such as hitting <see cref="MaximumIterationsPerRequest"/>.
/// </para>
/// <para>
/// If a requested function is an <see cref="AIFunctionDeclaration"/> but not an <see cref="AIFunction"/>, the
/// <see cref="FunctionInvokingRealtimeClientSession"/> will not attempt to invoke it, and instead allow that <see cref="FunctionCallContent"/>
/// to pass back out to the caller. It is then that caller's responsibility to create the appropriate <see cref="FunctionResultContent"/>
/// for that call and send it back as part of a subsequent request.
/// </para>
/// <para>
/// A <see cref="FunctionInvokingRealtimeClientSession"/> instance is thread-safe for concurrent use so long as the
/// <see cref="AIFunction"/> instances employed as part of the supplied <see cref="CreateResponseRealtimeClientMessage"/> are also safe.
/// The <see cref="AllowConcurrentInvocation"/> property can be used to control whether multiple function invocation
/// requests as part of the same request are invocable concurrently, but even with that set to <see langword="false"/>
/// (the default), multiple concurrent requests to this same instance and using the same tools could result in those
/// tools being used concurrently (one per request).
/// </para>
/// <para>
/// <b>Known limitation:</b> Function invocation blocks the message processing loop. While functions are being
/// invoked, incoming server messages (including user interruptions) are buffered and not processed until the
/// invocation completes.
/// </para>
/// </remarks>
internal sealed class FunctionInvokingRealtimeClientSession : IRealtimeClientSession
{
    /// <summary>The <see cref="FunctionInvocationContext"/> for the current function invocation.</summary>
    private static readonly AsyncLocal<FunctionInvocationContext?> _currentContext = new();

    /// <summary>Gets the <see cref="IServiceProvider"/> specified when constructing the <see cref="FunctionInvokingRealtimeClientSession"/>, if any.</summary>
    private IServiceProvider? FunctionInvocationServices { get; }

    /// <summary>The logger to use for logging information about function invocation.</summary>
    private readonly ILogger _logger;

    /// <summary>The <see cref="ActivitySource"/> to use for telemetry.</summary>
    /// <remarks>This component does not own the instance and should not dispose it.</remarks>
    private readonly ActivitySource? _activitySource;

    /// <summary>The <see cref="Meter"/> to use for metrics.</summary>
    /// <remarks>This component does not own the instance and should not dispose it.</remarks>
    private readonly Meter? _meter;

    /// <summary>The inner session to delegate to.</summary>
    private readonly IRealtimeClientSession _innerSession;

    /// <summary>The owning client that holds configuration.</summary>
    private readonly FunctionInvokingRealtimeClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionInvokingRealtimeClientSession"/> class.
    /// </summary>
    /// <param name="innerSession">The underlying <see cref="IRealtimeClientSession"/>, or the next instance in a chain of sessions.</param>
    /// <param name="client">The owning <see cref="FunctionInvokingRealtimeClient"/> that holds configuration.</param>
    /// <param name="loggerFactory">An <see cref="ILoggerFactory"/> to use for logging information about function invocation.</param>
    /// <param name="functionInvocationServices">An optional <see cref="IServiceProvider"/> to use for resolving services required by the <see cref="AIFunction"/> instances being invoked.</param>
    public FunctionInvokingRealtimeClientSession(IRealtimeClientSession innerSession, FunctionInvokingRealtimeClient client, ILoggerFactory? loggerFactory = null, IServiceProvider? functionInvocationServices = null)
    {
        _innerSession = Throw.IfNull(innerSession);
        _client = Throw.IfNull(client);
        _logger = (ILogger?)loggerFactory?.CreateLogger<FunctionInvokingRealtimeClientSession>() ?? NullLogger.Instance;
        _activitySource = innerSession.GetService<ActivitySource>();
        _meter = innerSession.GetService<Meter>();
        FunctionInvocationServices = functionInvocationServices;
    }

    /// <summary>Gets the function invocation processor, creating it lazily.</summary>
    private FunctionInvocationProcessor Processor => field ??= new FunctionInvocationProcessor(
        _logger,
        _activitySource,
        InvokeFunctionAsync,
        executeToolDurationHistogram: _meter is not null ? OtelMetricHelpers.CreateGenAIExecuteToolDurationHistogram(_meter) : null);

    /// <summary>
    /// Gets or sets the <see cref="FunctionInvocationContext"/> for the current function invocation.
    /// </summary>
    /// <remarks>
    /// This value flows across async calls.
    /// </remarks>
    internal static FunctionInvocationContext? CurrentContext
    {
        get => _currentContext.Value;
        set => _currentContext.Value = value;
    }

    private bool IncludeDetailedErrors => _client.IncludeDetailedErrors;

    private bool AllowConcurrentInvocation => _client.AllowConcurrentInvocation;

    private int MaximumIterationsPerRequest => _client.MaximumIterationsPerRequest;

    private int MaximumConsecutiveErrorsPerRequest => _client.MaximumConsecutiveErrorsPerRequest;

    private IList<AITool>? AdditionalTools => _client.AdditionalTools;

    private bool TerminateOnUnknownCalls => _client.TerminateOnUnknownCalls;

    private Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? FunctionInvoker => _client.FunctionInvoker;

    /// <inheritdoc />
    public RealtimeSessionOptions? Options => _innerSession.Options;

    /// <inheritdoc />
    public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default) =>
        _innerSession.SendAsync(message, cancellationToken);

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this :
            _innerSession.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _innerSession.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Create an activity to group function invocations together for better observability.
        using Activity? activity = FunctionInvocationHelpers.CurrentActivityIsInvokeAgent ? null : _activitySource?.StartActivity(OpenTelemetryConsts.GenAI.OrchestrateToolsName);

        // Track function calls from the client messages
        List<FunctionCallContent>? functionCallContents = null;
        int consecutiveErrorCount = 0;
        int iterationCount = 0;

        await foreach (var message in _innerSession.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
        {
            // Check if this message contains function calls
            bool hasFunctionCalls = false;
            if (message is ResponseOutputItemRealtimeServerMessage responseOutputItemMessage && responseOutputItemMessage.Type == RealtimeServerMessageType.ResponseOutputItemDone)
            {
                // Extract function calls from the message
                functionCallContents ??= [];
                hasFunctionCalls = ExtractFunctionCalls(responseOutputItemMessage, functionCallContents);
            }

            // Always yield the message so consumers can observe function calls and other events.
            yield return message;

            if (hasFunctionCalls)
            {
                if (iterationCount >= MaximumIterationsPerRequest)
                {
                    // Log and stop processing function calls
                    FunctionInvocationLogger.LogMaximumIterationsReached(_logger, MaximumIterationsPerRequest);
                    continue;
                }

                // Check whether the function calls can be handled; if not, terminate the loop.
                if (ShouldTerminateBasedOnFunctionCalls(functionCallContents!))
                {
                    yield break;
                }

                // Process function calls
                iterationCount++;
                var results = await InvokeFunctionsAsync(functionCallContents!, consecutiveErrorCount, cancellationToken).ConfigureAwait(false);

                // Update consecutive error count
                consecutiveErrorCount = results.newConsecutiveErrorCount;

                // Check if we should terminate
                if (results.shouldTerminate)
                {
                    yield break;
                }

                foreach (var resultMessage in results.functionResults)
                {
                    // inject back the function result messages to the inner session
                    await _innerSession.SendAsync(resultMessage, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Extracts function calls from a realtime server message.</summary>
    private static bool ExtractFunctionCalls(ResponseOutputItemRealtimeServerMessage message, List<FunctionCallContent> functionCallContents)
    {
        if (message.Item is null)
        {
            return false;
        }

        functionCallContents.Clear();

        foreach (var content in message.Item.Contents)
        {
            if (content is FunctionCallContent functionCallContent)
            {
                functionCallContents.Add(functionCallContent);
            }
        }

        return functionCallContents.Count > 0;
    }

    /// <summary>Finds a tool by name in the specified tool lists.</summary>
    private static AIFunctionDeclaration? FindTool(string name, params ReadOnlySpan<IEnumerable<AITool>?> toolLists)
    {
        foreach (var toolList in toolLists)
        {
            if (toolList is not null)
            {
                foreach (AITool tool in toolList)
                {
                    if (tool is AIFunctionDeclaration declaration && string.Equals(tool.Name, name, StringComparison.Ordinal))
                    {
                        return declaration;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Checks whether there are any tools in the specified tool lists.</summary>
    private static bool HasAnyTools(params ReadOnlySpan<IEnumerable<AITool>?> toolLists)
    {
        foreach (var toolList in toolLists)
        {
            if (toolList is not null)
            {
                using var enumerator = toolList.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets whether the function calling loop should exit based on the function call requests.</summary>
    /// <remarks>
    /// This mirrors the logic in <c>FunctionInvokingChatClient.ShouldTerminateLoopBasedOnHandleableFunctions</c>.
    /// If a function call references a non-invocable tool (a declaration but not an <see cref="AIFunction"/>),
    /// the loop always terminates. If the function is completely unknown, the loop terminates only when
    /// <see cref="TerminateOnUnknownCalls"/> is <see langword="true"/>.
    /// </remarks>
    private bool ShouldTerminateBasedOnFunctionCalls(List<FunctionCallContent> functionCallContents)
    {
        if (!HasAnyTools(AdditionalTools, _innerSession.Options?.Tools))
        {
            // No tools available at all. If TerminateOnUnknownCalls, stop the loop.
            if (TerminateOnUnknownCalls)
            {
                foreach (var fcc in functionCallContents)
                {
                    FunctionInvocationLogger.LogFunctionNotFound(_logger, fcc.Name);
                }

                return true;
            }

            return false;
        }

        foreach (var fcc in functionCallContents)
        {
            AIFunctionDeclaration? tool = FindTool(fcc.Name, AdditionalTools, _innerSession.Options?.Tools);
            if (tool is not null)
            {
                if (tool is not AIFunction)
                {
                    // The tool exists but is not invocable (e.g. AIFunctionDeclaration only).
                    // Always terminate so the caller can handle the call.
                    FunctionInvocationLogger.LogNonInvocableFunction(_logger, fcc.Name);
                    return true;
                }
            }
            else if (TerminateOnUnknownCalls)
            {
                // The tool is completely unknown. If configured, terminate.
                FunctionInvocationLogger.LogFunctionNotFound(_logger, fcc.Name);
                return true;
            }
        }

        return false;
    }

    /// <summary>Invokes the functions and returns results.</summary>
    private async Task<(bool shouldTerminate, int newConsecutiveErrorCount, List<RealtimeClientMessage> functionResults)> InvokeFunctionsAsync(
        List<FunctionCallContent> functionCallContents,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var captureCurrentIterationExceptions = consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest;

        // Use the processor to handle function calls
        var results = await Processor.ProcessFunctionCallsAsync(
            functionCallContents,
            name => FindTool(name, AdditionalTools, _innerSession.Options?.Tools),
            AllowConcurrentInvocation,
            (callContent, aiFunction, _) => new FunctionInvocationContext
            {
                Function = aiFunction,
                Arguments = new(callContent.Arguments) { Services = FunctionInvocationServices },
                CallContent = callContent
            },
            ctx => CurrentContext = ctx,
            captureCurrentIterationExceptions,
            cancellationToken).ConfigureAwait(false);

        var shouldTerminate = results.Exists(static r => r.Terminate);

        // Update consecutive error count
        bool hasErrors = results.Exists(static r => r.Status == FunctionInvocationStatus.Exception);
        int newConsecutiveErrorCount = hasErrors ? consecutiveErrorCount + 1 : 0;

        // Check if we exceeded the maximum consecutive errors
        if (newConsecutiveErrorCount > MaximumConsecutiveErrorsPerRequest)
        {
            var firstException = results.Find(static r => r.Exception is not null)?.Exception;
            if (firstException is not null)
            {
                throw firstException;
            }
        }

        // Create function result messages
        var functionResults = CreateFunctionResultMessages(results);

        return (shouldTerminate, newConsecutiveErrorCount, functionResults);
    }

    /// <summary>Creates function result messages from invocation results.</summary>
    private List<RealtimeClientMessage> CreateFunctionResultMessages(List<FunctionInvocationResult> results)
    {
        var messages = new List<RealtimeClientMessage>(results.Count);

        foreach (var result in results)
        {
            // Determine the result value to send back
            object? resultValue = result.Status switch
            {
                FunctionInvocationStatus.RanToCompletion => result.Result,
                FunctionInvocationStatus.NotFound => "Error: Function not found.",
                FunctionInvocationStatus.Exception => IncludeDetailedErrors && result.Exception is not null
                    ? $"Error: {result.Exception.Message}"
                    : "Error: Function invocation failed.",
                _ => "Error: Unknown status."
            };

            // Create the FunctionResultContent
            var functionResultContent = new FunctionResultContent(result.CallContent.CallId, resultValue)
            {
                Exception = result.Exception
            };

            // Create the RealtimeConversationItem with the function result
            var contentItem = new RealtimeConversationItem([functionResultContent]);

            // Create the conversation item create message
            var message = new CreateConversationItemRealtimeClientMessage(contentItem);
            messages.Add(message);
        }

        // Add a response create message so the model responds to the function results.
        // Do not hardcode output modalities; let the session defaults apply so audio sessions
        // continue to work correctly.
        messages.Add(new CreateResponseRealtimeClientMessage());

        return messages;
    }

    /// <summary>This method will invoke the function within the try block.</summary>
    /// <param name="context">The function invocation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The function result.</returns>
    private ValueTask<object?> InvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        _ = Throw.IfNull(context);

        return FunctionInvoker is { } invoker ?
            invoker(context, cancellationToken) :
            context.Function.InvokeAsync(context.Arguments, cancellationToken);
    }
}
