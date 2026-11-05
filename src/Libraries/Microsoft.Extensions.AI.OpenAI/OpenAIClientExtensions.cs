// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Containers;
using OpenAI.Embeddings;
using OpenAI.Files;
using OpenAI.Images;
using OpenAI.Responses;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line

namespace Microsoft.Extensions.AI;

/// <summary>Provides extension methods for working with <see cref="OpenAIClient"/>s.</summary>
public static class OpenAIClientExtensions
{
    /// <summary>Key into AdditionalProperties used to store a strict option.</summary>
    private const string StrictKey = "strict";

    /// <summary>Gets the default OpenAI endpoint.</summary>
    internal static Uri DefaultOpenAIEndpoint { get; } = new("https://api.openai.com/v1");

    /// <summary>Gets a <see cref="ChatRole"/> for "developer".</summary>
    internal static ChatRole ChatRoleDeveloper { get; } = new ChatRole("developer");

    /// <summary>Gets the media type for Python code content.</summary>
    internal const string PythonMediaType = "text/x-python";

    /// <summary>
    /// Gets the JSON schema transformer cache conforming to OpenAI <b>strict</b> / structured output restrictions per
    /// https://platform.openai.com/docs/guides/structured-outputs?api-mode=responses#supported-schemas.
    /// </summary>
    internal static AIJsonSchemaTransformCache StrictSchemaTransformCache { get; } = new(new()
    {
        DisallowAdditionalProperties = true,
        ConvertBooleanSchemas = true,
        MoveDefaultKeywordToDescription = true,
        RequireAllProperties = true,
        TransformSchemaNode = (ctx, node) =>
        {
            // Move content from common but unsupported properties to description. In particular, we focus on properties that
            // the AIJsonUtilities schema generator might produce and/or that are explicitly mentioned in the OpenAI documentation.

            if (node is JsonObject schemaObj)
            {
                StringBuilder? additionalDescription = null;

                ReadOnlySpan<string> unsupportedProperties =
                [
                    // Produced by AIJsonUtilities but not in allow list at https://platform.openai.com/docs/guides/structured-outputs#supported-properties:
                    "contentEncoding", "contentMediaType", "not",

                    // Explicitly mentioned at https://platform.openai.com/docs/guides/structured-outputs?api-mode=responses#key-ordering as being unsupported with some models:
                    "minLength", "maxLength", "pattern", "format",
                    "minimum", "maximum", "multipleOf",
                    "patternProperties",
                    "minItems", "maxItems",

                    // Explicitly mentioned at https://learn.microsoft.com/azure/ai-services/openai/how-to/structured-outputs?pivots=programming-language-csharp&tabs=python-secure%2Cdotnet-entra-id#unsupported-type-specific-keywords
                    // as being unsupported with Azure OpenAI:
                    "unevaluatedProperties", "propertyNames", "minProperties", "maxProperties",
                    "unevaluatedItems", "contains", "minContains", "maxContains", "uniqueItems",
                ];

                foreach (string propName in unsupportedProperties)
                {
                    if (schemaObj[propName] is { } propNode)
                    {
                        _ = schemaObj.Remove(propName);
                        AppendLine(ref additionalDescription, propName, propNode);
                    }
                }

                if (additionalDescription is not null)
                {
                    schemaObj["description"] = schemaObj["description"] is { } descriptionNode && descriptionNode.GetValueKind() == JsonValueKind.String ?
                        $"{descriptionNode.GetValue<string>()}{Environment.NewLine}{additionalDescription}" :
                        additionalDescription.ToString();
                }

                return node;

                static void AppendLine(ref StringBuilder? sb, string propName, JsonNode propNode)
                {
                    sb ??= new();

                    if (sb.Length > 0)
                    {
                        _ = sb.AppendLine();
                    }

                    _ = sb.Append(propName).Append(": ").Append(propNode);
                }
            }

            return node;
        },
    });

