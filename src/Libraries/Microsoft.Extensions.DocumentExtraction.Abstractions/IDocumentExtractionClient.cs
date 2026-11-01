// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>
/// Extracts a shared Documents document from a source stream.
/// </summary>
/// <remarks>
/// Implementations do not own or dispose the source stream. They must snapshot per-call options before
/// asynchronous work, honor cancellation, and release provider resources
/// on failure or cancellation. Cleanup failures must not replace the primary error.
/// The returned document is data and requires no disposal. Retained original response content must be
/// detached into managed bytes before provider responses, parsers, and temporary streams are released.
/// Providers register source frames and operations explicitly, including operations producing no sections.
/// Native result and source-reference objects are borrowed and may be exposed regardless of content capture.
/// Original response content is retained only when <see cref="DocumentExtractionOptions.CaptureRawContent"/> is enabled.
/// Providers must document unsupported explicit settings separately from response content that is legitimately
/// unavailable. Enabling capture must not synthesize response bytes by serializing mapped objects.
/// </remarks>
public interface IDocumentExtractionClient : IDisposable
{
    /// <summary>
    /// Extracts a canonical document without requiring a separate page or observation protocol.
    /// </summary>
    /// <param name="document">The source document stream, which remains owned by the caller.</param>
    /// <param name="identifier">The identifier for the resulting Documents document.</param>
    /// <param name="mediaType">The source media type.</param>
    /// <param name="options">Optional per-call overrides. Providers must reject unsupported explicit settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task producing the caller-owned extracted document.</returns>
    Task<ExtractionDocument> ExtractAsync(
        Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an optional service exposed by this client.
    /// </summary>
    /// <param name="serviceType">The requested service type.</param>
    /// <param name="serviceKey">An optional service key.</param>
    /// <returns>The service, or <see langword="null"/>.</returns>
    object? GetService(Type serviceType, object? serviceKey = null);
}
