// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Wraps the shared document returned by an extraction client for ingestion.</summary>
/// <remarks>
/// The client and input stream remain owned by the caller. Returned documents require no disposal.
/// This reader does not render, serialize, or reparse the extracted document.
/// </remarks>
public sealed class DocumentExtractionReader : IngestionDocumentReader
{
    private readonly IDocumentExtractionClient _client;
    private readonly DocumentExtractionOptions? _options;

    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionReader"/> class.</summary>
    /// <param name="client">The extraction client, which remains owned by the caller.</param>
    /// <param name="options">The per-operation options, copied at construction.</param>
    public DocumentExtractionReader(IDocumentExtractionClient client, DocumentExtractionOptions? options = null)
    {
        _client = Throw.IfNull(client);
        _options = options?.Clone();
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);
        _ = Throw.IfNullOrEmpty(mediaType);
        cancellationToken.ThrowIfCancellationRequested();

        var extractedDocument = await _client.ExtractAsync(source, identifier, mediaType, _options?.Clone(), cancellationToken).ConfigureAwait(false);
        return new IngestionDocument(identifier, extractedDocument);
    }
}
