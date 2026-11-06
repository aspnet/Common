// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a tool that stops being available to the model at this point in the conversation.
/// </summary>
/// <remarks>
/// <para>
/// Removing a tool from <see cref="ChatOptions.Tools"/> changes the tool definitions at the start of every request,
/// which invalidates any prompt cache built on them. A <see cref="ToolRemovalContent"/> in the chat history instead
/// tells the model that the tool is gone where the content appears, leaving everything before it unchanged.
/// </para>
/// <para>
/// The content only informs the model. A component such as <c>FunctionInvokingChatClient</c> can still invoke a function
/// that remains in <see cref="ChatOptions.Tools"/>, so to stop the tool from being invoked, remove the function from
/// <see cref="ChatOptions.Tools"/> as well.
/// </para>
/// <para>
/// If the function was declared in <see cref="ChatOptions.Tools"/> from the start of the conversation, removing it from
/// there changes the tool definitions at the start of the request, which invalidates any prompt cache built on them.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIToolChanges, UrlFormat = DiagnosticIds.UrlFormat)]
[DebuggerDisplay("ToolName = {ToolName}")]
public sealed class ToolRemovalContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRemovalContent"/> class.
    /// </summary>
    /// <param name="toolName">The name of the tool that stops being available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="toolName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="toolName"/> is empty or composed entirely of whitespace.</exception>
    [JsonConstructor]
    public ToolRemovalContent(string toolName)
    {
        ToolName = Throw.IfNullOrWhitespace(toolName);
    }

    /// <summary>
    /// Gets the name of the tool that stops being available.
    /// </summary>
    public string ToolName { get; }
}
