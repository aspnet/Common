// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a web search tool call invocation by a hosted service.
/// </summary>
/// <remarks>
/// This content type represents when a hosted AI service invokes a web search tool.
/// It is informational only and represents the call itself, not the result.
/// </remarks>
public sealed class WebSearchToolCallContent : ToolCallContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchToolCallContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public WebSearchToolCallContent(string callId)
        : base(callId)
    {
    }

    /// <summary>
    /// Gets or sets the search queries issued by the service.
    /// </summary>
    public IList<string>? Queries { get; set; }
}
