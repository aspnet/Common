// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
#if NET
using System.Collections;
#endif
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

#if NET
using System.Numerics.Tensors;
#endif
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry.Trace;
using Xunit;

#pragma warning disable CA2214 // Do not call overridable methods in constructors
#pragma warning disable S3967  // Multidimensional arrays should not be used

namespace Microsoft.Extensions.AI;

public abstract class EmbeddingGeneratorIntegrationTests : IDisposable
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    protected EmbeddingGeneratorIntegrationTests()
    {
        _embeddingGenerator = CreateEmbeddingGenerator();
    }

    public void Dispose()
    {
        _embeddingGenerator?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator();

    [Fact]
    public virtual async Task GenerateEmbedding_CreatesEmbeddingSuccessfully()
    {
        SkipIfNotEnabled();

        var embeddings = await _embeddingGenerator.GenerateAsync(["Using AI with .NET"]);

        Assert.NotNull(embeddings.Usage);
        Assert.NotNull(embeddings.Usage.InputTokenCount);
        Assert.NotNull(embeddings.Usage.TotalTokenCount);
        Assert.Single(embeddings);
        Assert.Equal(_embeddingGenerator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId, embeddings[0].ModelId);
        Assert.NotEmpty(embeddings[0].Vector.ToArray());
    }

    [Fact]
    public virtual async Task GenerateEmbeddings_CreatesEmbeddingsSuccessfully()
    {
        SkipIfNotEnabled();

        var embeddings = await _embeddingGenerator.GenerateAsync([
            "Red",
            "White",
            "Blue",
        ]);

        Assert.Equal(3, embeddings.Count);
        Assert.NotNull(embeddings.Usage);
        Assert.NotNull(embeddings.Usage.InputTokenCount);
        Assert.NotNull(embeddings.Usage.TotalTokenCount);
        Assert.All(embeddings, embedding =>
        {
            Assert.Equal(_embeddingGenerator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId, embedding.ModelId);
            Assert.NotEmpty(embedding.Vector.ToArray());
        });
    }

    [Fact]
    public virtual async Task Caching_SameOutputsForSameInput()
    {
        SkipIfNotEnabled();

        using var generator = CreateEmbeddingGenerator()!
            .AsBuilder()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .UseCallCounting()
            .Build();

        string input = "Red, White, and Blue";
        var embedding1 = await generator.GenerateAsync(input);
        var embedding2 = await generator.GenerateAsync(input);
        var embedding3 = await generator.GenerateAsync(input + "... and Green");
        var embedding4 = await generator.GenerateAsync(input);

        var callCounter = generator.GetService<CallCountingEmbeddingGenerator>();
        Assert.NotNull(callCounter);

        Assert.Equal(2, callCounter.CallCount);
    }

    [Fact]
    public virtual async Task OpenTelemetry_CanEmitTracesAndMetrics()
    {
        SkipIfNotEnabled();

        string sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        var embeddingGenerator = CreateEmbeddingGenerator()!
            .AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();

        _ = await embeddingGenerator.GenerateAsync("Hello, world!");

        Assert.Single(activities);
        var activity = activities.Single();
        Assert.StartsWith("embed", activity.DisplayName);
        Assert.Contains(".", (string)activity.GetTagItem("server.address")!);
        Assert.Equal(embeddingGenerator.GetService<EmbeddingGeneratorMetadata>()?.ProviderUri?.Port, (int)activity.GetTagItem("server.port")!);
        Assert.NotNull(activity.Id);
        Assert.NotEmpty(activity.Id);
        Assert.NotEqual(0, (int)activity.GetTagItem("gen_ai.usage.input_tokens")!);

        Assert.True(activity.Duration.TotalMilliseconds > 0);
    }

#if NET
    [Fact]
    public async Task Quantization_Binary_EmbeddingsCompareSuccessfully()
    {
        SkipIfNotEnabled();

        using IEmbeddingGenerator<string, BinaryEmbedding> generator =
            new QuantizationEmbeddingGenerator(
                CreateEmbeddingGenerator()!);

        var embeddings = await generator.GenerateAsync(["dog", "cat", "fork", "spoon"]);
        Assert.Equal(4, embeddings.Count);

        long[,] distances = new long[embeddings.Count, embeddings.Count];
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = 0; j < embeddings.Count; j++)
            {
                distances[i, j] = TensorPrimitives.HammingBitDistance<byte>(ToArray(embeddings[i].Vector), ToArray(embeddings[j].Vector));

                static byte[] ToArray(BitArray array)
                {
                    byte[] result = new byte[(array.Length + 7) / 8];
                    array.CopyTo(result, 0);
                    return result;
                }
            }
        }

        for (int i = 0; i < embeddings.Count; i++)
        {
            Assert.Equal(0, distances[i, i]);
        }

        Assert.True(distances[0, 1] < distances[0, 2]);
        Assert.True(distances[0, 1] < distances[0, 3]);
        Assert.True(distances[0, 1] < distances[1, 2]);
        Assert.True(distances[0, 1] < distances[1, 3]);

        Assert.True(distances[2, 3] < distances[0, 2]);
        Assert.True(distances[2, 3] < distances[0, 3]);
        Assert.True(distances[2, 3] < distances[1, 2]);
        Assert.True(distances[2, 3] < distances[1, 3]);
    }

    [Fact]
    public async Task Quantization_Half_EmbeddingsCompareSuccessfully()
    {
        SkipIfNotEnabled();

        using IEmbeddingGenerator<string, Embedding<Half>> generator =
            new QuantizationEmbeddingGenerator(
                CreateEmbeddingGenerator()!);

        var embeddings = await generator.GenerateAsync(["dog", "cat", "fork", "spoon"]);
        Assert.Equal(4, embeddings.Count);

        var distances = new Half[embeddings.Count, embeddings.Count];
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = 0; j < embeddings.Count; j++)
            {
                distances[i, j] = TensorPrimitives.CosineSimilarity(embeddings[i].Vector.Span, embeddings[j].Vector.Span);
            }
        }

        for (int i = 0; i < embeddings.Count; i++)
        {
            Assert.Equal(1.0, (double)distances[i, i], 0.001);
        }

        Assert.True(distances[0, 1] > distances[0, 2]);
        Assert.True(distances[0, 1] > distances[0, 3]);
        Assert.True(distances[0, 1] > distances[1, 2]);
        Assert.True(distances[0, 1] > distances[1, 3]);

        Assert.True(distances[2, 3] > distances[0, 2]);
        Assert.True(distances[2, 3] > distances[0, 3]);
        Assert.True(distances[2, 3] > distances[1, 2]);
        Assert.True(distances[2, 3] > distances[1, 3]);
    }
#endif

    [MemberNotNull(nameof(_embeddingGenerator))]
    protected void SkipIfNotEnabled()
    {
        Assert.SkipUnless(_embeddingGenerator is not null, "Generator is not enabled.");
    }
}
