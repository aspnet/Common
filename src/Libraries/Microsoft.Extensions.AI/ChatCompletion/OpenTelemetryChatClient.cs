// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

#pragma warning disable CA1307 // Specify StringComparison for clarity
#pragma warning disable CA1308 // Normalize strings to uppercase
#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter

namespace Microsoft.Extensions.AI;

/// <summary>Represents a delegating chat client that implements the OpenTelemetry Semantic Conventions for Generative AI systems.</summary>
/// <remarks>
/// This class provides an implementation of the Semantic Conventions for Generative AI systems v1.41, defined at <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />.
/// The specification is still experimental and subject to change; as such, the telemetry output by this client is also subject to change.
/// </remarks>
public sealed partial class OpenTelemetryChatClient : DelegatingChatClient
{
    internal const string SensitiveDataEnabledCustomKey = "__EnableSensitiveData__";
    internal const string SensitiveDataEnabledTrueValue = "true";

    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    private readonly ILogger? _logger;

    private readonly Histogram<int> _tokenUsageHistogram;
    private readonly Histogram<double> _operationDurationHistogram;
    private readonly Histogram<double> _timeToFirstChunkHistogram;
    private readonly Histogram<double> _timePerOutputChunkHistogram;

    private readonly string? _defaultModelId;
    private readonly string? _providerName;
    private readonly string? _serverAddress;
    private readonly int _serverPort;

    private JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryChatClient"/> class.</summary>
    /// <param name="innerClient">The underlying <see cref="IChatClient"/>.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for emitting any logging data from the client.</param>
    /// <param name="sourceName">An optional source name that will be used on the telemetry data.</param>
    public OpenTelemetryChatClient(IChatClient innerClient, ILogger? logger = null, string? sourceName = null)
        : base(innerClient)
    {
        Debug.Assert(innerClient is not null, "Should have been validated by the base ctor");

        _logger = logger;

        if (innerClient!.GetService<ChatClientMetadata>() is ChatClientMetadata metadata)
        {
            _defaultModelId = metadata.DefaultModelId;
            _providerName = metadata.ProviderName;
            _serverAddress = metadata.ProviderUri?.Host;
            _serverPort = metadata.ProviderUri?.Port ?? 0;
        }

        string name = string.IsNullOrEmpty(sourceName) ? OpenTelemetryConsts.DefaultSourceName : sourceName!;
        _activitySource = new(name);
        _meter = new(name);

        _tokenUsageHistogram = OtelMetricHelpers.CreateGenAITokenUsageHistogram(_meter);
        _operationDurationHistogram = OtelMetricHelpers.CreateGenAIOperationDurationHistogram(_meter);

        _timeToFirstChunkHistogram = _meter.CreateHistogram<double>(
            OpenTelemetryConsts.GenAI.Client.TimeToFirstChunk.Name,
            OpenTelemetryConsts.SecondsUnit,
            OpenTelemetryConsts.GenAI.Client.TimeToFirstChunk.Description,
            advice: new() { HistogramBucketBoundaries = OpenTelemetryConsts.GenAI.Client.TimeToFirstChunk.ExplicitBucketBoundaries }
            );

        _timePerOutputChunkHistogram = _meter.CreateHistogram<double>(
            OpenTelemetryConsts.GenAI.Client.TimePerOutputChunk.Name,
            OpenTelemetryConsts.SecondsUnit,
            OpenTelemetryConsts.GenAI.Client.TimePerOutputChunk.Description,
            advice: new() { HistogramBucketBoundaries = OpenTelemetryConsts.GenAI.Client.TimePerOutputChunk.ExplicitBucketBoundaries }
            );

        _jsonSerializerOptions = AIJsonUtilities.DefaultOptions;
    }

