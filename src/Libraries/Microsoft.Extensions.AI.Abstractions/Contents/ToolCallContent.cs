// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a tool call request.
/// </summary>
[JsonDerivedType(typeof(FunctionCallContent), "functionCall")]
[JsonDerivedType(typeof(McpServerToolCallContent), "mcpServerToolCall")]
[JsonDerivedType(typeof(ImageGenerationToolCallContent), "imageGenerationToolCall")]
[JsonDerivedType(typeof(CodeInterpreterToolCallContent), "codeInterpreterToolCall")]
[JsonDerivedType(typeof(WebSearchToolCallContent), "webSearchToolCall")]
public class ToolCallContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    /// <exception cref="ArgumentNullException"><paramref name="callId"/> is <see langword="null"/>.</exception>
    public ToolCallContent(string callId)
    {
        CallId = Throw.IfNull(callId);
    }

    /// <summary>
    /// Gets the tool call ID.
    /// </summary>
    public string CallId { get; }
}
