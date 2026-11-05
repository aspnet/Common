// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.VectorData;

namespace Microsoft.Extensions.DataIngestion.Writers.Tests;

public class InMemoryVectorStoreWriterTests : VectorStoreWriterTests
{
    protected override VectorStore CreateVectorStore(TestEmbeddingGenerator<string> testEmbeddingGenerator)
        => new InMemoryVectorStore(new() { EmbeddingGenerator = testEmbeddingGenerator });
}
