// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents a chunk of content extracted from an <see cref="ExtractionDocument"/>.
/// </summary>
[DebuggerDisplay("Content = {Content}")]
public class IngestionChunk
{
    private Dictionary<string, object>? _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunk"/> class.
    /// </summary>
    /// <param name="content">The content of the chunk.</param>
    /// <param name="document">The document from which this chunk was extracted.</param>
    /// <param name="tokenCount">The number of tokens used to represent the chunk.</param>
    /// <param name="context">Additional context for the chunk.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="document"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tokenCount"/> is zero or negative.
    /// </exception>
    public IngestionChunk(AIContent content, ExtractionDocument document, int tokenCount, string? context = null)
    {
        Content = Throw.IfNull(content);
        Document = Throw.IfNull(document);
        Context = context;
        TokenCount = Throw.IfLessThanOrEqual(tokenCount, 0);
    }

    /// <summary>
    /// Gets the content of the chunk.
    /// </summary>
    public AIContent Content { get; }

    /// <summary>
    /// Gets the document from which this chunk was extracted.
    /// </summary>
    public ExtractionDocument Document { get; }

    /// <summary>
    /// Gets the document elements contributing content or context to this chunk.
    /// </summary>
    /// <remarks>
    /// Elements retain their identity in <see cref="Document"/>, including source references and table cell addresses.
    /// An empty collection means source associations were not supplied. Source frame identifiers are not durable citation keys.
    /// </remarks>
    public IReadOnlyList<ExtractionDocumentElement> SourceElements { get; init; } = Array.Empty<ExtractionDocumentElement>();

    /// <summary>
    /// Gets additional context for the chunk.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets the number of tokens used to represent the chunk.
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// Gets a value indicating whether this chunk has metadata.
    /// </summary>
    public bool HasMetadata => _metadata?.Count > 0;

    /// <summary>
    /// Gets the metadata associated with this chunk.
    /// </summary>
    public IDictionary<string, object> Metadata => _metadata ??= [];
}
