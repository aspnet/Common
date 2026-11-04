// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Associates an ingestion identifier with a shared document graph.
/// </summary>
public sealed class IngestionDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocument"/> class with an empty shared document.
    /// </summary>
    /// <param name="identifier">The identifier used to persist the ingested document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is empty.</exception>
    public IngestionDocument(string identifier)
        : this(identifier, new ExtractionDocument(identifier))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocument"/> class around an existing shared document.
    /// </summary>
    /// <param name="identifier">The identifier used to persist the ingested document.</param>
    /// <param name="extractedDocument">The shared document, retained by reference without copying its graph.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="identifier"/> or <paramref name="extractedDocument"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is empty.</exception>
    public IngestionDocument(string identifier, ExtractionDocument extractedDocument)
    {
        Identifier = Throw.IfNullOrEmpty(identifier);
        ExtractedDocument = Throw.IfNull(extractedDocument);
    }

    /// <summary>Gets the identifier used to persist the ingested document.</summary>
    /// <remarks>This identifier is independent of <see cref="ExtractionDocument.Identifier"/>.</remarks>
    public string Identifier { get; }

    /// <summary>Gets the shared document that owns all content, metadata, source pages, and native state.</summary>
    public ExtractionDocument ExtractedDocument { get; }

    /// <summary>Gets the shared document's ordered root sections.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "The envelope exposes the exact mutable list owned by the shared document.")]
    public List<ExtractionDocumentSection> Sections => ExtractedDocument.Sections;

    /// <summary>Enumerates non-section content in the shared document's depth-first order.</summary>
    /// <remarks>
    /// This forwards the shared structural traversal. It does not flatten table cells or return
    /// document/section-local payloads.
    /// </remarks>
    /// <returns>The shared content elements in document order.</returns>
    public IEnumerable<ExtractionDocumentElement> EnumerateContent() => ExtractedDocument.EnumerateContent();
}
