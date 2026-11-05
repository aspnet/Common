// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of an image generation tool invocation by a hosted service.
/// </summary>
/// <remarks>
/// This content type is used to represent the result of an image generation tool invocation by a hosted service.
/// It is informational only.
/// </remarks>
public sealed class ImageGenerationToolResultContent : ToolResultContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageGenerationToolResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public ImageGenerationToolResultContent(string callId)
        : base(callId)
    {
    }

    /// <summary>
    /// Gets or sets the generated content items.
    /// </summary>
    /// <remarks>
    /// Content is typically <see cref="DataContent"/> for images streamed from the tool, or <see cref="UriContent"/> for remotely hosted images, but
    /// can also be provider-specific content types that represent the generated images.
    /// </remarks>
    public IList<AIContent>? Outputs { get; set; }
}
