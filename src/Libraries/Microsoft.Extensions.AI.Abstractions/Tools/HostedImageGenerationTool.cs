// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>Represents a hosted tool that can be specified to an AI service to enable it to perform image generation.</summary>
/// <remarks>
/// This tool does not itself implement image generation. It is a marker that can be used to inform a service
/// that the service is allowed to perform image generation if the service is capable of doing so.
/// </remarks>
public class HostedImageGenerationTool : AITool
{
    /// <summary>Any additional properties associated with the tool.</summary>
    private IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedImageGenerationTool"/> class with the specified options.
    /// </summary>
    public HostedImageGenerationTool()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HostedImageGenerationTool"/> class.</summary>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    public HostedImageGenerationTool(IReadOnlyDictionary<string, object?>? additionalProperties)
    {
        _additionalProperties = additionalProperties;
    }

    /// <inheritdoc />
    public override string Name => "image_generation";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties ?? base.AdditionalProperties;

    /// <summary>
    /// Gets or sets the options used to configure image generation.
    /// </summary>
    public ImageGenerationOptions? Options { get; set; }
}