    /// <summary>Gets an <see cref="IChatClient"/> for use with this <see cref="ChatClient"/>.</summary>
    /// <param name="chatClient">The client.</param>
    /// <returns>An <see cref="IChatClient"/> that can be used to converse via the <see cref="ChatClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public static IChatClient AsIChatClient(this ChatClient chatClient) =>
        new OpenAIChatClient(chatClient);

    /// <summary>Gets an <see cref="IChatClient"/> for use with this <see cref="ResponsesClient"/>.</summary>
    /// <param name="responseClient">The client.</param>
    /// <param name="defaultModelId">The default model ID to use for the chat client.</param>
    /// <returns>An <see cref="IChatClient"/> that can be used to converse via the <see cref="ResponsesClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="responseClient"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
    public static IChatClient AsIChatClient(this ResponsesClient responseClient, string? defaultModelId = null) =>
        new OpenAIResponsesChatClient(responseClient, defaultModelId);

    /// <summary>Gets an <see cref="ISpeechToTextClient"/> for use with this <see cref="AudioClient"/>.</summary>
    /// <param name="audioClient">The client.</param>
    /// <returns>An <see cref="ISpeechToTextClient"/> that can be used to transcribe audio via the <see cref="AudioClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="audioClient"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AISpeechToText, UrlFormat = DiagnosticIds.UrlFormat)]
    public static ISpeechToTextClient AsISpeechToTextClient(this AudioClient audioClient) =>
        new OpenAISpeechToTextClient(audioClient);

    /// <summary>Gets an <see cref="ITextToSpeechClient"/> for use with this <see cref="AudioClient"/>.</summary>
    /// <param name="audioClient">The client.</param>
    /// <returns>An <see cref="ITextToSpeechClient"/> that can be used to generate speech via the <see cref="AudioClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="audioClient"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AITextToSpeech, UrlFormat = DiagnosticIds.UrlFormat)]
    public static ITextToSpeechClient AsITextToSpeechClient(this AudioClient audioClient) =>
        new OpenAITextToSpeechClient(audioClient);

    /// <summary>Gets an <see cref="IImageGenerator"/> for use with this <see cref="ImageClient"/>.</summary>
    /// <param name="imageClient">The client.</param>
    /// <returns>An <see cref="IImageGenerator"/> that can be used to generate images via the <see cref="ImageClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageClient"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIImageGeneration, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IImageGenerator AsIImageGenerator(this ImageClient imageClient) =>
        new OpenAIImageGenerator(imageClient);

    /// <summary>Gets an <see cref="IEmbeddingGenerator{String, Single}"/> for use with this <see cref="EmbeddingClient"/>.</summary>
    /// <param name="embeddingClient">The client.</param>
    /// <param name="defaultModelDimensions">The number of dimensions to generate in each embedding.</param>
    /// <returns>An <see cref="IEmbeddingGenerator{String, Embedding}"/> that can be used to generate embeddings via the <see cref="EmbeddingClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="embeddingClient"/> is <see langword="null"/>.</exception>
    public static IEmbeddingGenerator<string, Embedding<float>> AsIEmbeddingGenerator(this EmbeddingClient embeddingClient, int? defaultModelDimensions = null) =>
        new OpenAIEmbeddingGenerator(embeddingClient, defaultModelDimensions);

    /// <summary>Gets an <see cref="IHostedFileClient"/> for use with this <see cref="OpenAIClient"/>.</summary>
    /// <param name="openAIClient">The client.</param>
    /// <returns>An <see cref="IHostedFileClient"/> that can be used to manage files via the <see cref="OpenAIClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="openAIClient"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The returned <see cref="IHostedFileClient"/> supports both the standard Files API and container files
    /// (used for code interpreter outputs). To download a container file, specify the container ID
    /// in the <see cref="HostedFileClientOptions.Scope"/> property.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.AIFiles, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHostedFileClient AsIHostedFileClient(this OpenAIClient openAIClient) =>
        new OpenAIHostedFileClient(openAIClient);

