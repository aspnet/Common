// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace OpenAI.Chat;

/// <summary>Provides extension methods for working with content associated with OpenAI.Chat.</summary>
public static class MicrosoftExtensionsAIChatExtensions
{
    /// <summary>Creates an OpenAI <see cref="ChatTool"/> from an <see cref="AIFunctionDeclaration"/>.</summary>
    /// <param name="function">The function to convert.</param>
    /// <returns>An OpenAI <see cref="ChatTool"/> representing <paramref name="function"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    public static ChatTool AsOpenAIChatTool(this AIFunctionDeclaration function) =>
        OpenAIChatClient.ToOpenAIChatTool(Throw.IfNull(function));

    /// <summary>
    /// Creates an OpenAI <see cref="ChatResponseFormat"/> from a <see cref="Microsoft.Extensions.AI.ChatResponseFormat"/>.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <param name="options">The options to use when interpreting the format.</param>
    /// <returns>The converted OpenAI <see cref="ChatResponseFormat"/>.</returns>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static ChatResponseFormat? AsOpenAIChatResponseFormat(this Microsoft.Extensions.AI.ChatResponseFormat? format, ChatOptions? options = null) =>
        OpenAIChatClient.ToOpenAIChatResponseFormat(format, options);

    /// <summary>Creates a sequence of OpenAI <see cref="ChatMessage"/> instances from the specified input messages.</summary>
    /// <param name="messages">The input messages to convert.</param>
    /// <param name="options">The options employed while processing <paramref name="messages"/>.</param>
    /// <returns>A sequence of OpenAI chat messages.</returns>
    public static IEnumerable<ChatMessage> AsOpenAIChatMessages(this IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null) =>
        OpenAIChatClient.ToOpenAIChatMessages(Throw.IfNull(messages), options);

