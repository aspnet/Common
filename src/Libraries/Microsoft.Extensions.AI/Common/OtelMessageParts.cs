// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;

#pragma warning disable SA1402 // File may only contain a single type — these POCOs are co-located on purpose.
#pragma warning disable SA1649 // File name should match first type name — this file holds the shared OTel POCOs as a group.

namespace Microsoft.Extensions.AI;

// Shared OTel message-part POCOs.
//
// Only types whose layout is byte-identical between the chat and realtime clients live here. Types
// that diverge remain in their respective client files.

internal sealed class OtelGenericPart
{
    public string Type { get; set; } = "text";
    public object? Content { get; set; } // should be a string when Type == "text"
}

internal sealed class OtelBlobPart
{
    public string Type { get; set; } = "blob";
    public string? Content { get; set; } // base64-encoded binary data
    public string? MimeType { get; set; }
    public string? Modality { get; set; }
}

internal sealed class OtelUriPart
{
    public string Type { get; set; } = "uri";
    public string? Uri { get; set; }
    public string? MimeType { get; set; }
    public string? Modality { get; set; }
}

internal sealed class OtelFilePart
{
    public string Type { get; set; } = "file";
    public string? FileId { get; set; }
    public string? MimeType { get; set; }
    public string? Modality { get; set; }
}

internal sealed class OtelToolCallResponsePart
{
    public string Type { get; set; } = "tool_call_response";
    public string? Id { get; set; }
    public object? Response { get; set; }
}

internal sealed class OtelServerToolCallPart<T>
    where T : class
{
    public string Type { get; set; } = "server_tool_call";
    public string? Id { get; set; }
    public string? Name { get; set; }
    public T? ServerToolCall { get; set; }
}

internal sealed class OtelServerToolCallResponsePart<T>
    where T : class
{
    public string Type { get; set; } = "server_tool_call_response";
    public string? Id { get; set; }
    public T? ServerToolCallResponse { get; set; }
}

internal sealed class OtelMcpToolCallResponse
{
    public string Type { get; set; } = "mcp";
    public object? Output { get; set; }
}

internal sealed class OtelMcpToolCall
{
    public string Type { get; set; } = "mcp";
    public string? ServerName { get; set; }
    public IReadOnlyDictionary<string, object?>? Arguments { get; set; }
}

internal sealed class OtelFunction
{
    public string Type { get; set; } = "function";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public JsonElement? Parameters { get; set; }

    /// <summary>Builds an <see cref="OtelFunction"/> from an <see cref="AITool"/>.</summary>
    /// <param name="tool">The tool to describe.</param>
    /// <param name="includeOptionalProperties">
    /// When <see langword="false"/>, the optional <see cref="Description"/> and <see cref="Parameters"/>
    /// properties will be set to <see langword="null"/>, as they may contain sensitive, user-authored
    /// values or large payloads.
    /// </param>
    public static OtelFunction Create(AITool tool, bool includeOptionalProperties)
    {
        if (tool.GetService<AIFunctionDeclaration>() is { } function)
        {
            return new()
            {
                Name = function.Name,
                Description = includeOptionalProperties ? function.Description : null,
                Parameters = includeOptionalProperties ? function.JsonSchema : null,
            };
        }

        return new()
        {
            Type = tool.Name,
            Name = tool.Name,
        };
    }
}
