// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace VectorData.ConformanceTests;

/// <summary>
/// Base class for integration tests that verify client-side embedding generation with hybrid search,
/// for any <see cref="IKeywordHybridSearchable{TRecord}"/>.
/// </summary>
/// <typeparam name="TKey">The type of key to use with the record collection.</typeparam>
public abstract class HybridSearchEmbeddingGenerationTests<TKey>(HybridSearchEmbeddingGenerationTests<TKey>.Fixture fixture)
    where TKey : notnull
{
    [Fact]
    public virtual async Task HybridSearchAsync_with_embedding_generator()
    {
        // The generated vector and the keyword both point at the Oranges record.
        var result = await fixture.HybridSearchable.HybridSearchAsync("[0, 1, 0]", ["Oranges"], top: 1).SingleAsync();

        Assert.Equal("Oranges are tangy and contain vitamin c", result.Record.Text);
    }

    [Fact]
    public virtual async Task HybridSearchAsync_with_embedding_generator_uses_generated_vector()
    {
        // The keyword doesn't match any record, so the ranking is driven by the generated vector only.
        var result = await fixture.HybridSearchable.HybridSearchAsync("[0, 0, 1]", ["Kiwis"], top: 1).SingleAsync();

        Assert.Equal("Grapes are healthy, sweet and juicy", result.Record.Text);
    }

    [Fact]
    public virtual async Task HybridSearchAsync_with_IncludeVectors_throws()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => fixture.HybridSearchable.HybridSearchAsync("[1, 0, 0]", ["Apples"], top: 1, new() { IncludeVectors = true }).ToListAsync().AsTask());

        Assert.Equal("When an embedding generator is configured, `Include Vectors` cannot be enabled.", exception.Message);
    }

    public class Record : TestRecord<TKey>
    {
        public string? Embedding { get; set; }

        public string? Text { get; set; }
    }

    public abstract class Fixture : VectorStoreCollectionFixture<TKey, Record>
    {
        protected override string CollectionNameBase => "HybridSearchEmbeddingGenerationTests";

        public IKeywordHybridSearchable<Record> HybridSearchable
            => (IKeywordHybridSearchable<Record>)Collection;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => new()
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(Record.Key), typeof(TKey)),
                    new VectorStoreDataProperty(nameof(Record.Text), typeof(string)) { IsFullTextIndexed = true },
                    new VectorStoreVectorProperty(nameof(Record.Embedding), typeof(string), dimensions: 3)
                    {
                        DistanceFunction = DefaultDistanceFunction,
                        IndexKind = DefaultIndexKind
                    }
                ],
#pragma warning disable CA2000 // Don't actually need to dispose FakeEmbeddingGenerator
                EmbeddingGenerator = new FakeEmbeddingGenerator()
#pragma warning restore CA2000
            };

        protected override List<Record> BuildTestData() =>
        [
            new() { Key = GenerateNextKey<TKey>(), Text = "Apples are a healthy and nourishing snack", Embedding = "[1, 0, 0]" },
            new() { Key = GenerateNextKey<TKey>(), Text = "Oranges are tangy and contain vitamin c", Embedding = "[0, 1, 0]" },
            new() { Key = GenerateNextKey<TKey>(), Text = "Grapes are healthy, sweet and juicy", Embedding = "[0, 0, 1]" }
        ];
    }

    /// <summary>Converts strings such as "[1, 0, 0]" to the corresponding vector.</summary>
    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var value in values)
            {
                var vector = value.TrimStart('[').TrimEnd(']').Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                results.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
