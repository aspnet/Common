// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.Diagnostics;

#pragma warning disable CA2213 // Disposable fields should be disposed
#pragma warning disable S2219 // Runtime type checking should be simplified
#pragma warning disable S3353 // Unchanged local variables should be "const"
#pragma warning disable SA1204 // Static members should appear before non-static members

namespace Microsoft.Extensions.AI;

/// <summary>
/// A delegating chat client that invokes functions defined on <see cref="ChatOptions"/>.
/// Include this in a chat pipeline to resolve function calls automatically.
/// </summary>
/// <remarks>
/// <para>
/// When this client receives a <see cref="FunctionCallContent"/> in a chat response from its inner
/// <see cref="IChatClient"/>, it responds by invoking the corresponding <see cref="AIFunction"/> defined
/// in <see cref="ChatOptions.Tools"/> (or in <see cref="AdditionalTools"/>), producing a <see cref="FunctionResultContent"/>
/// that it sends back to the inner client. This loop is repeated until there are no more function calls to make, or until
/// another stop condition is met, such as hitting <see cref="MaximumIterationsPerRequest"/>.
/// </para>
/// <para>
/// If a requested function is an <see cref="AIFunctionDeclaration"/> but not an <see cref="AIFunction"/>, the
/// <see cref="FunctionInvokingChatClient"/> will not attempt to invoke it, and instead allow that <see cref="FunctionCallContent"/>
/// to pass back out to the caller. It is then that caller's responsibility to create the appropriate <see cref="FunctionResultContent"/>
/// for that call and send it back as part of a subsequent request.
/// </para>
/// <para>
/// Further, if a requested function is an <see cref="ApprovalRequiredAIFunction"/>, the <see cref="FunctionInvokingChatClient"/> will not
/// attempt to invoke it directly. Instead, it will replace that <see cref="FunctionCallContent"/> with a <see cref="ToolApprovalRequestContent"/>
/// that wraps the <see cref="FunctionCallContent"/> and indicates that the function requires approval before it can be invoked. The caller is then
/// responsible for responding to that approval request by sending a corresponding <see cref="ToolApprovalResponseContent"/> in a subsequent
/// request. The <see cref="FunctionInvokingChatClient"/> will then process that approval response and invoke the function as appropriate.
/// </para>
/// <para>
/// Due to the nature of interactions with an underlying <see cref="IChatClient"/>, if any <see cref="FunctionCallContent"/> is received
/// for a function that requires approval, all received <see cref="FunctionCallContent"/> in that same response will also require approval,
/// even if they were not <see cref="ApprovalRequiredAIFunction"/> instances. If this is a concern, consider requesting that multiple tool call
/// requests not be made in a single response, by setting <see cref="ChatOptions.AllowMultipleToolCalls"/> to <see langword="false"/>.
/// </para>
/// <para>
/// A <see cref="FunctionInvokingChatClient"/> instance is thread-safe for concurrent use so long as the
/// <see cref="AIFunction"/> instances employed as part of the supplied <see cref="ChatOptions"/> are also safe.
/// The <see cref="AllowConcurrentInvocation"/> property can be used to control whether multiple function invocation
/// requests as part of the same request are invocable concurrently, but even with that set to <see langword="false"/>
/// (the default), multiple concurrent requests to this same instance and using the same tools could result in those
/// tools being used concurrently (one per request). For example, a function that accesses the HttpContext of a specific
/// ASP.NET web request should only be used as part of a single <see cref="ChatOptions"/> at a time, and only with
/// <see cref="AllowConcurrentInvocation"/> set to <see langword="false"/>, in case the inner client decided to issue multiple
/// invocation requests to that same function.
/// </para>
/// </remarks>
public class FunctionInvokingChatClient : DelegatingChatClient
{
    /// <summary>The <see cref="FunctionInvocationContext"/> for the current function invocation.</summary>
    private static readonly AsyncLocal<FunctionInvocationContext?> _currentContext = new();

    /// <summary>Gets the <see cref="IServiceProvider"/> specified when constructing the <see cref="FunctionInvokingChatClient"/>, if any.</summary>
    protected IServiceProvider? FunctionInvocationServices { get; }

    /// <summary>The logger to use for logging information about function invocation.</summary>
    private readonly ILogger _logger;

    /// <summary>The <see cref="ActivitySource"/> to use for telemetry.</summary>
    /// <remarks>This component does not own the instance and should not dispose it.</remarks>
    private readonly ActivitySource? _activitySource;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionInvokingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The underlying <see cref="IChatClient"/>, or the next instance in a chain of clients.</param>
    /// <param name="loggerFactory">An <see cref="ILoggerFactory"/> to use for logging information about function invocation.</param>
    /// <param name="functionInvocationServices">An optional <see cref="IServiceProvider"/> to use for resolving services required by the <see cref="AIFunction"/> instances being invoked.</param>
    public FunctionInvokingChatClient(IChatClient innerClient, ILoggerFactory? loggerFactory = null, IServiceProvider? functionInvocationServices = null)
        : base(innerClient)
    {
        _logger = (ILogger?)loggerFactory?.CreateLogger<FunctionInvokingChatClient>() ?? NullLogger.Instance;
        _activitySource = innerClient.GetService<ActivitySource>();
        FunctionInvocationServices = functionInvocationServices;
    }

    /// <summary>Gets the function invocation processor, creating it lazily.</summary>
    private FunctionInvocationProcessor Processor => field ??= new FunctionInvocationProcessor(
        _logger,
        _activitySource,
        InvokeFunctionAsync,
        invokeAgentActivity =>
            invokeAgentActivity is not null
                ? invokeAgentActivity.GetCustomProperty(OpenTelemetryChatClient.SensitiveDataEnabledCustomKey) as string is OpenTelemetryChatClient.SensitiveDataEnabledTrueValue
                : InnerClient.GetService<OpenTelemetryChatClient>()?.EnableSensitiveData is true);

