// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of a tool call.
/// </summary>
[JsonDerivedType(typeof(FunctionResultContent), "functionResult")]
[JsonDerivedType(typeof(McpServerToolResultContent), "mcpServerToolResult")]
[JsonDerivedType(typeof(ImageGenerationToolResultContent), "imageGenerationToolResult")]
[JsonDerivedType(typeof(CodeInterpreterToolResultContent), "codeInterpreterToolResult")]
[JsonDerivedType(typeof(WebSearchToolResultContent), "webSearchToolResult")]
public class ToolResultContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID for which this is the result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="callId"/> is <see langword="null"/>.</exception>
    public ToolResultContent(string callId)
    {
        CallId = Throw.IfNull(callId);
    }

    /// <summary>
    /// Gets the ID of the tool call for which this is the result.
    /// </summary>
    /// <remarks>
    /// If this is the result for a <see cref="ToolCallContent"/>, this property should contain the same
    /// <see cref="ToolCallContent.CallId"/> value.
    /// </remarks>
    public string CallId { get; }
}
