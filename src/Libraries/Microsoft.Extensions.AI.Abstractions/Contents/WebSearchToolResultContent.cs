// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of a web search tool invocation by a hosted service.
/// </summary>
/// <remarks>
/// This content type represents the results found by a hosted AI service's web search tool.
/// The results contain a list of <see cref="AIContent"/> items describing the web pages
/// found during the search, typically as <see cref="UriContent"/> instances.
/// </remarks>
public sealed class WebSearchToolResultContent : ToolResultContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchToolResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public WebSearchToolResultContent(string callId)
        : base(callId)
    {
    }

    /// <summary>
    /// Gets or sets the web search outputs.
    /// </summary>
    /// <remarks>
    /// Each output represents a web page result found during the search, typically as a <see cref="UriContent"/> instance.
    /// If a title is available for a result, it may be stored in the item's <see cref="AIContent.AdditionalProperties"/>
    /// under the key <c>"title"</c>.
    /// </remarks>
    public IList<AIContent>? Outputs { get; set; }
}