    /// <summary>
    /// Gets or sets the <see cref="FunctionInvocationContext"/> for the current function invocation.
    /// </summary>
    /// <remarks>
    /// This value flows across async calls.
    /// </remarks>
    public static FunctionInvocationContext? CurrentContext
    {
        get => _currentContext.Value;
        protected set => _currentContext.Value = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether detailed exception information should be included
    /// in the chat history when calling the underlying <see cref="IChatClient"/>.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the full exception message is added to the chat history
    /// when calling the underlying <see cref="IChatClient"/>.
    /// <see langword="false"/> if a generic error message is included in the chat history.
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Setting the value to <see langword="false"/> prevents the underlying language model from disclosing
    /// raw exception details to the end user, since it doesn't receive that information. Even in this
    /// case, the raw <see cref="Exception"/> object is available to application code by inspecting
    /// the <see cref="FunctionResultContent.Exception"/> property.
    /// </para>
    /// <para>
    /// Setting the value to <see langword="true"/> can help the underlying <see cref="IChatClient"/> bypass problems on
    /// its own, for example by retrying the function call with different arguments. However it might
    /// result in disclosing the raw exception information to external users, which can be a security
    /// concern depending on the application scenario.
    /// </para>
    /// <para>
    /// Changing the value of this property while the client is in use might result in inconsistencies
    /// as to whether detailed errors are provided during an in-flight request.
    /// </para>
    /// </remarks>
    public bool IncludeDetailedErrors { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to allow concurrent invocation of functions.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if multiple function calls can execute in parallel.
    /// <see langword="false"/> if function calls are processed serially.
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// An individual response from the inner client might contain multiple function call requests.
    /// By default, such function calls are processed serially. Set <see cref="AllowConcurrentInvocation"/> to
    /// <see langword="true"/> to enable concurrent invocation such that multiple function calls can execute in parallel.
    /// </remarks>
    public bool AllowConcurrentInvocation { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of iterations per request.
    /// </summary>
    /// <value>
    /// The maximum number of iterations per request.
    /// The default value is 40.
    /// </value>
    /// <remarks>
    /// <para>
    /// Each request to this <see cref="FunctionInvokingChatClient"/> might end up making
    /// multiple requests to the inner client. Each time the inner client responds with
    /// a function call request, this client might perform that invocation and send the results
    /// back to the inner client in a new request. This property limits the number of times
    /// such a roundtrip is performed. The value must be at least one, as it includes the initial request.
    /// </para>
    /// <para>
    /// Changing the value of this property while the client is in use might result in inconsistencies
    /// as to how many iterations are allowed for an in-flight request.
    /// </para>
    /// </remarks>
    public int MaximumIterationsPerRequest
    {
        get;
        set
        {
            if (value < 1)
            {
                Throw.ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = 40;

    /// <summary>
    /// Gets or sets the maximum number of consecutive iterations that are allowed to fail with an error.
    /// </summary>
    /// <value>
    /// The maximum number of consecutive iterations that are allowed to fail with an error.
    /// The default value is 3.
    /// </value>
    /// <remarks>
    /// <para>
    /// When function invocations fail with an exception, the <see cref="FunctionInvokingChatClient"/>
    /// continues to make requests to the inner client, optionally supplying exception information (as
    /// controlled by <see cref="IncludeDetailedErrors"/>). This allows the <see cref="IChatClient"/> to
    /// recover from errors by trying other function parameters that might succeed.
    /// </para>
    /// <para>
    /// However, in case function invocations continue to produce exceptions, this property can be used to
    /// limit the number of consecutive failing attempts. When the limit is reached, the exception will be
    /// rethrown to the caller.
    /// </para>
    /// <para>
    /// If the value is set to zero, all function calling exceptions immediately terminate the function
    /// invocation loop and the exception will be rethrown to the caller.
    /// </para>
    /// <para>
    /// Changing the value of this property while the client is in use might result in inconsistencies
    /// as to how many iterations are allowed for an in-flight request.
    /// </para>
    /// </remarks>
    public int MaximumConsecutiveErrorsPerRequest
    {
        get;
        set => field = Throw.IfLessThan(value, 0);
    } = 3;

    /// <summary>Gets or sets a collection of additional tools the client is able to invoke.</summary>
    /// <remarks>
    /// These will not impact the requests sent by the <see cref="FunctionInvokingChatClient"/>, which will pass through the
    /// <see cref="ChatOptions.Tools" /> unmodified. However, if the inner client requests the invocation of a tool
    /// that was not in <see cref="ChatOptions.Tools" />, this <see cref="AdditionalTools"/> collection will also be consulted
    /// to look for a corresponding tool to invoke. This is useful when the service might have been preconfigured to be aware
    /// of certain tools that aren't also sent on each individual request.
    /// </remarks>
    public IList<AITool>? AdditionalTools { get; set; }

    /// <summary>Gets or sets a value indicating whether a request to call an unknown function should terminate the function calling loop.</summary>
    /// <value>
    /// <see langword="true"/> to terminate the function calling loop and return the response if a request to call a tool
    /// that isn't available to the <see cref="FunctionInvokingChatClient"/> is received; <see langword="false"/> to create and send a
    /// function result message to the inner client stating that the tool couldn't be found. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// When <see langword="false"/>, call requests to any tools that aren't available to the <see cref="FunctionInvokingChatClient"/>
    /// will result in a response message automatically being created and returned to the inner client stating that the tool couldn't be
    /// found. This behavior can help in cases where a model hallucinates a function, but it's problematic if the model has been made aware
    /// of the existence of tools outside of the normal mechanisms, and requests one of those. <see cref="AdditionalTools"/> can be used
    /// to help with that. But if instead the consumer wants to know about all function call requests that the client can't handle,
    /// <see cref="TerminateOnUnknownCalls"/> can be set to <see langword="true"/>. Upon receiving a request to call a function
    /// that the <see cref="FunctionInvokingChatClient"/> doesn't know about, it will terminate the function calling loop and return
    /// the response, leaving the handling of the function call requests to the consumer of the client.
    /// </para>
    /// <para>
    /// <see cref="AITool"/>s that the <see cref="FunctionInvokingChatClient"/> is aware of (for example, because they're in
    /// <see cref="ChatOptions.Tools"/> or <see cref="AdditionalTools"/>) but that aren't <see cref="AIFunction"/>s aren't considered
    /// unknown, just not invocable. Any requests to a non-invocable tool will also result in the function calling loop terminating,
    /// regardless of <see cref="TerminateOnUnknownCalls"/>.
    /// </para>
    /// </remarks>
    public bool TerminateOnUnknownCalls { get; set; }

    /// <summary>Gets or sets a delegate used to invoke <see cref="AIFunction"/> instances.</summary>
    /// <remarks>
    /// By default, the protected <see cref="InvokeFunctionAsync"/> method is called for each <see cref="AIFunction"/> to be invoked,
    /// invoking the instance and returning its result. If this delegate is set to a non-<see langword="null"/> value,
    /// <see cref="InvokeFunctionAsync"/> will replace its normal invocation with a call to this delegate, enabling
    /// this delegate to assume all invocation handling of the function.
    /// </remarks>
    public Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? FunctionInvoker { get; set; }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        // A single request into this GetResponseAsync may result in multiple requests to the inner client.
        // Create an activity to group them together for better observability. If there's already a genai "invoke_agent"
        // or "invoke_workflow" span that's current, however, we just consider that the group and don't add a new one.
        using Activity? activity = FunctionInvocationHelpers.CurrentActivityIsInvokeAgent ? null : _activitySource?.StartActivity(OpenTelemetryConsts.GenAI.OrchestrateToolsName);

        // Copy the original messages in order to avoid enumerating the original messages multiple times.
        // The IEnumerable can represent an arbitrary amount of work.
        List<ChatMessage> originalMessages = [.. messages];
        messages = originalMessages;

        List<ChatMessage>? augmentedHistory = null; // the actual history of messages sent on turns other than the first
        ChatResponse? response = null; // the response from the inner client, which is possibly modified and then eventually returned
        List<ChatMessage>? responseMessages = null; // tracked list of messages, across multiple turns, to be used for the final response
        UsageDetails? totalUsage = null; // tracked usage across all turns, to be used for the final response
        List<FunctionCallContent>? functionCallContents = null; // function call contents that need responding to in the current turn
        bool lastIterationHadConversationId = false; // whether the last iteration's response had a ConversationId set
        int consecutiveErrorCount = 0;
        bool anyToolsRequireApproval = false;

        if (HasAnyFunctionApproval(originalMessages))
        {
            // A previous turn may have translated FunctionCallContents from the inner client into approval requests sent back to the caller,
            // for any AIFunctions that were actually ApprovalRequiredAIFunctions. If the incoming chat messages include responses to those
            // approval requests, we need to process them now. This entails removing these manufactured approval requests from the chat message
            // list and replacing them with the appropriate FunctionCallContents and FunctionResultContents that would have been generated if
            // the inner client had returned them directly. The reconstructed messages are inserted at the approval anchor so they stay adjacent
            // to the assistant tool-call and ahead of any trailing caller-supplied messages.
            (responseMessages, var notInvokedApprovals, int approvedResultInsertIndex) = ProcessFunctionApprovalResponses(
                originalMessages, !string.IsNullOrWhiteSpace(options?.ConversationId), toolMessageId: null, functionCallContentFallbackMessageId: null);
            (IList<ChatMessage>? invokedApprovedFunctionApprovalResponses, bool shouldTerminate, consecutiveErrorCount) =
                await InvokeApprovedFunctionApprovalResponsesAsync(notInvokedApprovals, originalMessages, options, consecutiveErrorCount, isStreaming: false, approvedResultInsertIndex, cancellationToken);

            if (invokedApprovedFunctionApprovalResponses is not null)
            {
                // Add any generated FRCs to the list we'll return to callers as part of the next response.
                (responseMessages ??= []).AddRange(invokedApprovedFunctionApprovalResponses);
            }

            if (shouldTerminate)
            {
                return new ChatResponse(responseMessages);
            }
        }

        // At this point, we've fully handled all approval responses that were part of the original messages,
        // and we can now enter the main function calling loop.

        for (int iteration = 0; ; iteration++)
        {
            functionCallContents?.Clear();

            // On the last iteration, we won't be processing any function calls, so we should not
            // include AIFunctionDeclaration tools in the request to prevent the inner client from
            // returning tool call requests that won't be handled.
            if (iteration >= MaximumIterationsPerRequest)
            {
                LogMaximumIterationsReached(MaximumIterationsPerRequest);
                PrepareOptionsForLastIteration(ref options);
            }

            // Make the call to the inner client.
            response = await base.GetResponseAsync(messages, options, cancellationToken);
            if (response is null)
            {
                Throw.InvalidOperationException($"The inner {nameof(IChatClient)} returned a null {nameof(ChatResponse)}.");
            }

            // Before we do any function execution, mark any FunctionCallContent as InformationalOnly if the
            // response also contains a matching FunctionResultContent, as that means the server already handled the call.
            MarkServerHandledFunctionCalls(response.Messages);

            // Before we do any function execution, make sure that any functions that require approval have been turned into
            // approval requests so that they don't get executed here. We recompute anyToolsRequireApproval on each iteration
            // because a function may have modified ChatOptions.Tools.
            anyToolsRequireApproval = AnyToolsRequireApproval(options?.Tools, AdditionalTools);
            if (anyToolsRequireApproval)
            {
                var approvalRequiredFunctions =
                    (options?.Tools ?? Enumerable.Empty<AITool>())
                    .Concat(AdditionalTools ?? Enumerable.Empty<AITool>())
                    .Where(t => t.GetService<ApprovalRequiredAIFunction>() is not null)
                    .ToArray();

                response.Messages = ReplaceFunctionCallsWithApprovalRequests(response.Messages, approvalRequiredFunctions);
            }

            // Any function call work to do? If yes, ensure we're tracking that work in functionCallContents.
            bool requiresFunctionInvocation =
                iteration < MaximumIterationsPerRequest &&
                CopyFunctionCalls(response.Messages, ref functionCallContents);

            if (!requiresFunctionInvocation && iteration == 0)
            {
                // In a common case where we make an initial request and there's no function calling work required,
                // fast path out by just returning the original response. We may already have some messages
                // in responseMessages from processing function approval responses, and we need to ensure
                // those are included in the final response, too.
                if (responseMessages is { Count: > 0 })
                {
                    responseMessages.AddRange(response.Messages);
                    response.Messages = responseMessages;
                }

                return response;
            }

            // Track aggregate details from the response, including all of the response messages and usage details.
            (responseMessages ??= []).AddRange(response.Messages);
            if (response.Usage is not null)
            {
                if (totalUsage is not null)
                {
                    totalUsage.Add(response.Usage);
                }
                else
                {
                    totalUsage = response.Usage;
                }
            }

            // If there's nothing more to do, break out of the loop and allow the handling at the
            // end to configure the response with aggregated data from previous requests.
            if (!requiresFunctionInvocation ||
                ShouldTerminateLoopBasedOnHandleableFunctions(functionCallContents, options))
            {
                break;
            }

            // Prepare the history for the next iteration.
            FixupHistories(originalMessages, ref messages, ref augmentedHistory, response, responseMessages, ref lastIterationHadConversationId);

            // Add the responses from the function calls into the augmented history and also into the tracked
            // list of response messages.
            var modeAndMessages = await ProcessFunctionCallsAsync(augmentedHistory, options, functionCallContents!, iteration, consecutiveErrorCount, isStreaming: false, cancellationToken);
            responseMessages.AddRange(modeAndMessages.MessagesAdded);
            consecutiveErrorCount = modeAndMessages.NewConsecutiveErrorCount;

            if (modeAndMessages.ShouldTerminate)
            {
                break;
            }

            UpdateOptionsForNextIteration(ref options, response.ConversationId);
        }

        Debug.Assert(responseMessages is not null, "Expected to only be here if we have response messages.");
        response.Messages = responseMessages!;
        response.Usage = totalUsage;

        AddUsageTags(activity, totalUsage);

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        // A single request into this GetStreamingResponseAsync may result in multiple requests to the inner client.
        // Create an activity to group them together for better observability. If there's already a genai "invoke_agent"
        // or "invoke_workflow" span that's current, however, we just consider that the group and don't add a new one.
        using Activity? activity = FunctionInvocationHelpers.CurrentActivityIsInvokeAgent ? null : _activitySource?.StartActivity(OpenTelemetryConsts.GenAI.OrchestrateToolsName);
        UsageDetails? totalUsage = activity is { IsAllDataRequested: true } ? new() : null; // tracked usage across all turns, to be used for activity purposes

        // Copy the original messages in order to avoid enumerating the original messages multiple times.
        // The IEnumerable can represent an arbitrary amount of work.
        List<ChatMessage> originalMessages = [.. messages];
        messages = originalMessages;

        List<ChatMessage>? augmentedHistory = null; // the actual history of messages sent on turns other than the first
        List<FunctionCallContent>? functionCallContents = null; // function call contents that need responding to in the current turn
        List<ChatMessage>? responseMessages = null; // tracked list of messages, across multiple turns, to be used in fallback cases to reconstitute history
        bool lastIterationHadConversationId = false; // whether the last iteration's response had a ConversationId set
        List<ChatResponseUpdate> updates = []; // updates from the current response
        int consecutiveErrorCount = 0;
        bool anyToolsRequireApproval = false;

        // This is a synthetic ID since we're generating the tool messages instead of getting them from
        // the underlying provider. When emitting the streamed chunks, it's perfectly valid for us to
        // use the same message ID for all of them within a given iteration, as this is a single logical
        // message with multiple content items. We could also use different message IDs per tool content,
        // but there's no benefit to doing so.
        string toolMessageId = Guid.NewGuid().ToString("N");

        if (HasAnyFunctionApproval(originalMessages))
        {
            // We also need a synthetic ID for the function call content for approved function calls
            // where we don't know what the original message id of the function call was.
            string functionCallContentFallbackMessageId = Guid.NewGuid().ToString("N");

            // A previous turn may have translated FunctionCallContents from the inner client into approval requests sent back to the caller,
            // for any AIFunctions that were actually ApprovalRequiredAIFunctions. If the incoming chat messages include responses to those
            // approval requests, we need to process them now. This entails removing these manufactured approval requests from the chat message
            // list and replacing them with the appropriate FunctionCallContents and FunctionResultContents that would have been generated if
            // the inner client had returned them directly. The reconstructed messages are inserted at the approval anchor so they stay adjacent
            // to the assistant tool-call and ahead of any trailing caller-supplied messages.
            var (preDownstreamCallHistory, notInvokedApprovals, approvedResultInsertIndex) = ProcessFunctionApprovalResponses(
                originalMessages, !string.IsNullOrWhiteSpace(options?.ConversationId), toolMessageId, functionCallContentFallbackMessageId);
            if (preDownstreamCallHistory is not null)
            {
                foreach (var message in preDownstreamCallHistory)
                {
                    yield return ConvertToolResultMessageToUpdate(message, options?.ConversationId, message.MessageId);
                    if (activity is not null)
                    {
                        Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                    }
                }
            }

            // Invoke approved approval responses, which generates some additional FRC wrapped in ChatMessage.
            (IList<ChatMessage>? invokedApprovedFunctionApprovalResponses, bool shouldTerminate, consecutiveErrorCount) =
                await InvokeApprovedFunctionApprovalResponsesAsync(notInvokedApprovals, originalMessages, options, consecutiveErrorCount, isStreaming: true, approvedResultInsertIndex, cancellationToken);

            if (invokedApprovedFunctionApprovalResponses is not null)
            {
                foreach (var message in invokedApprovedFunctionApprovalResponses)
                {
                    message.MessageId = toolMessageId;
                    yield return ConvertToolResultMessageToUpdate(message, options?.ConversationId, message.MessageId);
                    if (activity is not null)
                    {
                        Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                    }
                }
            }

            if (shouldTerminate)
            {
                yield break;
            }
        }

        // At this point, we've fully handled all approval responses that were part of the original messages,
        // and we can now enter the main function calling loop.

        for (int iteration = 0; ; iteration++)
        {
            updates.Clear();
            functionCallContents?.Clear();

            // On the last iteration, we won't be processing any function calls, so we should not
            // include AIFunctionDeclaration tools in the request to prevent the inner client from
            // returning tool call requests that won't be handled.
            if (iteration >= MaximumIterationsPerRequest)
            {
                LogMaximumIterationsReached(MaximumIterationsPerRequest);
                PrepareOptionsForLastIteration(ref options);
            }

            // Recompute anyToolsRequireApproval on each iteration because a function may have modified ChatOptions.Tools.
            anyToolsRequireApproval = AnyToolsRequireApproval(options?.Tools, AdditionalTools);

            AITool[]? approvalRequiredFunctions = null; // available tools that require approval
            bool hasApprovalRequiringFcc = false;
            int lastApprovalCheckedFCCIndex = 0;
            int lastYieldedUpdateIndex = 0;

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                if (update is null)
                {
                    Throw.InvalidOperationException($"The inner {nameof(IChatClient)} streamed a null {nameof(ChatResponseUpdate)}.");
                }

                updates.Add(update);

                _ = CopyFunctionCalls(update.Contents, ref functionCallContents);

                if (totalUsage is not null)
                {
                    IList<AIContent> contents = update.Contents;
                    int contentsCount = contents.Count;
                    for (int i = 0; i < contentsCount; i++)
                    {
                        if (contents[i] is UsageContent uc)
                        {
                            totalUsage.Add(uc.Details);
                        }
                    }
                }

                // We're streaming updates back to the caller. Once we encounter a FunctionCallContent, we need to buffer all
                // remaining updates until the stream completes. This is necessary for two reasons:
                // 1. Server-handled function calls: The server may yield both a FunctionCallContent and a matching
                //    FunctionResultContent. We need to see the full stream to detect these pairs and mark the FCCs as
                //    InformationalOnly so they aren't re-invoked locally.
                // 2. Approval handling: If any tools require approval, we need to convert all FunctionCallContents into
                //    approval requests, even those that don't require approval (we otherwise don't have a way to track the
                //    FCCs to a later turn, in particular when the conversation history is managed by the service / inner client).
                //    If any FCC _might_ require approval, we hold off on yielding until we know whether any actually do,
                //    which is either at the end of the stream or the first time we get an FCC that requires approval.
                //    At that point, we yield all buffered updates, replacing FCCs with approval requests if needed.
                // Generally, however, such buffering does not meaningfully impact consumption experience, as function
                // call contents typically come at the end of the streaming sequence.
                if (anyToolsRequireApproval && approvalRequiredFunctions is null && functionCallContents is { Count: > 0 })
                {
                    approvalRequiredFunctions =
                        (options?.Tools ?? Enumerable.Empty<AITool>())
                        .Concat(AdditionalTools ?? Enumerable.Empty<AITool>())
                        .Where(t => t.GetService<ApprovalRequiredAIFunction>() is not null)
                        .ToArray();
                }

                if (functionCallContents is not { Count: > 0 })
                {
                    // If there are no function calls to make yet, we can yield the update as-is.
                    lastYieldedUpdateIndex++;
                    yield return update;
                    if (activity is not null)
                    {
                        Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                    }
                    continue;
                }

                if (approvalRequiredFunctions is not { Length: > 0 })
                {
                    // We have function calls but no approval-required functions. We still need to buffer updates
                    // from this point on, because the server may also yield a FunctionResultContent for a FCC later
                    // in the stream, and we need to be able to mark those FCCs as InformationalOnly.
                    continue;
                }

                // There are function calls to make, some of which _may_ require approval.
                Debug.Assert(functionCallContents is { Count: > 0 }, "Expected to have function call contents to check for approval requiring functions.");
                Debug.Assert(approvalRequiredFunctions is { Length: > 0 }, "Expected to have approval requiring functions to check against function call contents.");

                // Check if any of the function call contents in this update requires approval.
                (hasApprovalRequiringFcc, lastApprovalCheckedFCCIndex) = CheckForApprovalRequiringFCC(
                    functionCallContents, approvalRequiredFunctions!, hasApprovalRequiringFcc, lastApprovalCheckedFCCIndex);
                if (hasApprovalRequiringFcc)
                {
                    // If we've encountered a function call content that requires approval,
                    // we need to ask for approval for all functions, since we cannot mix and match.
                    // Convert all function call contents into approval requests from the last yielded update index
                    // and yield all those updates.
                    for (; lastYieldedUpdateIndex < updates.Count; lastYieldedUpdateIndex++)
                    {
                        var updateToYield = updates[lastYieldedUpdateIndex];
                        if (TryReplaceFunctionCallsWithApprovalRequests(updateToYield.Contents, approvalRequiredFunctions, out var updatedContents))
                        {
                            updateToYield.Contents = updatedContents;
                        }

                        yield return updateToYield;
                        if (activity is not null)
                        {
                            Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                        }
                    }

                    continue;
                }

                // We don't have any approval requiring function calls yet, but we may receive some in future
                // so we cannot yield the updates yet. We'll just keep them in the updates list for later.
                // We will yield the updates as soon as we receive a function call content that requires approval
                // or when we reach the end of the updates stream.
            }

            // Mark any FunctionCallContent as InformationalOnly if the response also contains a matching
            // FunctionResultContent, as that means the server already handled the call. This is done after the
            // entire stream has been received so that FCC/FRC pairs can be matched across the full set of updates.
            // Any matched FCCs are also removed from functionCallContents so that they won't be invoked locally.
            MarkServerHandledFunctionCalls(updates, functionCallContents);

            // We need to yield any remaining updates that were not yielded while looping through the streamed updates.
            for (; lastYieldedUpdateIndex < updates.Count; lastYieldedUpdateIndex++)
            {
                var updateToYield = updates[lastYieldedUpdateIndex];
                yield return updateToYield;
                if (activity is not null)
                {
                    Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                }
            }

            // If there's nothing more to do, break out of the loop and allow the handling at the
            // end to configure the response with aggregated data from previous requests.
            if (iteration >= MaximumIterationsPerRequest ||
                hasApprovalRequiringFcc ||
                ShouldTerminateLoopBasedOnHandleableFunctions(functionCallContents, options))
            {
                break;
            }

            // We need to invoke functions.

            // Reconstitute a response from the response updates.
            var response = updates.ToChatResponse();
            (responseMessages ??= []).AddRange(response.Messages);

            // Prepare the history for the next iteration.
            FixupHistories(originalMessages, ref messages, ref augmentedHistory, response, responseMessages, ref lastIterationHadConversationId);

            // Process all of the functions, adding their results into the history.
            var modeAndMessages = await ProcessFunctionCallsAsync(augmentedHistory, options, functionCallContents!, iteration, consecutiveErrorCount, isStreaming: true, cancellationToken);
            responseMessages.AddRange(modeAndMessages.MessagesAdded);
            consecutiveErrorCount = modeAndMessages.NewConsecutiveErrorCount;

            // Stream any generated function results. This mirrors what's done for GetResponseAsync, where the returned messages
            // includes all activities, including generated function results.
            foreach (var message in modeAndMessages.MessagesAdded)
            {
                yield return ConvertToolResultMessageToUpdate(message, response.ConversationId, toolMessageId);
                if (activity is not null)
                {
                    Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                }
            }

            if (modeAndMessages.ShouldTerminate)
            {
                break;
            }

            UpdateOptionsForNextIteration(ref options, response.ConversationId);
        }

        AddUsageTags(activity, totalUsage);
    }

    private static ChatResponseUpdate ConvertToolResultMessageToUpdate(ChatMessage message, string? conversationId, string? messageId) =>
        new()
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            ConversationId = conversationId,
            CreatedAt = DateTimeOffset.UtcNow,
            Contents = message.Contents,
            RawRepresentation = message.RawRepresentation,
            ResponseId = messageId,
            MessageId = messageId,
            Role = message.Role,
        };

    /// <summary>Adds tags to <paramref name="activity"/> for usage details in <paramref name="usage"/>.</summary>
    private static void AddUsageTags(Activity? activity, UsageDetails? usage)
    {
        if (usage is not null && activity is { IsAllDataRequested: true })
        {
            if (usage.InputTokenCount is long inputTokens)
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTokens, (int)inputTokens);
            }

            if (usage.OutputTokenCount is long outputTokens)
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputTokens, (int)outputTokens);
            }
        }
    }

    /// <summary>Prepares the various chat message lists after a response from the inner client and before invoking functions.</summary>
    /// <param name="originalMessages">The original messages provided by the caller.</param>
    /// <param name="messages">The messages reference passed to the inner client.</param>
    /// <param name="augmentedHistory">The augmented history containing all the messages to be sent.</param>
    /// <param name="response">The most recent response being handled.</param>
    /// <param name="allTurnsResponseMessages">A list of all response messages received up until this point.</param>
    /// <param name="lastIterationHadConversationId">Whether the previous iteration's response had a conversation ID.</param>
    private static void FixupHistories(
        IEnumerable<ChatMessage> originalMessages,
        ref IEnumerable<ChatMessage> messages,
        [NotNull] ref List<ChatMessage>? augmentedHistory,
        ChatResponse response,
        List<ChatMessage> allTurnsResponseMessages,
        ref bool lastIterationHadConversationId)
    {
        // We're now going to need to augment the history with function result contents.
        // That means we need a separate list to store the augmented history.
        if (response.ConversationId is not null)
        {
            // The response indicates the inner client is tracking the history, so we don't want to send
            // anything we've already sent or received.
            if (augmentedHistory is not null)
            {
                augmentedHistory.Clear();
            }
            else
            {
                augmentedHistory = [];
            }

            lastIterationHadConversationId = true;
        }
        else if (lastIterationHadConversationId)
        {
            // In the very rare case where the inner client returned a response with a conversation ID but then
            // returned a subsequent response without one, we want to reconstitute the full history. To do that,
            // we can populate the history with the original chat messages and then all of the response
            // messages up until this point, which includes the most recent ones.
            augmentedHistory ??= [];
            augmentedHistory.Clear();
            augmentedHistory.AddRange(originalMessages);
            augmentedHistory.AddRange(allTurnsResponseMessages);

            lastIterationHadConversationId = false;
        }
        else
        {
            // If augmentedHistory is already non-null, then we've already populated it with everything up
            // until this point (except for the most recent response). If it's null, we need to seed it with
            // the chat history provided by the caller.
            augmentedHistory ??= originalMessages.ToList();

            // Now add the most recent response messages.
            augmentedHistory.AddMessages(response);

            lastIterationHadConversationId = false;
        }

        // Use the augmented history as the new set of messages to send.
        messages = augmentedHistory;
    }

    /// <summary>Determines whether any of the tools in the specified lists require approval.</summary>
    /// <param name="toolLists">The lists of tools to check.</param>
    /// <returns><see langword="true"/> if any tool requires approval; otherwise, <see langword="false"/>.</returns>
    private static bool AnyToolsRequireApproval(params ReadOnlySpan<IList<AITool>?> toolLists)
    {
        foreach (var toolList in toolLists)
        {
            if (toolList?.Count is int count && count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (toolList[i].GetService<ApprovalRequiredAIFunction>() is not null)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Finds a tool by name in the specified tool lists.</summary>
    /// <param name="name">The name of the tool to find.</param>
    /// <param name="toolLists">The lists of tools to search. Tools from earlier lists take precedence over tools from later lists if they have the same name.</param>
    /// <returns>The tool if found; otherwise, <see langword="null"/>.</returns>
    private static AIFunctionDeclaration? FindTool(string name, params ReadOnlySpan<IList<AITool>?> toolLists)
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
    /// <param name="toolLists">The lists of tools to check.</param>
    /// <returns><see langword="true"/> if there are any tools; otherwise, <see langword="false"/>.</returns>
    private static bool HasAnyTools(params ReadOnlySpan<IList<AITool>?> toolLists)
    {
        foreach (var toolList in toolLists)
        {
            if (toolList?.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether <paramref name="messages"/> contains any <see cref="ToolApprovalRequestContent"/> or <see cref="ToolApprovalResponseContent"/>
    /// instances with a <see cref="FunctionCallContent"/> tool call that the FICC needs to process.
    /// </summary>
    private static bool HasAnyFunctionApproval(List<ChatMessage> messages) =>
        messages.Exists(MessageHasFunctionApproval);

    private static bool MessageHasFunctionApproval(ChatMessage message) =>
        message.Contents.Any(static c =>
            c is ToolApprovalRequestContent { ToolCall: FunctionCallContent { InformationalOnly: false } }
            or ToolApprovalResponseContent { ToolCall: FunctionCallContent { InformationalOnly: false } });

    /// <summary>
    /// Determines whether every content item in <paramref name="message"/> is a function approval request or response
    /// (a <see cref="ToolApprovalRequestContent"/> or <see cref="ToolApprovalResponseContent"/> whose tool call is a
    /// non-informational <see cref="FunctionCallContent"/>). When this returns <see langword="false"/>, the anchor
    /// message carries content that survives extraction - genuine caller content (such as text) or approval content
    /// the FICC does not process itself (for example MCP or informational-only approvals). That surviving content is
    /// what causes the reconstructed tool-call/result block to be inserted before the anchor message rather than
    /// appended after it, keeping the reconstructed tool result adjacent to its function tool-call.
    /// </summary>
    private static bool MessageContainsOnlyFunctionApprovals(ChatMessage message) =>
        message.Contents.All(static c =>
            c is ToolApprovalRequestContent { ToolCall: FunctionCallContent { InformationalOnly: false } }
            or ToolApprovalResponseContent { ToolCall: FunctionCallContent { InformationalOnly: false } });

    /// <summary>Copies any <see cref="FunctionCallContent"/> from <paramref name="messages"/> to <paramref name="functionCalls"/>.</summary>
    private static bool CopyFunctionCalls(
        IList<ChatMessage> messages, [NotNullWhen(true)] ref List<FunctionCallContent>? functionCalls)
    {
        bool any = false;
        int count = messages.Count;
        for (int i = 0; i < count; i++)
        {
            any |= CopyFunctionCalls(messages[i].Contents, ref functionCalls);
        }

        return any;
    }

    /// <summary>Copies any <see cref="FunctionCallContent"/> from <paramref name="content"/> to <paramref name="functionCalls"/>.</summary>
    private static bool CopyFunctionCalls(
        IList<AIContent> content, [NotNullWhen(true)] ref List<FunctionCallContent>? functionCalls)
    {
        bool any = false;
        int count = content.Count;
        for (int i = 0; i < count; i++)
        {
            if (content[i] is FunctionCallContent functionCall && !functionCall.InformationalOnly)
            {
                (functionCalls ??= []).Add(functionCall);
                any = true;
            }
        }

        return any;
    }

    /// <summary>
    /// Marks any <see cref="FunctionCallContent"/> in <paramref name="messages"/> as <see cref="FunctionCallContent.InformationalOnly"/>
    /// if there is a matching <see cref="FunctionResultContent"/> with the same <see cref="ToolCallContent.CallId"/> in the same set of messages,
    /// regardless of order. This handles cases where the server has already executed the function and returned both the call and result.
    /// </summary>
    private static void MarkServerHandledFunctionCalls(IList<ChatMessage> messages)
    {
        // First, collect all FRC CallIds.
        HashSet<string>? resultCallIds = null;
        int messageCount = messages.Count;
        for (int i = 0; i < messageCount; i++)
        {
            IList<AIContent> contents = messages[i].Contents;
            int contentCount = contents.Count;
            for (int j = 0; j < contentCount; j++)
            {
                if (contents[j] is FunctionResultContent frc)
                {
                    _ = (resultCallIds ??= []).Add(frc.CallId);
                }
            }
        }

        if (resultCallIds is null)
        {
            return;
        }

        // Then mark any matching FCCs as InformationalOnly.
        for (int i = 0; i < messageCount; i++)
        {
            IList<AIContent> contents = messages[i].Contents;
            int contentCount = contents.Count;
            for (int j = 0; j < contentCount; j++)
            {
                if (contents[j] is FunctionCallContent fcc && !fcc.InformationalOnly && resultCallIds.Contains(fcc.CallId))
                {
                    fcc.InformationalOnly = true;
                }
            }
        }
    }

    /// <summary>
    /// Marks any <see cref="FunctionCallContent"/> in the streaming <paramref name="updates"/> as <see cref="FunctionCallContent.InformationalOnly"/>
    /// if there is a matching <see cref="FunctionResultContent"/> with the same <see cref="ToolCallContent.CallId"/>, regardless of order.
    /// Any matched entries are also removed from <paramref name="functionCallContents"/> so they won't be invoked locally.
    /// If <paramref name="functionCallContents"/> is <see langword="null"/> or empty, this method is a no-op.
    /// </summary>
    private static void MarkServerHandledFunctionCalls(List<ChatResponseUpdate> updates, List<FunctionCallContent>? functionCallContents)
    {
        if (functionCallContents is not { Count: > 0 })
        {
            return;
        }

        // Collect all FRC CallIds from the updates.
        HashSet<string>? resultCallIds = null;
        int updateCount = updates.Count;
        for (int i = 0; i < updateCount; i++)
        {
            IList<AIContent> contents = updates[i].Contents;
            int contentCount = contents.Count;
            for (int j = 0; j < contentCount; j++)
            {
                if (contents[j] is FunctionResultContent frc)
                {
                    _ = (resultCallIds ??= []).Add(frc.CallId);
                }
            }
        }

        if (resultCallIds is null)
        {
            return;
        }

        // Mark matching FCCs as InformationalOnly and remove them from functionCallContents.
        for (int i = functionCallContents.Count - 1; i >= 0; i--)
        {
            if (resultCallIds.Contains(functionCallContents[i].CallId))
            {
                functionCallContents[i].InformationalOnly = true;
                functionCallContents.RemoveAt(i);
            }
        }
    }

    private static void UpdateOptionsForNextIteration(ref ChatOptions? options, string? conversationId)
    {
        if (options is null)
        {
            if (conversationId is not null)
            {
                options = new() { ConversationId = conversationId };
            }
        }
        else if (options.ToolMode is RequiredChatToolMode)
        {
            // We have to reset the tool mode to be non-required after the first iteration,
            // as otherwise we'll be in an infinite loop.
            options = options.Clone();
            options.ToolMode = null;
            options.ConversationId = conversationId;
        }
        else if (options.ConversationId != conversationId)
        {
            // As with the other modes, ensure we've propagated the chat conversation ID to the options.
            // We only need to clone the options if we're actually mutating it.
            options = options.Clone();
            options.ConversationId = conversationId;
        }
        else if (options.ContinuationToken is not null)
        {
            // Clone options before resetting the continuation token below.
            options = options.Clone();
        }

        // Reset the continuation token of a background response operation
        // to signal the inner client to handle function call result rather
        // than getting the result of the operation.
        if (options?.ContinuationToken is not null)
        {
            options.ContinuationToken = null;
        }
    }

    /// <summary>
    /// Prepares options for the last iteration by removing AIFunctionDeclaration tools.
    /// </summary>
    /// <param name="options">The chat options to prepare.</param>
    /// <remarks>
    /// On the last iteration, we won't be processing any function calls, so we should not
    /// include AIFunctionDeclaration tools in the request. This prevents the inner client
    /// from returning tool call requests that won't be handled.
    /// </remarks>
    private static void PrepareOptionsForLastIteration(ref ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return;
        }

        // Filter out AIFunctionDeclaration tools, keeping only non-function tools
        List<AITool>? remainingTools = null;
        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunctionDeclaration)
            {
                remainingTools ??= [];
                remainingTools.Add(tool);
            }
        }

        // If we removed any tools (including removing all of them), clone and update options
        int remainingCount = remainingTools?.Count ?? 0;
        if (remainingCount < options.Tools.Count)
        {
            options = options.Clone();
            options.Tools = remainingTools;

            // If no tools remain, clear the ToolMode as well
            if (remainingCount == 0)
            {
                options.ToolMode = null;
            }
        }
    }

    /// <summary>Gets whether the function calling loop should exit based on the function call requests.</summary>
    /// <param name="functionCalls">The call requests.</param>
    /// <param name="options">The options used for the response being processed.</param>
    private bool ShouldTerminateLoopBasedOnHandleableFunctions(List<FunctionCallContent>? functionCalls, ChatOptions? options)
    {
        if (functionCalls is not { Count: > 0 })
        {
            // There are no functions to call, so there's no reason to keep going.
            return true;
        }

        if (!HasAnyTools(options?.Tools, AdditionalTools))
        {
            // There are functions to call but we have no tools, so we can't handle them.
            // If we're configured to terminate on unknown call requests, do so now.
            // Otherwise, ProcessFunctionCallsAsync will handle it by creating a NotFound response message.
            if (TerminateOnUnknownCalls)
            {
                // Log each function call that would cause termination
                foreach (var fcc in functionCalls)
                {
                    LogFunctionNotFound(fcc.Name);
                }
            }

            return TerminateOnUnknownCalls;
        }

        // At this point, we have both function call requests and some tools.
        // Look up each function.
        foreach (var fcc in functionCalls)
        {
            AIFunctionDeclaration? tool = FindTool(fcc.Name, options?.Tools, AdditionalTools);
            if (tool is not null)
            {
                if (tool is not AIFunction)
                {
                    // The tool was found but it's not invocable. Regardless of TerminateOnUnknownCallRequests,
                    // we need to break out of the loop so that callers can handle all the call requests.
                    LogNonInvocableFunction(fcc.Name);
                    return true;
                }
            }
            else
            {
                // The tool couldn't be found. If we're configured to terminate on unknown call requests,
                // break out of the loop now. Otherwise, ProcessFunctionCallsAsync will handle it by
                // creating a NotFound response message.
                if (TerminateOnUnknownCalls)
                {
                    LogFunctionNotFound(fcc.Name);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Processes the function calls in the <paramref name="functionCallContents"/> list.
    /// </summary>
    /// <param name="messages">The current chat contents, inclusive of the function call contents being processed.</param>
    /// <param name="options">The options used for the response being processed.</param>
    /// <param name="functionCallContents">The function call contents representing the functions to be invoked.</param>
    /// <param name="iteration">The iteration number of how many roundtrips have been made to the inner client.</param>
    /// <param name="consecutiveErrorCount">The number of consecutive iterations, prior to this one, that were recorded as having function invocation errors.</param>
    /// <param name="isStreaming">Whether the function calls are being processed in a streaming context.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <param name="insertIndex">
    /// The index at which to insert the generated result messages into <paramref name="messages"/>. When
    /// <see langword="null"/>, the results are appended to the end of the list.
    /// </param>
    /// <returns>A value indicating how the caller should proceed.</returns>
    private async Task<(bool ShouldTerminate, int NewConsecutiveErrorCount, IList<ChatMessage> MessagesAdded)> ProcessFunctionCallsAsync(
        List<ChatMessage> messages, ChatOptions? options,
        List<FunctionCallContent> functionCallContents, int iteration, int consecutiveErrorCount,
        bool isStreaming, CancellationToken cancellationToken, int? insertIndex = null)
    {
        // We must add a response for every tool call, regardless of whether we successfully executed it or not.
        // If we successfully execute it, we'll add the result. If we don't, we'll add an error.

        Debug.Assert(functionCallContents.Count > 0, "Expected at least one function call.");
        var captureCurrentIterationExceptions = consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest;

        // Use the processor to handle function calls
        var results = await Processor.ProcessFunctionCallsAsync(
            functionCallContents,
            name => FindTool(name, options?.Tools, AdditionalTools),
            AllowConcurrentInvocation,
            (callContent, aiFunction, callIndex) => new FunctionInvocationContext
            {
                Function = aiFunction,
                Arguments = new(callContent.Arguments) { Services = FunctionInvocationServices },
                Messages = messages,
                Options = options,
                CallContent = callContent,
                Iteration = iteration,
                FunctionCallIndex = callIndex,
                FunctionCount = functionCallContents.Count,
                IsStreaming = isStreaming
            },
            ctx => CurrentContext = ctx,
            captureCurrentIterationExceptions,
            cancellationToken).ConfigureAwait(false);

        // Mark all processed function calls as no longer requiring invocation
        // This prevents duplicate processing when multiple FunctionInvokingChatClients are in a pipeline
        foreach (var result in results)
        {
            result.CallContent.InformationalOnly = true;
        }

        var shouldTerminate = results.Exists(static r => r.Terminate);

        IList<ChatMessage> addedMessages = CreateResponseMessages(results.ToArray());
        ThrowIfNoFunctionResultsAdded(addedMessages);
        UpdateConsecutiveErrorCountOrThrow(addedMessages, ref consecutiveErrorCount);
        if (insertIndex is int idx)
        {
            messages.InsertRange(idx, addedMessages);
        }
        else
        {
            messages.AddRange(addedMessages);
        }

        return (shouldTerminate, consecutiveErrorCount, addedMessages);
    }

    /// <summary>
    /// Updates the consecutive error count, and throws an exception if the count exceeds the maximum.
    /// </summary>
    /// <param name="added">Added messages.</param>
    /// <param name="consecutiveErrorCount">Consecutive error count.</param>
    /// <exception cref="AggregateException">Thrown if the maximum consecutive error count is exceeded.</exception>
    private void UpdateConsecutiveErrorCountOrThrow(IList<ChatMessage> added, ref int consecutiveErrorCount)
    {
        if (added.Any(static m => m.Contents.Any(static c => c is FunctionResultContent { Exception: not null })))
        {
            consecutiveErrorCount++;
            if (consecutiveErrorCount > MaximumConsecutiveErrorsPerRequest)
            {
                LogMaxConsecutiveErrorsExceeded(MaximumConsecutiveErrorsPerRequest);

                var allExceptionsArray = added
                    .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
                    .Select(frc => frc.Exception!)
                    .Where(e => e is not null)
                    .ToArray();

                if (allExceptionsArray.Length == 1)
                {
                    ExceptionDispatchInfo.Capture(allExceptionsArray[0]).Throw();
                }

                throw new AggregateException(allExceptionsArray);
            }
        }
        else
        {
            consecutiveErrorCount = 0;
        }
    }

    /// <summary>
    /// Throws an exception if <see cref="CreateResponseMessages"/> doesn't create any messages.
    /// </summary>
    private void ThrowIfNoFunctionResultsAdded(IList<ChatMessage>? messages)
    {
        if (messages is not { Count: > 0 })
        {
            Throw.InvalidOperationException($"{GetType().Name}.{nameof(CreateResponseMessages)} returned null or an empty collection of messages.");
        }
    }

    /// <summary>Creates one or more response messages for function invocation results.</summary>
    /// <param name="results">Information about the function call invocations and results.</param>
    /// <returns>A list of all chat messages created from <paramref name="results"/>.</returns>
    protected virtual IList<ChatMessage> CreateResponseMessages(
        ReadOnlySpan<FunctionInvocationResult> results)
    {
        var contents = new List<AIContent>(results.Length);
        for (int i = 0; i < results.Length; i++)
        {
            contents.Add(CreateFunctionResultContent(results[i]));
        }

        return [new(ChatRole.Tool, contents)];

        FunctionResultContent CreateFunctionResultContent(FunctionInvocationResult result)
        {
            _ = Throw.IfNull(result);

            object? functionResult;
            if (result.Status == FunctionInvocationStatus.RanToCompletion)
            {
                // If the result is already a FunctionResultContent with a matching CallId, use it directly.
                if (result.Result is FunctionResultContent frc &&
                    frc.CallId == result.CallContent.CallId)
                {
                    return frc;
                }

                functionResult = result.Result ?? "Success: Function completed.";
            }
            else
            {
                string message = result.Status switch
                {
                    FunctionInvocationStatus.NotFound => $"Error: Requested function \"{result.CallContent.Name}\" not found.",
                    FunctionInvocationStatus.Exception => "Error: Function failed.",
                    _ => "Error: Unknown error.",
                };

                if (IncludeDetailedErrors && result.Exception is not null)
                {
                    message = $"{message} Exception: {result.Exception.Message}";
                }

                functionResult = message;
            }

            return new FunctionResultContent(result.CallContent.CallId, functionResult) { Exception = result.Exception };
        }
    }

    /// <summary>This method will invoke the function within the try block.</summary>
    /// <param name="context">The function invocation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The function result.</returns>
    protected virtual ValueTask<object?> InvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        _ = Throw.IfNull(context);

        return FunctionInvoker is { } invoker ?
            invoker(context, cancellationToken) :
            context.Function.InvokeAsync(context.Arguments, cancellationToken);
    }

    /// <summary>
    /// 1. Remove all <see cref="ToolApprovalRequestContent"/> and <see cref="ToolApprovalResponseContent"/> from the <paramref name="originalMessages"/>.
    /// 2. Recreate <see cref="FunctionCallContent"/> for any <see cref="ToolApprovalResponseContent"/> that haven't been executed yet.
    /// 3. Generate failed <see cref="FunctionResultContent"/> for any rejected <see cref="ToolApprovalResponseContent"/>.
    /// 4. add all the new content items to <paramref name="originalMessages"/> and return them as the pre-invocation history.
    /// </summary>
    private (List<ChatMessage>? preDownstreamCallHistory, List<ApprovalResultWithRequestMessage>? approvals, int approvedResultInsertIndex) ProcessFunctionApprovalResponses(
        List<ChatMessage> originalMessages, bool hasConversationId, string? toolMessageId, string? functionCallContentFallbackMessageId)
    {
        // Determine where the reconstructed tool-call/tool-result block is inserted into the outgoing list.
        //
        // The block is anchored just before the last message that carries function approval content. Everything from
        // that message onwards - its residual content that survives extraction (if the approval response shared a
        // message with other content), plus every message after it - ends up after the block; everything before it
        // stays before. This keeps a reconstructed tool result adjacent to the assistant tool-call it belongs to
        // (whether that tool-call is reconstructed by us in client-managed conversation mode, or held by the service
        // in service-managed conversation mode) and ahead of any trailing or residual caller-supplied content.
        //
        // The count is computed before extraction. Any message positioned after lastApprovalIndex carries no
        // extracted function approval content - lastApprovalIndex is the last one that does - so extraction never
        // removes those messages. The last approval message itself survives when it also carries other content that
        // is not a function approval (genuine caller content, or approval content the FICC does not process itself
        // such as MCP or informational-only approvals). That makes the trailing count stable across extraction, so
        // the resulting insert index is always in range.
        //
        // Known limitation: content interleaved between multiple approval responses, or placed before an approval
        // response, remains before the block (only the last approval position is used as the anchor). In
        // service-managed conversation mode - where the service holds every prior tool-call ahead of all new
        // messages - such interleaved/leading content can therefore break tool_calls->tool adjacency. Interleaving
        // other content among, or before, approval responses is not a supported usage pattern.
        int lastApprovalIndex = originalMessages.FindLastIndex(MessageHasFunctionApproval);
        int trailingMessageCount = 0;
        if (lastApprovalIndex >= 0)
        {
            trailingMessageCount = originalMessages.Count - (lastApprovalIndex + 1);
            if (!MessageContainsOnlyFunctionApprovals(originalMessages[lastApprovalIndex]))
            {
                trailingMessageCount++;
            }
        }

        // Extract any approval responses where we need to execute or reject the function calls.
        // The original messages are also modified to remove all approval requests and responses.
        var notInvokedResponses = ExtractAndRemoveApprovalRequestsAndResponses(originalMessages);

        // Insert just before the last approval message's surviving content (see above). When there are no trailing
        // or residual messages this reduces to appending at the end of the list.
        int insertIndex = originalMessages.Count - trailingMessageCount;

        // Wrap the function call content in message(s).
        ICollection<ChatMessage>? allPreDownstreamCallMessages = ConvertToFunctionCallContentMessages(
            [.. notInvokedResponses.rejections ?? Enumerable.Empty<ApprovalResultWithRequestMessage>(), .. notInvokedResponses.approvals ?? Enumerable.Empty<ApprovalResultWithRequestMessage>()],
            functionCallContentFallbackMessageId);

        // Generate failed function result contents for any rejected requests and wrap it in a message.
        List<AIContent>? rejectedFunctionCallResults = GenerateRejectedFunctionResults(notInvokedResponses.rejections);
        ChatMessage? rejectedPreDownstreamCallResultsMessage = rejectedFunctionCallResults is not null ?
            new ChatMessage(ChatRole.Tool, rejectedFunctionCallResults) { MessageId = toolMessageId } :
            null;

        // Add all the FCC that we generated to the pre-downstream-call history so that they can be returned to the caller as part of the next response.
        // Also, if we are not dealing with a service thread (i.e. we don't have a conversation ID), insert them
        // into the original messages list at the anchor so that they are passed to the inner client and can be used to generate a result.
        List<ChatMessage>? preDownstreamCallHistory = null;
        if (allPreDownstreamCallMessages is not null)
        {
            preDownstreamCallHistory = [.. allPreDownstreamCallMessages];
            if (!hasConversationId)
            {
                originalMessages.InsertRange(insertIndex, preDownstreamCallHistory);
                insertIndex += preDownstreamCallHistory.Count;
            }
        }

        // Add all the FRC that we generated to the pre-downstream-call history so that they can be returned to the caller as part of the next response.
        // Also, insert them into the original messages list at the anchor so that they are passed to the inner client and can be used to generate a result.
        if (rejectedPreDownstreamCallResultsMessage is not null)
        {
            (preDownstreamCallHistory ??= []).Add(rejectedPreDownstreamCallResultsMessage);
            originalMessages.Insert(insertIndex, rejectedPreDownstreamCallResultsMessage);
            insertIndex++;
        }

        // insertIndex now points just after the reconstructed tool-call/rejected-result messages, which is where
        // any approved function results should be inserted so they stay ahead of trailing caller messages.
        return (preDownstreamCallHistory, notInvokedResponses.approvals, insertIndex);
    }

    /// <summary>
    /// This method extracts the approval requests and responses from the provided list of messages,
    /// validates them, filters them to ones that require execution, and splits them into approved and rejected.
    /// </summary>
    /// <remarks>
    /// We return the messages containing the approval requests since these are the same messages that originally contained the FunctionCallContent from the downstream service.
    /// We can then use the metadata from these messages when we re-create the FunctionCallContent messages/updates to return to the caller. This way, when we finally do return
    /// the FunctionCallContent to users it's part of a message/update that contains the same metadata as originally returned to the downstream service.
    /// </remarks>
    private (List<ApprovalResultWithRequestMessage>? approvals, List<ApprovalResultWithRequestMessage>? rejections) ExtractAndRemoveApprovalRequestsAndResponses(
        List<ChatMessage> messages)
    {
        Dictionary<string, (ChatMessage Message, ToolApprovalRequestContent RequestContent)>? allApprovalRequestsMessages = null;
        List<ToolApprovalResponseContent>? allApprovalResponses = null;
        HashSet<string>? approvalRequestCallIds = null;
        HashSet<string>? functionResultCallIds = null;

        // 1st iteration, over all messages and content:
        // - Build a list of all function call ids that are already executed.
        // - Build a list of all function approval requests and responses.
        // - Build a list of the content we want to keep (everything except approval requests and responses) and create a new list of messages for those.
        // - Validate that we have an approval response for each approval request.
        bool anyRemoved = false;
        int i = 0;
        for (; i < messages.Count; i++)
        {
            var message = messages[i];

            List<AIContent>? keptContents = null;

            // Examine all content to populate our various collections.
            for (int j = 0; j < message.Contents.Count; j++)
            {
                var content = message.Contents[j];
                switch (content)
                {
                    case ToolApprovalRequestContent tarc when tarc.ToolCall is FunctionCallContent { InformationalOnly: false }:
                        // Validation: Capture each call id for each approval request to ensure later we have a matching response.
                        _ = (approvalRequestCallIds ??= []).Add(tarc.ToolCall.CallId);
                        (allApprovalRequestsMessages ??= []).Add(tarc.RequestId, (message, tarc));
                        break;

                    case ToolApprovalResponseContent tarc when tarc.ToolCall is FunctionCallContent { InformationalOnly: false }:
                        // Validation: Remove the call id for each approval response, to check it off the list of requests we need responses for.
                        _ = approvalRequestCallIds?.Remove(tarc.ToolCall.CallId);
                        (allApprovalResponses ??= []).Add(tarc);
                        break;

                    case FunctionResultContent frc:
                        // Maintain a list of function calls that have already been invoked to avoid invoking them twice.
                        _ = (functionResultCallIds ??= []).Add(frc.CallId);
                        goto default;

                    default:
                        // Content to keep.
                        (keptContents ??= []).Add(content);
                        break;
                }
            }

            // If any contents were filtered out, we need to either remove the message entirely (if no contents remain) or create a new message with the filtered contents.
            if (keptContents?.Count != message.Contents.Count)
            {
                if (keptContents is { Count: > 0 })
                {
                    // Create a new replacement message to store the filtered contents.
                    var newMessage = message.Clone();
                    newMessage.Contents = keptContents;
                    messages[i] = newMessage;
                }
                else
                {
                    // Remove the message entirely since it has no contents left. Rather than doing an O(N) removal, which could possibly
                    // result in an O(N^2) overall operation, we mark the message as null and then do a single pass removal of all nulls after the loop.
                    anyRemoved = true;
                    messages[i] = null!;
                }
            }
        }

        // Clean up any messages that were marked for removal during the iteration.
        if (anyRemoved)
        {
            _ = messages.RemoveAll(static m => m is null);
        }

        // Validation: If we got an approval for each request, we should have no call ids left.
        if (approvalRequestCallIds is { Count: > 0 })
        {
            Throw.InvalidOperationException(
                $"ToolApprovalRequestContent found with FunctionCall.CallId(s) '{string.Join(", ", approvalRequestCallIds)}' that have no matching ToolApprovalResponseContent.");
        }

        // 2nd iteration, over all approval responses:
        // - Filter out any approval responses that already have a matching function result (i.e. already executed).
        // - Find the matching function approval request for any response (where available).
        // - Split the approval responses into two lists: approved and rejected, with their request messages (where available).
        List<ApprovalResultWithRequestMessage>? approvedFunctionCalls = null, rejectedFunctionCalls = null;
        if (allApprovalResponses is { Count: > 0 })
        {
            foreach (var approvalResponse in allApprovalResponses)
            {
                // Skip any approval responses that have already been processed.
                if (approvalResponse.ToolCall is not FunctionCallContent fcc || functionResultCallIds?.Contains(fcc.CallId) is true)
                {
                    continue;
                }

                LogProcessingApprovalResponse(fcc.Name, approvalResponse.Approved);

                // Split the responses into approved and rejected.
                ref List<ApprovalResultWithRequestMessage>? targetList = ref approvalResponse.Approved ? ref approvedFunctionCalls : ref rejectedFunctionCalls;

                ChatMessage? requestMessage = null;
                ToolApprovalRequestContent? requestContent = null;
                if (allApprovalRequestsMessages?.TryGetValue(approvalResponse.RequestId, out var requestInfo) is true)
                {
                    requestMessage = requestInfo.Message;
                    requestContent = requestInfo.RequestContent;
                }

                (targetList ??= []).Add(new() { Response = approvalResponse, Request = requestContent, RequestMessage = requestMessage });
            }
        }

        return (approvedFunctionCalls, rejectedFunctionCalls);
    }

    /// <summary>
    /// If we have any rejected approval responses, we need to generate failed function results for them.
    /// </summary>
    /// <param name="rejections">Any rejected approval responses.</param>
    /// <returns>The <see cref="AIContent"/> for the rejected function calls.</returns>
    private List<AIContent>? GenerateRejectedFunctionResults(List<ApprovalResultWithRequestMessage>? rejections) =>
        rejections is { Count: > 0 } ?
            rejections.ConvertAll(m =>
            {
                LogFunctionRejected(m.ResponseFunctionCallContent.Name, m.Response.Reason);

                string result = "Tool call invocation rejected.";
                if (!string.IsNullOrWhiteSpace(m.Response.Reason))
                {
                    result = $"{result} {m.Response.Reason}";
                }

                // Mark the function call as purely informational since we're handling it (by rejecting it).
                // We mark both the response and request FunctionCallContent to ensure consistency
                // across serialization boundaries where they may be separate object instances.
                m.ResponseFunctionCallContent.InformationalOnly = true;
                _ = m.RequestFunctionCallContent?.InformationalOnly = true;

                return (AIContent)new FunctionResultContent(m.ResponseFunctionCallContent.CallId, result);
            }) :
            null;

    /// <summary>
    /// Extracts the <see cref="FunctionCallContent"/> from the provided <see cref="ToolApprovalResponseContent"/> to recreate the original function call messages.
    /// The output messages tries to mimic the original messages that contained the <see cref="FunctionCallContent"/>, e.g. if the <see cref="FunctionCallContent"/>
    /// had been split into separate messages, this method will recreate similarly split messages, each with their own <see cref="FunctionCallContent"/>.
    /// </summary>
    private static ICollection<ChatMessage>? ConvertToFunctionCallContentMessages(
        List<ApprovalResultWithRequestMessage>? resultWithRequestMessages, string? fallbackMessageId)
    {
        if (resultWithRequestMessages is not null)
        {
            ChatMessage? currentMessage = null;
            Dictionary<string, ChatMessage>? messagesById = null;

            foreach (var resultWithRequestMessage in resultWithRequestMessages)
            {
                // Don't need to create a dictionary if we already have one or if it's the first iteration.
                if (messagesById is null && currentMessage is not null

                    // Everywhere we have no RequestMessage we use the fallbackMessageId, so in this case there is only one message.
                    && !(resultWithRequestMessage.RequestMessage is null && currentMessage.MessageId == fallbackMessageId)

                    // Where we do have a RequestMessage, we can check if its message id differs from the current one.
                    && (resultWithRequestMessage.RequestMessage is not null && currentMessage.MessageId != resultWithRequestMessage.RequestMessage.MessageId))
                {
                    // The majority of the time, all FCC would be part of a single message, so no need to create a dictionary for this case.
                    // If we are dealing with multiple messages though, we need to keep track of them by their message ID.
                    messagesById = [];

                    // Use the effective key for the previous message, accounting for fallbackMessageId substitution.
                    // If the message's MessageId was set to fallbackMessageId (because the original RequestMessage.MessageId was null),
                    // we should use empty string as the key to match the lookup key used elsewhere.
                    var previousMessageKey = currentMessage.MessageId == fallbackMessageId
                        ? string.Empty
                        : (currentMessage.MessageId ?? string.Empty);
                    messagesById[previousMessageKey] = currentMessage;
                }

                // Use RequestMessage.MessageId for the lookup key, since that's the original message ID from the provider.
                // We must use the same key for both lookup and storage to ensure proper grouping.
                // Note: currentMessage.MessageId may differ from RequestMessage.MessageId because
                // ConvertToFunctionCallContentMessage sets a fallbackMessageId when RequestMessage.MessageId is null.
                var messageKey = resultWithRequestMessage.RequestMessage?.MessageId ?? string.Empty;

                _ = messagesById?.TryGetValue(messageKey, out currentMessage);

                if (currentMessage is null)
                {
                    currentMessage = ConvertToFunctionCallContentMessage(resultWithRequestMessage, fallbackMessageId);
                }
                else
                {
                    currentMessage.Contents.Add(resultWithRequestMessage.Response.ToolCall);
                }

#pragma warning disable IDE0058 // Temporary workaround for Roslyn analyzer issue (see https://github.com/dotnet/roslyn/issues/80499)
                messagesById?[messageKey] = currentMessage;
#pragma warning restore IDE0058
            }

            if (messagesById?.Values is ICollection<ChatMessage> cm)
            {
                return cm;
            }

            if (currentMessage is not null)
            {
                return [currentMessage];
            }
        }

        return null;
    }

    /// <summary>
    /// Takes the <see cref="FunctionCallContent"/> from the <paramref name="resultWithRequestMessage"/> and wraps it in a <see cref="ChatMessage"/>
    /// using the same message id that the <see cref="FunctionCallContent"/> was originally returned with from the downstream <see cref="IChatClient"/>.
    /// </summary>
    private static ChatMessage ConvertToFunctionCallContentMessage(ApprovalResultWithRequestMessage resultWithRequestMessage, string? fallbackMessageId)
    {
        ChatMessage functionCallMessage = resultWithRequestMessage.RequestMessage?.Clone() ?? new() { Role = ChatRole.Assistant };
        functionCallMessage.Contents = [resultWithRequestMessage.Response.ToolCall];
        functionCallMessage.MessageId ??= fallbackMessageId;
        return functionCallMessage;
    }

    /// <summary>
    /// Check if any of the provided <paramref name="functionCallContents"/> require approval.
    /// Supports checking from a provided index up to the end of the list, to allow efficient incremental checking
    /// when streaming.
    /// </summary>
    private static (bool hasApprovalRequiringFcc, int lastApprovalCheckedFCCIndex) CheckForApprovalRequiringFCC(
        List<FunctionCallContent>? functionCallContents,
        AITool[] approvalRequiredFunctions,
        bool hasApprovalRequiringFcc,
        int lastApprovalCheckedFCCIndex)
    {
        // If we already found an approval requiring FCC, we can skip checking the rest.
        if (hasApprovalRequiringFcc)
        {
            Debug.Assert(functionCallContents is not null, "functionCallContents must not be null here, since we have already encountered approval requiring functionCallContents");
            return (true, functionCallContents!.Count);
        }

        if (functionCallContents is not null)
        {
            for (; lastApprovalCheckedFCCIndex < functionCallContents.Count; lastApprovalCheckedFCCIndex++)
            {
                var fcc = functionCallContents![lastApprovalCheckedFCCIndex];
                foreach (var arf in approvalRequiredFunctions)
                {
                    if (arf.Name == fcc.Name)
                    {
                        hasApprovalRequiringFcc = true;
                        break;
                    }
                }
            }
        }

        return (hasApprovalRequiringFcc, lastApprovalCheckedFCCIndex);
    }

    /// <summary>
    /// Replaces all <see cref="FunctionCallContent"/> with <see cref="ToolApprovalRequestContent"/> and ouputs a new list if any of them were replaced.
    /// </summary>
    /// <returns>true if any <see cref="FunctionCallContent"/> was replaced, false otherwise.</returns>
    private static bool TryReplaceFunctionCallsWithApprovalRequests(
        IList<AIContent> content,
        AITool[] approvalRequiredFunctions,
        out List<AIContent>? updatedContent)
    {
        updatedContent = null;

        if (content is { Count: > 0 })
        {
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i] is FunctionCallContent fcc && !fcc.InformationalOnly)
                {
                    updatedContent ??= [.. content]; // Clone the list if we haven't already

                    bool requiresConfirmation = false;
                    for (int j = 0; j < approvalRequiredFunctions.Length; j++)
                    {
                        if (string.Equals(approvalRequiredFunctions[j].Name, fcc.Name, StringComparison.Ordinal))
                        {
                            requiresConfirmation = true;
                            break;
                        }
                    }

                    updatedContent[i] = new ToolApprovalRequestContent(ComposeApprovalRequestId(fcc.CallId), fcc)
                    {
                        RequiresConfirmation = requiresConfirmation,
                    };
                }
            }
        }

        return updatedContent is not null;
    }

    /// <summary>
    /// Replaces all <see cref="FunctionCallContent"/> from <paramref name="messages"/> with <see cref="ToolApprovalRequestContent"/>
    /// if any one of them requires approval.
    /// </summary>
    private IList<ChatMessage> ReplaceFunctionCallsWithApprovalRequests(
        IList<ChatMessage> messages,
        AITool[] approvalRequiredFunctions)
    {
        var outputMessages = messages;

        bool anyApprovalRequired = false;
        List<(int MessageIndex, int ContentIndex, bool RequiresConfirmation)>? allFunctionCallContentIndices = null;

        // Build a list of the indices of all FunctionCallContent items.
        // Also check whether each call's target name matches an approval-required function.
        for (int i = 0; i < messages.Count; i++)
        {
            var content = messages[i].Contents;
            for (int j = 0; j < content.Count; j++)
            {
                if (content[j] is FunctionCallContent functionCall && !functionCall.InformationalOnly)
                {
                    bool requiresConfirmation = false;
                    for (int k = 0; k < approvalRequiredFunctions.Length; k++)
                    {
                        if (string.Equals(approvalRequiredFunctions[k].Name, functionCall.Name, StringComparison.Ordinal))
                        {
                            requiresConfirmation = true;
                            break;
                        }
                    }

                    (allFunctionCallContentIndices ??= []).Add((i, j, requiresConfirmation));

                    anyApprovalRequired |= requiresConfirmation;
                }
            }
        }

        // If any function calls were found, and any of them required approval, we should replace all of them with approval requests.
        // This is because we do not have a way to deal with cases where some function calls require approval and others do not, so we just replace all of them.
        if (anyApprovalRequired)
        {
            Debug.Assert(allFunctionCallContentIndices is not null, "We have already encountered function call contents that require approval.");

            // Clone the list so, we don't mutate the input.
            outputMessages = [.. messages];
            int lastMessageIndex = -1;

            foreach (var (messageIndex, contentIndex, requiresConfirmation) in allFunctionCallContentIndices!)
            {
                // Clone the message if we didn't already clone it in a previous iteration.
                var message = lastMessageIndex != messageIndex ? outputMessages[messageIndex].Clone() : outputMessages[messageIndex];
                message.Contents = [.. message.Contents];

                var functionCall = (FunctionCallContent)message.Contents[contentIndex];
                LogFunctionRequiresApproval(functionCall.Name);
                message.Contents[contentIndex] = new ToolApprovalRequestContent(ComposeApprovalRequestId(functionCall.CallId), functionCall)
                {
                    RequiresConfirmation = requiresConfirmation,
                };
                outputMessages[messageIndex] = message;

                lastMessageIndex = messageIndex;
            }
        }

        return outputMessages;
    }

    /// <summary>Composes an approval request ID from a function call ID.</summary>
    private static string ComposeApprovalRequestId(string callId) => $"ficc_{callId}";

    /// <summary>
    /// Execute the provided <see cref="ToolApprovalResponseContent"/> and return the resulting <see cref="FunctionCallContent"/>
    /// wrapped in <see cref="ChatMessage"/> objects.
    /// </summary>
    private async Task<(IList<ChatMessage>? FunctionResultContentMessages, bool ShouldTerminate, int ConsecutiveErrorCount)> InvokeApprovedFunctionApprovalResponsesAsync(
        List<ApprovalResultWithRequestMessage>? notInvokedApprovals,
        List<ChatMessage> originalMessages,
        ChatOptions? options,
        int consecutiveErrorCount,
        bool isStreaming,
        int insertIndex,
        CancellationToken cancellationToken)
    {
        // Check if there are any function calls to do for any approved functions and execute them.
        if (notInvokedApprovals is { Count: > 0 })
        {
            // The FRC that is generated here is inserted into originalMessages by ProcessFunctionCallsAsync at the
            // supplied index so it stays adjacent to the reconstructed tool-call and ahead of any trailing
            // caller-supplied messages. The trailing messages remain in the list during invocation, so the
            // invoked function still receives the full input via FunctionInvocationContext.Messages.
            var modeAndMessages = await ProcessFunctionCallsAsync(
                originalMessages, options, notInvokedApprovals.Select(x => x.Response.ToolCall).OfType<FunctionCallContent>().ToList(), 0, consecutiveErrorCount, isStreaming, cancellationToken, insertIndex);
            consecutiveErrorCount = modeAndMessages.NewConsecutiveErrorCount;

            // Also mark the request's FCC as InformationalOnly to ensure consistency
            // across serialization boundaries where they may be separate object instances.
            foreach (var approval in notInvokedApprovals)
            {
                _ = approval.RequestFunctionCallContent?.InformationalOnly = true;
            }

            return (modeAndMessages.MessagesAdded, modeAndMessages.ShouldTerminate, consecutiveErrorCount);
        }

        return (null, false, consecutiveErrorCount);
    }

    // Wrapper methods that delegate to FunctionInvocationLogger for consistency with the refactored architecture
    private void LogMaximumIterationsReached(int maximumIterationsPerRequest) =>
        FunctionInvocationLogger.LogMaximumIterationsReached(_logger, maximumIterationsPerRequest);

    private void LogFunctionRequiresApproval(string functionName) =>
        FunctionInvocationLogger.LogFunctionRequiresApproval(_logger, functionName);

    private void LogProcessingApprovalResponse(string functionName, bool approved) =>
        FunctionInvocationLogger.LogProcessingApprovalResponse(_logger, functionName, approved);

    private void LogFunctionRejected(string functionName, string? reason) =>
        FunctionInvocationLogger.LogFunctionRejected(_logger, functionName, reason);

    private void LogMaxConsecutiveErrorsExceeded(int maxErrors) =>
        FunctionInvocationLogger.LogMaxConsecutiveErrorsExceeded(_logger, maxErrors);

    private void LogFunctionNotFound(string functionName) =>
        FunctionInvocationLogger.LogFunctionNotFound(_logger, functionName);

    private void LogNonInvocableFunction(string functionName) =>
        FunctionInvocationLogger.LogNonInvocableFunction(_logger, functionName);

    /// <summary>Provides information about the invocation of a function call.</summary>
    public sealed class FunctionInvocationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionInvocationResult"/> class.
        /// </summary>
        /// <param name="terminate">Indicates whether the caller should terminate the processing loop.</param>
        /// <param name="status">Indicates the status of the function invocation.</param>
        /// <param name="callContent">Contains information about the function call.</param>
        /// <param name="result">The result of the function call.</param>
        /// <param name="exception">The exception thrown by the function call, if any.</param>
        internal FunctionInvocationResult(bool terminate, FunctionInvocationStatus status, FunctionCallContent callContent, object? result, Exception? exception)
        {
            Terminate = terminate;
            Status = status;
            CallContent = callContent;
            Result = result;
            Exception = exception;
        }

        /// <summary>Gets status about how the function invocation completed.</summary>
        public FunctionInvocationStatus Status { get; }

        /// <summary>Gets the function call content information associated with this invocation.</summary>
        public FunctionCallContent CallContent { get; }

        /// <summary>Gets the result of the function call.</summary>
        public object? Result { get; }

        /// <summary>Gets any exception the function call threw.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets a value indicating whether the caller should terminate the processing loop.</summary>
        public bool Terminate { get; }
    }

    /// <summary>Provides error codes for when errors occur as part of the function calling loop.</summary>
    public enum FunctionInvocationStatus
    {
        /// <summary>The operation completed successfully.</summary>
        RanToCompletion,

        /// <summary>The requested function could not be found.</summary>
        NotFound,

        /// <summary>The function call failed with an exception.</summary>
        Exception,
    }

    private readonly struct ApprovalResultWithRequestMessage
    {
        public ToolApprovalResponseContent Response { get; init; }
        public ToolApprovalRequestContent? Request { get; init; }
        public ChatMessage? RequestMessage { get; init; }
        public FunctionCallContent ResponseFunctionCallContent => (FunctionCallContent)Response.ToolCall;
        public FunctionCallContent? RequestFunctionCallContent => Request?.ToolCall as FunctionCallContent;
    }
}
