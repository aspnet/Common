// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace VectorData.ConformanceTests.Support;

#pragma warning disable CA1721 // Property names should not match get methods
#pragma warning disable S4059 // Property names should not match get methods

/// <summary>
/// A test fixture that sets up a single collection in the test vector store, with a specific record definition
/// and test data.
/// </summary>
public abstract class VectorStoreCollectionFixtureBase<TKey, TRecord> : VectorStoreFixture
    where TKey : notnull
    where TRecord : class
{
    public abstract VectorStoreCollectionDefinition CreateRecordDefinition();
    protected virtual List<TRecord> BuildTestData() => [];

    /// <summary>
    /// The base name for the test collection used in tests, before any provider-specific collection naming rules have been applied.
    /// </summary>
    protected abstract string CollectionNameBase { get; }

    /// <summary>
    /// Gets the actual name of the test collection after any provider-specific collection naming rules have been applied.
    /// </summary>
    public virtual string CollectionName => TestStore.AdjustCollectionName(CollectionNameBase);

    protected virtual string DistanceFunction => TestStore.DefaultDistanceFunction;
    protected virtual string IndexKind => TestStore.DefaultIndexKind;

    protected virtual VectorStoreCollection<TKey, TRecord> GetCollection()
        => TestStore.CreateCollection<TKey, TRecord>(CollectionName, CreateRecordDefinition());

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Collection = GetCollection();

        if (await Collection.CollectionExistsAsync())
        {
            await Collection.EnsureCollectionDeletedAsync();
        }

        await Collection.EnsureCollectionExistsAsync();
        await SeedAsync();
    }

    public virtual VectorStoreCollection<TKey, TRecord> Collection { get; private set; } = null!;

    public List<TRecord> TestData => field ??= BuildTestData();

    protected virtual async Task SeedAsync()
    {
        if (TestData.Count > 0)
        {
            await Collection.UpsertAsync(TestData);
            await WaitForDataAsync();
        }
    }

    protected virtual Task WaitForDataAsync()
        => TestStore.WaitForDataAsync(Collection, recordCount: TestData.Count);
}
