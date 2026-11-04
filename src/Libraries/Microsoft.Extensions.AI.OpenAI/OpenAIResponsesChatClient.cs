// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable S1226 // Method parameters, caught exceptions and foreach variables' initial values should not be ignored
#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
#pragma warning disable S3254 // Default parameter values should not be passed as arguments
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable MEAI001 // OpenAIRequestPolicies is experimental

namespace Microsoft.Extensions.AI;

/// <summary>Represents an <see cref="IChatClient"/> for an <see cref="ResponsesClient"/>.</summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
internal sealed class OpenAIResponsesChatClient : IChatClient
{
    // AdditionalProperties key used to roundtrip a reasoning item's service-assigned id. Unlike
    // RawRepresentation, AdditionalProperties survives both content coalescing and JSON serialization of the
    // chat history, so the id is available when reconstructing the reasoning item for a subsequent request.
    // Stateless (store=false) resume of encrypted reasoning requires this id or the service rejects the item.
    private const string ReasoningItemIdKey = "reasoningItemId";

    // These delegate instances are used to call the internal overloads of CreateResponseAsync and CreateResponseStreamingAsync that accept
    // a RequestOptions. These should be replaced once a better way to pass RequestOptions is available.

    private static readonly Func<ResponsesClient, CreateResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>?
        _createResponseStreamingAsync =
        (Func<ResponsesClient, CreateResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>?)
        typeof(ResponsesClient).GetMethod(
            nameof(ResponsesClient.CreateResponseStreamingAsync), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(CreateResponseOptions), typeof(RequestOptions)], null)
        ?.CreateDelegate(typeof(Func<ResponsesClient, CreateResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>));

    private static readonly Func<ResponsesClient, GetResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>?
        _getResponseStreamingAsync =
        (Func<ResponsesClient, GetResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>?)
        typeof(ResponsesClient).GetMethod(
            nameof(ResponsesClient.GetResponseStreamingAsync), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(GetResponseOptions), typeof(RequestOptions)], null)
        ?.CreateDelegate(typeof(Func<ResponsesClient, GetResponseOptions, RequestOptions, AsyncCollectionResult<StreamingResponseUpdate>>));

    /// <summary>Metadata about the client.</summary>
    private readonly ChatClientMetadata _metadata;

    /// <summary>The underlying <see cref="ResponsesClient" />.</summary>
    private readonly ResponsesClient _responseClient;

    /// <summary>The default model ID to use for the chat client.</summary>
    private readonly string? _defaultModelId;

    /// <summary>Caller-registered policies applied to every <see cref="RequestOptions"/>.</summary>
    private readonly OpenAIRequestPolicies _requestPolicies = new();

    /// <summary>Initializes a new instance of the <see cref="OpenAIResponsesChatClient"/> class for the specified <see cref="ResponsesClient"/>.</summary>
    /// <param name="responseClient">The underlying client.</param>
    /// <param name="defaultModelId">The default model ID to use for the chat client.</param>
    /// <exception cref="ArgumentNullException"><paramref name="responseClient"/> is <see langword="null"/>.</exception>
    public OpenAIResponsesChatClient(ResponsesClient responseClient, string? defaultModelId)
    {
        _ = Throw.IfNull(responseClient);

        _responseClient = responseClient;
        _defaultModelId = defaultModelId;

        _metadata = new("openai", responseClient.Endpoint, defaultModelId);
    }

    /// <inheritdoc />
    object? IChatClient.GetService(Type serviceType, object? serviceKey)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(ChatClientMetadata) ? _metadata :
            serviceType == typeof(ResponsesClient) ? _responseClient :
            serviceType == typeof(OpenAIRequestPolicies) ? _requestPolicies :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        OpenAIClientExtensions.AddOpenAIApiType(OpenAIClientExtensions.OpenAIApiTypeResponses);

        // Convert the inputs into what ResponsesClient expects.
        var openAIOptions = AsCreateResponseOptions(options, out string? openAIConversationId);

        // Provided continuation token signals that an existing background response should be fetched.
        if (GetContinuationToken(messages, options) is { } token)
        {
            var getTask = _responseClient.GetResponseAsync(token.ResponseId, include: null, stream: null, startingAfter: null, includeObfuscation: null, cancellationToken.ToRequestOptions(streaming: false, _requestPolicies));
            var response = (ResponseResult)await getTask.ConfigureAwait(false);
            return FromOpenAIResponse(response, openAIOptions, openAIConversationId);
        }

        foreach (var responseItem in ToOpenAIResponseItems(messages, options))
        {
            openAIOptions.InputItems.Add(responseItem);
        }

        // Make the call to the ResponsesClient.
        var createTask = _responseClient.CreateResponseAsync((BinaryContent)openAIOptions, cancellationToken.ToRequestOptions(streaming: false, _requestPolicies));
        var openAIResponsesResult = (ResponseResult)await createTask.ConfigureAwait(false);

        // Convert the response to a ChatResponse.
        return FromOpenAIResponse(openAIResponsesResult, openAIOptions, openAIConversationId);
    }

    internal static ChatResponse FromOpenAIResponse(ResponseResult responseResult, CreateResponseOptions? openAIOptions, string? conversationId)
    {
        OpenAIClientExtensions.AddOpenAIResponseAttributes(responseResult.ServiceTier?.ToString(), systemFingerprint: null);

        // Convert and return the results.
        ChatResponse response = new()
        {
            ConversationId = IsStoredOutputDisabled(openAIOptions, responseResult) ? null : (conversationId ?? responseResult.Id),
            CreatedAt = responseResult.CreatedAt,
            ContinuationToken = CreateContinuationToken(responseResult),
            FinishReason = AsFinishReason(responseResult.IncompleteStatusDetails?.Reason),
            ModelId = responseResult.Model,
            RawRepresentation = responseResult,
            ResponseId = responseResult.Id,
            Usage = ToUsageDetails(responseResult),
        };

        if (!string.IsNullOrEmpty(responseResult.EndUserId))
        {
            (response.AdditionalProperties ??= [])[nameof(responseResult.EndUserId)] = responseResult.EndUserId;
        }

        if (responseResult.Error is not null)
        {
            (response.AdditionalProperties ??= [])[nameof(responseResult.Error)] = responseResult.Error;
        }

        if (responseResult.OutputItems is not null)
        {
            response.Messages = [.. ToChatMessages(responseResult.OutputItems, openAIOptions)];

            if (response.Messages.LastOrDefault() is { } lastMessage && responseResult.Error is { } error)
            {
                lastMessage.Contents.Add(new ErrorContent(error.Message) { ErrorCode = error.Code.ToString() });
            }

            foreach (var message in response.Messages)
            {
                message.CreatedAt ??= responseResult.CreatedAt;
            }
        }

        if (responseResult.SafetyIdentifier is not null)
        {
            (response.AdditionalProperties ??= [])[nameof(responseResult.SafetyIdentifier)] = responseResult.SafetyIdentifier;
        }

        return response;
    }

    internal static IEnumerable<ChatMessage> ToChatMessages(IEnumerable<ResponseItem> items, CreateResponseOptions? options = null)
    {
        ChatMessage? message = null;
        Dictionary<string, ToolApprovalRequestContent>? mcpApprovalRequests = null;

        foreach (ResponseItem outputItem in items)
        {
            message ??= new(ChatRole.Assistant, (string?)null);

            switch (outputItem)
            {
                case MessageResponseItem messageItem:
                    if (message.MessageId is not null && message.MessageId != messageItem.Id)
                    {
                        yield return message;
                        message = new ChatMessage();
                    }

                    message.MessageId = messageItem.Id;
                    message.RawRepresentation = messageItem;
                    message.Role = AsChatRole(messageItem.Role);
                    ((List<AIContent>)message.Contents).AddRange(ToAIContents(messageItem.Content));
                    break;

                case ReasoningResponseItem reasoningItem:
                    message.Contents.Add(CreateReasoningContent(reasoningItem.GetSummaryText(), reasoningItem.EncryptedContent, reasoningItem.Id, outputItem));
                    break;

                case FunctionCallResponseItem functionCall:
                    var fcc = OpenAIClientExtensions.ParseCallContent(functionCall.FunctionArguments, functionCall.CallId, functionCall.FunctionName);
                    fcc.RawRepresentation = outputItem;
                    message.Contents.Add(fcc);
                    break;

                case FunctionCallOutputResponseItem functionCallOutputItem:
                    message.Contents.Add(new FunctionResultContent(functionCallOutputItem.CallId, functionCallOutputItem.FunctionOutput) { RawRepresentation = functionCallOutputItem });
                    break;

                case McpToolCallItem mtci:
                    AddMcpToolCallContent(mtci, message.Contents);
                    break;

                case McpToolCallApprovalRequestItem mtcari:
                    // We are reusing the mtcari.Id as the McpServerToolCallContent.CallId since we don't have one yet.
                    var approvalRequest = new ToolApprovalRequestContent(mtcari.Id, new McpServerToolCallContent(mtcari.Id, mtcari.ToolName, mtcari.ServerLabel)
                    {
                        Arguments = JsonSerializer.Deserialize(mtcari.ToolArguments, OpenAIJsonContext.Default.IDictionaryStringObject),
                        RawRepresentation = mtcari,
                    })
                    {
                        RawRepresentation = mtcari,
                    };

                    // Store for correlation with responses.
                    (mcpApprovalRequests ??= new())[mtcari.Id] = approvalRequest;
                    message.Contents.Add(approvalRequest);
                    break;

                case McpToolCallApprovalResponseItem mtcari
                    when mcpApprovalRequests?.TryGetValue(mtcari.ApprovalRequestId, out ToolApprovalRequestContent? request) is true:
                    _ = mcpApprovalRequests.Remove(mtcari.ApprovalRequestId);

                    // Correlate with the original request to reuse its ToolCall.
                    // McpToolCallApprovalResponseItem without a correlated request falls through to default.
                    message.Contents.Add(new ToolApprovalResponseContent(
                        mtcari.ApprovalRequestId,
                        mtcari.Approved,
                        request.ToolCall)
                    {
                        RawRepresentation = mtcari,
                    });
                    break;

                case CodeInterpreterCallResponseItem cicri:
                    message.Contents.Add(new CodeInterpreterToolCallContent(cicri.Id)
                    {
                        Inputs = !string.IsNullOrWhiteSpace(cicri.Code) ? [new DataContent(Encoding.UTF8.GetBytes(cicri.Code), OpenAIClientExtensions.PythonMediaType)] : null,

                        // We purposefully do not set the RawRepresentation on the CodeInterpreterToolCallContent, only on the CodeInterpreterToolResultContent, to avoid
                        // the same CodeInterpreterCallResponseItem being included on two different AIContent instances. When these are roundtripped, we want only one
                        // CodeInterpreterCallResponseItem sent back for the pair.
                    });

                    message.Contents.Add(CreateCodeInterpreterResultContent(cicri));
                    break;

                case ImageGenerationCallResponseItem imageGenItem:
                    AddImageGenerationContents(imageGenItem, options, message.Contents);
                    break;

                case WebSearchCallResponseItem wscri:
                    message.Contents.Add(new WebSearchToolCallContent(wscri.Id)
                    {
                        Queries = GetWebSearchQueries(wscri),

                        // We purposefully do not set the RawRepresentation on the WebSearchToolCallContent, only on the WebSearchToolResultContent, to avoid
                        // the same WebSearchCallResponseItem being included on two different AIContent instances. When these are roundtripped, we want only one
                        // WebSearchCallResponseItem sent back for the pair.
                    });

                    message.Contents.Add(new WebSearchToolResultContent(wscri.Id)
                    {
                        Outputs = GetWebSearchSources(wscri),
                        RawRepresentation = wscri,
                    });
                    break;

                // These tool types don't have dedicated AIContent-derived types. We use the base ToolCallContent/ToolResultContent
                // to represent them, with the original ResponseItem accessible via RawRepresentation for type-specific data.
                case FileSearchCallResponseItem:
                    message.Contents.Add(new ToolCallContent(outputItem.Id));
                    message.Contents.Add(new ToolResultContent(outputItem.Id) { RawRepresentation = outputItem });
                    break;

                case ComputerCallResponseItem computerCall:
                    message.Contents.Add(new ToolCallContent(computerCall.CallId) { RawRepresentation = computerCall });
                    break;

                case ComputerCallOutputResponseItem computerCallOutput:
                    message.Contents.Add(new ToolResultContent(computerCallOutput.CallId) { RawRepresentation = computerCallOutput });
                    break;

                case ApplyPatchCallItem patchCall:
                    message.Contents.Add(new ToolCallContent(patchCall.CallId) { RawRepresentation = patchCall });
                    break;

                case ApplyPatchCallOutputItem patchCallOutput:
                    message.Contents.Add(new ToolResultContent(patchCallOutput.CallId) { RawRepresentation = patchCallOutput });
                    break;

                default:
                    message.Contents.Add(new() { RawRepresentation = outputItem });
                    break;
            }
        }

        if (message is not null)
        {
            yield return message;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        OpenAIClientExtensions.AddOpenAIApiType(OpenAIClientExtensions.OpenAIApiTypeResponses);

        var openAIOptions = AsCreateResponseOptions(options, out string? openAIConversationId);
        openAIOptions.StreamingEnabled = true;

        // Provided continuation token signals that an existing background response should be fetched.
        if (GetContinuationToken(messages, options) is { } token)
        {
            GetResponseOptions getOptions = new(token.ResponseId) { StartingAfter = token.SequenceNumber, StreamingEnabled = true };

            Debug.Assert(_getResponseStreamingAsync is not null, $"Unable to find {nameof(_getResponseStreamingAsync)} method");
            IAsyncEnumerable<StreamingResponseUpdate> getUpdates = _getResponseStreamingAsync is not null ?
                _getResponseStreamingAsync(_responseClient, getOptions, cancellationToken.ToRequestOptions(streaming: true, _requestPolicies)) :
                _responseClient.GetResponseStreamingAsync(getOptions, cancellationToken);

            return FromOpenAIStreamingResponseUpdatesAsync(getUpdates, openAIOptions, openAIConversationId, token.ResponseId, cancellationToken);
        }

        foreach (var responseItem in ToOpenAIResponseItems(messages, options))
        {
            openAIOptions.InputItems.Add(responseItem);
        }

        Debug.Assert(_createResponseStreamingAsync is not null, $"Unable to find {nameof(_createResponseStreamingAsync)} method");
        AsyncCollectionResult<StreamingResponseUpdate> createUpdates = _createResponseStreamingAsync is not null ?
            _createResponseStreamingAsync(_responseClient, openAIOptions, cancellationToken.ToRequestOptions(streaming: true, _requestPolicies)) :
            _responseClient.CreateResponseStreamingAsync(openAIOptions, cancellationToken);

        return FromOpenAIStreamingResponseUpdatesAsync(createUpdates, openAIOptions, openAIConversationId, cancellationToken: cancellationToken);
    }

    internal static async IAsyncEnumerable<ChatResponseUpdate> FromOpenAIStreamingResponseUpdatesAsync(
        IAsyncEnumerable<StreamingResponseUpdate> streamingResponseUpdates,
        CreateResponseOptions? options,
        string? conversationId,
        string? resumeResponseId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DateTimeOffset? createdAt = null;
        string? responseId = resumeResponseId;
        string? modelId = null;
        string? lastMessageId = null;
        ChatRole? lastRole = null;
        bool anyFunctions = false;
        bool storedOutputDisabled = false;
        string? serviceTier = null;
        string? systemFingerprint = null;
        ResponseStatus? latestResponseStatus = null;
        Dictionary<string, ToolApprovalRequestContent>? mcpApprovalRequests = null;

        UpdateConversationId(resumeResponseId);

        await foreach (var streamingUpdate in streamingResponseUpdates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Create an update populated with the current state of the response.
            ChatResponseUpdate CreateUpdate(AIContent? content = null) =>
                new(lastRole, content is not null ? [content] : null)
                {
                    ContinuationToken = CreateContinuationToken(
                        responseId!,
                        latestResponseStatus,
                        options?.BackgroundModeEnabled,
                        streamingUpdate.SequenceNumber),
                    ConversationId = conversationId,
                    CreatedAt = createdAt,
                    MessageId = lastMessageId,
                    ModelId = modelId,
                    RawRepresentation = streamingUpdate,
                    ResponseId = responseId,
                };

            switch (streamingUpdate)
            {
                case StreamingResponseCreatedUpdate createdUpdate:
                    createdAt = createdUpdate.Response.CreatedAt;
                    responseId = createdUpdate.Response.Id;
                    UpdateConversationId(responseId, createdUpdate.Response);
                    modelId = createdUpdate.Response.Model;
                    latestResponseStatus = createdUpdate.Response.Status;
                    goto default;

                case StreamingResponseQueuedUpdate queuedUpdate:
                    createdAt = queuedUpdate.Response.CreatedAt;
                    responseId = queuedUpdate.Response.Id;
                    UpdateConversationId(responseId, queuedUpdate.Response);
                    modelId = queuedUpdate.Response.Model;
                    latestResponseStatus = queuedUpdate.Response.Status;
                    goto default;

                case StreamingResponseInProgressUpdate inProgressUpdate:
                    createdAt = inProgressUpdate.Response.CreatedAt;
                    responseId = inProgressUpdate.Response.Id;
                    UpdateConversationId(responseId, inProgressUpdate.Response);
                    modelId = inProgressUpdate.Response.Model;
                    latestResponseStatus = inProgressUpdate.Response.Status;
                    goto default;

                case StreamingResponseIncompleteUpdate incompleteUpdate:
                    createdAt = incompleteUpdate.Response.CreatedAt;
                    responseId = incompleteUpdate.Response.Id;
                    UpdateConversationId(responseId, incompleteUpdate.Response);
                    modelId = incompleteUpdate.Response.Model;
                    latestResponseStatus = incompleteUpdate.Response.Status;
                    goto default;

                case StreamingResponseFailedUpdate failedUpdate:
                    createdAt = failedUpdate.Response.CreatedAt;
                    responseId = failedUpdate.Response.Id;
                    UpdateConversationId(responseId, failedUpdate.Response);
                    modelId = failedUpdate.Response.Model;
                    latestResponseStatus = failedUpdate.Response.Status;
                    goto default;

                case StreamingResponseCompletedUpdate completedUpdate:
                {
                    createdAt = completedUpdate.Response.CreatedAt;
                    responseId = completedUpdate.Response.Id;
                    UpdateConversationId(responseId, completedUpdate.Response);
                    modelId = completedUpdate.Response.Model;
                    latestResponseStatus = completedUpdate.Response?.Status;
                    var update = CreateUpdate(ToUsageDetails(completedUpdate.Response) is { } usage ? new UsageContent(usage) : null);
                    update.FinishReason =
                        AsFinishReason(completedUpdate.Response?.IncompleteStatusDetails?.Reason) ??
                        (anyFunctions ? ChatFinishReason.ToolCalls :
                        ChatFinishReason.Stop);
                    yield return update;
                    break;
                }

                case StreamingResponseOutputItemAddedUpdate outputItemAddedUpdate:
                    switch (outputItemAddedUpdate.Item)
                    {
                        case MessageResponseItem mri:
                            lastMessageId = outputItemAddedUpdate.Item.Id;
                            lastRole = AsChatRole(mri.Role);
                            break;

                        case FunctionCallResponseItem fcri:
                            anyFunctions = true;
                            lastRole = ChatRole.Assistant;
                            break;
                    }

                    goto default;

                case StreamingResponseOutputTextDeltaUpdate outputTextDeltaUpdate:
                    yield return CreateUpdate(new TextContent(outputTextDeltaUpdate.Delta));
                    break;

                case StreamingResponseReasoningSummaryTextDeltaUpdate reasoningSummaryTextDeltaUpdate:
                    yield return CreateUpdate(CreateReasoningContent(reasoningSummaryTextDeltaUpdate.Delta, protectedData: null, reasoningSummaryTextDeltaUpdate.ItemId));
                    break;

                case StreamingResponseReasoningTextDeltaUpdate reasoningTextDeltaUpdate:
                    yield return CreateUpdate(CreateReasoningContent(reasoningTextDeltaUpdate.Delta, protectedData: null, reasoningTextDeltaUpdate.ItemId));
                    break;

                case StreamingResponseImageGenerationCallInProgressUpdate imageGenInProgress:
                    yield return CreateUpdate(new ImageGenerationToolCallContent(imageGenInProgress.ItemId)
                    {
                        RawRepresentation = imageGenInProgress,
                    });
                    break;

                case StreamingResponseImageGenerationCallPartialImageUpdate streamingImageGenUpdate:
                    yield return CreateUpdate(GetImageGenerationResult(streamingImageGenUpdate, options));
                    break;

                case StreamingResponseCodeInterpreterCallCodeDeltaUpdate codeInterpreterDeltaUpdate:
                    yield return CreateUpdate(new CodeInterpreterToolCallContent(codeInterpreterDeltaUpdate.ItemId)
                    {
                        Inputs = [new DataContent(Encoding.UTF8.GetBytes(codeInterpreterDeltaUpdate.Delta), OpenAIClientExtensions.PythonMediaType)],
                        RawRepresentation = codeInterpreterDeltaUpdate,
                    });
                    break;

                case StreamingResponseWebSearchCallInProgressUpdate webSearchInProgressUpdate:
                    yield return CreateUpdate(new WebSearchToolCallContent(webSearchInProgressUpdate.ItemId)
                    {
                        RawRepresentation = webSearchInProgressUpdate,
                    });
                    break;

                case StreamingResponseOutputItemDoneUpdate outputItemDoneUpdate:
                    switch (outputItemDoneUpdate.Item)
                    {
                        // Translate completed ResponseItems into their corresponding abstraction representations.
                        case FunctionCallResponseItem fcri:
                            yield return CreateUpdate(OpenAIClientExtensions.ParseCallContent(fcri.FunctionArguments.ToString(), fcri.CallId, fcri.FunctionName));
                            break;

                        case McpToolCallItem mtci:
                            var mcpUpdate = CreateUpdate();
                            AddMcpToolCallContent(mtci, mcpUpdate.Contents);
                            yield return mcpUpdate;
                            break;

                        case McpToolCallApprovalRequestItem mtcari:
                            // We are reusing the mtcari.Id as the McpServerToolCallContent.CallId since we don't have one yet.
                            var streamApprovalRequest = new ToolApprovalRequestContent(mtcari.Id, new McpServerToolCallContent(mtcari.Id, mtcari.ToolName, mtcari.ServerLabel)
                            {
                                Arguments = JsonSerializer.Deserialize(mtcari.ToolArguments, OpenAIJsonContext.Default.IDictionaryStringObject),
                                RawRepresentation = mtcari,
                            })
                            {
                                RawRepresentation = mtcari,
                            };

                            // Store for correlation with responses.
                            (mcpApprovalRequests ??= new())[mtcari.Id] = streamApprovalRequest;
                            yield return CreateUpdate(streamApprovalRequest);
                            break;

                        case McpToolCallApprovalResponseItem mtcari
                            when mcpApprovalRequests?.TryGetValue(mtcari.ApprovalRequestId, out ToolApprovalRequestContent? request) is true:
                            _ = mcpApprovalRequests.Remove(mtcari.ApprovalRequestId);

                            // Correlate with the original request to reuse its ToolCall.
                            // McpToolCallApprovalResponseItem without a correlated request falls through to default.
                            yield return CreateUpdate(new ToolApprovalResponseContent(
                                mtcari.ApprovalRequestId,
                                mtcari.Approved,
                                request.ToolCall)
                            {
                                RawRepresentation = mtcari,
                            });
                            break;

                        case FunctionCallOutputResponseItem functionCallOutputItem:
                            lastRole ??= ChatRole.Assistant;
                            yield return CreateUpdate(new FunctionResultContent(functionCallOutputItem.CallId, functionCallOutputItem.FunctionOutput) { RawRepresentation = functionCallOutputItem });
                            break;

                        case CodeInterpreterCallResponseItem cicri:
                            // The CodeInterpreterToolCallContent has already been yielded as part of delta updates.
                            // Only yield the CodeInterpreterToolResultContent here for the outputs.
                            yield return CreateUpdate(CreateCodeInterpreterResultContent(cicri));
                            break;

                        case WebSearchCallResponseItem wscri:
                            // The WebSearchToolCallContent has already been yielded as part of in-progress updates.
                            // Yield a second one here with queries populated, which coalescing will merge with the first.
                            yield return CreateUpdate(new WebSearchToolCallContent(wscri.Id)
                            {
                                Queries = GetWebSearchQueries(wscri),
                            });

                            // Also yield the WebSearchToolResultContent.
                            yield return CreateUpdate(new WebSearchToolResultContent(wscri.Id)
                            {
                                Outputs = GetWebSearchSources(wscri),
                                RawRepresentation = wscri,
                            });
                            break;

                        // MessageResponseItems will have already had their content yielded as part of delta updates.
                        // However, those deltas didn't yield annotations. If there are any annotations, yield them now.
                        case MessageResponseItem mri when mri.Content is { Count: > 0 } mriContent && mriContent.Any(c => c.OutputTextAnnotations is { Count: > 0 }):
                            AIContent annotatedContent = new(); // do not include RawRepresentation to avoid duplication with already yielded deltas
                            foreach (var c in mriContent)
                            {
                                PopulateAnnotations(c, annotatedContent);
                            }
                            yield return CreateUpdate(annotatedContent);
                            break;

                        // For ReasoningResponseItem, if there's encrypted content, we need to yield that
                        // so that it can be coalesced with the streamed text deltas and roundtripped.
                        // Since we may have already yielded reasoning deltas, we explicitly avoid setting
                        // the RawRepresentation here to avoid duplication, as when roundtripping that
                        // raw representation will be preferred. The reasoning item's id is stashed in
                        // AdditionalProperties (which survives coalescing and serialization, unlike
                        // RawRepresentation) so that it can be sent back on a subsequent request; stateless
                        // (store=false) resume of encrypted reasoning is rejected by the service without it.
                        case ReasoningResponseItem rri when rri.EncryptedContent is { Length: > 0 } encryptedContent:
                            yield return CreateUpdate(CreateReasoningContent(text: null, protectedData: encryptedContent, itemId: rri.Id));
                            break;

                        // The ImageGenerationToolCallContent has already been yielded as part of in-progress updates,
                        // but any partial images yielded along the way are lower-quality intermediate renders rather than
                        // deltas of the final image. Yield the final image here; coalescing will replace the partial
                        // results with this one, since they share the same CallId. This mirrors the non-streaming path,
                        // which produces a result for every ImageGenerationCallResponseItem.
                        case ImageGenerationCallResponseItem imageGenItem:
                            yield return CreateUpdate(CreateImageGenerationResultContent(imageGenItem, options));
                            break;

                        // For ResponseItems where we've already yielded partial deltas for the whole content,
                        // we still want to yield an update, but we don't want it to include the ResponseItem
                        // as the RawRepresentation, since if it did, when roundtripping we'd end up sending
                        // the same content twice (first from the deltas, then from the raw response item).
                        // Just yield an update without AIContent for the ResponseItem.
                        case MessageResponseItem or ReasoningResponseItem or ImageGenerationCallResponseItem:
                            yield return CreateUpdate();
                            break;

                        // FileSearch items contain both the call and results inline, so we emit a call+result pair.
                        // ComputerCall results arrive as a separate ComputerCallOutputResponseItem.
                        case FileSearchCallResponseItem:
                            var toolCallUpdate = CreateUpdate(new ToolCallContent(outputItemDoneUpdate.Item.Id));
                            toolCallUpdate.Contents.Add(new ToolResultContent(outputItemDoneUpdate.Item.Id) { RawRepresentation = outputItemDoneUpdate.Item });
                            yield return toolCallUpdate;
                            break;

                        case ComputerCallResponseItem computerCall:
                            yield return CreateUpdate(new ToolCallContent(computerCall.CallId) { RawRepresentation = outputItemDoneUpdate.Item });
                            break;

                        case ComputerCallOutputResponseItem computerCallOutput:
                            yield return CreateUpdate(new ToolResultContent(computerCallOutput.CallId) { RawRepresentation = computerCallOutput });
                            break;

                        // For everything else, yield an AIContent for the ResponseItem.
                        default:
                            yield return CreateUpdate(new AIContent { RawRepresentation = outputItemDoneUpdate.Item });
                            break;
                    }
                    break;

                case StreamingResponseErrorUpdate errorUpdate:
                    string? errorMessage = errorUpdate.Message;
                    string? errorCode = errorUpdate.Code;
                    string? errorParam = errorUpdate.Param;

                    // Workaround for https://github.com/openai/openai-dotnet/issues/849.
                    // The OpenAI service is sending down error information in a different format
                    // than is documented and thus a different format from what the OpenAI client
                    // library deserializes. Until that's addressed such that the data is correctly
                    // propagated through the OpenAI library, if it looks like the update doesn't
                    // contain the properly deserialized error information, try accessing it
                    // directly from the underlying JSON.
                    {
                        if (string.IsNullOrEmpty(errorMessage))
                        {
                            _ = errorUpdate.Patch.TryGetValue("$.error.message"u8, out errorMessage);
                        }

                        if (string.IsNullOrEmpty(errorCode))
                        {
                            _ = errorUpdate.Patch.TryGetValue("$.error.code"u8, out errorCode);
                        }

                        if (string.IsNullOrEmpty(errorParam))
                        {
                            _ = errorUpdate.Patch.TryGetValue("$.error.param"u8, out errorParam);
                        }
                    }

                    yield return CreateUpdate(new ErrorContent(errorMessage)
                    {
                        ErrorCode = errorCode,
                        Details = errorParam,
                    });
                    break;

                case StreamingResponseRefusalDoneUpdate refusalDone:
                    yield return CreateUpdate(new ErrorContent(refusalDone.Refusal)
                    {
                        ErrorCode = nameof(ResponseContentPart.Refusal),
                    });
                    break;

                default:
                    yield return CreateUpdate();
                    break;
            }
        }

        void UpdateConversationId(string? id, ResponseResult? response = null)
        {
            // Record the service tier and system fingerprint each once if not yet recorded.
            OpenAIClientExtensions.AddOpenAIResponseAttributes(
                response?.ServiceTier?.ToString(), systemFingerprint: null,
                ref serviceTier, ref systemFingerprint);

            storedOutputDisabled |= IsStoredOutputDisabled(options, response);
            if (storedOutputDisabled)
            {
                conversationId = null;
            }
            else
            {
                conversationId ??= id;
            }
        }
    }

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        // Nothing to dispose.
    }

