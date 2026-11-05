// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

#pragma warning disable CA1308 // Normalize strings to uppercase
#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter
#pragma warning disable SA1204 // Static members should appear before non-static members

namespace Microsoft.Extensions.AI;

/// <summary>Represents a delegating realtime session that follows the OpenTelemetry Semantic Conventions for Generative AI systems where applicable.</summary>
/// <remarks>
/// <para>
/// This class follows the patterns of the Semantic Conventions for Generative AI systems v1.41 where applicable, as defined at
/// <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />, with custom extensions for realtime-specific behavior.
/// The specification does not currently define a realtime operation; a custom operation name is used.
/// </para>
/// <para>
/// The specification is still experimental and subject to change; as such, the telemetry output by this session is also subject to change.
/// </para>
/// <para>
/// The following standard OpenTelemetry GenAI conventions are supported:
/// <list type="bullet">
///   <item><c>gen_ai.operation.name</c> - Operation name ("chat")</item>
///   <item><c>gen_ai.request.model</c> - Model name from options</item>
///   <item><c>gen_ai.request.stream</c> - Indicates streaming response requests; always <see langword="true"/> as realtime is inherently streaming</item>
///   <item><c>gen_ai.provider.name</c> - Provider name from metadata</item>
///   <item><c>gen_ai.response.id</c> - Response ID from ResponseDone messages</item>
///   <item><c>gen_ai.response.model</c> - Model ID from response</item>
///   <item><c>gen_ai.response.time_to_first_chunk</c> - Time to first streaming response chunk</item>
///   <item><c>gen_ai.usage.input_tokens</c> - Input token count</item>
///   <item><c>gen_ai.usage.output_tokens</c> - Output token count</item>
///   <item><c>gen_ai.usage.reasoning.output_tokens</c> - Reasoning output token count</item>
///   <item><c>gen_ai.request.max_tokens</c> - Max output tokens from options</item>
///   <item><c>gen_ai.system_instructions</c> - Instructions from options (sensitive data)</item>
///   <item><c>gen_ai.conversation.id</c> - Conversation ID from response</item>
///   <item><c>gen_ai.tool.definitions</c> - Tool definitions</item>
///   <item><c>gen_ai.input.messages</c> - Input tool/MCP messages (sensitive data)</item>
///   <item><c>gen_ai.output.messages</c> - Output tool/MCP messages (sensitive data)</item>
///   <item><c>server.address</c> / <c>server.port</c> - Server endpoint info</item>
///   <item><c>error.type</c> - Error type on failures</item>
/// </list>
/// </para>
/// <para>
/// MCP (Model Context Protocol) semantic conventions are supported for tool calls and responses, including:
/// <list type="bullet">
///   <item>MCP server tool calls and results</item>
///   <item>MCP approval requests and responses</item>
///   <item>Function calls and results</item>
/// </list>
/// </para>
/// <para>
/// Additionally, the following custom attributes are supported (not part of OpenTelemetry GenAI semantic conventions as of v1.41):
/// <list type="bullet">
///   <item><c>gen_ai.request.tool_choice</c> - Tool choice mode ("none", "auto", "required") or specific tool name</item>
///   <item><c>gen_ai.realtime.voice</c> - Voice setting from options</item>
///   <item><c>gen_ai.realtime.output_modalities</c> - Output modalities (text, audio)</item>
///   <item><c>gen_ai.realtime.voice_speed</c> - Voice speed setting</item>
///   <item><c>gen_ai.realtime.session_kind</c> - Session kind (Realtime/Transcription)</item>
/// </list>
/// </para>
/// <para>
/// Metrics include:
/// <list type="bullet">
///   <item><c>gen_ai.client.operation.duration</c> - Duration histogram</item>
///   <item><c>gen_ai.client.token.usage</c> - Token usage histogram</item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class OpenTelemetryRealtimeClientSession : IRealtimeClientSession
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    private readonly Histogram<int> _tokenUsageHistogram;
    private readonly Histogram<double> _operationDurationHistogram;

    private readonly string? _defaultModelId;
    private readonly string? _providerName;
    private readonly string? _serverAddress;
    private readonly int _serverPort;

    private readonly IRealtimeClientSession _innerSession;
    private readonly ILogger? _logger;

    private JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryRealtimeClientSession"/> class.</summary>
    /// <param name="innerSession">The underlying <see cref="IRealtimeClientSession"/>.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for emitting any logging data from the session.</param>
    /// <param name="sourceName">An optional source name that will be used on the telemetry data.</param>
    public OpenTelemetryRealtimeClientSession(IRealtimeClientSession innerSession, ILogger? logger = null, string? sourceName = null)
    {
        _innerSession = Throw.IfNull(innerSession);
        _logger = logger;

        // Try to get metadata from the inner session's ChatClientMetadata if available
        if (innerSession.GetService(typeof(ChatClientMetadata)) is ChatClientMetadata metadata)
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

        _jsonSerializerOptions = AIJsonUtilities.DefaultOptions;
    }

    /// <summary>Gets or sets JSON serialization options to use when formatting realtime data into telemetry strings.</summary>
    public JsonSerializerOptions JsonSerializerOptions
    {
        get => _jsonSerializerOptions;
        set => _jsonSerializerOptions = Throw.IfNull(value);
    }

    /// <inheritdoc />
    public RealtimeSessionOptions? Options => _innerSession.Options;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _activitySource.Dispose();
        _meter.Dispose();
        await _innerSession.DisposeAsync().ConfigureAwait(false);
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
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceType == typeof(ActivitySource) ? _activitySource :
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this :
            _innerSession.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        if (EnableSensitiveData && _activitySource.HasListeners())
        {
            var otelMessage = ExtractClientOtelMessage(message);

            if (otelMessage is not null)
            {
                using Activity? inputActivity = CreateAndConfigureActivity(options: null);
                if (inputActivity is { IsAllDataRequested: true })
                {
                    _ = inputActivity.AddTag(OpenTelemetryConsts.GenAI.Input.Messages, SerializeMessage(otelMessage));
                }
            }
        }

        await _innerSession.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _jsonSerializerOptions.MakeReadOnly();

        RealtimeSessionOptions? options = Options;
        string? requestModelId = options?.Model ?? _defaultModelId;

        // Start timing from the beginning of the streaming operation
        bool trackStreamingResponseTime = _activitySource.HasListeners();
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled || trackStreamingResponseTime ? Stopwatch.StartNew() : null;
        double? timeToFirstChunk = null;

        // Determine if we should capture messages for telemetry
        bool captureMessages = EnableSensitiveData && _activitySource.HasListeners();

        IAsyncEnumerable<RealtimeServerMessage> responses;
        try
        {
            responses = _innerSession.GetStreamingResponseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Create an activity for the error case
            using Activity? errorActivity = CreateAndConfigureActivity(options, streamingResponse: true);
            TraceStreamingResponse(errorActivity, requestModelId, response: null, ex, stopwatch, timeToFirstChunk);
            throw;
        }

        var responseEnumerator = responses.GetAsyncEnumerator(cancellationToken);
        Exception? error = null;
        List<RealtimeOtelMessage>? outputMessages = captureMessages ? [] : null;
        HashSet<string>? outputModalities = _activitySource.HasListeners() ? [] : null;
        try
        {
            while (true)
            {
                RealtimeServerMessage message;
                try
                {
                    if (!await responseEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    message = responseEnumerator.Current;
                }
                catch (Exception ex)
                {
                    error = ex;
                    throw;
                }

                if (timeToFirstChunk is null && stopwatch is not null)
                {
                    timeToFirstChunk = stopwatch.Elapsed.TotalSeconds;
                }

                // Track output modalities
                if (outputModalities is not null)
                {
                    var modality = GetOutputModality(message);
                    if (modality is not null)
                    {
                        _ = outputModalities.Add(modality);
                    }
                }

                // Capture output content from all server message types
                if (outputMessages is not null)
                {
                    var otelMessage = ExtractServerOtelMessage(message);
                    if (otelMessage is not null)
                    {
                        outputMessages.Add(otelMessage);
                    }
                }

                // Create activity for ResponseDone message for telemetry
                if (message is ResponseCreatedRealtimeServerMessage responseDoneMsg &&
                    responseDoneMsg.Type == RealtimeServerMessageType.ResponseDone)
                {
                    using Activity? responseActivity = CreateAndConfigureActivity(options, streamingResponse: true);

                    // Add output modalities and messages tags
                    AddOutputModalitiesTag(responseActivity, outputModalities);
                    AddOutputMessagesTag(responseActivity, outputMessages);
                    TraceStreamingResponse(responseActivity, requestModelId, responseDoneMsg, error, stopwatch, timeToFirstChunk);
                }

                yield return message;
            }
        }
        finally
        {
            // Trace error if an exception was thrown during streaming
            if (error is not null)
            {
                using Activity? errorActivity = CreateAndConfigureActivity(options, streamingResponse: true);
                AddOutputModalitiesTag(errorActivity, outputModalities);
                AddOutputMessagesTag(errorActivity, outputMessages);
                TraceStreamingResponse(errorActivity, requestModelId, response: null, error, stopwatch, timeToFirstChunk);
            }

            await responseEnumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Adds output modalities tag to the activity.</summary>
    private static void AddOutputModalitiesTag(Activity? activity, HashSet<string>? outputModalities)
    {
        if (activity is { IsAllDataRequested: true } && outputModalities is { Count: > 0 })
        {
            _ = activity.AddTag(OpenTelemetryConsts.GenAI.Realtime.ReceivedModalities, $"[{string.Join(", ", outputModalities.Select(m => $"\"{m}\""))}]");
        }
    }

    /// <summary>Adds output messages tag to the activity if there are messages to add.</summary>
    private static void AddOutputMessagesTag(Activity? activity, List<RealtimeOtelMessage>? outputMessages)
    {
        if (activity is { IsAllDataRequested: true } && outputMessages is { Count: > 0 })
        {
            _ = activity.AddTag(OpenTelemetryConsts.GenAI.Output.Messages, SerializeMessages(outputMessages));
        }
    }

    /// <summary>Gets the output modality from a server message, if applicable.</summary>
    private static string? GetOutputModality(RealtimeServerMessage message)
    {
        if (message is OutputTextAudioRealtimeServerMessage textAudio)
        {
            if (textAudio.Type == RealtimeServerMessageType.OutputTextDelta || textAudio.Type == RealtimeServerMessageType.OutputTextDone)
            {
                return "text";
            }

            if (textAudio.Type == RealtimeServerMessageType.OutputAudioDelta || textAudio.Type == RealtimeServerMessageType.OutputAudioDone)
            {
                return "audio";
            }

            if (textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta || textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
            {
                return "transcription";
            }
        }

        if (message is ResponseOutputItemRealtimeServerMessage)
        {
            return "item";
        }

        return null;
    }

    /// <summary>Extracts an OTel message from a realtime client message.</summary>
    private RealtimeOtelMessage? ExtractClientOtelMessage(RealtimeClientMessage message)
    {
        switch (message)
        {
            case CreateConversationItemRealtimeClientMessage createMsg:
                return ExtractOtelMessage(createMsg.Item);

            case InputAudioBufferAppendRealtimeClientMessage audioAppendMsg:
                var audioMessage = new RealtimeOtelMessage { Role = "user" };
                audioMessage.Parts.Add(new OtelBlobPart
                {
                    Content = audioAppendMsg.Content.Base64Data.ToString(),
                    MimeType = audioAppendMsg.Content.MediaType,
                    Modality = "audio",
                });
                return audioMessage;

            case InputAudioBufferCommitRealtimeClientMessage:
                // Commit message has no content, just a signal
                return new RealtimeOtelMessage
                {
                    Role = "user",
                    Parts = { new OtelGenericPart { Type = "audio_commit" } },
                };

            case CreateResponseRealtimeClientMessage responseCreateMsg:
                var responseMessage = new RealtimeOtelMessage { Role = "user" };

                // Add instructions if present
                if (!string.IsNullOrWhiteSpace(responseCreateMsg.Instructions))
                {
                    responseMessage.Parts.Add(new OtelGenericPart
                    {
                        Type = "instructions",
                        Content = responseCreateMsg.Instructions,
                    });
                }

                // Add items if present
                if (responseCreateMsg.Items is { Count: > 0 } items)
                {
                    foreach (var item in items)
                    {
                        var itemMessage = ExtractOtelMessage(item);
                        if (itemMessage is not null)
                        {
                            foreach (var part in itemMessage.Parts)
                            {
                                responseMessage.Parts.Add(part);
                            }
                        }
                    }
                }

                return responseMessage.Parts.Count > 0 ? responseMessage : null;

            default:
                return null;
        }
    }

    /// <summary>Extracts an OTel message from a realtime server message.</summary>
    private RealtimeOtelMessage? ExtractServerOtelMessage(RealtimeServerMessage message)
    {
        switch (message)
        {
            case ResponseOutputItemRealtimeServerMessage outputItemMsg:
                return ExtractOtelMessage(outputItemMsg.Item);

            case OutputTextAudioRealtimeServerMessage textAudioMsg:
                string partType;
                string? content;

                if (textAudioMsg.Type == RealtimeServerMessageType.OutputAudioDelta || textAudioMsg.Type == RealtimeServerMessageType.OutputAudioDone)
                {
                    partType = "audio";
                    content = string.IsNullOrEmpty(textAudioMsg.Audio) ? "[audio data]" : textAudioMsg.Audio;
                }
                else if (textAudioMsg.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta || textAudioMsg.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
                {
                    partType = "output_transcription";
                    content = textAudioMsg.Text;
                }
                else
                {
                    partType = "text";
                    content = textAudioMsg.Text;
                }

                // Skip if no meaningful content
                if (string.IsNullOrEmpty(content))
                {
                    return null;
                }

                var textAudioOtelMessage = new RealtimeOtelMessage { Role = "assistant" };
                textAudioOtelMessage.Parts.Add(new OtelGenericPart
                {
                    Type = partType,
                    Content = content,
                });
                return textAudioOtelMessage;

            case InputAudioTranscriptionRealtimeServerMessage transcriptionMsg when !string.IsNullOrEmpty(transcriptionMsg.Transcription):
                var transcriptionOtelMessage = new RealtimeOtelMessage { Role = "user" };
                transcriptionOtelMessage.Parts.Add(new OtelGenericPart
                {
                    Type = "input_transcription",
                    Content = transcriptionMsg.Transcription,
                });
                return transcriptionOtelMessage;

            case ErrorRealtimeServerMessage errorMsg when errorMsg.Error is not null:
                var errorOtelMessage = new RealtimeOtelMessage { Role = "system" };
                errorOtelMessage.Parts.Add(new OtelGenericPart
                {
                    Type = "error",
                    Content = errorMsg.Error.Message,
                });
                return errorOtelMessage;

            case ResponseCreatedRealtimeServerMessage responseCreatedMsg when responseCreatedMsg.Items is { Count: > 0 }:
                // Only capture items from ResponseCreated, not ResponseDone (which we use for tracing)
                if (responseCreatedMsg.Type == RealtimeServerMessageType.ResponseCreated)
                {
                    var responseOtelMessage = new RealtimeOtelMessage { Role = "assistant" };
                    foreach (var item in responseCreatedMsg.Items)
                    {
                        var itemMessage = ExtractOtelMessage(item);
                        if (itemMessage is not null)
                        {
                            foreach (var part in itemMessage.Parts)
                            {
                                responseOtelMessage.Parts.Add(part);
                            }
                        }
                    }

                    return responseOtelMessage.Parts.Count > 0 ? responseOtelMessage : null;
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>Serializes a single message to OTel format (as an array with one element).</summary>
    private static string SerializeMessage(RealtimeOtelMessage message)
    {
        return JsonSerializer.Serialize(new[] { message }, OtelContext.Default.IEnumerableRealtimeOtelMessage);
    }

    /// <summary>Serializes content items to OTel format.</summary>
    private static string SerializeMessages(IEnumerable<RealtimeOtelMessage> messages)
    {
        return JsonSerializer.Serialize(messages, OtelContext.Default.IEnumerableRealtimeOtelMessage);
    }

    /// <summary>Extracts content from an AIContent list and converts to OTel format.</summary>
    private RealtimeOtelMessage? ExtractOtelMessage(RealtimeConversationItem? item)
    {
        if (item?.Contents is null or { Count: 0 })
        {
            return null;
        }

        var message = new RealtimeOtelMessage
        {
            Role = item.Role?.Value,
        };

        foreach (var content in item.Contents)
        {
            switch (content)
            {
                // Standard text content
                case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                    message.Parts.Add(new OtelGenericPart { Content = tc.Text });
                    break;

                case TextReasoningContent trc when !string.IsNullOrEmpty(trc.Text):
                    message.Parts.Add(new OtelGenericPart { Type = "reasoning", Content = trc.Text });
                    break;

                // Function call content
                case FunctionCallContent fcc:
                    message.Parts.Add(new RealtimeOtelToolCallPart
                    {
                        Id = fcc.CallId,
                        Name = fcc.Name,
                        Arguments = fcc.Arguments,
                    });
                    break;

                case FunctionResultContent frc:
                    message.Parts.Add(new OtelToolCallResponsePart
                    {
                        Id = frc.CallId,
                        Response = frc.Result,
                    });
                    break;

                // Data content (binary data)
                case DataContent dc:
                    message.Parts.Add(new OtelBlobPart
                    {
                        Content = dc.Base64Data.ToString(),
                        MimeType = dc.MediaType,
                        Modality = OtelMessageSerializer.DeriveModalityFromMediaType(dc.MediaType),
                    });
                    break;

                // URI content
                case UriContent uc:
                    message.Parts.Add(new OtelUriPart
                    {
                        Uri = uc.Uri.AbsoluteUri,
                        MimeType = uc.MediaType,
                        Modality = OtelMessageSerializer.DeriveModalityFromMediaType(uc.MediaType),
                    });
                    break;

                // Hosted file content
                case HostedFileContent fc:
                    message.Parts.Add(new OtelFilePart
                    {
                        FileId = fc.FileId,
                        MimeType = fc.MediaType,
                        Modality = OtelMessageSerializer.DeriveModalityFromMediaType(fc.MediaType),
                    });
                    break;

                // Non-standard "generic" parts
                case HostedVectorStoreContent vsc:
                    message.Parts.Add(new OtelGenericPart { Type = "vector_store", Content = vsc.VectorStoreId });
                    break;

                case ErrorContent ec:
                    message.Parts.Add(new OtelGenericPart { Type = "error", Content = ec.Message });
                    break;

                // MCP server tool content
                case McpServerToolCallContent mstcc:
                    message.Parts.Add(new OtelServerToolCallPart<OtelMcpToolCall>
                    {
                        Id = mstcc.CallId,
                        Name = mstcc.Name,
                        ServerToolCall = new OtelMcpToolCall
                        {
                            Arguments = mstcc.Arguments as IReadOnlyDictionary<string, object?> ?? mstcc.Arguments?.ToDictionary(k => k.Key, v => v.Value),
                            ServerName = mstcc.ServerName,
                        },
                    });
                    break;

                case McpServerToolResultContent mstrc:
                    message.Parts.Add(new OtelServerToolCallResponsePart<OtelMcpToolCallResponse>
                    {
                        Id = mstrc.CallId,
                        ServerToolCallResponse = new OtelMcpToolCallResponse
                        {
                            Output = mstrc.Outputs,
                        },
                    });
                    break;

                default:
                    // For unknown content types, try to serialize them
                    JsonElement element = default;
                    try
                    {
                        JsonTypeInfo? unknownContentTypeInfo = null;
                        if (_jsonSerializerOptions?.TryGetTypeInfo(content.GetType(), out JsonTypeInfo? ctsi) ?? false)
                        {
                            unknownContentTypeInfo = ctsi;
                        }
                        else if (AIJsonUtilities.DefaultOptions.TryGetTypeInfo(content.GetType(), out JsonTypeInfo? dtsi))
                        {
                            unknownContentTypeInfo = dtsi;
                        }

                        if (unknownContentTypeInfo is not null)
                        {
                            element = JsonSerializer.SerializeToElement(content, unknownContentTypeInfo);
                        }
                    }
                    catch
                    {
                        // Ignore serialization failures
                    }

                    if (element.ValueKind != JsonValueKind.Undefined)
                    {
                        message.Parts.Add(new OtelGenericPart
                        {
                            Type = content.GetType().Name,
                            Content = element,
                        });
                    }

                    break;
            }
        }

        return message.Parts.Count > 0 ? message : null;
    }

    /// <summary>Creates an activity for a realtime session request, or returns <see langword="null"/> if not enabled.</summary>
    private Activity? CreateAndConfigureActivity(RealtimeSessionOptions? options, bool streamingResponse = false)
    {
        Activity? activity = null;
        if (_activitySource.HasListeners())
        {
            string? modelId = options?.Model ?? _defaultModelId;

            activity = _activitySource.StartActivity(
                string.IsNullOrWhiteSpace(modelId) ? OpenTelemetryConsts.GenAI.RealtimeName : $"{OpenTelemetryConsts.GenAI.RealtimeName} {modelId}",
                ActivityKind.Client);

            if (activity is { IsAllDataRequested: true })
            {
                _ = activity
                    .AddTag(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.ChatName)
                    .AddTag(OpenTelemetryConsts.GenAI.Request.Model, modelId)
                    .AddTag(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

                if (streamingResponse)
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
                    // Standard GenAI attributes
                    if (options.MaxOutputTokens is int maxTokens)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.MaxTokens, maxTokens);
                    }

                    // Realtime-specific attributes
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Realtime.SessionKind, options.SessionKind.ToString());

                    if (!string.IsNullOrEmpty(options.Voice))
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Realtime.Voice, options.Voice);
                    }

                    if (options.OutputModalities is { Count: > 0 } modalities)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Realtime.OutputModalities, $"[{string.Join(", ", modalities.Select(m => $"\"{m}\""))}]");
                    }

                    if (EnableSensitiveData)
                    {
                        if (!string.IsNullOrWhiteSpace(options.Instructions))
                        {
                            _ = activity.AddTag(
                                OpenTelemetryConsts.GenAI.SystemInstructions,
                                JsonSerializer.Serialize(new object[1] { new OtelGenericPart { Content = options.Instructions } }, OtelContext.Default.IListObject));
                        }

                    }

                    if (options.Tools is { Count: > 0 })
                    {
                        _ = activity.AddTag(
                            OpenTelemetryConsts.GenAI.Tool.Definitions,
                            JsonSerializer.Serialize(options.Tools.Select(t => OtelFunction.Create(t, includeOptionalProperties: EnableSensitiveData)), OtelContext.Default.IEnumerableOtelFunction));
                    }
                }
            }
        }

        return activity;
    }

    /// <summary>Adds streaming response information to the activity.</summary>
    private void TraceStreamingResponse(
        Activity? activity,
        string? requestModelId,
        ResponseCreatedRealtimeServerMessage? response,
        Exception? error,
        Stopwatch? stopwatch,
        double? timeToFirstChunk = null)
    {
        if (_operationDurationHistogram.Enabled && stopwatch is not null)
        {
            TagList tags = default;
            AddMetricTags(ref tags, requestModelId, responseModelId: null);

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
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)inputTokens, tags);
            }

            if (usage.OutputTokenCount is long outputTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeOutput);
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)outputTokens, tags);
            }

            if (usage.InputAudioTokenCount is long inputAudioTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeInputAudio);
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)inputAudioTokens, tags);
            }

            if (usage.InputTextTokenCount is long inputTextTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeInputText);
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)inputTextTokens, tags);
            }

            if (usage.OutputAudioTokenCount is long outputAudioTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeOutputAudio);
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)outputAudioTokens, tags);
            }

            if (usage.OutputTextTokenCount is long outputTextTokens)
            {
                TagList tags = default;
                tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeOutputText);
                AddMetricTags(ref tags, requestModelId, responseModelId: null);
                _tokenUsageHistogram.Record((int)outputTextTokens, tags);
            }
        }

        OpenTelemetryLog.RecordOperationError(activity, _logger, error);

        if (response is not null && activity is not null)
        {
            // Log metadata first so standard tags take precedence if keys collide
            if (EnableSensitiveData && response.AdditionalProperties is { } metadata)
            {
                foreach (var prop in metadata)
                {
                    _ = activity.AddTag(prop.Key, prop.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(response.ResponseId))
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.Id, response.ResponseId);
            }

            if (timeToFirstChunk is double timeToFirstChunkValue)
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.TimeToFirstChunk, timeToFirstChunkValue);
            }

            if (!string.IsNullOrWhiteSpace(response.Status))
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.FinishReasons, $"[\"{response.Status}\"]");
            }

            if (response.Usage is { } responseUsage)
            {
                if (responseUsage.InputTokenCount is long inputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTokens, (int)inputTokens);
                }

                if (responseUsage.OutputTokenCount is long outputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputTokens, (int)outputTokens);
                }

                if (responseUsage.CachedInputTokenCount is long cachedInputTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.CacheReadInputTokens, (int)cachedInputTokens);
                }

                if (responseUsage.ReasoningTokenCount is long reasoningTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.ReasoningOutputTokens, (int)reasoningTokens);
                }

                if (responseUsage.InputAudioTokenCount is long inputAudioTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputAudioTokens, (int)inputAudioTokens);
                }

                if (responseUsage.InputTextTokenCount is long inputTextTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTextTokens, (int)inputTextTokens);
                }

                if (responseUsage.OutputAudioTokenCount is long outputAudioTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputAudioTokens, (int)outputAudioTokens);
                }

                if (responseUsage.OutputTextTokenCount is long outputTextTokens)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputTextTokens, (int)outputTextTokens);
                }
            }

            // Log error content if available
            if (response.Error is { } responseError)
            {
                _ = activity.AddTag(OpenTelemetryConsts.Error.Type, responseError.ErrorCode ?? "RealtimeError");
                _ = activity.SetStatus(ActivityStatusCode.Error, responseError.Message);
            }
        }
    }

    private void AddMetricTags(ref TagList tags, string? requestModelId, string? responseModelId)
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

        if (responseModelId is string responseModel)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Response.Model, responseModel);
        }
    }

    #region OTel Serialization Types

    // Realtime-specific OTel serialization POCOs.
    //
    // Types whose layout is shared 1:1 with OpenTelemetryChatClient live in
    // Common/OtelMessageParts.cs. The types below are either entirely realtime-specific or
    // contain realtime-specific fields. The shared JsonSerializerContext lives in Common/OtelContext.cs.

    #endregion
}

#pragma warning disable SA1402 // File may only contain a single type — realtime-specific OTel POCOs are co-located with the realtime session.

internal sealed class RealtimeOtelMessage
{
    public string? Role { get; set; }
    public List<object> Parts { get; set; } = [];
}

internal sealed class RealtimeOtelToolCallPart
{
    public string Type { get; set; } = "tool_call";
    public string? Id { get; set; }
    public string? Name { get; set; }
    public IDictionary<string, object?>? Arguments { get; set; }
}
