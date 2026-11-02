// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents the result of an ingestion operation.
/// </summary>
public sealed class IngestionResult
{
    /// <summary>
    /// Gets the ID of the document that was ingested.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Gets the ingestion document created from the source file, if reading the document has succeeded.
    /// </summary>
    public IngestionDocument? Document { get; }

    /// <summary>
    /// Gets the exception that occurred during ingestion, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets a value indicating whether the ingestion succeeded.
    /// </summary>
    public bool Succeeded => Exception is null;

    internal IngestionResult(string documentId, IngestionDocument? document, Exception? exception)
    {
        DocumentId = Throw.IfNullOrEmpty(documentId);
        Document = document;
        Exception = exception;
    }
}