    /// <summary>Creates an OpenAI <see cref="ChatCompletion"/> from a <see cref="ChatResponse"/>.</summary>
    /// <param name="response">The <see cref="ChatResponse"/> to convert to a <see cref="ChatCompletion"/>.</param>
    /// <returns>A converted <see cref="ChatCompletion"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static ChatCompletion AsOpenAIChatCompletion(this ChatResponse response)
    {
        _ = Throw.IfNull(response);

        if (response.RawRepresentation is ChatCompletion chatCompletion)
        {
            return chatCompletion;
        }

        var lastMessage = response.Messages.LastOrDefault();

        ChatMessageRole role = ToChatMessageRole(lastMessage?.Role);

        ChatFinishReason finishReason = ToChatFinishReason(response.FinishReason);

        ChatTokenUsage usage = OpenAIChatModelFactory.ChatTokenUsage(
            (int?)response.Usage?.OutputTokenCount ?? 0,
            (int?)response.Usage?.InputTokenCount ?? 0,
            (int?)response.Usage?.TotalTokenCount ?? 0);

        IEnumerable<ChatToolCall>? toolCalls = lastMessage?.Contents
            .OfType<FunctionCallContent>().Select(c => ChatToolCall.CreateFunctionToolCall(c.CallId, c.Name,
                new BinaryData(JsonSerializer.SerializeToUtf8Bytes(c.Arguments, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))))));

        return OpenAIChatModelFactory.ChatCompletion(
            response.ResponseId,
            finishReason,
            new(OpenAIChatClient.ToOpenAIChatContent(lastMessage?.Contents ?? [])),
            toolCalls: toolCalls,
            role: role,
            createdAt: response.CreatedAt ?? default,
            model: response.ModelId,
            usage: usage,
            outputAudio: lastMessage?.Contents.OfType<DataContent>().Where(dc => dc.HasTopLevelMediaType("audio")).Select(a => OpenAIChatModelFactory.ChatOutputAudio(new(a.Data))).FirstOrDefault(),
            messageAnnotations: ConvertAnnotations(lastMessage?.Contents));

        static IEnumerable<ChatMessageAnnotation> ConvertAnnotations(IEnumerable<AIContent>? contents)
        {
            if (contents is null)
            {
                yield break;
            }

            foreach (var content in contents)
            {
                if (content.Annotations is null)
                {
                    continue;
                }

                foreach (var annotation in content.Annotations)
                {
                    if (annotation is not CitationAnnotation citation)
                    {
                        continue;
                    }

                    if (citation.AnnotatedRegions?.OfType<TextSpanAnnotatedRegion>().ToArray() is { Length: > 0 } regions)
                    {
                        foreach (var region in regions)
                        {
                            yield return OpenAIChatModelFactory.ChatMessageAnnotation(region.StartIndex ?? 0, region.EndIndex ?? 0, citation.Url, citation.Title);
                        }
                    }
                    else
                    {
                        yield return OpenAIChatModelFactory.ChatMessageAnnotation(webResourceUri: citation.Url, webResourceTitle: citation.Title);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a sequence of OpenAI <see cref="StreamingChatCompletionUpdate"/> instances from the specified
    /// sequence of <see cref="ChatResponseUpdate"/> instances.
    /// </summary>
    /// <param name="responseUpdates">The update instances.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A sequence of converted <see cref="ChatResponseUpdate"/> instances.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="responseUpdates"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static async IAsyncEnumerable<StreamingChatCompletionUpdate> AsOpenAIStreamingChatCompletionUpdatesAsync(
        this IAsyncEnumerable<ChatResponseUpdate> responseUpdates, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(responseUpdates);

        await foreach (var update in responseUpdates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update.RawRepresentation is StreamingChatCompletionUpdate streamingUpdate)
            {
                yield return streamingUpdate;
                continue;
            }

            var usage = update.Contents.FirstOrDefault(c => c is UsageContent) is UsageContent usageContent ?
                OpenAIChatModelFactory.ChatTokenUsage(
                    (int?)usageContent.Details.OutputTokenCount ?? 0,
                    (int?)usageContent.Details.InputTokenCount ?? 0,
                    (int?)usageContent.Details.TotalTokenCount ?? 0) :
                null;

            var toolCallUpdates = update.Contents.OfType<FunctionCallContent>().Select((fcc, index) =>
                OpenAIChatModelFactory.StreamingChatToolCallUpdate(
                    index,
                    fcc.CallId,
                    functionName: fcc.Name,
                    functionArgumentsUpdate: new(JsonSerializer.SerializeToUtf8Bytes(fcc.Arguments, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))))))
                .ToList();

            yield return OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                update.ResponseId,
                new(OpenAIChatClient.ToOpenAIChatContent(update.Contents)),
                toolCallUpdates: toolCallUpdates,
                role: ToChatMessageRole(update.Role),
                finishReason: update.FinishReason is null ? null : ToChatFinishReason(update.FinishReason),
                createdAt: update.CreatedAt ?? default,
                model: update.ModelId,
                usage: usage);
        }
    }

    /// <summary>Creates a sequence of <see cref="Microsoft.Extensions.AI.ChatMessage"/> instances from the specified input messages.</summary>
    /// <param name="messages">The input messages to convert.</param>
    /// <returns>A sequence of Microsoft.Extensions.AI chat messages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    public static IEnumerable<Microsoft.Extensions.AI.ChatMessage> AsChatMessages(this IEnumerable<ChatMessage> messages)
    {
        _ = Throw.IfNull(messages);

        foreach (var message in messages)
        {
            Microsoft.Extensions.AI.ChatMessage resultMessage = new()
            {
                RawRepresentation = message,
            };

            switch (message)
            {
                case AssistantChatMessage acm:
                    resultMessage.Role = ChatRole.Assistant;
                    resultMessage.AuthorName = acm.ParticipantName;
                    OpenAIChatClient.ConvertContentParts(acm.Content, resultMessage.Contents);
                    foreach (var toolCall in acm.ToolCalls)
                    {
                        var fcc = OpenAIClientExtensions.ParseCallContent(toolCall.FunctionArguments, toolCall.Id, toolCall.FunctionName);
                        fcc.RawRepresentation = toolCall;
                        resultMessage.Contents.Add(fcc);
                    }

                    break;

                case UserChatMessage ucm:
                    resultMessage.Role = ChatRole.User;
                    resultMessage.AuthorName = ucm.ParticipantName;
                    OpenAIChatClient.ConvertContentParts(ucm.Content, resultMessage.Contents);
                    break;

#pragma warning disable OPENAI001 // Developer role is experimental
                case DeveloperChatMessage dcm:
                    resultMessage.Role = ChatRole.System;
                    resultMessage.AuthorName = dcm.ParticipantName;
                    OpenAIChatClient.ConvertContentParts(dcm.Content, resultMessage.Contents);
                    break;
#pragma warning restore OPENAI001

                case SystemChatMessage scm:
                    resultMessage.Role = ChatRole.System;
                    resultMessage.AuthorName = scm.ParticipantName;
                    OpenAIChatClient.ConvertContentParts(scm.Content, resultMessage.Contents);
                    break;

                case ToolChatMessage tcm:
                    resultMessage.Role = ChatRole.Tool;
                    resultMessage.Contents.Add(new FunctionResultContent(tcm.ToolCallId, ToToolResult(tcm.Content))
                    {
                        RawRepresentation = tcm,
                    });

                    static object ToToolResult(ChatMessageContent content)
                    {
                        if (content.Count == 1 && content[0] is { Text: { } text })
                        {
                            return text;
                        }

                        MemoryStream ms = new();
                        using Utf8JsonWriter writer = new(ms, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        foreach (IJsonModel<ChatMessageContentPart> part in content)
                        {
                            part.Write(writer, ModelReaderWriterOptions.Json);
                        }

                        return JsonElement.Parse(ms.GetBuffer().AsSpan(0, (int)ms.Position));
                    }

                    break;
            }

            yield return resultMessage;
        }
    }

    /// <summary>Creates a Microsoft.Extensions.AI <see cref="ChatResponse"/> from a <see cref="ChatCompletion"/>.</summary>
    /// <param name="chatCompletion">The <see cref="ChatCompletion"/> to convert to a <see cref="ChatResponse"/>.</param>
    /// <param name="options">The options employed in the creation of the response.</param>
    /// <returns>A converted <see cref="ChatResponse"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatCompletion"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static ChatResponse AsChatResponse(this ChatCompletion chatCompletion, ChatCompletionOptions? options = null) =>
        OpenAIChatClient.FromOpenAIChatCompletion(Throw.IfNull(chatCompletion), options);

    /// <summary>
    /// Creates a sequence of Microsoft.Extensions.AI <see cref="ChatResponseUpdate"/> instances from the specified
    /// sequence of OpenAI <see cref="StreamingChatCompletionUpdate"/> instances.
    /// </summary>
    /// <param name="chatCompletionUpdates">The update instances.</param>
    /// <param name="options">The options employed in the creation of the response.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A sequence of converted <see cref="ChatResponseUpdate"/> instances.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatCompletionUpdates"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static IAsyncEnumerable<ChatResponseUpdate> AsChatResponseUpdatesAsync(
        this IAsyncEnumerable<StreamingChatCompletionUpdate> chatCompletionUpdates, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        OpenAIChatClient.FromOpenAIStreamingChatCompletionAsync(Throw.IfNull(chatCompletionUpdates), options, cancellationToken);

    /// <summary>Converts the <see cref="ChatRole"/> to a <see cref="ChatMessageRole"/>.</summary>
    private static ChatMessageRole ToChatMessageRole(ChatRole? role) =>
        role?.Value switch
        {
            "user" => ChatMessageRole.User,
            "function" => ChatMessageRole.Function,
            "tool" => ChatMessageRole.Tool,
#pragma warning disable OPENAI001 // Developer role is experimental
            "developer" => ChatMessageRole.Developer,
#pragma warning restore OPENAI001
            "system" => ChatMessageRole.System,
            _ => ChatMessageRole.Assistant,
        };

    /// <summary>Converts the <see cref="Microsoft.Extensions.AI.ChatFinishReason"/> to a <see cref="ChatFinishReason"/>.</summary>
    private static ChatFinishReason ToChatFinishReason(Microsoft.Extensions.AI.ChatFinishReason? finishReason) =>
        finishReason?.Value switch
        {
            "length" => ChatFinishReason.Length,
            "content_filter" => ChatFinishReason.ContentFilter,
            "tool_calls" => ChatFinishReason.ToolCalls,
            "function_call" => ChatFinishReason.FunctionCall,
            _ => ChatFinishReason.Stop,
        };
}
