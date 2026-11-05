// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Extensions.VectorData;

namespace Microsoft.Extensions.DataIngestion.Writers.Tests;

public sealed class SqliteVectorStoreWriterTests : VectorStoreWriterTests, IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    protected override VectorStore CreateVectorStore(TestEmbeddingGenerator<string> testEmbeddingGenerator)
        => new SqliteVectorStore($"Data Source={_tempFile};Pooling=false", new() { EmbeddingGenerator = testEmbeddingGenerator });
}
