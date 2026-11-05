// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a code interpreter tool call invocation by a hosted service.
/// </summary>
/// <remarks>
/// This content type represents when a hosted AI service invokes a code interpreter tool.
/// It is informational only and represents the call itself, not the result.
/// </remarks>
public sealed class CodeInterpreterToolCallContent : ToolCallContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeInterpreterToolCallContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public CodeInterpreterToolCallContent(string callId)
        : base(callId)
    {
    }

    /// <summary>
    /// Gets or sets the inputs to the code interpreter tool.
    /// </summary>
    /// <remarks>
    /// Inputs can include various types of content such as <see cref="HostedFileContent"/> for files,
    /// <see cref="DataContent"/> for binary data, or other <see cref="AIContent"/> types as supported
    /// by the service. Typically <see cref="Inputs"/> includes a <see cref="DataContent"/> with a "text/x-python"
    /// media type representing the code for execution by the code interpreter tool.
    /// </remarks>
    public IList<AIContent>? Inputs { get; set; }
}