    /// <summary>Gets an <see cref="IHostedFileClient"/> for use with this <see cref="OpenAIFileClient"/>.</summary>
    /// <param name="fileClient">The client.</param>
    /// <returns>An <see cref="IHostedFileClient"/> that can be used to manage files via the <see cref="OpenAIFileClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileClient"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The returned <see cref="IHostedFileClient"/> supports only the standard Files API.
    /// Operations requiring container access (via <c>Scope</c>) will throw <see cref="InvalidOperationException"/>.
    /// To access container files, use <see cref="AsIHostedFileClient(ContainerClient, string?)"/> or <see cref="AsIHostedFileClient(OpenAIClient)"/>.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.AIFiles, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHostedFileClient AsIHostedFileClient(this OpenAIFileClient fileClient) =>
        new OpenAIHostedFileClient(fileClient);

    /// <summary>Gets an <see cref="IHostedFileClient"/> for use with this <see cref="ContainerClient"/>.</summary>
    /// <param name="containerClient">The client.</param>
    /// <param name="defaultScope">
    /// The default container ID for operations. If not specified, a container ID must be
    /// provided via the <c>Scope</c> property on per-call options.
    /// </param>
    /// <returns>An <see cref="IHostedFileClient"/> that can be used to manage files within containers via the <see cref="ContainerClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="containerClient"/> is <see langword="null"/>.</exception>
    [Experimental(DiagnosticIds.Experiments.AIFiles, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHostedFileClient AsIHostedFileClient(this ContainerClient containerClient, string? defaultScope = null) =>
        new OpenAIHostedFileClient(containerClient, defaultScope);

    /// <summary>Gets whether the properties specify that strict schema handling is desired.</summary>
    internal static bool? HasStrict(IReadOnlyDictionary<string, object?>? additionalProperties) =>
        additionalProperties?.TryGetValue(StrictKey, out object? strictObj) is true &&
        strictObj is bool strictValue ?
        strictValue : null;

    /// <summary>Extracts from an <see cref="AIFunctionDeclaration"/> the parameters and strictness setting for use with OpenAI's APIs.</summary>
    internal static BinaryData ToOpenAIFunctionParameters(AIFunctionDeclaration aiFunction, bool? strict)
    {
        // Perform any desirable transformations on the function's JSON schema, if it'll be used in a strict setting.
        JsonElement jsonSchema = strict is true ?
            StrictSchemaTransformCache.GetOrCreateTransformedSchema(aiFunction) :
            aiFunction.JsonSchema;

        // Roundtrip the schema through the ToolJson model type to force missing properties
        // into existence, then return the serialized UTF8 bytes as BinaryData.
#pragma warning disable OPENAI001 // OpenAIJsonContext is marked as experimental since it relies on source-generated serializers
        var tool = JsonSerializer.Deserialize(jsonSchema, OpenAIJsonContext.Default.ToolJson)!;
        var functionParameters = BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(tool, OpenAIJsonContext.Default.ToolJson));
#pragma warning restore OPENAI001

        return functionParameters;
    }

    /// <summary>Creates a new instance of <see cref="FunctionCallContent"/> parsing arguments using a specified encoding and parser.</summary>
    /// <param name="json">The input arguments to be parsed.</param>
    /// <param name="callId">The function call ID.</param>
    /// <param name="name">The function name.</param>
    /// <returns>A new instance of <see cref="FunctionCallContent"/> containing the parse result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
#pragma warning disable OPENAI001 // OpenAIJsonContext is marked as experimental since it relies on source-generated serializers
    internal static FunctionCallContent ParseCallContent(string json, string callId, string name) =>
        FunctionCallContent.CreateFromParsedArguments(json, callId, name,
            static json => JsonSerializer.Deserialize(json, OpenAIJsonContext.Default.IDictionaryStringObject)!);
#pragma warning restore OPENAI001

    /// <summary>Creates a new instance of <see cref="FunctionCallContent"/> parsing arguments using a specified encoding and parser.</summary>
    /// <param name="utf8json">The input arguments to be parsed.</param>
    /// <param name="callId">The function call ID.</param>
    /// <param name="name">The function name.</param>
    /// <returns>A new instance of <see cref="FunctionCallContent"/> containing the parse result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
#pragma warning disable OPENAI001 // OpenAIJsonContext is marked as experimental since it relies on source-generated serializers
    internal static FunctionCallContent ParseCallContent(BinaryData utf8json, string callId, string name) =>
        FunctionCallContent.CreateFromParsedArguments(utf8json, callId, name,
            static utf8json => JsonSerializer.Deserialize(utf8json, OpenAIJsonContext.Default.IDictionaryStringObject)!);
#pragma warning restore OPENAI001

    /// <summary>Gets a media type for an image based on the file extension in the provided URI.</summary>
    internal static string ImageUriToMediaType(Uri uri)
    {
        return MediaTypeMap.GetMediaType(uri.AbsoluteUri) ?? "image/*";
    }

    /// <summary>Sets $.model in <paramref name="patch"/> to <paramref name="modelId"/> if not already set.</summary>
    [Experimental(DiagnosticIds.Experiments.AIOpenAIJsonPatch)]
    internal static void PatchModelIfNotSet(ref JsonPatch patch, string? modelId)
    {
        if (modelId is not null)
        {
            _ = patch.TryGetValue("$.model"u8, out string? existingModel);
            if (existingModel is null)
            {
                patch.Set("$.model"u8, modelId);
            }
        }
    }

    /// <summary>Gets the typed property of the specified name from the tool's <see cref="AITool.AdditionalProperties"/>.</summary>
    internal static T? GetProperty<T>(this AITool tool, string name) =>
        tool.AdditionalProperties?.TryGetValue(name, out object? value) is true && value is T tValue ? tValue : default;

    /// <summary>Gets whether an ID is an OpenAI conversation ID.</summary>
    /// <remarks>
    /// Technically, OpenAI's IDs are opaque. However, by convention conversation IDs start with "conv_" and
    /// we can use that to disambiguate whether we're looking at a conversation ID or something else, like a response ID.
    /// </remarks>
    internal static bool IsConversationId(string? id) =>
        id?.StartsWith("conv_", StringComparison.OrdinalIgnoreCase) is true;

    /// <summary>Used to create the JSON payload for an OpenAI tool description.</summary>
    internal sealed class ToolJson
    {
        private static readonly JsonElement _falseElement = JsonElement.Parse("false"u8);

        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("required")]
        public HashSet<string> Required { get; set; } = [];

        [JsonPropertyName("properties")]
        public Dictionary<string, JsonElement> Properties { get; set; } = [];

        [JsonPropertyName("additionalProperties")]
        public JsonElement AdditionalProperties { get; set; } = _falseElement;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The "openai.api.type" tag name per the OpenTelemetry semantic conventions for OpenAI.</summary>
    internal const string OpenAIApiTypeTag = "openai.api.type";

    /// <summary>The "openai.response.service_tier" tag name per the OpenTelemetry semantic conventions for OpenAI.</summary>
    internal const string OpenAIResponseServiceTierTag = "openai.response.service_tier";

    /// <summary>The "openai.response.system_fingerprint" tag name per the OpenTelemetry semantic conventions for OpenAI.</summary>
    internal const string OpenAIResponseSystemFingerprintTag = "openai.response.system_fingerprint";

    /// <summary>The "chat_completions" value for the "openai.api.type" tag.</summary>
    internal const string OpenAIApiTypeChatCompletions = "chat_completions";

    /// <summary>The "responses" value for the "openai.api.type" tag.</summary>
    internal const string OpenAIApiTypeResponses = "responses";

    /// <summary>The "chat" operation name used by the OpenTelemetry chat client.</summary>
    private const string ChatOperationName = "chat";

    /// <summary>
    /// If the current <see cref="Activity"/> represents a "chat" operation span,
    /// adds the "openai.api.type" tag with the specified value.
    /// </summary>
    internal static void AddOpenAIApiType(string apiType)
    {
        if (GetCurrentChatActivity() is { } activity)
        {
            _ = activity.AddTag(OpenAIApiTypeTag, apiType);
        }
    }

    /// <summary>
    /// If the current <see cref="Activity"/> represents a "chat" operation span,
    /// adds OpenAI-specific response tags with the specified values.
    /// </summary>
    internal static void AddOpenAIResponseAttributes(string? serviceTier, string? systemFingerprint)
    {
        if (GetCurrentChatActivity() is { } activity)
        {
            if (!string.IsNullOrWhiteSpace(serviceTier))
            {
                _ = activity.SetTag(OpenAIResponseServiceTierTag, serviceTier);
            }

            if (!string.IsNullOrWhiteSpace(systemFingerprint))
            {
                _ = activity.SetTag(OpenAIResponseSystemFingerprintTag, systemFingerprint);
            }
        }
    }

    /// <summary>
    /// Streaming-friendly overload of <see cref="AddOpenAIResponseAttributes(string?, string?)"/>
    /// that records each tag at most once per stream. Once a non-null value has been written for
    /// either tag, subsequent calls short-circuit without performing the activity lookup or the
    /// per-tag <see cref="Activity.SetTag(string, object?)"/> call.
    /// </summary>
    /// <remarks>
    /// Each tag is gated independently so a stream that never reports one of the two values still
    /// captures the other on its first non-null arrival.
    /// </remarks>
    /// <param name="serviceTier">The service tier value from the current update, if any.</param>
    /// <param name="systemFingerprint">The system fingerprint value from the current update, if any.</param>
    /// <param name="capturedServiceTier">
    /// A per-stream cache of the value already written for <c>openai.response.service_tier</c>.
    /// Initialize to <see langword="null"/> at the start of the stream.
    /// </param>
    /// <param name="capturedSystemFingerprint">
    /// A per-stream cache of the value already written for <c>openai.response.system_fingerprint</c>.
    /// Initialize to <see langword="null"/> at the start of the stream.
    /// </param>
    internal static void AddOpenAIResponseAttributes(
        string? serviceTier,
        string? systemFingerprint,
        ref string? capturedServiceTier,
        ref string? capturedSystemFingerprint)
    {
        bool needsServiceTier = capturedServiceTier is null && !string.IsNullOrWhiteSpace(serviceTier);
        bool needsSystemFingerprint = capturedSystemFingerprint is null && !string.IsNullOrWhiteSpace(systemFingerprint);

        if (!needsServiceTier && !needsSystemFingerprint)
        {
            return;
        }

        if (GetCurrentChatActivity() is { } activity)
        {
            if (needsServiceTier)
            {
                capturedServiceTier = serviceTier;
                _ = activity.SetTag(OpenAIResponseServiceTierTag, serviceTier);
            }

            if (needsSystemFingerprint)
            {
                capturedSystemFingerprint = systemFingerprint;
                _ = activity.SetTag(OpenAIResponseSystemFingerprintTag, systemFingerprint);
            }
        }
    }

    /// <summary>
    /// Returns <see cref="Activity.Current"/> if it has data requested and its
    /// <see cref="Activity.DisplayName"/> represents a gen_ai "chat" span
    /// (the name is "chat" or "chat {name}"); otherwise <see langword="null"/>.
    /// </summary>
    private static Activity? GetCurrentChatActivity()
    {
        Activity? activity = Activity.Current;
        if (activity is { IsAllDataRequested: true })
        {
            // Recognize an activity name of "chat" or "chat {name}".
            string name = activity.DisplayName;

            if (name.StartsWith(ChatOperationName, StringComparison.Ordinal) &&
                (name.Length == ChatOperationName.Length || name[ChatOperationName.Length] == ' '))
            {
                return activity;
            }
        }

        return null;
    }
}
