// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Part of the document processing pipeline that takes a <see cref="IngestionDocument"/> as input and produces a (potentially modified) <see cref="IngestionDocument"/> as output.
/// </summary>
public abstract class IngestionDocumentProcessor
{
    /// <summary>
    /// Processes the given ingestion document.
    /// </summary>
    /// <param name="document">The ingestion document to process.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous processing operation, with the processed document as the result.</returns>
    public abstract Task<IngestionDocument> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default);
}
