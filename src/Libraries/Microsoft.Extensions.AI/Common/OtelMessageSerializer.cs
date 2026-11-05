// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

#pragma warning disable CA1307 // Specify StringComparison for clarity
#pragma warning disable CA1308 // Normalize strings to uppercase

namespace Microsoft.Extensions.AI;

/// <summary>Shared helpers for serializing chat messages to the OpenTelemetry gen-ai message-parts shape.</summary>
internal static class OtelMessageSerializer
{
    internal static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    private static readonly JsonElement _emptyObject =
        JsonSerializer.SerializeToElement(new object(), DefaultOptions.GetTypeInfo(typeof(object)));

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        JsonSerializerOptions options = new(OtelContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);
        options.MakeReadOnly();

        return options;
    }

    internal static string SerializeChatMessages(
        IEnumerable<ChatMessage> messages, ChatFinishReason? chatFinishReason = null, JsonSerializerOptions? customContentSerializerOptions = null)
    {
        List<object> output = [];

        string? finishReason =
            chatFinishReason?.Value is null ? null :
            chatFinishReason == ChatFinishReason.Length ? "length" :
            chatFinishReason == ChatFinishReason.ContentFilter ? "content_filter" :
            chatFinishReason == ChatFinishReason.ToolCalls ? "tool_call" :
            "stop";

        foreach (ChatMessage message in messages)
        {
            OtelMessage m = new()
            {
                FinishReason = finishReason,
                Role =
                    message.Role == ChatRole.Assistant ? "assistant" :
                    message.Role == ChatRole.Tool ? "tool" :
                    message.Role == ChatRole.System || message.Role == new ChatRole("developer") ? "system" :
                    "user",
                Name = message.AuthorName,
            };

            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    // These are all specified in the convention:

                    case TextContent tc when !string.IsNullOrWhiteSpace(tc.Text):
                        m.Parts.Add(new OtelGenericPart { Content = tc.Text });
                        break;

                    case TextReasoningContent trc when !string.IsNullOrWhiteSpace(trc.Text):
                        m.Parts.Add(new OtelGenericPart { Type = "reasoning", Content = trc.Text });
                        break;

                    case FunctionCallContent fcc:
                        m.Parts.Add(new OtelToolCallRequestPart
                        {
                            Id = fcc.CallId,
                            Name = fcc.Name,
                            Arguments = fcc.Arguments,
                        });
                        break;

                    case FunctionResultContent frc:
                        m.Parts.Add(new OtelToolCallResponsePart
                        {
                            Id = frc.CallId,
                            Response = frc.Result,
                        });
                        break;

                    case DataContent dc:
                        m.Parts.Add(new OtelBlobPart
                        {
                            Content = dc.Base64Data.ToString(),
                            MimeType = dc.MediaType,
                            Modality = DeriveModalityFromMediaType(dc.MediaType),
                        });
                        break;

                    case UriContent uc:
                        m.Parts.Add(new OtelUriPart
                        {
                            Uri = uc.Uri.AbsoluteUri,
                            MimeType = uc.MediaType,
                            Modality = DeriveModalityFromMediaType(uc.MediaType),
                        });
                        break;

                    case HostedFileContent fc:
                        m.Parts.Add(new OtelFilePart
                        {
                            FileId = fc.FileId,
                            MimeType = fc.MediaType,
                            Modality = DeriveModalityFromMediaType(fc.MediaType),
                        });
                        break;

                    // These are non-standard and are using the "generic" non-text part that provides an extensibility mechanism:

                    case HostedVectorStoreContent vsc:
                        m.Parts.Add(new OtelGenericPart { Type = "vector_store", Content = vsc.VectorStoreId });
                        break;

                    case ErrorContent ec:
                        m.Parts.Add(new OtelGenericPart { Type = "error", Content = ec.Message });
                        break;

                    // Server tool call content types as specified in the OpenTelemetry semantic conventions:

                    case CodeInterpreterToolCallContent citcc:
                        m.Parts.Add(new OtelServerToolCallPart<OtelCodeInterpreterToolCall>
                        {
                            Id = citcc.CallId,
                            Name = "code_interpreter",
                            ServerToolCall = new OtelCodeInterpreterToolCall
                            {
                                Code = ExtractCodeFromInputs(citcc.Inputs),
                            },
                        });
                        break;

                    case CodeInterpreterToolResultContent citrc:
                        m.Parts.Add(new OtelServerToolCallResponsePart<OtelCodeInterpreterToolCallResponse>
                        {
                            Id = citrc.CallId,
                            ServerToolCallResponse = new OtelCodeInterpreterToolCallResponse
                            {
                                Output = citrc.Outputs,
                            },
                        });
                        break;

                    case ImageGenerationToolCallContent igtcc:
                        m.Parts.Add(new OtelServerToolCallPart<OtelImageGenerationToolCall>
                        {
                            Id = igtcc.CallId,
                            Name = "image_generation",
                            ServerToolCall = new OtelImageGenerationToolCall(),
                        });
                        break;

                    case ImageGenerationToolResultContent igtrc:
                        m.Parts.Add(new OtelServerToolCallResponsePart<OtelImageGenerationToolCallResponse>
                        {
                            Id = igtrc.CallId,
                            ServerToolCallResponse = new OtelImageGenerationToolCallResponse
                            {
                                Output = igtrc.Outputs,
                            },
                        });
                        break;

                    case McpServerToolCallContent mstcc:
                        m.Parts.Add(new OtelServerToolCallPart<OtelMcpToolCall>
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
                        m.Parts.Add(new OtelServerToolCallResponsePart<OtelMcpToolCallResponse>
                        {
                            Id = mstrc.CallId,
                            ServerToolCallResponse = new OtelMcpToolCallResponse
                            {
                                Output = mstrc.Outputs,
                            },
                        });
                        break;

                    case ToolApprovalRequestContent fareqc when fareqc.ToolCall is McpServerToolCallContent mcpToolCall:
                        m.Parts.Add(new OtelServerToolCallPart<OtelMcpApprovalRequest>
                        {
                            Id = fareqc.RequestId,
                            Name = mcpToolCall.Name,
                            ServerToolCall = new OtelMcpApprovalRequest
                            {
                                Arguments = mcpToolCall.Arguments,
                                ServerName = mcpToolCall.ServerName,
                            },
                        });
                        break;

                    case ToolApprovalResponseContent farespc when farespc.ToolCall is McpServerToolCallContent:
                        m.Parts.Add(new OtelServerToolCallResponsePart<OtelMcpApprovalResponse>
                        {
                            Id = farespc.RequestId,
                            ServerToolCallResponse = new OtelMcpApprovalResponse
                            {
                                Approved = farespc.Approved,
                            },
                        });
                        break;

                    default:
                        JsonElement element = _emptyObject;
                        try
                        {
                            JsonTypeInfo? unknownContentTypeInfo =
                                customContentSerializerOptions?.TryGetTypeInfo(content.GetType(), out JsonTypeInfo? ctsi) is true ? ctsi :
                                DefaultOptions.TryGetTypeInfo(content.GetType(), out JsonTypeInfo? dtsi) ? dtsi :
                                null;

                            if (unknownContentTypeInfo is not null)
                            {
                                element = JsonSerializer.SerializeToElement(content, unknownContentTypeInfo);
                            }
                        }
                        catch
                        {
                            // Ignore the contents of any parts that can't be serialized.
                        }

                        m.Parts.Add(new OtelGenericPart
                        {
                            Type = content.GetType().FullName!,
                            Content = element,
                        });
                        break;
                }
            }

            output.Add(m);
        }

        return JsonSerializer.Serialize(output, DefaultOptions.GetTypeInfo(typeof(IList<object>)));
    }

    /// <summary>Derives the OTel <c>modality</c> classifier from a media type's top-level type.</summary>
    internal static string? DeriveModalityFromMediaType(string? mediaType)
    {
        if (mediaType is not null)
        {
            int pos = mediaType.IndexOf('/');
            if (pos >= 0)
            {
                ReadOnlySpan<char> topLevel = mediaType.AsSpan(0, pos);
                return
                    topLevel.Equals("image", StringComparison.OrdinalIgnoreCase) ? "image" :
                    topLevel.Equals("audio", StringComparison.OrdinalIgnoreCase) ? "audio" :
                    topLevel.Equals("video", StringComparison.OrdinalIgnoreCase) ? "video" :
                    null;
            }
        }

        return null;
    }

    /// <summary>Extracts code text from code interpreter inputs.</summary>
    /// <remarks>
    /// Code interpreter inputs typically contain a DataContent with a "text/x-python" or similar
    /// media type representing the code to execute.
    /// </remarks>
    private static string? ExtractCodeFromInputs(IList<AIContent>? inputs)
    {
        if (inputs is not null)
        {
            foreach (var input in inputs)
            {
                // Check for DataContent with text MIME types
                if (input is DataContent dc && dc.HasTopLevelMediaType("text"))
                {
                    // Return the data as a string (decode bytes as UTF8)
                    return Encoding.UTF8.GetString(dc.Data.ToArray());
                }

                // Check for TextContent
                if (input is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                {
                    return tc.Text;
                }
            }
        }

        return null;
    }
}
