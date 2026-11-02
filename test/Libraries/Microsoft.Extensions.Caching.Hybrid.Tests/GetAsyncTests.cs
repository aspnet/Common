// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class GetAsyncTests : IClassFixture<TestEventListener>
{
    private static ServiceProvider GetDefaultCache(out DefaultHybridCache cache, IDistributedCache? l2 = null)
    {
        var services = new ServiceCollection();
        if (l2 is not null)
        {
            services.AddSingleton(l2);
        }

        services.AddHybridCache();
        ServiceProvider provider = services.BuildServiceProvider();
        cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());
        return provider;
    }

    private static DelegatingDistributedCache CreateL2(TimeSpan delay = default)
        => new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), delay);

    [Fact]
    public async Task MissReturnsNotFoundAndDoesNotCreate()
    {
        using var provider = GetDefaultCache(out var cache);
        var key = Me();

        var (found, value) = await cache.GetAsync<string>(key);
        Assert.False(found);
        Assert.Null(value);

        // nothing was stored, so the factory runs
        Assert.Equal("created", await cache.GetOrCreateAsync(key, _ => new ValueTask<string>("created")));
    }

    [Fact]
    public async Task HitReturnsStoredValue()
    {
        using var provider = GetDefaultCache(out var cache);
        var key = Me();
        await cache.SetAsync(key, 42);

        var (found, value) = await cache.GetAsync<int>(key);
        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task CachedDefaultValueIsFound()
    {
        using var provider = GetDefaultCache(out var cache);
        var key = Me();
        await cache.SetAsync(key, 0);

        var (found, value) = await cache.GetAsync<int>(key);
        Assert.True(found);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task MutableTypeIsDefensivelyCopied()
    {
        using var provider = GetDefaultCache(out var cache);
        var key = Me();
        await cache.SetAsync(key, new Customer { Name = "abc" });

        var (found1, first) = await cache.GetAsync<Customer>(key);
        var (found2, second) = await cache.GetAsync<Customer>(key);
        Assert.True(found1);
        Assert.True(found2);
        Assert.Equal("abc", first!.Name);
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\u0000b")]
    public async Task InvalidKeyReturnsNotFound(string key)
    {
        using var provider = GetDefaultCache(out var cache);

        var (found, value) = await cache.GetAsync<string>(key);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task ValueIsReadFromL2()
    {
        var l2 = CreateL2();
        using var provider = GetDefaultCache(out var cache, l2);
        var key = Me();
        await cache.SetAsync(key, "from L2");
        cache.LocalCache.Remove(key);

        var (found, value) = await cache.GetAsync<string>(key);
        Assert.True(found);
        Assert.Equal("from L2", value);

        // the L2 hit was copied into L1
        Assert.True(cache.LocalCache.TryGetValue(key, out _));
    }

    [Fact]
    public async Task L2HitIsNotCopiedToL1WhenLocalWriteIsDisabled()
    {
        var l2 = CreateL2();
        using var provider = GetDefaultCache(out var cache, l2);
        var key = Me();
        await cache.SetAsync(key, "from L2");
        cache.LocalCache.Remove(key);

        var (found, _) = await cache.GetAsync<string>(key, new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });
        Assert.True(found);
        Assert.False(cache.LocalCache.TryGetValue(key, out _));
    }

    [Fact]
    public async Task MissIsNotWrittenToL1OrL2()
    {
        var l2 = CreateL2();
        using var provider = GetDefaultCache(out var cache, l2);
        var key = Me();

        var (found, _) = await cache.GetAsync<string>(key);
        Assert.False(found);
        Assert.False(cache.LocalCache.TryGetValue(key, out _));
        Assert.Null(await l2.GetAsync(key));
        Assert.Equal(0, l2.SetCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentMissesAreAllReportedAsNotFound(bool cancellable)
    {
        var l2 = CreateL2(TimeSpan.FromMilliseconds(250));
        using var provider = GetDefaultCache(out var cache, l2);
        var key = Me();
        using var cts = new CancellationTokenSource();
        CancellationToken token = cancellable ? cts.Token : CancellationToken.None;
        int getsBefore = l2.GetCount; // construction reads the wildcard tag from L2

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetAsync<string>(key, cancellationToken: token).AsTask()));

        Assert.All(results, result => Assert.False(result.Found));
        Assert.Equal(1, l2.GetCount - getsBefore); // the readers shared one L2 fetch
    }

    [Fact]
    public async Task JoinedGetOrCreateMissesWithoutUnderlyingDataBalanceReservations()
    {
        // regression: a miss used to complete every joined caller with one shared item that held a single
        // reservation, so each extra caller over-released it (a Debug.Assert, and a drifting counter in release)
        var l2 = CreateL2(TimeSpan.FromMilliseconds(250));
        using var provider = GetDefaultCache(out var cache, l2);
        var options = new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableUnderlyingData };

        for (int round = 0; round < 2; round++)
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
                cache.GetOrCreateAsync(Me(), _ => new ValueTask<string>("not called"), options).AsTask()));
            Assert.All(results, Assert.Null);
        }
    }

    [Fact]
    public async Task ConcurrentHitsAreAllReportedAsFound()
    {
        var l2 = CreateL2(TimeSpan.FromMilliseconds(250));
        using var provider = GetDefaultCache(out var cache, l2);
        var key = Me();
        await cache.SetAsync(key, "shared");
        cache.LocalCache.Remove(key);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetAsync<string>(key).AsTask()));

        Assert.All(results, result =>
        {
            Assert.True(result.Found);
            Assert.Equal("shared", result.Value);
        });
    }

    [Fact]
    public async Task CancelledTokenThrows()
    {
        using var provider = GetDefaultCache(out var cache);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cache.GetAsync<string>(Me(), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NullCacheThrows()
    {
        HybridCache cache = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await cache.GetAsync<string>("key"));
    }

    [Fact]
    public async Task OtherImplementationsUseFactoryWithWritesDisabled()
    {
        var cache = new DictionaryHybridCache();
        await cache.SetAsync("present", 0);

        var (found, value) = await cache.GetAsync<int>("present");
        Assert.True(found);
        Assert.Equal(0, value);

        (found, value) = await cache.GetAsync<int>("absent");
        Assert.False(found);
        Assert.Equal(0, value);
        Assert.False(cache.Contains("absent"));
        Assert.Equal(HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite, cache.LastFlags);
    }

    public sealed class Customer
    {
        public string? Name { get; set; }
    }

    // a non-default HybridCache that honours the write flags, to exercise the fallback path
    private sealed class DictionaryHybridCache : HybridCache
    {
        private readonly ConcurrentDictionary<string, object?> _values = new();

        public HybridCacheEntryFlags? LastFlags { get; private set; }

        public bool Contains(string key) => _values.ContainsKey(key);

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            LastFlags = options?.Flags;
            if (_values.TryGetValue(key, out object? existing))
            {
                return (T)existing!;
            }

            T value = await factory(state, cancellationToken);
            if (((options?.Flags ?? HybridCacheEntryFlags.None) & HybridCacheEntryFlags.DisableLocalCacheWrite) == 0)
            {
                _values[key] = value;
            }

            return value;
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return default;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _ = _values.TryRemove(key, out _);
            return default;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => default;
    }

    // wraps MemoryDistributedCache so that HybridCache treats it as a real L2, and counts the calls
    private sealed class DelegatingDistributedCache(IDistributedCache inner, TimeSpan delay) : IDistributedCache
    {
        private int _getCount;
        private int _setCount;

        public int GetCount => Volatile.Read(ref _getCount);
        public int SetCount => Volatile.Read(ref _setCount);

        public byte[]? Get(string key) => inner.Get(key);

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            _ = Interlocked.Increment(ref _getCount);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            return await inner.GetAsync(key, token);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _ = Interlocked.Increment(ref _setCount);
            return inner.SetAsync(key, value, options, token);
        }
    }

    private static string Me([CallerMemberName] string caller = "") => caller;
}
