// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace VectorData.ConformanceTests.ModelTests;

/// <summary>
/// Tests using a model with multiple vectors.
/// </summary>
public abstract class MultiVectorModelTests<TKey>(MultiVectorModelTests<TKey>.Fixture fixture) : IAsyncLifetime
    where TKey : notnull
{
    [Theory]
    [MemberData(nameof(IncludeVectorsData))]
    public virtual async Task GetAsync_single_record(bool includeVectors)
    {
        var expectedRecord = fixture.TestData[0];

        var received = await Collection.GetAsync(expectedRecord.Key, new() { IncludeVectors = includeVectors });

        expectedRecord.AssertEqual(received, includeVectors, fixture.TestStore.VectorsComparable);
    }

    [Fact]
    public virtual async Task Insert_single_record()
    {
        TKey expectedKey = fixture.GenerateNextKey<TKey>();
        MultiVectorRecord inserted = new()
        {
            Key = expectedKey,
            Number = 10,
            Vector1 = new([10, 0, 0]),
            Vector2 = new([10, 0, 0]),
        };

        Assert.Null(await Collection.GetAsync(expectedKey));
        await Collection.UpsertAsync(inserted);

        var received = await Collection.GetAsync(expectedKey, new() { IncludeVectors = true });
        inserted.AssertEqual(received, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    [Fact]
    public virtual async Task Delete_single_record()
    {
        var keyToRemove = fixture.TestData[0].Key;

        await Collection.DeleteAsync(keyToRemove);
        Assert.Null(await Collection.GetAsync(keyToRemove));
    }

    [Fact]
    public virtual async Task SearchAsync_with_multiple_vector_properties()
    {
        var result = await Collection
            .SearchAsync(new ReadOnlyMemory<float>([1, 2, 3]), top: 1, new() { VectorProperty = r => r.Vector1, IncludeVectors = true })
            .SingleAsync();
        fixture.TestData[0].AssertEqual(result.Record, includeVectors: true, fixture.TestStore.VectorsComparable);

        result = await Collection
            .SearchAsync(new ReadOnlyMemory<float>([10, 2, 6]), top: 1, new() { VectorProperty = r => r.Vector2, IncludeVectors = true })
            .SingleAsync();
        fixture.TestData[1].AssertEqual(result.Record, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    [Fact]
    public virtual async Task Search_without_explicitly_specified_vector_property_fails()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Collection.SearchAsync(new ReadOnlyMemory<float>([1, 2, 3]), top: 1).ToListAsync());

        Assert.Equal($"The '{nameof(MultiVectorRecord)}' type has multiple vector properties, please specify your chosen property via options.", exception.Message);
    }

    protected VectorStoreCollection<TKey, MultiVectorRecord> Collection => fixture.Collection;

    public abstract class Fixture : VectorStoreCollectionFixture<TKey, MultiVectorRecord>
    {
        protected override string CollectionNameBase => "MultiVectorModelTests";

        protected override List<MultiVectorRecord> BuildTestData() =>
        [
            new()
            {
                Key = GenerateNextKey<TKey>(),
                Number = 1,
                Vector1 = new([1, 2, 3]),
                Vector2 = new([10, 2, 4])
            },
            new()
            {
                Key = GenerateNextKey<TKey>(),
                Number = 2,
                Vector1 = new([1, 2, 5]),
                Vector2 = new([10, 2, 6])
            }
        ];

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => new()
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(MultiVectorRecord.Key), typeof(TKey)),
                    new VectorStoreDataProperty(nameof(MultiVectorRecord.Number), typeof(int)),

                    new VectorStoreVectorProperty(nameof(MultiVectorRecord.Vector1), typeof(ReadOnlyMemory<float>), 3)
                    {
                        DistanceFunction = DistanceFunction,
                        IndexKind = IndexKind
                    },

                    new VectorStoreVectorProperty(nameof(MultiVectorRecord.Vector2), typeof(ReadOnlyMemory<float>), 3)
                    {
                        DistanceFunction = DistanceFunction,
                        IndexKind = IndexKind
                    }
                ]
            };

        protected override Task WaitForDataAsync()
            => TestStore.WaitForDataAsync(Collection, recordCount: TestData.Count, vectorProperty: r => r.Vector1);
    }

    public sealed class MultiVectorRecord : TestRecord<TKey>
    {
        public int Number { get; set; }

        public ReadOnlyMemory<float> Vector1 { get; set; }
        public ReadOnlyMemory<float> Vector2 { get; set; }

        public void AssertEqual(MultiVectorRecord? other, bool includeVectors, bool compareVectors)
        {
            Assert.NotNull(other);

            Assert.Equal(Key, other.Key);
            Assert.Equal(Number, other.Number);

            if (includeVectors)
            {
                Assert.Equal(Vector1.Span.Length, other.Vector1.Span.Length);
                Assert.Equal(Vector2.Span.Length, other.Vector2.Span.Length);

                if (compareVectors)
                {
                    Assert.True(Vector1.Span.SequenceEqual(other.Vector1.Span));
                    Assert.True(Vector2.Span.SequenceEqual(other.Vector2.Span));
                }
            }
        }
    }

    public ValueTask InitializeAsync()
        => new(fixture.ReseedAsync());

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    public static readonly TheoryData<bool> IncludeVectorsData = [false, true];
}
