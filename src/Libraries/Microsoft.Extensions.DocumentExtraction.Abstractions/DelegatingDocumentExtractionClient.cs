// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>
/// Provides a base class for extraction middleware that delegates to another client.
/// </summary>
public class DelegatingDocumentExtractionClient : IDocumentExtractionClient
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegatingDocumentExtractionClient"/> class.
    /// </summary>
    /// <param name="innerClient">The wrapped client.</param>
    protected DelegatingDocumentExtractionClient(IDocumentExtractionClient innerClient)
    {
        InnerClient = Throw.IfNull(innerClient);
    }

    /// <summary>Gets the wrapped client.</summary>
    protected IDocumentExtractionClient InnerClient { get; }

    /// <inheritdoc />
    public virtual Task<ExtractionDocument> ExtractAsync(
        Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);
        _ = Throw.IfNullOrEmpty(identifier);
        _ = Throw.IfNullOrEmpty(mediaType);
        return InnerClient.ExtractAsync(document, identifier, mediaType, options?.Clone(), cancellationToken);
    }

    /// <inheritdoc />
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : InnerClient.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources owned by this middleware.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            InnerClient.Dispose();
        }
    }
}
