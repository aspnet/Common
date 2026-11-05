// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of a code interpreter tool invocation by a hosted service.
/// </summary>
public sealed class CodeInterpreterToolResultContent : ToolResultContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeInterpreterToolResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public CodeInterpreterToolResultContent(string callId)
        : base(callId)
    {
    }

    /// <summary>
    /// Gets or sets the output of code interpreter tool.
    /// </summary>
    /// <remarks>
    /// Outputs can include various types of content such as <see cref="HostedFileContent"/> for files,
    /// <see cref="DataContent"/> for binary data, <see cref="TextContent"/> for standard output text,
    /// or other <see cref="AIContent"/> types as supported by the service.
    /// </remarks>
    public IList<AIContent>? Outputs { get; set; }
}
