// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Splits an <see cref="IngestionDocument"/> into chunks.
/// </summary>
public abstract class IngestionChunker
{
    /// <summary>
    /// Splits a document into chunks asynchronously.
    /// </summary>
    /// <param name="document">The document to split.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The chunks created from the document.</returns>
    public abstract IAsyncEnumerable<IngestionChunk> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default);
}
