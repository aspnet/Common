// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Part of the document processing pipeline that takes a <see cref="ExtractionDocument"/> as input and produces a (potentially modified) <see cref="ExtractionDocument"/> as output.
/// </summary>
public abstract class IngestionDocumentProcessor
{
    /// <summary>
    /// Processes the given ingestion document.
    /// </summary>
    /// <param name="document">The ingestion document to process.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous processing operation, with the processed document as the result.</returns>
    public abstract Task<ExtractionDocument> ProcessAsync(ExtractionDocument document, CancellationToken cancellationToken = default);
}
