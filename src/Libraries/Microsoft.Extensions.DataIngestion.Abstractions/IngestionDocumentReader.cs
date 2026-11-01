// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Reads source content and converts it to an <see cref="ExtractionDocument"/>.
/// </summary>
/// <remarks>
/// Returned documents and detached byte artifacts require no disposal. Implementations must detach returned
/// content from temporary streams and provider resources before releasing them. Caller-supplied streams remain borrowed.
/// </remarks>
public abstract class IngestionDocumentReader
{
    /// <summary>
    /// Reads a file and converts it to an <see cref="ExtractionDocument"/>.
    /// </summary>
    /// <param name="source">The file to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous read operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public Task<ExtractionDocument> ReadAsync(FileInfo source, CancellationToken cancellationToken = default)
    {
        string identifier = Throw.IfNull(source).FullName; // entire path is more unique than just part of it.
        return ReadAsync(source, identifier, GetMediaType(source), cancellationToken);
    }

    /// <summary>
    /// Reads a file and converts it to an <see cref="ExtractionDocument"/>.
    /// </summary>
    /// <param name="source">The file to read.</param>
    /// <param name="identifier">The unique identifier for the document.</param>
    /// <param name="mediaType">The media type of the file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous read operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="identifier"/> is <see langword="null"/> or empty.</exception>
    public virtual async Task<ExtractionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        using FileStream stream = new(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);
        return await ReadAsync(stream, identifier, string.IsNullOrEmpty(mediaType) ? GetMediaType(source) : mediaType!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a stream and converts it to an <see cref="ExtractionDocument"/>.
    /// </summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="identifier">The unique identifier for the document.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous read operation.</returns>
    public abstract Task<ExtractionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default);

    private static string GetMediaType(FileInfo source) =>
        MediaTypeMap.GetMediaType(source.Extension) ?? "application/octet-stream";
}
