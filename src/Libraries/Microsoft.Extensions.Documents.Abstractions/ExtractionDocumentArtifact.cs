// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents detached binary content, independent of provider resource lifetimes.</summary>
public sealed class ExtractionDocumentArtifact
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentArtifact"/> class.</summary>
    /// <param name="mediaType">The media type of the artifact.</param>
    /// <param name="content">The content bytes, copied into managed storage.</param>
    /// <param name="source">An optional opaque source reference.</param>
    public ExtractionDocumentArtifact(string mediaType, ReadOnlyMemory<byte> content, Uri? source = null)
    {
        MediaType = Throw.IfNullOrEmpty(mediaType);
        Content = content.ToArray();
        Source = source;
    }

    /// <summary>Gets the media type of the artifact.</summary>
    public string MediaType { get; }

    /// <summary>Gets the detached content bytes.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Gets the optional opaque source reference.</summary>
    public Uri? Source { get; }
}
