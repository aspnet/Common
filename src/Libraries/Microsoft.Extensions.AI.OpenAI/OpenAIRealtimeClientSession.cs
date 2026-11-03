// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using Sdk = OpenAI.Realtime;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
#pragma warning disable OPENAI002 // OpenAI Realtime API is experimental
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access
#pragma warning disable IL3050 // Members annotated with 'RequiresDynamicCodeAttribute' require dynamic access

namespace Microsoft.Extensions.AI;

/// <summary>Represents an <see cref="IRealtimeClientSession"/> for the OpenAI Realtime API over WebSocket.</summary>
[Experimental(DiagnosticIds.Experiments.AIRealTime, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class OpenAIRealtimeClientSession : IRealtimeClientSession
{
    /// <summary>The model to use for the session.</summary>
    private readonly string _model;

    /// <summary>Metadata about this session's provider and model, used for OpenTelemetry.</summary>
    private readonly ChatClientMetadata _metadata;

    /// <summary>Owned <see cref="Sdk.RealtimeClient"/> created from the (apiKey, model) constructor path.</summary>
    private Sdk.RealtimeClient? _ownedRealtimeClient;

    /// <summary>The SDK session client for communication with the Realtime API.</summary>
    private Sdk.RealtimeSessionClient? _sessionClient;

    /// <summary>Whether the session has been disposed (0 = false, 1 = true).</summary>
    private int _disposed;

    /// <inheritdoc />
    public RealtimeSessionOptions? Options { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="OpenAIRealtimeClientSession"/> class.</summary>
    /// <param name="apiKey">The API key used for authentication.</param>
    /// <param name="model">The model to use for the session.</param>
    public OpenAIRealtimeClientSession(string apiKey, string model)
    {
        _ownedRealtimeClient = new Sdk.RealtimeClient(Throw.IfNull(apiKey));
        _model = Throw.IfNull(model);
        _metadata = new("openai", defaultModelId: _model);
    }

    /// <summary>Initializes a new instance of the <see cref="OpenAIRealtimeClientSession"/> class from an already-connected session client.</summary>
    /// <param name="sessionClient">The connected SDK session client.</param>
    /// <param name="model">The model name for metadata.</param>
    internal OpenAIRealtimeClientSession(Sdk.RealtimeSessionClient sessionClient, string model)
    {
        _sessionClient = Throw.IfNull(sessionClient);
        _model = model ?? string.Empty;
        _metadata = new("openai", defaultModelId: _model);
    }

    /// <summary>Connects the WebSocket to the OpenAI Realtime API.</summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous connect operation.</returns>
    /// <exception cref="InvalidOperationException">The session was not created with an owned realtime client.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_ownedRealtimeClient is null)
        {
            Throw.InvalidOperationException("Cannot connect a session that was not created with an owned realtime client.");
        }

        _sessionClient = await _ownedRealtimeClient.StartConversationSessionAsync(
            _model, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateSessionAsync(RealtimeSessionOptions options, CancellationToken cancellationToken)
    {
        if (_sessionClient is not null)
        {
            // Allow callers to provide a pre-configured SDK-specific options instance.
            object? rawOptions = options.RawRepresentationFactory?.Invoke();

            if (rawOptions is Sdk.RealtimeTranscriptionSessionOptions rawTransOptions)
            {
                await _sessionClient.ConfigureTranscriptionSessionAsync(rawTransOptions, cancellationToken).ConfigureAwait(false);
            }
            else if (options.SessionKind == RealtimeSessionKind.Transcription)
            {
                var transOpts = BuildTranscriptionSessionOptions(options);
                await _sessionClient.ConfigureTranscriptionSessionAsync(transOpts, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var convOpts = BuildConversationSessionOptions(options, rawOptions as Sdk.RealtimeConversationSessionOptions);
                await _sessionClient.ConfigureConversationSessionAsync(convOpts, cancellationToken).ConfigureAwait(false);
            }
        }

        Options = options;
    }

    /// <inheritdoc />
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        if (_sessionClient is null)
        {
            Throw.InvalidOperationException("The session is not connected.");
        }

        switch (message)
        {
            case SessionUpdateRealtimeClientMessage sessionUpdate:
                await UpdateSessionAsync(sessionUpdate.Options, cancellationToken).ConfigureAwait(false);
                break;

            case CreateResponseRealtimeClientMessage responseCreate:
                await SendResponseCreateAsync(responseCreate, cancellationToken).ConfigureAwait(false);
                break;

            case CreateConversationItemRealtimeClientMessage itemCreate:
                await SendConversationItemCreateAsync(itemCreate, cancellationToken).ConfigureAwait(false);
                break;

            case InputAudioBufferAppendRealtimeClientMessage audioAppend:
                await SendInputAudioAppendAsync(audioAppend, cancellationToken).ConfigureAwait(false);
                break;

            case InputAudioBufferCommitRealtimeClientMessage:
                if (message.MessageId is not null)
                {
                    var cmd = new Sdk.RealtimeClientCommandInputAudioBufferCommit { EventId = message.MessageId };
                    await _sessionClient.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _sessionClient.CommitPendingAudioAsync(cancellationToken).ConfigureAwait(false);
                }

                break;

            default:
                await SendRawCommandAsync(message, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_sessionClient is null)
        {
            yield break;
        }

        await foreach (var update in _sessionClient.ReceiveUpdatesAsync(cancellationToken).ConfigureAwait(false))
        {
            var serverMessage = MapServerUpdate(update);
            if (serverMessage is not null)
            {
                yield return serverMessage;
            }
        }
    }

    /// <inheritdoc />
    object? IRealtimeClientSession.GetService(Type serviceType, object? serviceKey)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(ChatClientMetadata) ? _metadata :
            serviceType.IsInstanceOfType(this) ? this :
            _sessionClient is not null && serviceType.IsInstanceOfType(_sessionClient) ? _sessionClient :
            null;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        _sessionClient?.Dispose();
        return default;
    }

    #region Send Helpers (MEAI → SDK)

    private async Task SendResponseCreateAsync(CreateResponseRealtimeClientMessage responseCreate, CancellationToken cancellationToken)
    {
        var responseOptions = new Sdk.RealtimeResponseOptions();

        // Audio output options.
        if (responseCreate.OutputAudioOptions is not null || !string.IsNullOrEmpty(responseCreate.OutputVoice))
        {
            responseOptions.AudioOptions = new Sdk.RealtimeResponseAudioOptions();
            if (responseCreate.OutputAudioOptions is not null)
            {
                responseOptions.AudioOptions.OutputAudioOptions.AudioFormat = ToSdkAudioFormat(responseCreate.OutputAudioOptions);
            }

            if (!string.IsNullOrEmpty(responseCreate.OutputVoice))
            {
                responseOptions.AudioOptions.OutputAudioOptions.Voice = new Sdk.RealtimeVoice(responseCreate.OutputVoice);
            }
        }

        // Conversation mode.
        if (responseCreate.ExcludeFromConversation is bool excludeFromConversation)
        {
            responseOptions.DefaultConversationConfiguration = excludeFromConversation
                ? Sdk.RealtimeResponseDefaultConversationConfiguration.None
                : Sdk.RealtimeResponseDefaultConversationConfiguration.Auto;
        }

        // Input items.
        if (responseCreate.Items is { } items)
        {
            foreach (var item in items)
            {
                if (ToRealtimeItem(item) is Sdk.RealtimeItem sdkItem)
                {
                    responseOptions.InputItems.Add(sdkItem);
                }
            }
        }

        if (!string.IsNullOrEmpty(responseCreate.Instructions))
        {
            responseOptions.Instructions = responseCreate.Instructions;
        }

        if (responseCreate.MaxOutputTokens.HasValue)
        {
            responseOptions.MaxOutputTokenCount = responseCreate.MaxOutputTokens.Value;
        }

        if (responseCreate.AdditionalProperties is { Count: > 0 })
        {
            var metadata = new Dictionary<string, BinaryData>();
            foreach (var kvp in responseCreate.AdditionalProperties)
            {
                metadata[kvp.Key] = BinaryData.FromString(kvp.Value?.ToString() ?? string.Empty);
            }

            responseOptions.Metadata = metadata;
        }

        if (responseCreate.OutputModalities is not null)
        {
            foreach (var modality in responseCreate.OutputModalities)
            {
                responseOptions.OutputModalities.Add(new Sdk.RealtimeOutputModality(modality));
            }
        }

        if (responseCreate.ToolMode is { } toolMode)
        {
            responseOptions.ToolChoice = ToSdkToolChoice(toolMode);
        }

        if (responseCreate.Tools is not null)
        {
            foreach (var tool in responseCreate.Tools)
            {
                if (ToRealtimeTool(tool) is Sdk.RealtimeTool sdkTool)
                {
                    responseOptions.Tools.Add(sdkTool);
                }
            }
        }

        if (responseCreate.MessageId is not null)
        {
            var cmd = new Sdk.RealtimeClientCommandResponseCreate
            {
                ResponseOptions = responseOptions,
                EventId = responseCreate.MessageId,
            };
            await _sessionClient!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sessionClient!.StartResponseAsync(responseOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendConversationItemCreateAsync(CreateConversationItemRealtimeClientMessage itemCreate, CancellationToken cancellationToken)
    {
        if (itemCreate.Item is null)
        {
            return;
        }

        var sdkItem = ToRealtimeItem(itemCreate.Item);
        if (sdkItem is null)
        {
            return;
        }

        string? previousId = null;
        if (itemCreate.RawRepresentation is Sdk.RealtimeClientCommandConversationItemCreate rawCmd)
        {
            previousId = rawCmd.PreviousItemId;
        }

        if (itemCreate.MessageId is not null || previousId is not null)
        {
            var cmd = new Sdk.RealtimeClientCommandConversationItemCreate(sdkItem)
            {
                EventId = itemCreate.MessageId,
                PreviousItemId = previousId,
            };
            await _sessionClient!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sessionClient!.AddItemAsync(sdkItem, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendInputAudioAppendAsync(InputAudioBufferAppendRealtimeClientMessage audioAppend, CancellationToken cancellationToken)
    {
        if (audioAppend.Content is null || !audioAppend.Content.HasTopLevelMediaType("audio"))
        {
            return;
        }

        BinaryData audioData = ExtractAudioBinaryData(audioAppend.Content);

        if (audioAppend.MessageId is not null)
        {
            var cmd = new Sdk.RealtimeClientCommandInputAudioBufferAppend(audioData) { EventId = audioAppend.MessageId };
            await _sessionClient!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sessionClient!.SendInputAudioAsync(audioData, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendRawCommandAsync(RealtimeClientMessage message, CancellationToken cancellationToken)
    {
        if (message.RawRepresentation is Sdk.RealtimeClientCommand sdkCmd)
        {
            await _sessionClient!.SendCommandAsync(sdkCmd, cancellationToken).ConfigureAwait(false);
            return;
        }

        string? jsonString = message.RawRepresentation switch
        {
            string s => s,
            JsonObject obj => obj.ToJsonString(),
            _ => null,
        };

        if (jsonString is not null)
        {
            // Inject event_id if the message has one but the raw JSON does not.
            if (message.MessageId is not null && !jsonString.Contains("\"event_id\"", StringComparison.Ordinal))
            {
                jsonString = jsonString.Insert(1, $"\"event_id\":{JsonSerializer.Serialize(message.MessageId, OpenAIJsonContext.Default.String)},");
            }

            await _sessionClient!.SendCommandAsync(BinaryData.FromString(jsonString), null).ConfigureAwait(false);
        }
    }

    private static Sdk.RealtimeConversationSessionOptions BuildConversationSessionOptions(RealtimeSessionOptions options, Sdk.RealtimeConversationSessionOptions? seedOptions = null)
    {
        var convOptions = seedOptions ?? new Sdk.RealtimeConversationSessionOptions();

        // Audio configuration. Reuse any existing options from the seed to preserve provider-specific settings.
        var audioOptions = convOptions.AudioOptions ?? new Sdk.RealtimeConversationSessionAudioOptions();
        var inputAudioOptions = audioOptions.InputAudioOptions ?? new Sdk.RealtimeConversationSessionInputAudioOptions();
        var outputAudioOptions = audioOptions.OutputAudioOptions ?? new Sdk.RealtimeConversationSessionOutputAudioOptions();

        if (options.InputAudioFormat is not null)
        {
            inputAudioOptions.AudioFormat = ToSdkAudioFormat(options.InputAudioFormat);
        }

        if (options.TranscriptionOptions is not null)
        {
            inputAudioOptions.AudioTranscriptionOptions = new Sdk.RealtimeAudioTranscriptionOptions
            {
                Language = options.TranscriptionOptions.SpeechLanguage,
                Model = options.TranscriptionOptions.ModelId,
                Prompt = options.TranscriptionOptions.Prompt,
            };
        }

        if (options.OutputAudioFormat is not null)
        {
            outputAudioOptions.AudioFormat = ToSdkAudioFormat(options.OutputAudioFormat);
        }

        if (options.Voice is not null)
        {
            outputAudioOptions.Voice = new Sdk.RealtimeVoice(options.Voice);
        }

        if (options.VoiceActivityDetection is { } vad)
        {
            if (!vad.Enabled)
            {
                inputAudioOptions.DisableTurnDetection();
            }
            else if (inputAudioOptions.TurnDetection is Sdk.RealtimeServerVadTurnDetection existingVad)
            {
                existingVad.InterruptResponseEnabled = vad.AllowInterruption;
            }
            else
            {
                inputAudioOptions.TurnDetection = new Sdk.RealtimeServerVadTurnDetection
                {
                    InterruptResponseEnabled = vad.AllowInterruption,
                    CreateResponseEnabled = true,
                };
            }
        }

        audioOptions.InputAudioOptions = inputAudioOptions;
        audioOptions.OutputAudioOptions = outputAudioOptions;
        convOptions.AudioOptions = audioOptions;

        if (options.Instructions is not null)
        {
            convOptions.Instructions = options.Instructions;
        }

        if (options.MaxOutputTokens.HasValue)
        {
            convOptions.MaxOutputTokenCount = options.MaxOutputTokens.Value;
        }

        if (options.Model is not null)
        {
            convOptions.Model = options.Model;
        }

        if (options.OutputModalities is not null)
        {
            foreach (var modality in options.OutputModalities)
            {
                convOptions.OutputModalities.Add(new Sdk.RealtimeOutputModality(modality));
            }
        }

        if (options.ToolMode is { } toolMode)
        {
            convOptions.ToolChoice = ToSdkToolChoice(toolMode);
        }

        if (options.Tools is not null)
        {
            foreach (var tool in options.Tools)
            {
                if (ToRealtimeTool(tool) is Sdk.RealtimeTool sdkTool)
                {
                    convOptions.Tools.Add(sdkTool);
                }
            }
        }

        return convOptions;
    }

    private static Sdk.RealtimeTranscriptionSessionOptions BuildTranscriptionSessionOptions(RealtimeSessionOptions options)
    {
        var transOptions = new Sdk.RealtimeTranscriptionSessionOptions();

        if (options.InputAudioFormat is not null || options.TranscriptionOptions is not null || options.VoiceActivityDetection is not null)
        {
            var inputAudioOptions = new Sdk.RealtimeTranscriptionSessionInputAudioOptions();

            if (options.InputAudioFormat is not null)
            {
                inputAudioOptions.AudioFormat = ToSdkAudioFormat(options.InputAudioFormat);
            }

            if (options.TranscriptionOptions is not null)
            {
                inputAudioOptions.AudioTranscriptionOptions = new Sdk.RealtimeAudioTranscriptionOptions
                {
                    Language = options.TranscriptionOptions.SpeechLanguage,
                    Model = options.TranscriptionOptions.ModelId,
                    Prompt = options.TranscriptionOptions.Prompt,
                };
            }

            if (options.VoiceActivityDetection is { } vad)
            {
                if (!vad.Enabled)
                {
                    inputAudioOptions.DisableTurnDetection();
                }
                else if (inputAudioOptions.TurnDetection is Sdk.RealtimeServerVadTurnDetection existingVad)
                {
                    existingVad.InterruptResponseEnabled = vad.AllowInterruption;
                }
                else
                {
                    inputAudioOptions.TurnDetection = new Sdk.RealtimeServerVadTurnDetection
                    {
                        InterruptResponseEnabled = vad.AllowInterruption,
                        CreateResponseEnabled = true,
                    };
                }
            }

            transOptions.AudioOptions = new Sdk.RealtimeTranscriptionSessionAudioOptions
            {
                InputAudioOptions = inputAudioOptions,
            };
        }

        return transOptions;
    }

    private static Sdk.RealtimeTool? ToRealtimeTool(AITool tool)
    {
        if (tool is AIFunction aiFunction && !string.IsNullOrEmpty(aiFunction.Name))
        {
            return OpenAIRealtimeConversationClient.ToOpenAIRealtimeFunctionTool(aiFunction);
        }

        if (tool is HostedMcpServerTool mcpTool)
        {
            return ToRealtimeMcpTool(mcpTool);
        }

        return null;
    }

    private static Sdk.RealtimeMcpTool ToRealtimeMcpTool(HostedMcpServerTool mcpTool)
    {
        Sdk.RealtimeMcpTool sdkTool;

        if (Uri.TryCreate(mcpTool.ServerAddress, UriKind.Absolute, out var uri))
        {
            sdkTool = new Sdk.RealtimeMcpTool(mcpTool.ServerName, uri);

            if (mcpTool.Headers is { } headers)
            {
                var sdkHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in headers)
                {
                    sdkHeaders[kvp.Key] = kvp.Value;
                }

                sdkTool.Headers = sdkHeaders;
            }
        }
        else
        {
            sdkTool = new Sdk.RealtimeMcpTool(mcpTool.ServerName, new Sdk.RealtimeMcpToolConnectorId(mcpTool.ServerAddress));
        }

        if (mcpTool.ServerDescription is not null)
        {
            sdkTool.ServerDescription = mcpTool.ServerDescription;
        }

        if (mcpTool.AllowedTools is { Count: > 0 })
        {
            sdkTool.AllowedTools = new Sdk.RealtimeMcpToolFilter();
            foreach (var toolName in mcpTool.AllowedTools)
            {
                sdkTool.AllowedTools.ToolNames.Add(toolName);
            }
        }

        if (mcpTool.ApprovalMode is not null)
        {
            sdkTool.ToolCallApprovalPolicy = mcpTool.ApprovalMode switch
            {
                HostedMcpServerToolAlwaysRequireApprovalMode => Sdk.RealtimeDefaultMcpToolCallApprovalPolicy.AlwaysRequireApproval,
                HostedMcpServerToolNeverRequireApprovalMode => Sdk.RealtimeDefaultMcpToolCallApprovalPolicy.NeverRequireApproval,
                HostedMcpServerToolRequireSpecificApprovalMode specific => ToSdkCustomApprovalPolicy(specific),
                _ => Sdk.RealtimeDefaultMcpToolCallApprovalPolicy.AlwaysRequireApproval,
            };
        }

        return sdkTool;
    }

    private static Sdk.RealtimeMcpToolCallApprovalPolicy ToSdkCustomApprovalPolicy(HostedMcpServerToolRequireSpecificApprovalMode mode)
    {
        var custom = new Sdk.RealtimeCustomMcpToolCallApprovalPolicy();

        if (mode.AlwaysRequireApprovalToolNames is { Count: > 0 })
        {
            custom.ToolsAlwaysRequiringApproval = new Sdk.RealtimeMcpToolFilter();
            foreach (var name in mode.AlwaysRequireApprovalToolNames)
            {
                custom.ToolsAlwaysRequiringApproval.ToolNames.Add(name);
            }
        }

        if (mode.NeverRequireApprovalToolNames is { Count: > 0 })
        {
            custom.ToolsNeverRequiringApproval = new Sdk.RealtimeMcpToolFilter();
            foreach (var name in mode.NeverRequireApprovalToolNames)
            {
                custom.ToolsNeverRequiringApproval.ToolNames.Add(name);
            }
        }

        return custom;
    }

    private static Sdk.RealtimeItem? ToRealtimeItem(RealtimeConversationItem? contentItem)
    {
        if (contentItem?.Contents is null or { Count: 0 })
        {
            return null;
        }

        var firstContent = contentItem.Contents[0];

        if (firstContent is FunctionResultContent functionResult)
        {
            string resultJson = functionResult.Result is not null
                ? JsonSerializer.Serialize(functionResult.Result, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)))
                : string.Empty;
            return Sdk.RealtimeItem.CreateFunctionCallOutputItem(
                functionResult.CallId ?? string.Empty,
                resultJson);
        }

        if (firstContent is FunctionCallContent functionCall)
        {
            var arguments = functionCall.Arguments is not null
                ? BinaryData.FromString(JsonSerializer.Serialize(functionCall.Arguments, OpenAIJsonContext.Default.IDictionaryStringObject))
                : BinaryData.FromString("{}");
            return Sdk.RealtimeItem.CreateFunctionCallItem(
                functionCall.CallId ?? string.Empty,
                functionCall.Name,
                arguments);
        }

        if (firstContent is ToolApprovalResponseContent approvalResponse)
        {
            return Sdk.RealtimeItem.CreateMcpApprovalResponseItem(
                approvalResponse.RequestId ?? string.Empty,
                approvalResponse.Approved);
        }

        // Message item with content parts.
        var contentParts = new List<Sdk.RealtimeMessageContentPart>();
        foreach (var content in contentItem.Contents)
        {
            if (content is TextContent textContent)
            {
                contentParts.Add(new Sdk.RealtimeInputTextMessageContentPart(textContent.Text ?? string.Empty));
            }
            else if (content is DataContent dataContent)
            {
                if (dataContent.MediaType?.StartsWith("audio/", StringComparison.Ordinal) == true)
                {
                    contentParts.Add(new Sdk.RealtimeInputAudioMessageContentPart(
                        BinaryData.FromBytes(dataContent.Data.ToArray())));
                }
                else if (dataContent.MediaType?.StartsWith("image/", StringComparison.Ordinal) == true && dataContent.Uri is not null)
                {
                    contentParts.Add(new Sdk.RealtimeInputImageMessageContentPart(new Uri(dataContent.Uri)));
                }
            }
        }

        if (contentParts.Count == 0)
        {
            return null;
        }

        var role = contentItem.Role?.Value switch
        {
            "assistant" => Sdk.RealtimeMessageRole.Assistant,
            "system" => Sdk.RealtimeMessageRole.System,
            _ => Sdk.RealtimeMessageRole.User,
        };

        var messageItem = new Sdk.RealtimeMessageItem(role, contentParts);
        if (contentItem.Id is not null)
        {
            messageItem.Id = contentItem.Id;
        }

        return messageItem;
    }

    private static Sdk.RealtimeToolChoice ToSdkToolChoice(ChatToolMode toolMode) => toolMode switch
    {
        RequiredChatToolMode r when r.RequiredFunctionName is not null =>
            new Sdk.RealtimeToolChoice(new Sdk.RealtimeCustomFunctionToolChoice(r.RequiredFunctionName)),
        RequiredChatToolMode => Sdk.RealtimeDefaultToolChoice.Required,
        NoneChatToolMode => Sdk.RealtimeDefaultToolChoice.None,
        _ => Sdk.RealtimeDefaultToolChoice.Auto,
    };

    private static Sdk.RealtimeAudioFormat? ToSdkAudioFormat(RealtimeAudioFormat? format)
    {
        if (format is null)
        {
            return null;
        }

        return format.MediaType switch
        {
            "audio/pcm" => new Sdk.RealtimePcmAudioFormat(),
            "audio/pcmu" => new Sdk.RealtimePcmuAudioFormat(),
            "audio/pcma" => new Sdk.RealtimePcmaAudioFormat(),
            _ => null,
        };
    }

    private static BinaryData ExtractAudioBinaryData(DataContent content)
    {
        string dataUri = content.Uri?.ToString() ?? string.Empty;
        int commaIndex = dataUri.LastIndexOf(",", StringComparison.Ordinal);

        if (commaIndex >= 0 && commaIndex < dataUri.Length - 1)
        {
            string base64 = dataUri.Substring(commaIndex + 1);
            return BinaryData.FromBytes(Convert.FromBase64String(base64));
        }

        return BinaryData.FromBytes(content.Data.ToArray());
    }

    #endregion

    #region Receive Helpers (SDK → MEAI)

    private RealtimeServerMessage? MapServerUpdate(Sdk.RealtimeServerUpdate update) => update switch
    {
        Sdk.RealtimeServerUpdateError e => MapError(e),
        Sdk.RealtimeServerUpdateSessionCreated e => HandleSessionEvent(e.Session, e),
        Sdk.RealtimeServerUpdateSessionUpdated e => HandleSessionEvent(e.Session, e),
        Sdk.RealtimeServerUpdateResponseCreated e => MapResponseCreatedOrDone(e.EventId, e.Response, RealtimeServerMessageType.ResponseCreated, e),
        Sdk.RealtimeServerUpdateResponseDone e => MapResponseCreatedOrDone(e.EventId, e.Response, RealtimeServerMessageType.ResponseDone, e),
        Sdk.RealtimeServerUpdateResponseOutputItemAdded e => MapResponseOutputItem(e.EventId, e.ResponseId, e.OutputIndex, e.Item, RealtimeServerMessageType.ResponseOutputItemAdded, e),
        Sdk.RealtimeServerUpdateResponseOutputItemDone e => MapResponseOutputItem(e.EventId, e.ResponseId, e.OutputIndex, e.Item, RealtimeServerMessageType.ResponseOutputItemDone, e),
        Sdk.RealtimeServerUpdateResponseOutputAudioDelta e => new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
        {
            MessageId = e.EventId,
            ResponseId = e.ResponseId,
            ItemId = e.ItemId,
            OutputIndex = e.OutputIndex,
            ContentIndex = e.ContentIndex,
            Audio = e.Delta is not null ? Convert.ToBase64String(e.Delta.ToArray()) : null,
            RawRepresentation = e,
        },
        Sdk.RealtimeServerUpdateResponseOutputAudioDone e => new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDone)
        {
            MessageId = e.EventId,
            ResponseId = e.ResponseId,
            ItemId = e.ItemId,
            OutputIndex = e.OutputIndex,
            ContentIndex = e.ContentIndex,
            RawRepresentation = e,
        },
        Sdk.RealtimeServerUpdateResponseOutputAudioTranscriptDelta e => new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            MessageId = e.EventId,
            ResponseId = e.ResponseId,
            ItemId = e.ItemId,
            OutputIndex = e.OutputIndex,
            ContentIndex = e.ContentIndex,
            Text = e.Delta,
            RawRepresentation = e,
        },
        Sdk.RealtimeServerUpdateResponseOutputAudioTranscriptDone e => new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            MessageId = e.EventId,
            ResponseId = e.ResponseId,
            ItemId = e.ItemId,
            OutputIndex = e.OutputIndex,
            ContentIndex = e.ContentIndex,
            Text = e.Transcript,
            RawRepresentation = e,
        },
        Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionDelta e => MapInputTranscriptionDelta(e),
        Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionCompleted e => MapInputTranscriptionCompleted(e),
        Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionFailed e => MapInputTranscriptionFailed(e),
        Sdk.RealtimeServerUpdateConversationItemAdded e => MapConversationItem(e.EventId, e.Item, RealtimeServerMessageType.ConversationItemAdded, e),
        Sdk.RealtimeServerUpdateConversationItemDone e => MapConversationItem(e.EventId, e.Item, RealtimeServerMessageType.ConversationItemDone, e),
        Sdk.RealtimeServerUpdateResponseMcpCallInProgress e => MapMcpCallEvent(e.EventId, e.ItemId, e.OutputIndex, new RealtimeServerMessageType("McpCallInProgress"), e),
        Sdk.RealtimeServerUpdateResponseMcpCallCompleted e => MapMcpCallEvent(e.EventId, e.ItemId, e.OutputIndex, new RealtimeServerMessageType("McpCallCompleted"), e),
        Sdk.RealtimeServerUpdateResponseMcpCallFailed e => MapMcpCallEvent(e.EventId, e.ItemId, e.OutputIndex, new RealtimeServerMessageType("McpCallFailed"), e),
        Sdk.RealtimeServerUpdateMcpListToolsInProgress e => MapMcpListToolsEvent(e.EventId, e.ItemId, new RealtimeServerMessageType("McpListToolsInProgress"), e),
        Sdk.RealtimeServerUpdateMcpListToolsCompleted e => MapMcpListToolsEvent(e.EventId, e.ItemId, new RealtimeServerMessageType("McpListToolsCompleted"), e),
        Sdk.RealtimeServerUpdateMcpListToolsFailed e => MapMcpListToolsEvent(e.EventId, e.ItemId, new RealtimeServerMessageType("McpListToolsFailed"), e),
        _ => new RealtimeServerMessage
        {
            Type = RealtimeServerMessageType.RawContentOnly,
            RawRepresentation = update,
        },
    };

    private static ErrorRealtimeServerMessage MapError(Sdk.RealtimeServerUpdateError e)
    {
        var msg = new ErrorRealtimeServerMessage
        {
            MessageId = e.EventId,
            Error = new ErrorContent(e.Error?.Message),
            RawRepresentation = e,
        };

        if (e.Error?.Code is not null)
        {
            msg.Error.ErrorCode = e.Error.Code;
        }

        if (e.Error?.ParameterName is not null)
        {
            msg.Error.Details = e.Error.ParameterName;
        }

        return msg;
    }

    private RealtimeServerMessage HandleSessionEvent(Sdk.RealtimeSession? session, Sdk.RealtimeServerUpdate update)
    {
        if (session is Sdk.RealtimeConversationSession convSession)
        {
            Options = MapConversationSessionToOptions(convSession);
        }

        return new RealtimeServerMessage
        {
            Type = RealtimeServerMessageType.RawContentOnly,
            RawRepresentation = update,
        };
    }

    private RealtimeSessionOptions MapConversationSessionToOptions(Sdk.RealtimeConversationSession session)
    {
        RealtimeAudioFormat? inputAudioFormat = null;
        TranscriptionOptions? transcription = null;
        RealtimeAudioFormat? outputAudioFormat = null;
        string? voice = null;

        if (session.AudioOptions is { } audioOptions)
        {
            if (audioOptions.InputAudioOptions is { } inputOpts)
            {
                inputAudioFormat = MapSdkAudioFormat(inputOpts.AudioFormat);

                if (inputOpts.AudioTranscriptionOptions is { } transcriptionOpts)
                {
                    transcription = new TranscriptionOptions
                    {
                        SpeechLanguage = transcriptionOpts.Language,
                        ModelId = transcriptionOpts.Model,
                        Prompt = transcriptionOpts.Prompt,
                    };
                }
            }

            if (audioOptions.OutputAudioOptions is { } outputOpts)
            {
                outputAudioFormat = MapSdkAudioFormat(outputOpts.AudioFormat);

                if (outputOpts.Voice.HasValue)
                {
                    voice = outputOpts.Voice.Value.ToString();
                }
            }
        }

        int? maxOutputTokens = null;
        if (session.MaxOutputTokenCount is { } maxTokens)
        {
            maxOutputTokens = maxTokens.CustomMaxOutputTokenCount ?? int.MaxValue;
        }

        List<string>? outputModalities = null;
        if (session.OutputModalities is { Count: > 0 } modalities)
        {
            outputModalities = modalities.Select(m => m.ToString()).ToList();
        }

        return new RealtimeSessionOptions
        {
            SessionKind = RealtimeSessionKind.Conversation,
            Model = session.Model,
            Instructions = session.Instructions,
            MaxOutputTokens = maxOutputTokens,
            OutputModalities = outputModalities,
            InputAudioFormat = inputAudioFormat,
            TranscriptionOptions = transcription,
            OutputAudioFormat = outputAudioFormat,
            Voice = voice,

            // Preserve client-side properties that the server cannot round-trip.
            Tools = Options?.Tools,
            ToolMode = Options?.ToolMode,
        };
    }

    private static ResponseCreatedRealtimeServerMessage MapResponseCreatedOrDone(
        string? eventId, Sdk.RealtimeResponse? response, RealtimeServerMessageType type, Sdk.RealtimeServerUpdate update)
    {
        var msg = new ResponseCreatedRealtimeServerMessage(type)
        {
            MessageId = eventId,
            RawRepresentation = update,
        };

        if (response is null)
        {
            return msg;
        }

        msg.ResponseId = response.Id;
        msg.Status = response.Status?.ToString();

        if (response.AudioOptions?.OutputAudioOptions is { } audioOut)
        {
            msg.OutputAudioOptions = MapSdkAudioFormat(audioOut.AudioFormat);
            if (audioOut.Voice.HasValue)
            {
                msg.OutputVoice = audioOut.Voice.Value.ToString();
            }
        }

        if (response.MaxOutputTokenCount is { } maxTokens)
        {
            msg.MaxOutputTokens = maxTokens.CustomMaxOutputTokenCount ?? int.MaxValue;
        }

        if (response.Metadata is { Count: > 0 } metadata)
        {
            var dict = new AdditionalPropertiesDictionary();
            foreach (var kvp in metadata)
            {
                dict[kvp.Key] = kvp.Value;
            }

            msg.AdditionalProperties = dict;
        }

        if (response.OutputModalities is { Count: > 0 } modalities)
        {
            msg.OutputModalities = modalities.Select(m => m.ToString()).ToList();
        }

        if (response.StatusDetails?.Error is { } error)
        {
            msg.Error = new ErrorContent(error.Kind)
            {
                ErrorCode = error.Code,
            };
        }

        if (response.Usage is { } usage)
        {
            msg.Usage = MapUsageDetails(usage);
        }

        if (response.OutputItems is { Count: > 0 } outputItems)
        {
            var items = new List<RealtimeConversationItem>();
            foreach (var item in outputItems)
            {
                if (MapRealtimeItem(item) is RealtimeConversationItem contentItem)
                {
                    items.Add(contentItem);
                }
            }

            msg.Items = items;
        }

        return msg;
    }

    private static ResponseOutputItemRealtimeServerMessage MapResponseOutputItem(
        string? eventId, string? responseId, int outputIndex, Sdk.RealtimeItem? item,
        RealtimeServerMessageType type, Sdk.RealtimeServerUpdate update)
    {
        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            MessageId = eventId,
            ResponseId = responseId,
            OutputIndex = outputIndex,
            Item = item is not null ? MapRealtimeItem(item) : null,
            RawRepresentation = update,
        };
    }

    private static ResponseOutputItemRealtimeServerMessage MapConversationItem(
        string? eventId, Sdk.RealtimeItem? item, RealtimeServerMessageType type, Sdk.RealtimeServerUpdate update)
    {
        var mapped = item is not null ? MapRealtimeItem(item) : null;
        if (mapped is null)
        {
            return new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.RawContentOnly)
            {
                MessageId = eventId,
                RawRepresentation = update,
            };
        }

        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            MessageId = eventId,
            Item = mapped,
            RawRepresentation = update,
        };
    }

    private static InputAudioTranscriptionRealtimeServerMessage MapInputTranscriptionDelta(Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionDelta e)
    {
        return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            MessageId = e.EventId,
            ItemId = e.ItemId,
            ContentIndex = e.ContentIndex,
            Transcription = e.Delta,
            RawRepresentation = e,
        };
    }

    private static InputAudioTranscriptionRealtimeServerMessage MapInputTranscriptionCompleted(Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionCompleted e)
    {
        return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            MessageId = e.EventId,
            ItemId = e.ItemId,
            ContentIndex = e.ContentIndex,
            Transcription = e.Transcript,
            RawRepresentation = e,
        };
    }

    private static InputAudioTranscriptionRealtimeServerMessage MapInputTranscriptionFailed(Sdk.RealtimeServerUpdateConversationItemInputAudioTranscriptionFailed e)
    {
        var msg = new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionFailed)
        {
            MessageId = e.EventId,
            ItemId = e.ItemId,
            ContentIndex = e.ContentIndex,
            RawRepresentation = e,
        };

        if (e.Error is not null)
        {
            msg.Error = new ErrorContent(e.Error.Message)
            {
                ErrorCode = e.Error.Code,
                Details = e.Error.ParameterName,
            };
        }

        return msg;
    }

    private static ResponseOutputItemRealtimeServerMessage MapMcpCallEvent(
        string? eventId, string? itemId, int outputIndex, RealtimeServerMessageType type, Sdk.RealtimeServerUpdate update)
    {
        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            MessageId = eventId,
            Item = itemId is not null ? new RealtimeConversationItem([], itemId) : null,
            OutputIndex = outputIndex,
            RawRepresentation = update,
        };
    }

    private static ResponseOutputItemRealtimeServerMessage MapMcpListToolsEvent(
        string? eventId, string? itemId, RealtimeServerMessageType type, Sdk.RealtimeServerUpdate update)
    {
        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            MessageId = eventId,
            Item = itemId is not null ? new RealtimeConversationItem([], itemId) : null,
            RawRepresentation = update,
        };
    }

    private static RealtimeConversationItem? MapRealtimeItem(Sdk.RealtimeItem item) => item switch
    {
        Sdk.RealtimeMessageItem messageItem => MapMessageItem(messageItem),
        Sdk.RealtimeFunctionCallItem funcCallItem => MapFunctionCallItem(funcCallItem),
        Sdk.RealtimeFunctionCallOutputItem funcOutputItem => new RealtimeConversationItem(
            [new FunctionResultContent(funcOutputItem.CallId ?? string.Empty, funcOutputItem.FunctionOutput)],
            funcOutputItem.Id),
        Sdk.RealtimeMcpToolCallItem mcpItem => MapMcpToolCallItem(mcpItem),
        Sdk.RealtimeMcpToolCallApprovalRequestItem approvalItem => MapMcpApprovalRequestItem(approvalItem),
        Sdk.RealtimeMcpToolDefinitionListItem toolListItem => MapMcpToolDefinitionListItem(toolListItem),
        _ => null,
    };

    private static RealtimeConversationItem MapFunctionCallItem(Sdk.RealtimeFunctionCallItem funcCallItem)
    {
        var arguments = funcCallItem.FunctionArguments is not null && !funcCallItem.FunctionArguments.IsEmpty
            ? JsonSerializer.Deserialize(funcCallItem.FunctionArguments, OpenAIJsonContext.Default.IDictionaryStringObject)
            : null;
        return new RealtimeConversationItem(
            [new FunctionCallContent(funcCallItem.CallId ?? string.Empty, funcCallItem.FunctionName, arguments)],
            funcCallItem.Id);
    }

    private static RealtimeConversationItem MapMessageItem(Sdk.RealtimeMessageItem messageItem)
    {
        var contents = new List<AIContent>();
        if (messageItem.Content is not null)
        {
            foreach (var part in messageItem.Content)
            {
                if (part is Sdk.RealtimeInputTextMessageContentPart textPart)
                {
                    contents.Add(new TextContent(textPart.Text));
                }
                else if (part is Sdk.RealtimeOutputTextMessageContentPart outputTextPart)
                {
                    contents.Add(new TextContent(outputTextPart.Text));
                }
                else if (part is Sdk.RealtimeInputAudioMessageContentPart audioPart)
                {
                    if (audioPart.AudioBytes is not null)
                    {
                        contents.Add(new DataContent($"data:audio/pcm;base64,{Convert.ToBase64String(audioPart.AudioBytes.ToArray())}"));
                    }
                }
                else if (part is Sdk.RealtimeOutputAudioMessageContentPart outputAudioPart)
                {
                    if (outputAudioPart.Transcript is not null)
                    {
                        contents.Add(new TextContent(outputAudioPart.Transcript));
                    }

                    if (outputAudioPart.AudioBytes is not null)
                    {
                        contents.Add(new DataContent($"data:audio/pcm;base64,{Convert.ToBase64String(outputAudioPart.AudioBytes.ToArray())}"));
                    }
                }
                else if (part is Sdk.RealtimeInputImageMessageContentPart imagePart && imagePart.ImageUri is not null)
                {
                    contents.Add(new DataContent(imagePart.ImageUri.ToString()));
                }
            }
        }

        ChatRole? role = messageItem.Role == Sdk.RealtimeMessageRole.Assistant ? ChatRole.Assistant
            : messageItem.Role == Sdk.RealtimeMessageRole.User ? ChatRole.User
            : messageItem.Role == Sdk.RealtimeMessageRole.System ? ChatRole.System
            : null;

        return new RealtimeConversationItem(contents, messageItem.Id, role);
    }

    private static RealtimeConversationItem MapMcpToolCallItem(Sdk.RealtimeMcpToolCallItem mcpItem)
    {
        string callId = mcpItem.Id ?? string.Empty;

        IDictionary<string, object?>? arguments = null;
        if (mcpItem.ToolArguments is not null)
        {
            string argsJson = mcpItem.ToolArguments.ToString();
            if (!string.IsNullOrEmpty(argsJson))
            {
                arguments = JsonSerializer.Deserialize(argsJson, OpenAIJsonContext.Default.IDictionaryStringObject);
            }
        }

        var contents = new List<AIContent>
        {
            new McpServerToolCallContent(callId, mcpItem.ToolName ?? string.Empty, mcpItem.ServerLabel)
            {
                Arguments = arguments,
            },
        };

        // Parse output/error into result content.
        if (mcpItem.ToolOutput is not null || mcpItem.Error is not null)
        {
            AIContent resultContent = mcpItem.Error is not null
                ? new ErrorContent(mcpItem.Error.Message)
                : new TextContent(mcpItem.ToolOutput);

            contents.Add(new McpServerToolResultContent(callId)
            {
                Outputs = [resultContent],
                RawRepresentation = mcpItem,
            });
        }

        return new RealtimeConversationItem(contents, mcpItem.Id);
    }

    private static RealtimeConversationItem MapMcpApprovalRequestItem(Sdk.RealtimeMcpToolCallApprovalRequestItem approvalItem)
    {
        string approvalId = approvalItem.Id ?? string.Empty;

        IDictionary<string, object?>? arguments = null;
        if (approvalItem.ToolArguments is not null)
        {
            string argsJson = approvalItem.ToolArguments.ToString();
            if (!string.IsNullOrEmpty(argsJson))
            {
                arguments = JsonSerializer.Deserialize(argsJson, OpenAIJsonContext.Default.IDictionaryStringObject);
            }
        }

        var toolCall = new McpServerToolCallContent(approvalId, approvalItem.ToolName ?? string.Empty, approvalItem.ServerLabel)
        {
            Arguments = arguments,
            RawRepresentation = approvalItem,
        };

        return new RealtimeConversationItem(
            [new ToolApprovalRequestContent(approvalId, toolCall) { RawRepresentation = approvalItem }],
            approvalItem.Id);
    }

    private static RealtimeConversationItem MapMcpToolDefinitionListItem(Sdk.RealtimeMcpToolDefinitionListItem toolListItem)
    {
        var contents = new List<AIContent>();
        foreach (var toolDef in toolListItem.ToolDefinitions)
        {
            if (toolDef.Name is not null)
            {
                contents.Add(new McpServerToolCallContent(toolDef.Name, toolDef.Name, toolListItem.ServerLabel)
                {
                    RawRepresentation = toolDef,
                });
            }
        }

        return new RealtimeConversationItem(contents, toolListItem.Id);
    }

    private static UsageDetails? MapUsageDetails(Sdk.RealtimeResponseUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        var details = new UsageDetails
        {
            InputTokenCount = usage.InputTokenCount ?? 0,
            OutputTokenCount = usage.OutputTokenCount ?? 0,
            TotalTokenCount = usage.TotalTokenCount ?? 0,
        };

        if (usage.InputTokenDetails is { } inputDetails)
        {
            details.InputAudioTokenCount = inputDetails.AudioTokenCount ?? 0;
            details.InputTextTokenCount = inputDetails.TextTokenCount ?? 0;
        }

        if (usage.OutputTokenDetails is { } outputDetails)
        {
            details.OutputAudioTokenCount = outputDetails.AudioTokenCount ?? 0;
            details.OutputTextTokenCount = outputDetails.TextTokenCount ?? 0;
        }

        return details;
    }

    private static RealtimeAudioFormat? MapSdkAudioFormat(Sdk.RealtimeAudioFormat? format) => format switch
    {
        Sdk.RealtimePcmAudioFormat pcm => new RealtimeAudioFormat("audio/pcm", pcm.Rate),
        Sdk.RealtimePcmuAudioFormat => new RealtimeAudioFormat("audio/pcmu", 8000),
        Sdk.RealtimePcmaAudioFormat => new RealtimeAudioFormat("audio/pcma", 8000),
        _ => null,
    };

    #endregion
}
