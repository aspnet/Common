// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Extensions.AI;

public static partial class AIJsonUtilities
{
    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> singleton used as the default in JSON serialization operations.
    /// </summary>
    /// <remarks>
    /// <para>For Native AOT or applications disabling <see cref="JsonSerializer.IsReflectionEnabledByDefault"/> this instance includes source generated contracts
    /// for all common exchange types contained in the Microsoft.Extensions.AI.Abstractions library.
    /// </para>
    /// <para>
    /// It additionally turns on the following settings:
    /// <list type="number">
    /// <item>Enables the <see cref="JsonSerializerOptions.WriteIndented"/> property.</item>
    /// <item>Enables string based enum serialization as implemented by <see cref="JsonStringEnumConverter"/>.</item>
    /// <item>Enables <see cref="JsonIgnoreCondition.WhenWritingNull"/> as the default ignore condition for properties.</item>
    /// <item>
    /// Enables <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> when escaping JSON strings.
    /// Consuming applications must ensure that JSON outputs are adequately escaped before embedding in other document formats, such as HTML and XML.
    /// </item>
    /// </list>
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>Creates the default <see cref="JsonSerializerOptions"/> to use for serialization-related operations.</summary>
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // Copy configuration from the source generated context.
        JsonSerializerOptions options = new(JsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            // If reflection-based serialization is enabled by default, use it as a fallback for all other types.
            // Also turn on string-based enum serialization for all unknown enums.
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
            options.Converters.Add(new JsonStringEnumConverter());
        }

        options.MakeReadOnly();
        return options;
    }

    // Keep in sync with CreateDefaultOptions above.
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        UseStringEnumConverter = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true)]

    // JSON
    [JsonSerializable(typeof(JsonDocument))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonNode))]
    [JsonSerializable(typeof(JsonObject))]
    [JsonSerializable(typeof(JsonValue))]
    [JsonSerializable(typeof(JsonArray))]

    // Primitives
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(char))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]

    // AIFunction
    [JsonSerializable(typeof(AIFunctionArguments))]

    // IChatClient
    [JsonSerializable(typeof(IEnumerable<ChatMessage>))]
    [JsonSerializable(typeof(IList<ChatMessage>))]
    [JsonSerializable(typeof(ChatMessage[]))]
    [JsonSerializable(typeof(ChatOptions))]
    [JsonSerializable(typeof(ChatClientMetadata))]
    [JsonSerializable(typeof(ChatResponse))]
    [JsonSerializable(typeof(ChatResponseUpdate))]
    [JsonSerializable(typeof(IReadOnlyList<ChatResponseUpdate>))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(IDictionary<string, object?>))]
    [JsonSerializable(typeof(IEnumerable<string>))]
    [JsonSerializable(typeof(AIContent))]
    [JsonSerializable(typeof(IEnumerable<AIContent>))]

    // IEmbeddingGenerator
    [JsonSerializable(typeof(EmbeddingGenerationOptions))]
    [JsonSerializable(typeof(EmbeddingGeneratorMetadata))]
    [JsonSerializable(typeof(Embedding))]
    [JsonSerializable(typeof(Embedding<byte>))]
    [JsonSerializable(typeof(Embedding<int>))]
#if NET
    [JsonSerializable(typeof(Embedding<Half>))]
#endif
    [JsonSerializable(typeof(Embedding<float>))]
    [JsonSerializable(typeof(Embedding<double>))]

    // ISpeechToTextClient
    [JsonSerializable(typeof(SpeechToTextOptions))]
    [JsonSerializable(typeof(SpeechToTextClientMetadata))]
    [JsonSerializable(typeof(SpeechToTextResponse))]
    [JsonSerializable(typeof(SpeechToTextResponseUpdate))]
    [JsonSerializable(typeof(IReadOnlyList<SpeechToTextResponseUpdate>))]

    // ITextToSpeechClient
    [JsonSerializable(typeof(TextToSpeechOptions))]
    [JsonSerializable(typeof(TextToSpeechClientMetadata))]
    [JsonSerializable(typeof(TextToSpeechResponse))]
    [JsonSerializable(typeof(TextToSpeechResponseUpdate))]
    [JsonSerializable(typeof(IReadOnlyList<TextToSpeechResponseUpdate>))]

    // IImageGenerator
    [JsonSerializable(typeof(ImageGenerationOptions))]
    [JsonSerializable(typeof(ImageGenerationResponse))]

    // IHostedFileClient
    [JsonSerializable(typeof(HostedFileClientOptions))]
    [JsonSerializable(typeof(HostedFileClientMetadata))]

    [EditorBrowsable(EditorBrowsableState.Never)] // Never use JsonContext directly, use DefaultOptions instead.
    private sealed partial class JsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        UseStringEnumConverter = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false)]
    [JsonSerializable(typeof(JsonNode))]
    private sealed partial class JsonContextNoIndentation : JsonSerializerContext;
}
