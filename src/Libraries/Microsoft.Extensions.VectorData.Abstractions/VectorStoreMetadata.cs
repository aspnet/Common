// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.VectorData;

/// <summary>Provides metadata about an <see cref="VectorStore"/>.</summary>
public class VectorStoreMetadata
{
    /// <summary>Gets the name of the vector store system.</summary>
    /// <remarks>
    /// Where possible, this value maps to the "db.system.name" attribute defined in the
    /// OpenTelemetry Semantic Conventions for database calls and systems; see <see href="https://opentelemetry.io/docs/specs/semconv/database/"/>.
    /// Example: redis, sqlite, mysql.
    /// </remarks>
    public string? VectorStoreSystemName { get; init; }

    /// <summary>
    /// Gets or initializes the name of the vector store (database).
    /// </summary>
    public string? VectorStoreName { get; init; }
}
