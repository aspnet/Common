// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace VectorData.ConformanceTests;

public abstract class CollectionManagementTests<TKey>(VectorStoreFixture fixture) : IAsyncLifetime
    where TKey : notnull
{
    public ValueTask InitializeAsync()
        => new(fixture.VectorStore.EnsureCollectionDeletedAsync(CollectionName));

    [Fact]
    public virtual async Task Collection_Ensure_Exists_Delete()
    {
        var collection = GetCollection();

        Assert.False(await collection.CollectionExistsAsync());
        await collection.EnsureCollectionExistsAsync();
        Assert.True(await collection.CollectionExistsAsync());
        await collection.EnsureCollectionDeletedAsync();
        Assert.False(await collection.CollectionExistsAsync());

        // Deleting a non-existing collection does not throw
        await fixture.TestStore.DefaultVectorStore.EnsureCollectionDeletedAsync(collection.Name);
    }

    [Fact]
    public virtual async Task EnsureCollectionExists_twice_does_not_throw()
    {
        var collection = GetCollection();

        await collection.EnsureCollectionExistsAsync();
        await collection.EnsureCollectionExistsAsync();
        Assert.True(await collection.CollectionExistsAsync());
    }

    [Fact]
    public virtual async Task Store_CollectionExists()
    {
        var store = fixture.VectorStore;
        var collection = GetCollection();

        Assert.False(await store.CollectionExistsAsync(collection.Name));
        await collection.EnsureCollectionExistsAsync();
        Assert.True(await store.CollectionExistsAsync(collection.Name));
    }

    [Fact]
    public virtual async Task Store_DeleteCollection()
    {
        var store = fixture.VectorStore;
        var collection = GetCollection();

        await collection.EnsureCollectionExistsAsync();
        await fixture.TestStore.DefaultVectorStore.EnsureCollectionDeletedAsync(collection.Name);
        Assert.False(await collection.CollectionExistsAsync());
    }

    [Fact]
    public virtual async Task Store_ListCollections()
    {
        var store = fixture.VectorStore;
        var collection = GetCollection();

        Assert.Empty(await store.ListCollectionNamesAsync().Where(n => n == collection.Name).ToListAsync());

        await collection.EnsureCollectionExistsAsync();

        var name = Assert.Single(await store.ListCollectionNamesAsync().Where(n => n == collection.Name).ToListAsync());
        Assert.Equal(collection.Name, name);
    }

    [Fact]
    public virtual void Collection_metadata()
    {
        var collection = GetCollection();

        var collectionMetadata = (VectorStoreCollectionMetadata?)collection.GetService(typeof(VectorStoreCollectionMetadata));

        Assert.NotNull(collectionMetadata);
        Assert.NotNull(collectionMetadata.VectorStoreSystemName);
        Assert.NotNull(collectionMetadata.CollectionName);
    }

    protected virtual string CollectionNameBase => nameof(CollectionManagementTests<object>);
    public virtual string CollectionName => fixture.TestStore.AdjustCollectionName(CollectionNameBase);

    public sealed class Record : TestRecord<TKey>
    {
        public string? Text { get; set; }
        public int Number { get; set; }
        public ReadOnlyMemory<float> Floats { get; set; }
    }

    public virtual VectorStoreCollection<TKey, Record> GetCollection()
        => fixture.TestStore.CreateCollection<TKey, Record>(CollectionName, CreateRecordDefinition());

    public virtual VectorStoreCollectionDefinition CreateRecordDefinition()
        => new()
        {
            Properties =
            [
                new VectorStoreKeyProperty(nameof(Record.Key), typeof(TKey)) { StorageName = "key" },
                new VectorStoreDataProperty(nameof(Record.Text), typeof(string)) { StorageName = "text" },
                new VectorStoreDataProperty(nameof(Record.Number), typeof(int)) { StorageName = "number" },
                new VectorStoreVectorProperty(nameof(Record.Floats), typeof(ReadOnlyMemory<float>), 10) { IndexKind = fixture.TestStore.DefaultIndexKind }
            ]
        };

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
