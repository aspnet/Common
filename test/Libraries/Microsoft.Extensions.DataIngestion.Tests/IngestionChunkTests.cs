// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionChunkTests
{
    [Fact]
    public void Constructor_SetsTokenCountProperty()
    {
        ExtractionDocument document = new("test");
        IngestionChunk chunk = new(new TextContent("test content"), document, 42);

        Assert.Equal(42, chunk.TokenCount);
    }

    [Fact]
    public void Constructor_ThrowsWhenTokenCountIsNegative()
    {
        ExtractionDocument document = new("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, -1));

        Assert.Equal("tokenCount", exception.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsWhenTokenCountIsZero()
    {
        ExtractionDocument document = new("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, 0));

        Assert.Equal("tokenCount", exception.ParamName);
    }
}
