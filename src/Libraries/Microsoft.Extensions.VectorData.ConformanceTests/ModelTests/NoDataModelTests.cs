// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace VectorData.ConformanceTests.ModelTests;

/// <summary>
/// Tests using a model without data fields, only a key and an embedding.
/// </summary>
public abstract class NoDataModelTests<TKey>(NoDataModelTests<TKey>.Fixture fixture) : IAsyncLifetime
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
        NoDataRecord inserted = new()
        {
            Key = expectedKey,
            Floats = new([10, 0, 0])
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

    protected VectorStoreCollection<TKey, NoDataRecord> Collection => fixture.Collection;

    public abstract class Fixture : VectorStoreCollectionFixture<TKey, NoDataRecord>
    {
        protected override string CollectionNameBase => nameof(NoDataModelTests<int>);

        protected override List<NoDataRecord> BuildTestData() =>
        [
            new()
            {
                Key = GenerateNextKey<TKey>(),
                Floats = new([1, 2, 3])
            },
            new()
            {
                Key = GenerateNextKey<TKey>(),
                Floats = new([1, 2, 4])
            }
        ];

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => new()
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(NoDataRecord.Key), typeof(TKey)),
                    new VectorStoreVectorProperty(nameof(NoDataRecord.Floats), typeof(ReadOnlyMemory<float>), 3)
                    {
                        IndexKind = IndexKind
                    }
                ]
            };
    }

    public sealed class NoDataRecord : TestRecord<TKey>
    {
        [VectorStoreVector(dimensions: 3, StorageName = "embedding")]
        public ReadOnlyMemory<float> Floats { get; set; }

        public void AssertEqual(NoDataRecord? other, bool includeVectors, bool compareVectors)
        {
            Assert.NotNull(other);
            Assert.Equal(Key, other.Key);

            if (includeVectors)
            {
                Assert.Equal(Floats.Span.Length, other.Floats.Span.Length);

                if (compareVectors)
                {
                    Assert.True(Floats.Span.SequenceEqual(other.Floats.Span));
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