    /// <summary>Gets or sets JSON serialization options to use when formatting chat data into telemetry strings.</summary>
    public JsonSerializerOptions JsonSerializerOptions
    {
        get => _jsonSerializerOptions;
        set => _jsonSerializerOptions = Throw.IfNull(value);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activitySource.Dispose();
            _meter.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Gets or sets a value indicating whether potentially sensitive information should be included in telemetry.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if potentially sensitive information should be included in telemetry;
    /// <see langword="false"/> if telemetry shouldn't include raw inputs and outputs.
    /// The default value is <see langword="false"/>, unless the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable is set to "true" (case-insensitive).
    /// </value>
    /// <remarks>
    /// By default, telemetry includes metadata, such as token counts, but not raw inputs
    /// and outputs, such as message content, function call arguments, and function call results.
    /// The default value can be overridden by setting the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable to "true". Explicitly setting this property will override the environment variable.
    /// </remarks>
    public bool EnableSensitiveData { get; set; } = TelemetryHelpers.EnableSensitiveDataDefault;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ActivitySource) ? _activitySource :
        base.GetService(serviceType, serviceKey);

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);
        _jsonSerializerOptions.MakeReadOnly();

        using Activity? activity = CreateAndConfigureActivity(options);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;
        string? requestModelId = options?.ModelId ?? _defaultModelId;

        AddInputMessagesTags(messages, options, activity);

        ChatResponse? response = null;
        Exception? error = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            TraceResponse(activity, requestModelId, response, error, stopwatch);
        }
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);
        _jsonSerializerOptions.MakeReadOnly();

        using Activity? activity = CreateAndConfigureActivity(options, streaming: true);
        bool recordChunkHistograms = _timeToFirstChunkHistogram.Enabled || _timePerOutputChunkHistogram.Enabled;
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled || recordChunkHistograms || activity is not null ? Stopwatch.StartNew() : null;
        string? requestModelId = options?.ModelId ?? _defaultModelId;

        AddInputMessagesTags(messages, options, activity);

        IAsyncEnumerable<ChatResponseUpdate> updates;
        try
        {
            updates = base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            TraceResponse(activity, requestModelId, response: null, ex, stopwatch);
            throw;
        }

        var responseEnumerator = updates.GetAsyncEnumerator(cancellationToken);
        List<ChatResponseUpdate> trackedUpdates = [];
        TimeSpan lastChunkElapsed = default;
        bool isFirstChunk = true;
        bool responseModelSet = false;
        double? timeToFirstChunk = null;
        TagList chunkMetricTags = default;
        if (recordChunkHistograms)
        {
            AddMetricTags(ref chunkMetricTags, requestModelId, response: null);
        }

        Exception? error = null;
        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await responseEnumerator.MoveNextAsync())
                    {
                        break;
                    }

                    update = responseEnumerator.Current;
                }
                catch (Exception ex)
                {
                    error = ex;
                    throw;
                }

                if (recordChunkHistograms)
                {
                    Debug.Assert(stopwatch is not null, "stopwatch should have been initialized when recordChunkHistograms is true");
                    TimeSpan currentElapsed = stopwatch!.Elapsed;
                    double delta = (currentElapsed - lastChunkElapsed).TotalSeconds;

                    if (!responseModelSet && update.ModelId is string modelId)
                    {
                        chunkMetricTags.Add(OpenTelemetryConsts.GenAI.Response.Model, modelId);
                        responseModelSet = true;
                    }

                    if (isFirstChunk)
                    {
                        isFirstChunk = false;
                        timeToFirstChunk = delta;
                        if (_timeToFirstChunkHistogram.Enabled)
                        {
                            _timeToFirstChunkHistogram.Record(delta, chunkMetricTags);
                        }
                    }
                    else if (_timePerOutputChunkHistogram.Enabled)
                    {
                        _timePerOutputChunkHistogram.Record(delta, chunkMetricTags);
                    }

                    lastChunkElapsed = currentElapsed;
                }
                else if (activity is not null && timeToFirstChunk is null)
                {
                    Debug.Assert(stopwatch is not null, "stopwatch should have been initialized when activity is not null");
                    timeToFirstChunk = stopwatch!.Elapsed.TotalSeconds;
                }

                trackedUpdates.Add(update);
                yield return update;
                if (activity is not null)
                {
                    Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                }
            }
        }
        finally
        {
            TraceResponse(activity, requestModelId, trackedUpdates.ToChatResponse(), error, stopwatch, timeToFirstChunk);

            await responseEnumerator.DisposeAsync();
        }
    }

    /// <summary>Creates an activity for a chat request, or returns <see langword="null"/> if not enabled.</summary>
    private Activity? CreateAndConfigureActivity(ChatOptions? options, bool streaming = false)
    {
        Activity? activity = null;
        if (_activitySource.HasListeners())
        {
            string? modelId = options?.ModelId ?? _defaultModelId;

            activity = _activitySource.StartActivity(
                string.IsNullOrWhiteSpace(modelId) ? OpenTelemetryConsts.GenAI.ChatName : $"{OpenTelemetryConsts.GenAI.ChatName} {modelId}",
                ActivityKind.Client);

            if (EnableSensitiveData)
            {
                activity?.SetCustomProperty(SensitiveDataEnabledCustomKey, SensitiveDataEnabledTrueValue);
            }

            if (activity is { IsAllDataRequested: true })
            {
                _ = activity
                    .AddTag(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.ChatName)
                    .AddTag(OpenTelemetryConsts.GenAI.Request.Model, modelId)
                    .AddTag(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

                if (streaming)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.Stream, true);
                }

                if (_serverAddress is not null)
                {
                    _ = activity
                        .AddTag(OpenTelemetryConsts.Server.Address, _serverAddress)
                        .AddTag(OpenTelemetryConsts.Server.Port, _serverPort);
                }

                if (options is not null)
                {
                    if (options.ConversationId is string conversationId)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Conversation.Id, conversationId);
                    }

                    if (options.FrequencyPenalty is float frequencyPenalty)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.FrequencyPenalty, frequencyPenalty);
                    }

                    if (options.MaxOutputTokens is int maxTokens)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.MaxTokens, maxTokens);
                    }

                    if (options.PresencePenalty is float presencePenalty)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.PresencePenalty, presencePenalty);
                    }

                    if (options.Seed is long seed)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.Seed, seed);
                    }

                    if (options.StopSequences is IList<string> { Count: > 0 } stopSequences)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.StopSequences, $"[{string.Join(", ", stopSequences.Select(s => $"\"{s}\""))}]");
                    }

                    if (options.Temperature is float temperature)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.Temperature, temperature);
                    }

                    if (options.TopK is int topK)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.TopK, topK);
                    }

                    if (options.TopP is float top_p)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.TopP, top_p);
                    }

                    if (options.ResponseFormat is not null)
                    {
                        switch (options.ResponseFormat)
                        {
                            case ChatResponseFormatText:
                                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Output.Type, OpenTelemetryConsts.TypeText);
                                break;
                            case ChatResponseFormatJson:
                                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Output.Type, OpenTelemetryConsts.TypeJson);
                                break;
                        }
                    }

                    if (options.Tools is { Count: > 0 })
                    {
                        _ = activity.AddTag(
                            OpenTelemetryConsts.GenAI.Tool.Definitions,
                            JsonSerializer.Serialize(options.Tools.Select(t => OtelFunction.Create(t, includeOptionalProperties: EnableSensitiveData)), OtelContext.Default.IEnumerableOtelFunction));
                    }

                    if (EnableSensitiveData)
                    {
                        // Log all additional request options as raw values on the span.
                        // Since AdditionalProperties has undefined meaning, we treat it as potentially sensitive data.
                        if (options.AdditionalProperties is { } props)
                        {
                            foreach (KeyValuePair<string, object?> prop in props)
                            {
                                _ = activity.AddTag(prop.Key, prop.Value);
                            }
                        }
                    }
                }
            }
        }

        return activity;
    }

    /// <summary>Adds chat response information to the activity.</summary>
    private void TraceResponse(
        Activity? activity,
        string? requestModelId,
        ChatResponse? response,
        Exception? error,
        Stopwatch? stopwatch,
        double? timeToFirstChunk = null)
    {
        if (_operationDurationHistogram.Enabled && stopwatch is not null)
        {
            TagList tags = default;

            AddMetricTags(ref tags, requestModelId, response);
            if (error is not null)
            {
                tags.Add(OpenTelemetryConsts.Error.Type, error.GetType().FullName);
            }

            _operationDurationHistogram.Record(stopwatch.Elapsed.TotalSeconds, tags);
        }

        if (_tokenUsageHistogram.Enabled && response?.Usage is { } usage)
        {
            if (usage.InputTokenCount is long inputTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeInput);
                AddMetricTags(ref tags, requestModelId, response);
                _tokenUsageHistogram.Record((int)inputTokens, tags);
            }

            if (usage.OutputTokenCount is long outputTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeOutput);
                AddMetricTags(ref tags, requestModelId, response);
                _tokenUsageHistogram.Record((int)outputTokens, tags);
            }
        }

        OpenTelemetryLog.RecordOperationError(activity, _logger, error);

        if (response is not null)
        {
            AddOutputMessagesTags(response, activity);

            if (activity is not null)
            {
                if (response.FinishReason is ChatFinishReason finishReason)
                {
#pragma warning disable CA1308 // Normalize strings to uppercase
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.FinishReasons, $"[\"{finishReason.Value.ToLowerInvariant()}\"]");
#pragma warning restore CA1308
                }

                if (!string.IsNullOrWhiteSpace(response.ResponseId))
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.Id, response.ResponseId);
                }

                if (response.ModelId is not null)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.Model, response.ModelId);
                }

                if (timeToFirstChunk is double timeToFirstChunkValue)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.TimeToFirstChunk, timeToFirstChunkValue);
                }

                if (response.Usage?.InputTokenCount is long inputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTokens, (int)inputTokens);
                }

                if (response.Usage?.OutputTokenCount is long outputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputTokens, (int)outputTokens);
                }

                if (response.Usage?.CachedInputTokenCount is long cachedInputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.CacheReadInputTokens, (int)cachedInputTokens);
                }

                if (response.Usage?.ReasoningTokenCount is long reasoningTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.ReasoningOutputTokens, (int)reasoningTokens);
                }

                // Log all additional response properties as raw values on the span.
                // Since AdditionalProperties has undefined meaning, we treat it as potentially sensitive data.
                if (EnableSensitiveData && response.AdditionalProperties is { } props)
                {
                    foreach (KeyValuePair<string, object?> prop in props)
                    {
                        _ = activity.AddTag(prop.Key, prop.Value);
                    }
                }
            }
        }
    }

    private void AddMetricTags(ref TagList tags, string? requestModelId, ChatResponse? response)
    {
        tags.Add(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.ChatName);

        if (requestModelId is not null)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Request.Model, requestModelId);
        }

        tags.Add(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

        if (_serverAddress is string endpointAddress)
        {
            tags.Add(OpenTelemetryConsts.Server.Address, endpointAddress);
            tags.Add(OpenTelemetryConsts.Server.Port, _serverPort);
        }

        if (response?.ModelId is string responseModel)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Response.Model, responseModel);
        }
    }

    private void AddInputMessagesTags(IEnumerable<ChatMessage> messages, ChatOptions? options, Activity? activity)
    {
        if (EnableSensitiveData && activity is { IsAllDataRequested: true })
        {
            if (!string.IsNullOrWhiteSpace(options?.Instructions))
            {
                _ = activity.AddTag(
                    OpenTelemetryConsts.GenAI.SystemInstructions,
                    JsonSerializer.Serialize(new object[1] { new OtelGenericPart { Content = options!.Instructions } }, OtelMessageSerializer.DefaultOptions.GetTypeInfo(typeof(IList<object>))));
            }

            _ = activity.AddTag(
                OpenTelemetryConsts.GenAI.Input.Messages,
                OtelMessageSerializer.SerializeChatMessages(messages, customContentSerializerOptions: _jsonSerializerOptions));
        }
    }

    private void AddOutputMessagesTags(ChatResponse response, Activity? activity)
    {
        if (EnableSensitiveData && activity is { IsAllDataRequested: true })
        {
            _ = activity.AddTag(
                OpenTelemetryConsts.GenAI.Output.Messages,
                OtelMessageSerializer.SerializeChatMessages(response.Messages, response.FinishReason, customContentSerializerOptions: _jsonSerializerOptions));
        }
    }

    // Chat-specific OTel serialization POCOs.
    //
    // Types whose layout is shared 1:1 with OpenTelemetryRealtimeClientSession live in
    // Common/OtelMessageParts.cs. The types below are either entirely chat-specific or
    // contain chat-specific fields. The shared JsonSerializerContext lives in Common/OtelContext.cs,
    // and the shared serialization helpers live in Common/OtelMessageSerializer.cs.
}