#pragma warning disable SCME0001 // JsonPatch is experimental
    /// <summary>
    /// Determines whether stored output is disabled, either via the request options
    /// or by checking the actual response's "store" field via Patch.
    /// </summary>
    private static bool IsStoredOutputDisabled(CreateResponseOptions? options, ResponseResult? response) =>
        options?.StoredOutputEnabled is false ||
        (response is not null && response.Patch.TryGetValue("$.store"u8, out bool store) && !store);
#pragma warning restore SCME0001

    internal static ResponseTool? ToResponseTool(AITool tool, ChatOptions? options = null, ToolSearchLookup? toolSearchLookup = null)
    {
        switch (tool)
        {
            case ResponseToolAITool rtat:
                return rtat.Tool;

            case AIFunctionDeclaration aiFunction:
                var functionTool = ToResponseTool(aiFunction, options);
                if ((toolSearchLookup ??= ToolSearchLookup.Create(options?.Tools)).IsDeferred(aiFunction.Name))
                {
                    functionTool.Patch.Set("$.defer_loading"u8, "true"u8);
                }

                return functionTool;

            case HostedToolSearchTool:
                // Workaround: The OpenAI .NET SDK doesn't yet expose a ToolSearchTool type.
                // See https://github.com/openai/openai-dotnet/issues/1053
                return ModelReaderWriter.Read<ResponseTool>(BinaryData.FromString("""{"type": "tool_search"}"""), ModelReaderWriterOptions.Json, OpenAIContext.Default)!;

            case HostedWebSearchTool webSearchTool:
                return new WebSearchTool
                {
                    Filters = webSearchTool.GetProperty<WebSearchToolFilters?>(nameof(WebSearchTool.Filters)),
                    SearchContextSize = webSearchTool.GetProperty<WebSearchToolContextSize?>(nameof(WebSearchTool.SearchContextSize)),
                    UserLocation = webSearchTool.GetProperty<WebSearchToolLocation?>(nameof(WebSearchTool.UserLocation)),
                };

            case HostedFileSearchTool fileSearchTool:
                return new FileSearchTool(fileSearchTool.Inputs?.OfType<HostedVectorStoreContent>().Select(c => c.VectorStoreId) ?? [])
                {
                    Filters = fileSearchTool.GetProperty<BinaryData?>(nameof(FileSearchTool.Filters)),
                    MaxResultCount = fileSearchTool.MaximumResultCount,
                    RankingOptions = fileSearchTool.GetProperty<FileSearchToolRankingOptions?>(nameof(FileSearchTool.RankingOptions)),
                };

            case HostedCodeInterpreterTool codeTool:
                return new CodeInterpreterTool(
                    new(codeTool.Inputs?.OfType<HostedFileContent>().Select(f => f.FileId).ToList() is { Count: > 0 } ids ?
                        CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(ids) :
                        new()));

            case HostedImageGenerationTool imageGenerationTool:
                ImageGenerationOptions? igo = imageGenerationTool.Options;
                return new ImageGenerationTool
                {
                    Background = imageGenerationTool.GetProperty<ImageGenerationToolBackground?>(nameof(ImageGenerationTool.Background)),
                    InputFidelity = imageGenerationTool.GetProperty<ImageGenerationToolInputFidelity?>(nameof(ImageGenerationTool.InputFidelity)),
                    InputImageMask = imageGenerationTool.GetProperty<ImageGenerationToolInputImageMask?>(nameof(ImageGenerationTool.InputImageMask)),
                    Model = igo?.ModelId,
                    ModerationLevel = imageGenerationTool.GetProperty<ImageGenerationToolModerationLevel?>(nameof(ImageGenerationTool.ModerationLevel)),
                    OutputCompressionFactor = imageGenerationTool.GetProperty<int?>(nameof(ImageGenerationTool.OutputCompressionFactor)),
                    OutputFileFormat = igo?.MediaType is { } mediaType ?
                        mediaType switch
                        {
                            "image/png" => ImageGenerationToolOutputFileFormat.Png,
                            "image/jpeg" => ImageGenerationToolOutputFileFormat.Jpeg,
                            "image/webp" => ImageGenerationToolOutputFileFormat.Webp,

                            // The SDK defines nullable and non-nullable op_Implicit conversions from string. Force a nullable result so
                            // null does not bind to the non-nullable conversion, which throws ArgumentNullException.
                            _ => (ImageGenerationToolOutputFileFormat?)null,
                        } :
                        null,
                    PartialImageCount = igo?.StreamingCount,
                    Quality = imageGenerationTool.GetProperty<ImageGenerationToolQuality?>(nameof(ImageGenerationTool.Quality)),
                    Size = igo?.ImageSize is { } size ?
                        new ImageGenerationToolSize(size.Width, size.Height) :
                        null,
                };

            case HostedMcpServerTool mcpTool:
                bool isUrl = Uri.TryCreate(mcpTool.ServerAddress, UriKind.Absolute, out Uri? serverAddressUrl);
                McpTool responsesMcpTool = isUrl ?
                    new McpTool(mcpTool.ServerName, serverAddressUrl!) :
                    new McpTool(mcpTool.ServerName, new McpToolConnectorId(mcpTool.ServerAddress));

                responsesMcpTool.ServerDescription = mcpTool.ServerDescription;

                if (isUrl)
                {
                    if (mcpTool.Headers is { Count: > 0 })
                    {
                        responsesMcpTool.Headers = mcpTool.Headers;
                    }
                }
                else
                {
                    // For connectors: extract Bearer token from Headers and set as AuthorizationToken.
                    // Use case-insensitive comparison since auth scheme is case-insensitive per RFC 7235.
                    // Allow flexible whitespace in the header value.
                    if (mcpTool.Headers?.TryGetValue("Authorization", out string? authHeader) is true &&
                        authHeader.AsSpan().Trim() is { Length: > 0 } trimmedAuthHeader &&
                        trimmedAuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        responsesMcpTool.AuthorizationToken = trimmedAuthHeader.Slice("Bearer ".Length).TrimStart().ToString();
                    }
                }

                if (mcpTool.AllowedTools is not null)
                {
                    responsesMcpTool.AllowedTools = new();
                    AddAllMcpFilters(mcpTool.AllowedTools, responsesMcpTool.AllowedTools);
                }

                switch (mcpTool.ApprovalMode)
                {
                    case HostedMcpServerToolAlwaysRequireApprovalMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(DefaultMcpToolCallApprovalPolicy.AlwaysRequireApproval);
                        break;

                    case HostedMcpServerToolNeverRequireApprovalMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(DefaultMcpToolCallApprovalPolicy.NeverRequireApproval);
                        break;

                    case HostedMcpServerToolRequireSpecificApprovalMode specificMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(new CustomMcpToolCallApprovalPolicy());

                        if (specificMode.AlwaysRequireApprovalToolNames is { Count: > 0 } alwaysRequireToolNames)
                        {
                            responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval = new();
                            AddAllMcpFilters(alwaysRequireToolNames, responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval);
                        }

                        if (specificMode.NeverRequireApprovalToolNames is { Count: > 0 } neverRequireToolNames)
                        {
                            responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval = new();
                            AddAllMcpFilters(neverRequireToolNames, responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval);
                        }

                        break;
                }

                if ((toolSearchLookup ??= ToolSearchLookup.Create(options?.Tools)).IsDeferred(mcpTool.ServerName))
                {
                    responsesMcpTool.Patch.Set("$.defer_loading"u8, "true"u8);
                }

                return responsesMcpTool;

            default:
                return null;
        }
    }

    internal static FunctionTool ToResponseTool(AIFunctionDeclaration aiFunction, ChatOptions? options = null)
    {
        bool? strictModeEnabled =
            OpenAIClientExtensions.HasStrict(aiFunction.AdditionalProperties) ??
            OpenAIClientExtensions.HasStrict(options?.AdditionalProperties);

        return new FunctionTool(
            aiFunction.Name,
            OpenAIClientExtensions.ToOpenAIFunctionParameters(aiFunction, strictModeEnabled),
            strictModeEnabled)
        {
            FunctionDescription = aiFunction.Description,
        };
    }

    /// <summary>
    /// Builds a <c>{"type":"namespace"}</c> <see cref="ResponseTool"/> from a name and set of tools.
    /// The OpenAI .NET SDK doesn't expose a NamespaceTool type, so we construct the JSON manually.
    /// </summary>
    internal static ResponseTool ToNamespaceResponseTool(string name, string? description, IEnumerable<ResponseTool> namespacedTools)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "namespace"u8);
            writer.WriteString("name"u8, name);

            if (!string.IsNullOrEmpty(description))
            {
                writer.WriteString("description"u8, description);
            }

            writer.WriteStartArray("tools"u8);
            foreach (var namespacedTool in namespacedTools)
            {
                var toolData = ModelReaderWriter.Write(namespacedTool, ModelReaderWriterOptions.Json, OpenAIContext.Default);
                using var doc = JsonDocument.Parse(toolData);
                doc.RootElement.WriteTo(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ModelReaderWriter.Read<ResponseTool>(BinaryData.FromBytes(stream.ToArray()), ModelReaderWriterOptions.Json, OpenAIContext.Default)!;
    }

    /// <summary>Creates a <see cref="ChatRole"/> from a <see cref="MessageRole"/>.</summary>
    private static ChatRole AsChatRole(MessageRole? role) =>
        role switch
        {
            MessageRole.System => ChatRole.System,
            MessageRole.Developer => OpenAIClientExtensions.ChatRoleDeveloper,
            MessageRole.User => ChatRole.User,
            _ => ChatRole.Assistant,
        };

    /// <summary>Creates a <see cref="TextReasoningContent"/>, stashing the reasoning item's id (when available)
    /// in <see cref="AIContent.AdditionalProperties"/> so it roundtrips back to the service.</summary>
    private static TextReasoningContent CreateReasoningContent(string? text, string? protectedData, string? itemId, object? rawRepresentation = null)
    {
        TextReasoningContent content = new(text)
        {
            ProtectedData = protectedData,
            RawRepresentation = rawRepresentation,
        };

        if (!string.IsNullOrEmpty(itemId))
        {
            (content.AdditionalProperties ??= [])[ReasoningItemIdKey] = itemId;
        }

        return content;
    }

    /// <summary>Creates a <see cref="ChatFinishReason"/> from a <see cref="ResponseIncompleteStatusReason"/>.</summary>
    private static ChatFinishReason? AsFinishReason(ResponseIncompleteStatusReason? statusReason) =>
        statusReason == ResponseIncompleteStatusReason.ContentFilter ? ChatFinishReason.ContentFilter :
        statusReason == ResponseIncompleteStatusReason.MaxOutputTokens ? ChatFinishReason.Length :
        null;

    /// <summary>Converts a <see cref="ChatOptions"/> to a <see cref="CreateResponseOptions"/>.</summary>
    private CreateResponseOptions AsCreateResponseOptions(ChatOptions? options, out string? openAIConversationId)
    {
        openAIConversationId = null;

        if (options is null)
        {
            return new()
            {
                Model = _defaultModelId,
            };
        }

        bool hasRawRco = false;
        if (options.RawRepresentationFactory?.Invoke(this) is CreateResponseOptions result)
        {
            hasRawRco = true;
        }
        else
        {
            result = new();
        }

        result.BackgroundModeEnabled ??= options.AllowBackgroundResponses;
        result.MaxOutputTokenCount ??= options.MaxOutputTokens;
        result.Model ??= options.ModelId ?? _defaultModelId;
        result.Temperature ??= options.Temperature;
        result.TopP ??= options.TopP;
        result.ReasoningOptions ??= ToOpenAIResponseReasoningOptions(options.Reasoning);

        // If the CreateResponseOptions.PreviousResponseId is already set (likely rare), then we don't need to do
        // anything with regards to Conversation, because they're mutually exclusive and we would want to ignore
        // ChatOptions.ConversationId regardless of its value. If it's null, we want to examine the CreateResponseOptions
        // instance to see if a conversation ID has already been set on it and use that conversation ID subsequently if
        // it has. If one hasn't been set, but ChatOptions.ConversationId has been set, we'll either set
        // CreateResponseOptions.Conversation if the string represents a conversation ID or else PreviousResponseId.
        if (result.PreviousResponseId is null)
        {
            bool chatOptionsHasOpenAIConversationId = OpenAIClientExtensions.IsConversationId(options.ConversationId);

            if (hasRawRco || chatOptionsHasOpenAIConversationId)
            {
                openAIConversationId = result.ConversationOptions?.ConversationId;
                if (openAIConversationId is null && chatOptionsHasOpenAIConversationId)
                {
                    result.ConversationOptions = new(options.ConversationId);
                    openAIConversationId = options.ConversationId;
                }
            }

            // If we still don't have a conversation ID, and ChatOptions.ConversationId is set, treat it as a response ID.
            if (openAIConversationId is null && options.ConversationId is { } previousResponseId)
            {
                result.PreviousResponseId = previousResponseId;
            }
        }

        if (options.Instructions is { } instructions)
        {
            result.Instructions = string.IsNullOrEmpty(result.Instructions) ?
                instructions :
                $"{result.Instructions}{Environment.NewLine}{instructions}";
        }

        // Populate tools if there are any.
        if (options.Tools is { Count: > 0 } tools)
        {
            ToolSearchLookup toolSearchLookup = ToolSearchLookup.Create(tools);
            Dictionary<ToolSearchLookup.Namespace, List<ResponseTool>>? namespaceGroups = null;
            bool toolSearchAdded = false;

            foreach (AITool tool in tools)
            {
                if (ToResponseTool(tool, options, toolSearchLookup) is { } responseTool)
                {
                    // Avoid sending multiple tool_search entries when callers supply more than one
                    // HostedToolSearchTool; the OpenAI Responses API only accepts one.
                    if (tool is HostedToolSearchTool)
                    {
                        if (toolSearchAdded)
                        {
                            continue;
                        }

                        toolSearchAdded = true;
                    }

                    // When a namespaced HostedToolSearchTool claims this deferred tool,
                    // collect it for later wrapping in a namespace container.
                    string? responseToolName = responseTool is FunctionTool ft ? ft.FunctionName
                        : responseTool is McpTool mcp ? mcp.ServerLabel
                        : null;

                    if (responseToolName is not null
                        && toolSearchLookup.GetNamespace(responseToolName) is { } ns)
                    {
                        namespaceGroups ??= new();
                        if (!namespaceGroups.TryGetValue(ns, out var group))
                        {
                            group = new();
                            namespaceGroups[ns] = group;
                        }

                        group.Add(responseTool);
                        continue;
                    }

                    result.Tools.Add(responseTool);
                }
            }

            if (namespaceGroups is not null)
            {
                foreach (KeyValuePair<ToolSearchLookup.Namespace, List<ResponseTool>> kvp in namespaceGroups)
                {
                    result.Tools.Add(ToNamespaceResponseTool(kvp.Key.Name, kvp.Key.Description, kvp.Value));
                }
            }

            if (result.Tools.Count > 0)
            {
                result.ParallelToolCallsEnabled ??= options.AllowMultipleToolCalls;
            }

            if (result.ToolChoice is null && result.Tools.Count > 0)
            {
                switch (options.ToolMode)
                {
                    case NoneChatToolMode:
                        result.ToolChoice = ResponseToolChoice.CreateNoneChoice();
                        break;

                    case AutoChatToolMode:
                        result.ToolChoice = ResponseToolChoice.CreateAutoChoice();
                        break;

                    case RequiredChatToolMode required:
                        result.ToolChoice = required.RequiredFunctionName is not null ?
                            ResponseToolChoice.CreateFunctionChoice(required.RequiredFunctionName) :
                            ResponseToolChoice.CreateRequiredChoice();
                        break;
                }
            }
        }

        if (result.TextOptions?.TextFormat is null &&
            ToOpenAIResponseTextFormat(options.ResponseFormat, options) is { } newFormat)
        {
            (result.TextOptions ??= new()).TextFormat = newFormat;
        }

        return result;
    }

    internal sealed class ToolSearchLookup
    {
        private static readonly ToolSearchLookup _empty = new(deferAll: false, deferredToolNames: [], namespacedToolNames: []);
        private readonly bool _deferAll;
        private readonly HashSet<string> _deferredToolNames;
        private readonly Dictionary<string, Namespace> _namespacedToolNames;

        private ToolSearchLookup(bool deferAll, HashSet<string> deferredToolNames, Dictionary<string, Namespace> namespacedToolNames)
        {
            _deferAll = deferAll;
            _deferredToolNames = deferredToolNames;
            _namespacedToolNames = namespacedToolNames;
        }

        public static ToolSearchLookup Create(IList<AITool>? tools)
        {
            if (tools is not { Count: > 0 })
            {
                return _empty;
            }

            HashSet<string> functionAndMcpToolNames = new(
                tools.Select(
                    static tool => tool switch
                    {
                        AIFunctionDeclaration aiFunction => aiFunction.Name,
                        HostedMcpServerTool mcpTool => mcpTool.ServerName,
                        _ => null,
                    })
                .OfType<string>(),
                StringComparer.Ordinal);

            if (functionAndMcpToolNames.Count == 0)
            {
                return _empty;
            }

            bool deferAll = false;
            HashSet<string> deferredToolNames = new(StringComparer.Ordinal);
            Dictionary<string, Namespace> namespacedToolNames = new(StringComparer.Ordinal);
            Dictionary<string, Namespace> namespacesByName = new(StringComparer.Ordinal);
            HashSet<string> unclaimedToolNames = new(functionAndMcpToolNames, StringComparer.Ordinal);

            foreach (AITool tool in tools)
            {
                if (tool is not HostedToolSearchTool toolSearch)
                {
                    continue;
                }

                if (toolSearch.DeferredTools is not { } deferredTools)
                {
                    deferAll = true;
                    deferredToolNames.UnionWith(functionAndMcpToolNames);

                    if (toolSearch.Namespace is { } nsName && unclaimedToolNames.Count > 0)
                    {
                        Namespace ns = GetOrCreateNamespace(namespacesByName, nsName, toolSearch.NamespaceDescription);
                        foreach (string toolName in unclaimedToolNames)
                        {
                            namespacedToolNames[toolName] = ns;
                        }

                        unclaimedToolNames.Clear();
                    }

                    continue;
                }

                foreach (string deferredTool in deferredTools)
                {
                    if (!functionAndMcpToolNames.Contains(deferredTool))
                    {
                        continue;
                    }

                    _ = deferredToolNames.Add(deferredTool);
                    if (toolSearch.Namespace is { } nsName && unclaimedToolNames.Remove(deferredTool))
                    {
                        namespacedToolNames[deferredTool] = GetOrCreateNamespace(namespacesByName, nsName, toolSearch.NamespaceDescription);
                    }
                }
            }

            return new(deferAll, deferredToolNames, namespacedToolNames);
        }

        public bool IsDeferred(string toolName) =>
            _deferAll || _deferredToolNames.Contains(toolName);

        public Namespace? GetNamespace(string toolName) =>
            _namespacedToolNames.TryGetValue(toolName, out Namespace? ns) ? ns : null;

        // Prefers the first non-empty description supplied for a given namespace name; later
        // HostedToolSearchTool instances may upgrade a previously-empty description but cannot
        // overwrite one that's already set.
        private static Namespace GetOrCreateNamespace(Dictionary<string, Namespace> namespacesByName, string name, string? description)
        {
            if (!namespacesByName.TryGetValue(name, out Namespace? existing))
            {
                existing = new Namespace(name) { Description = description };
                namespacesByName[name] = existing;
            }
            else if (string.IsNullOrEmpty(existing.Description))
            {
                existing.Description = description;
            }

            return existing;
        }

        // A class (not a record) so that all entries in _namespacedToolNames sharing a namespace
        // name reference the same instance; updating Description in place propagates to every
        // tool already grouped under it.
        internal sealed class Namespace
        {
            public Namespace(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public string? Description { get; set; }
        }
    }

    internal static ResponseTextFormat? ToOpenAIResponseTextFormat(ChatResponseFormat? format, ChatOptions? options = null) =>
        format switch
        {
            ChatResponseFormatText => ResponseTextFormat.CreateTextFormat(),

            ChatResponseFormatJson jsonFormat when OpenAIClientExtensions.StrictSchemaTransformCache.GetOrCreateTransformedSchema(jsonFormat) is { } jsonSchema =>
                ResponseTextFormat.CreateJsonSchemaFormat(
                    jsonFormat.SchemaName ?? "json_schema",
                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(jsonSchema, OpenAIJsonContext.Default.JsonElement)),
                    jsonFormat.SchemaDescription,
                    OpenAIClientExtensions.HasStrict(options?.AdditionalProperties)),

            ChatResponseFormatJson => ResponseTextFormat.CreateJsonObjectFormat(),

            _ => null,
        };

    private static ResponseReasoningOptions? ToOpenAIResponseReasoningOptions(ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return null;
        }

        ResponseReasoningEffortLevel? effortLevel = reasoning.Effort switch
        {
            ReasoningEffort.None => ResponseReasoningEffortLevel.None,
            ReasoningEffort.Low => ResponseReasoningEffortLevel.Low,
            ReasoningEffort.Medium => ResponseReasoningEffortLevel.Medium,
            ReasoningEffort.High => ResponseReasoningEffortLevel.High,
            ReasoningEffort.ExtraHigh => new ResponseReasoningEffortLevel("xhigh"),
            _ => (ResponseReasoningEffortLevel?)null,
        };

        ResponseReasoningSummaryVerbosity? summary = reasoning.Output switch
        {
            ReasoningOutput.Summary => ResponseReasoningSummaryVerbosity.Concise,
            ReasoningOutput.Full => ResponseReasoningSummaryVerbosity.Detailed,
            _ => (ResponseReasoningSummaryVerbosity?)null, // None or null - let OpenAI use its default
        };

        if (effortLevel is null && summary is null)
        {
            return null;
        }

        return new ResponseReasoningOptions
        {
            ReasoningEffortLevel = effortLevel,
            ReasoningSummaryVerbosity = summary,
        };
    }

    /// <summary>Convert a sequence of <see cref="ChatMessage"/>s to <see cref="ResponseItem"/>s.</summary>
    internal static IEnumerable<ResponseItem> ToOpenAIResponseItems(IEnumerable<ChatMessage> inputs, ChatOptions? options)
    {
        _ = options; // currently unused

        Dictionary<string, McpServerToolCallContent>? idToContentMapping = null;

        foreach (ChatMessage input in inputs)
        {
            if (input.Role == ChatRole.System ||
                input.Role == OpenAIClientExtensions.ChatRoleDeveloper)
            {
                string text = input.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return input.Role == ChatRole.System ?
                        ResponseItem.CreateSystemMessageItem(text) :
                        ResponseItem.CreateDeveloperMessageItem(text);
                }

                continue;
            }

            if (input.Role == ChatRole.User)
            {
                // Some AIContent items may map to ResponseItems directly. Others map to ResponseContentParts that need to be grouped together.
                // In order to preserve ordering, we yield ResponseItems as we find them, grouping ResponseContentParts between those yielded
                // items together into their own yielded item.

                List<ResponseContentPart>? parts = null;
                bool responseItemYielded = false;

                foreach (AIContent item in input.Contents)
                {
                    // Items that directly map to a ResponseItem.
                    ResponseItem? directItem = item switch
                    {
                        { RawRepresentation: ResponseItem rawRep } => rawRep,
                        ToolApprovalResponseContent { ToolCall: McpServerToolCallContent } toolResp => ResponseItem.CreateMcpApprovalResponseItem(toolResp.RequestId, toolResp.Approved),
                        _ => null
                    };

                    if (directItem is not null)
                    {
                        // Yield any parts already accumulated.
                        if (parts is not null)
                        {
                            yield return ResponseItem.CreateUserMessageItem(parts);
                            parts = null;
                        }

                        // Now yield the directly mapped item.
                        yield return directItem;

                        responseItemYielded = true;
                        continue;
                    }

                    // Items that map into ResponseContentParts and are grouped.
                    switch (item)
                    {
                        case AIContent when item.RawRepresentation is ResponseContentPart rawRep:
                            (parts ??= []).Add(rawRep);
                            break;

                        case TextContent textContent:
                            (parts ??= []).Add(ResponseContentPart.CreateInputTextPart(textContent.Text));
                            break;

                        case UriContent uriContent when uriContent.HasTopLevelMediaType("image"):
                            (parts ??= []).Add(ResponseContentPart.CreateInputImagePart(uriContent.Uri, GetImageDetail(item)));
                            break;

                        case UriContent uriContent when uriContent.MediaType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase):
                            (parts ??= []).Add(ResponseContentPart.CreateInputFilePart(uriContent.Uri));
                            break;

                        case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                            (parts ??= []).Add(ResponseContentPart.CreateInputImagePart(BinaryData.FromBytes(dataContent.Data, dataContent.MediaType), GetImageDetail(item)));
                            break;

                        case DataContent dataContent when dataContent.MediaType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase):
                            (parts ??= []).Add(ResponseContentPart.CreateInputFilePart(BinaryData.FromBytes(dataContent.Data, dataContent.MediaType), dataContent.MediaType, dataContent.Name ?? $"{Guid.NewGuid():N}.pdf"));
                            break;

                        case HostedFileContent fileContent when fileContent.HasTopLevelMediaType("image"):
                            (parts ??= []).Add(ResponseContentPart.CreateInputImagePart(fileContent.FileId, GetImageDetail(item)));
                            break;

                        case HostedFileContent fileContent:
                            (parts ??= []).Add(ResponseContentPart.CreateInputFilePart(fileContent.FileId));
                            break;

                        case ErrorContent errorContent when errorContent.ErrorCode == nameof(ResponseContentPartKind.Refusal):
                            (parts ??= []).Add(ResponseContentPart.CreateRefusalPart(errorContent.Message));
                            break;
                    }
                }

                // If we haven't accumulated any parts nor have we yielded any items, manufacture an empty input text part
                // to guarantee that every user message results in at least one ResponseItem.
                if (parts is null && !responseItemYielded)
                {
                    parts = [];
                    parts.Add(ResponseContentPart.CreateInputTextPart(string.Empty));
                    responseItemYielded = true;
                }

                // Final yield of any accumulated parts.
                if (parts is not null)
                {
                    yield return ResponseItem.CreateUserMessageItem(parts);
                    parts = null;
                }

                continue;
            }

            if (input.Role == ChatRole.Tool)
            {
                foreach (AIContent item in input.Contents)
                {
                    switch (item)
                    {
                        case AIContent when item.RawRepresentation is ResponseItem rawRep:
                            yield return rawRep;
                            break;

                        case ToolApprovalResponseContent toolResp when toolResp.ToolCall is McpServerToolCallContent:
                            yield return ResponseItem.CreateMcpApprovalResponseItem(toolResp.RequestId, toolResp.Approved);
                            break;

                        case FunctionResultContent resultContent:
                            static FunctionCallOutputResponseItem SerializeAIContent(string callId, IEnumerable<AIContent> contents)
                            {
                                List<FunctionToolCallOutputElement> elements = [];

                                foreach (var content in contents)
                                {
                                    switch (content)
                                    {
                                        case TextContent tc:
                                            elements.Add(new()
                                            {
                                                Type = "input_text",
                                                Text = tc.Text
                                            });
                                            break;

                                        case DataContent dc when dc.HasTopLevelMediaType("image"):
                                            elements.Add(new()
                                            {
                                                Type = "input_image",
                                                ImageUrl = dc.Uri
                                            });
                                            break;

                                        case DataContent dc:
                                            elements.Add(new()
                                            {
                                                Type = "input_file",
                                                FileData = dc.Uri, // contrary to the docs, file_data is expected to be a data URI, not just the base64 portion
                                                FileName = dc.Name ?? $"file_{Guid.NewGuid():N}", // contrary to the docs, file_name is required
                                            });
                                            break;

                                        case UriContent uc when uc.HasTopLevelMediaType("image"):
                                            elements.Add(new()
                                            {
                                                Type = "input_image",
                                                ImageUrl = uc.Uri.AbsoluteUri,
                                            });
                                            break;

                                        case UriContent uc:
                                            elements.Add(new()
                                            {
                                                Type = "input_file",
                                                FileUrl = uc.Uri.AbsoluteUri,
                                            });
                                            break;

                                        case HostedFileContent fc:
                                            elements.Add(new()
                                            {
                                                Type = fc.HasTopLevelMediaType("image") ? "input_image" : "input_file",
                                                FileId = fc.FileId,
                                                FileName = fc.Name,
                                            });
                                            break;

                                        default:
                                            // Fallback to serializing and storing the resulting JSON as text.
                                            try
                                            {
                                                elements.Add(new()
                                                {
                                                    Type = "input_text",
                                                    Text = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object))),
                                                });
                                            }
                                            catch (NotSupportedException)
                                            {
                                                // If the type can't be serialized, skip it.
                                            }
                                            break;
                                    }
                                }

                                FunctionCallOutputResponseItem outputItem = new(callId, string.Empty);
                                if (elements.Count > 0)
                                {
                                    outputItem.Patch.Set("$.output"u8, JsonSerializer.SerializeToUtf8Bytes(elements, OpenAIJsonContext.Default.ListFunctionToolCallOutputElement).AsSpan());
                                }

                                return outputItem;
                            }

                            switch (resultContent.Result)
                            {
                                case AIContent ac:
                                    yield return SerializeAIContent(resultContent.CallId, [ac]);
                                    break;

                                case IEnumerable<AIContent> items:
                                    yield return SerializeAIContent(resultContent.CallId, items);
                                    break;

                                default:
                                    string? result = resultContent.Result as string;
                                    if (result is null && resultContent.Result is { } resultObj)
                                    {
                                        try
                                        {
                                            result = JsonSerializer.Serialize(resultContent.Result, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
                                        }
                                        catch (NotSupportedException)
                                        {
                                            // If the type can't be serialized, skip it.
                                        }
                                    }

                                    yield return ResponseItem.CreateFunctionCallOutputItem(resultContent.CallId, result ?? string.Empty);
                                    break;
                            }
                            break;
                    }
                }

                continue;
            }

            if (input.Role == ChatRole.Assistant)
            {
                foreach (AIContent item in input.Contents)
                {
                    switch (item)
                    {
                        case AIContent when item.RawRepresentation is ResponseItem rawRep:
                            yield return rawRep;
                            break;

                        case TextContent textContent:
                            yield return ResponseItem.CreateAssistantMessageItem(textContent.Text);
                            break;

                        case TextReasoningContent reasoningContent:
                            ReasoningResponseItem reasoningResponseItem = new(reasoningContent.Text)
                            {
                                EncryptedContent = reasoningContent.ProtectedData,
                            };

                            // Restore the reasoning item's id (stashed in AdditionalProperties on the way in)
                            // so that encrypted reasoning roundtrips in stateless (store=false) scenarios,
                            // where the service rejects a reasoning item that is missing its id. The value may
                            // be a string (in-process) or a JsonElement (after the history has been serialized
                            // and rehydrated, e.g. a persisted human-in-the-loop approval session).
                            if (reasoningContent.AdditionalProperties?.TryGetValue(ReasoningItemIdKey, out object? reasoningItemIdValue) is true)
                            {
                                string? reasoningItemId =
                                    reasoningItemIdValue as string ??
                                    (reasoningItemIdValue is JsonElement { ValueKind: JsonValueKind.String } jsonElement ? jsonElement.GetString() : null);

                                if (!string.IsNullOrEmpty(reasoningItemId))
                                {
                                    reasoningResponseItem.Id = reasoningItemId;
                                }
                            }

                            yield return reasoningResponseItem;
                            break;

                        case McpServerToolCallContent mstcc:
                            (idToContentMapping ??= [])[mstcc.CallId] = mstcc;
                            break;

                        case FunctionCallContent callContent:
                            yield return ResponseItem.CreateFunctionCallItem(
                                callContent.CallId,
                                callContent.Name,
                                BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(
                                    callContent.Arguments,
                                    AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))));
                            break;

                        case ToolApprovalRequestContent toolReq when toolReq.ToolCall is McpServerToolCallContent mcpToolCall:
                            yield return ResponseItem.CreateMcpApprovalRequestItem(
                                toolReq.RequestId,
                                mcpToolCall.ServerName,
                                mcpToolCall.Name,
                                BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(
                                    mcpToolCall.Arguments!,
                                    AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))));
                            break;

                        case McpServerToolResultContent mstrc:
                            if (idToContentMapping?.TryGetValue(mstrc.CallId, out McpServerToolCallContent? associatedCall) is true)
                            {
                                _ = idToContentMapping.Remove(mstrc.CallId);
                                McpToolCallItem mtci = ResponseItem.CreateMcpToolCallItem(
                                    associatedCall.ServerName,
                                    associatedCall.Name,
                                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(
                                        associatedCall.Arguments!,
                                        AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))));
                                if (mstrc.Outputs?.OfType<ErrorContent>().FirstOrDefault() is ErrorContent errorContent)
                                {
                                    mtci.Error = BinaryData.FromString(errorContent.Message);
                                }
                                else
                                {
                                    mtci.ToolOutput = string.Concat(mstrc.Outputs?.OfType<TextContent>() ?? []);
                                }

                                yield return mtci;
                            }

                            break;
                    }
                }

                continue;
            }
        }
    }

    /// <summary>Extract usage details from a <see cref="ResponseResult"/> into a <see cref="UsageDetails"/>.</summary>
    private static UsageDetails? ToUsageDetails(ResponseResult? responseResult)
    {
        UsageDetails? ud = null;
        if (responseResult?.Usage is { } usage)
        {
            ud = new()
            {
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                TotalTokenCount = usage.TotalTokenCount,
                CachedInputTokenCount = usage.InputTokenDetails?.CachedTokenCount,
                ReasoningTokenCount = usage.OutputTokenDetails?.ReasoningTokenCount,
            };
        }

        return ud;
    }

    /// <summary>Converts a <see cref="UsageDetails"/> to a <see cref="ResponseTokenUsage"/>.</summary>
    internal static ResponseTokenUsage? ToResponseTokenUsage(UsageDetails? usageDetails)
    {
        ResponseTokenUsage? rtu = null;
        if (usageDetails is not null)
        {
            rtu = new()
            {
                InputTokenCount = (int?)usageDetails.InputTokenCount ?? 0,
                OutputTokenCount = (int?)usageDetails.OutputTokenCount ?? 0,
                TotalTokenCount = (int?)usageDetails.TotalTokenCount ?? 0,
                InputTokenDetails = new(),
                OutputTokenDetails = new(),
            };

            if (usageDetails.AdditionalCounts is { } additionalCounts)
            {
                if (additionalCounts.TryGetValue($"{nameof(ResponseTokenUsage.InputTokenDetails)}.{nameof(ResponseInputTokenUsageDetails.CachedTokenCount)}", out int? cachedTokenCount))
                {
                    rtu.InputTokenDetails.CachedTokenCount = cachedTokenCount.GetValueOrDefault();
                }

                if (additionalCounts.TryGetValue($"{nameof(ResponseTokenUsage.OutputTokenDetails)}.{nameof(ResponseOutputTokenUsageDetails.ReasoningTokenCount)}", out int? reasoningTokenCount))
                {
                    rtu.OutputTokenDetails.ReasoningTokenCount = reasoningTokenCount.GetValueOrDefault();
                }
            }
        }

        return rtu;
    }

    /// <summary>Convert a sequence of <see cref="ResponseContentPart"/>s to a list of <see cref="AIContent"/>.</summary>
    private static List<AIContent> ToAIContents(IEnumerable<ResponseContentPart> contents)
    {
        List<AIContent> results = [];

        foreach (ResponseContentPart part in contents)
        {
            AIContent content;
            switch (part.Kind)
            {
                case ResponseContentPartKind.InputText or ResponseContentPartKind.OutputText:
                    TextContent text = new(part.Text);
                    PopulateAnnotations(part, text);
                    content = text;
                    break;

                case ResponseContentPartKind.InputFile or ResponseContentPartKind.InputImage:
                    if (!string.IsNullOrWhiteSpace(part.InputImageFileId))
                    {
                        content = new HostedFileContent(part.InputImageFileId) { MediaType = "image/*" };
                    }
                    else if (!string.IsNullOrWhiteSpace(part.InputFileId))
                    {
                        content = new HostedFileContent(part.InputFileId) { Name = part.InputFilename };
                    }
                    else if (part.InputFileBytes is not null)
                    {
                        content = new DataContent(part.InputFileBytes, part.InputFileBytesMediaType ?? "application/octet-stream") { Name = part.InputFilename };
                    }
                    else if (part.InputImageUri is { } inputImageUrl)
                    {
                        if (inputImageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            content = new DataContent(inputImageUrl);
                        }
                        else if (Uri.TryCreate(inputImageUrl, UriKind.Absolute, out Uri? imageUri))
                        {
                            content = new UriContent(imageUri, OpenAIClientExtensions.ImageUriToMediaType(imageUri));
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    else
                    {
                        goto default;
                    }

                    break;

                case ResponseContentPartKind.Refusal:
                    content = new ErrorContent(part.Refusal)
                    {
                        ErrorCode = nameof(ResponseContentPartKind.Refusal),
                    };
                    break;

                default:
                    content = new();
                    break;
            }

            content.RawRepresentation = part;
            results.Add(content);
        }

        return results;
    }

    /// <summary>Converts any annotations from <paramref name="source"/> and stores them in <paramref name="destination"/>.</summary>
    private static void PopulateAnnotations(ResponseContentPart source, AIContent destination)
    {
        if (source.OutputTextAnnotations is { Count: > 0 })
        {
            foreach (var ota in source.OutputTextAnnotations)
            {
                CitationAnnotation ca = new()
                {
                    RawRepresentation = ota,
                };

                switch (ota)
                {
                    case ContainerFileCitationMessageAnnotation cfcma:
                        ca.AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = cfcma.StartIndex, EndIndex = cfcma.EndIndex }];
                        ca.FileId = cfcma.FileId;
                        ca.Title = cfcma.Filename;
                        break;

                    case FilePathMessageAnnotation fpma:
                        ca.FileId = fpma.FileId;
                        break;

                    case FileCitationMessageAnnotation fcma:
                        ca.FileId = fcma.FileId;
                        ca.Title = fcma.Filename;
                        break;

                    case UriCitationMessageAnnotation ucma:
                        ca.AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = ucma.StartIndex, EndIndex = ucma.EndIndex }];
                        ca.Url = ucma.Uri;
                        ca.Title = ucma.Title;
                        break;
                }

                (destination.Annotations ??= []).Add(ca);
            }
        }
    }

    /// <summary>
    /// Extracts web search queries from a <see cref="WebSearchCallResponseItem"/>.
    /// </summary>
    private static List<string>? GetWebSearchQueries(WebSearchCallResponseItem wscri)
    {
        if (wscri.Action is WebSearchSearchAction searchAction)
        {
            if (searchAction.Queries is { Count: > 0 } queries)
            {
                return [.. queries];
            }

#pragma warning disable CS0618 // Query is deprecated in favor of Queries; used here as a fallback
            if (searchAction.Query is not null)
            {
                return [searchAction.Query];
            }
#pragma warning restore CS0618
        }

        return null;
    }

    /// <summary>
    /// Extracts web search sources from a <see cref="WebSearchCallResponseItem"/> when available.
    /// Sources are present when the developer opts in via <c>include: ["web_search_call.action.sources"]</c>.
    /// </summary>
    private static List<AIContent>? GetWebSearchSources(WebSearchCallResponseItem wscri)
    {
        if (wscri.Action is not WebSearchSearchAction { Sources.Count: > 0 } searchAction)
        {
            return null;
        }

        List<AIContent>? results = null;
        foreach (var source in searchAction.Sources)
        {
            if (source is WebSearchActionUriSource { Uri: not null } uriSource)
            {
                (results ??= []).Add(new UriContent(uriSource.Uri, "text/html")
                {
                    RawRepresentation = uriSource,
                });
            }
        }

        return results;
    }

    /// <summary>Adds new <see cref="AIContent"/> for the specified <paramref name="mtci"/> into <paramref name="contents"/>.</summary>
    private static void AddMcpToolCallContent(McpToolCallItem mtci, IList<AIContent> contents)
    {
        contents.Add(new McpServerToolCallContent(mtci.Id, mtci.ToolName, mtci.ServerLabel)
        {
            Arguments = JsonSerializer.Deserialize(mtci.ToolArguments, OpenAIJsonContext.Default.IDictionaryStringObject),

            // We purposefully do not set the RawRepresentation on the McpServerToolCallContent, only on the McpServerToolResultContent, to avoid
            // the same McpToolCallItem being included on two different AIContent instances. When these are roundtripped, we want only one
            // McpToolCallItem sent back for the pair.
        });

        contents.Add(new McpServerToolResultContent(mtci.Id)
        {
            RawRepresentation = mtci,
            Outputs = [mtci.Error is not null ?
                new ErrorContent(mtci.Error.ToString()) :
                new TextContent(mtci.ToolOutput)],
        });
    }

    /// <summary>Adds all of the tool names from <paramref name="toolNames"/> to <paramref name="filter"/>.</summary>
    private static void AddAllMcpFilters(IList<string> toolNames, McpToolFilter filter)
    {
        foreach (var toolName in toolNames)
        {
            filter.ToolNames.Add(toolName);
        }
    }

    /// <summary>Creates a <see cref="CodeInterpreterToolResultContent"/> for the specified <paramref name="cicri"/>.</summary>
    private static CodeInterpreterToolResultContent CreateCodeInterpreterResultContent(CodeInterpreterCallResponseItem cicri)
    {
        List<AIContent>? outputContents = null;

        if (cicri.Outputs is { Count: > 0 } outputs)
        {
            outputContents = [];
            foreach (var o in outputs)
            {
                switch (o)
                {
                    case CodeInterpreterCallImageOutput cicio:
                        outputContents.Add(new UriContent(cicio.ImageUri, OpenAIClientExtensions.ImageUriToMediaType(cicio.ImageUri)) { RawRepresentation = cicio });
                        break;

                    case CodeInterpreterCallLogsOutput ciclo:
                        outputContents.Add(new TextContent(ciclo.Logs) { RawRepresentation = ciclo });
                        break;

                    default:
                        // The SDK doesn't publicly expose file output types, so try to extract
                        // file references from the raw JSON via the Patch property.
                        AddHostedFileContents(outputContents, o, cicri.ContainerId);
                        break;
                }
            }

            if (outputContents.Count == 0)
            {
                outputContents = null;
            }
        }

        return new(cicri.Id)
        {
            Outputs = outputContents,
            RawRepresentation = cicri,
        };
    }

    /// <summary>
    /// Tries to extract file references from an unknown <see cref="CodeInterpreterCallOutput"/> by
    /// reading its JSON data via the <see cref="CodeInterpreterCallOutput.Patch"/> property.
    /// </summary>
    private static void AddHostedFileContents(List<AIContent> contents, CodeInterpreterCallOutput output, string? containerId)
    {
        // Try to read a "files" array from the output's raw JSON. The OpenAI API returns file outputs
        // with a "files" array containing objects with "file_id" and "mime_type" properties, but the
        // SDK doesn't expose a public type for them.
        if (!output.Patch.TryGetJson("$.files"u8, out ReadOnlyMemory<byte> filesJson))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(filesJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var fileElement in doc.RootElement.EnumerateArray())
            {
                string? fileId =
                    (fileElement.TryGetProperty("file_id", out var idProp) ? idProp.GetString() : null) ??
                    (fileElement.TryGetProperty("id", out idProp) ? idProp.GetString() : null);

                if (fileId is null)
                {
                    continue;
                }

                string? mimeType = fileElement.TryGetProperty("mime_type", out var mimeProp) ? mimeProp.GetString() : null;

                var hfc = new HostedFileContent(fileId) { MediaType = mimeType, RawRepresentation = output };
                if (containerId is not null)
                {
                    hfc.Scope = containerId;
                }

                contents.Add(hfc);
            }
        }
    }

    private static void AddImageGenerationContents(ImageGenerationCallResponseItem outputItem, CreateResponseOptions? options, IList<AIContent> contents)
    {
        contents.Add(new ImageGenerationToolCallContent(outputItem.Id));
        contents.Add(CreateImageGenerationResultContent(outputItem, options));
    }

    private static ImageGenerationToolResultContent CreateImageGenerationResultContent(ImageGenerationCallResponseItem outputItem, CreateResponseOptions? options)
    {
        var imageGenTool = options?.Tools.OfType<ImageGenerationTool>().FirstOrDefault();
        string outputFormat = imageGenTool?.OutputFileFormat?.ToString() ?? "png";

        return new ImageGenerationToolResultContent(outputItem.Id)
        {
            RawRepresentation = outputItem,
            Outputs = [new DataContent(outputItem.ImageResultBytes, $"image/{outputFormat}")]
        };
    }

    private static ImageGenerationToolResultContent GetImageGenerationResult(StreamingResponseImageGenerationCallPartialImageUpdate update, CreateResponseOptions? options)
    {
        var imageGenTool = options?.Tools.OfType<ImageGenerationTool>().FirstOrDefault();
        var outputType = imageGenTool?.OutputFileFormat?.ToString() ?? "png";

        return new ImageGenerationToolResultContent(update.ItemId)
        {
            RawRepresentation = update,
            Outputs =
            [
                new DataContent(update.PartialImageBytes, $"image/{outputType}")
                {
                    AdditionalProperties = new()
                    {
                        [nameof(update.ItemId)] = update.ItemId,
                        [nameof(update.OutputIndex)] = update.OutputIndex,
                        [nameof(update.PartialImageIndex)] = update.PartialImageIndex
                    }
                }
            ]
        };
    }

    private static ResponsesClientContinuationToken? CreateContinuationToken(ResponseResult responseResult) =>
        CreateContinuationToken(
            responseId: responseResult.Id,
            responseStatus: responseResult.Status,
            isBackgroundModeEnabled: responseResult.BackgroundModeEnabled);

    private static ResponsesClientContinuationToken? CreateContinuationToken(
        string responseId,
        ResponseStatus? responseStatus,
        bool? isBackgroundModeEnabled,
        int? updateSequenceNumber = null)
    {
        if (isBackgroundModeEnabled is not true)
        {
            return null;
        }

        // Returns a continuation token for in-progress or queued responses as they are not yet complete.
        // Also returns a continuation token if there is no status but there is a sequence number,
        // which can occur for certain streaming updates related to response content part updates: response.content_part.*,
        // response.output_text.*
        if ((responseStatus is ResponseStatus.InProgress or ResponseStatus.Queued) ||
            (responseStatus is null && updateSequenceNumber is not null))
        {
            return new ResponsesClientContinuationToken(responseId)
            {
                SequenceNumber = updateSequenceNumber,
            };
        }

        // For all other statuses: completed, failed, canceled, incomplete
        // return null to indicate the operation is finished allowing the caller
        // to stop and access the final result, failure details, reason for incompletion, etc.
        return null;
    }

    private static ResponsesClientContinuationToken? GetContinuationToken(IEnumerable<ChatMessage> messages, ChatOptions? options = null)
    {
        if (options?.ContinuationToken is { } token)
        {
            if (messages.Any())
            {
                throw new InvalidOperationException("Messages are not allowed when continuing a background response using a continuation token.");
            }

            return ResponsesClientContinuationToken.FromToken(token);
        }

        return null;
    }

    private static ResponseImageDetailLevel? GetImageDetail(AIContent content)
    {
        if (content.AdditionalProperties?.TryGetValue("detail", out object? value) is true)
        {
            return value switch
            {
                string detailString => new ResponseImageDetailLevel(detailString),
                ResponseImageDetailLevel detail => detail,

                // The SDK defines nullable and non-nullable op_Implicit conversions from string. Force a nullable result so
                // null does not bind to the non-nullable conversion, which throws ArgumentNullException.
                _ => (ResponseImageDetailLevel?)null
            };
        }

        return null;
    }

    /// <summary>Provides an <see cref="AITool"/> wrapper for a <see cref="ResponseTool"/>.</summary>
    internal sealed class ResponseToolAITool(ResponseTool tool) : AITool
    {
        public ResponseTool Tool => tool;
        public override string Name => Tool.GetType().Name;

        /// <inheritdoc />
        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = Throw.IfNull(serviceType);

            return
                serviceKey is null && serviceType.IsInstanceOfType(Tool) ? Tool :
                base.GetService(serviceType, serviceKey);
        }
    }

    /// <summary>DTO for an array element in OpenAI Responses' "Function tool call output".</summary>
    internal sealed class FunctionToolCallOutputElement
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; } // input_text, input_image, or input_file

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("file_id")]
        public string? FileId { get; set; }

        [JsonPropertyName("file_data")]
        public string? FileData { get; set; }

        [JsonPropertyName("file_url")]
        public string? FileUrl { get; set; }

        [JsonPropertyName("filename")]
        public string? FileName { get; set; }
    }
}
