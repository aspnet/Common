// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a tool that becomes available to the model at this point in the conversation.
/// </summary>
/// <remarks>
/// <para>
/// Adding a tool to <see cref="ChatOptions.Tools"/> changes the tool definitions at the start of every request,
/// which invalidates any prompt cache built on them. A <see cref="ToolAdditionContent"/> in the chat history instead
/// tells the model about the tool where the content appears, leaving everything before it unchanged.
/// </para>
/// <para>
/// The content only describes the tool to the model. To have the tool invoked by a component such as
/// <c>FunctionInvokingChatClient</c>, also add the invocable function to <see cref="ChatOptions.Tools"/>. When a tool's
/// first change in the history is a <see cref="ToolAdditionContent"/>, a chat client that supports positional tool changes
/// leaves the tool out of the tool definitions at the start of the request. A chat client without that support instead
/// applies the changes in the history to the tool definitions it sends.
/// </para>
/// <para>
/// Keep the content in the history unchanged for as long as the conversation continues. Its definition, rather than
/// the one in <see cref="ChatOptions.Tools"/>, is what is sent at its position, so the earlier part of the conversation
/// stays the same from request to request.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIToolChanges, UrlFormat = DiagnosticIds.UrlFormat)]
[DebuggerDisplay("Tool = {Tool.Name}")]
public sealed class ToolAdditionContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolAdditionContent"/> class.
    /// </summary>
    /// <param name="tool">The declaration of the tool that becomes available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    [JsonConstructor]
    public ToolAdditionContent(AIFunctionDeclaration tool)
    {
        Tool = Throw.IfNull(tool);
    }

    /// <summary>
    /// Gets the declaration of the tool that becomes available.
    /// </summary>
    /// <remarks>
    /// When the content is serialized, only the tool's name, description and JSON schemas are written, so a deserialized
    /// content carries a declaration that can't be invoked.
    /// </remarks>
    [JsonConverter(typeof(DeclarationConverter))]
    public AIFunctionDeclaration Tool { get; }

    /// <summary>Serializes an <see cref="AIFunctionDeclaration"/> as its name, description and JSON schemas.</summary>
    internal sealed class DeclarationConverter : JsonConverter<AIFunctionDeclaration>
    {
        public override AIFunctionDeclaration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected a JSON object for the tool declaration.");
            }

            using var document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            string? name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                throw new JsonException("The tool declaration has no name.");
            }

            string? description = root.TryGetProperty("description", out JsonElement descriptionElement) ? descriptionElement.GetString() : null;
            JsonElement jsonSchema = root.TryGetProperty("jsonSchema", out JsonElement schemaElement) ? schemaElement.Clone() : AIJsonUtilities.DefaultJsonSchema;
            JsonElement? returnJsonSchema = root.TryGetProperty("returnJsonSchema", out JsonElement returnElement) ? returnElement.Clone() : null;

            return AIFunctionFactory.CreateDeclaration(name!, description, jsonSchema, returnJsonSchema);
        }

        public override void Write(Utf8JsonWriter writer, AIFunctionDeclaration value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            if (!string.IsNullOrEmpty(value.Description))
            {
                writer.WriteString("description", value.Description);
            }

            writer.WritePropertyName("jsonSchema");
            value.JsonSchema.WriteTo(writer);
            if (value.ReturnJsonSchema is { } returnJsonSchema)
            {
                writer.WritePropertyName("returnJsonSchema");
                returnJsonSchema.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
