// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using Xunit;

namespace VectorData.ConformanceTests.Support;

public abstract class VectorStoreFixture : IAsyncLifetime
{
    private int _nextKeyValue = 1;

    public abstract TestStore TestStore { get; }
    public virtual VectorStore VectorStore => TestStore.DefaultVectorStore;

    public virtual string DefaultDistanceFunction => TestStore.DefaultDistanceFunction;
    public virtual string DefaultIndexKind => TestStore.DefaultIndexKind;

    public virtual ValueTask InitializeAsync()
        => new(TestStore.ReferenceCountingStartAsync());

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(TestStore.ReferenceCountingStopAsync());
    }

    public virtual TKey GenerateNextKey<TKey>()
        => TestStore.GenerateKey<TKey>(Interlocked.Increment(ref _nextKeyValue));

    /// <summary>
    /// Creates a collection for the given name and definition.
    /// </summary>
    /// <remarks>
    /// This delegates to <see cref="TestStore.CreateCollection{TKey, TRecord}"/>, which can be overridden for provider-specific options.
    /// </remarks>
    public virtual VectorStoreCollection<TKey, TRecord> CreateCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition definition)
        where TKey : notnull
        where TRecord : class
        => TestStore.CreateCollection<TKey, TRecord>(name, definition);

    /// <summary>
    /// Creates a dynamic collection for the given name and definition.
    /// </summary>
    /// <remarks>
    /// This delegates to <see cref="TestStore.CreateDynamicCollection"/>, which can be overridden for provider-specific options.
    /// </remarks>
    public virtual VectorStoreCollection<object, Dictionary<string, object?>> CreateDynamicCollection(string name, VectorStoreCollectionDefinition definition)
        => TestStore.CreateDynamicCollection(name, definition);
}
