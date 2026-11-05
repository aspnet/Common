// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using OllamaSharp;
using Xunit;

namespace Microsoft.Extensions.AI;

public class OllamaSharpEmbeddingGeneratorIntegrationTests : EmbeddingGeneratorIntegrationTests
{
    protected override IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator() =>
        IntegrationTestHelpers.GetOllamaUri() is Uri endpoint ?
            new OllamaApiClient(endpoint, "all-minilm") :
            null;

    [Fact]
    public async Task InvalidModelParameter_ThrowsInvalidOperationException()
    {
        SkipIfNotEnabled();

        var endpoint = IntegrationTestHelpers.GetOllamaUri();
        Assert.NotNull(endpoint);

        using var client = new OllamaApiClient(endpoint, defaultModel: "inexistent-model");

        InvalidOperationException ex;
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(new OllamaSharp.Models.EmbedRequest
        {
            Input = ["Hello, world!"],
        }));
        Assert.Contains("inexistent-model", ex.Message);
    }
}