#pragma warning disable SA1402 // File may only contain a single type — chat-specific OTel POCOs are co-located with the chat client.

internal sealed class OtelMessage
{
    public string? Role { get; set; }
    public string? Name { get; set; }
    public List<object> Parts { get; set; } = [];
    public string? FinishReason { get; set; }
}

internal sealed class OtelToolCallRequestPart
{
    public string Type { get; set; } = "tool_call";
    public string? Id { get; set; }
    public string? Name { get; set; }
    public IDictionary<string, object?>? Arguments { get; set; }
}

internal sealed class OtelCodeInterpreterToolCall
{
    public string Type { get; set; } = "code_interpreter";
    public string? Code { get; set; }
}

internal sealed class OtelCodeInterpreterToolCallResponse
{
    public string Type { get; set; } = "code_interpreter";
    public object? Output { get; set; }
}

internal sealed class OtelImageGenerationToolCall
{
    public string Type { get; set; } = "image_generation";
}

internal sealed class OtelImageGenerationToolCallResponse
{
    public string Type { get; set; } = "image_generation";
    public object? Output { get; set; }
}

internal sealed class OtelMcpApprovalRequest
{
    public string Type { get; set; } = "mcp_approval_request";
    public string? ServerName { get; set; }
    public IDictionary<string, object?>? Arguments { get; set; }
}

internal sealed class OtelMcpApprovalResponse
{
    public string Type { get; set; } = "mcp_approval_response";
    public bool Approved { get; set; }
}
