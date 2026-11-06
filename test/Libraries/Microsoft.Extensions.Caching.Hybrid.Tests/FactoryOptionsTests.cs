// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if RUNTIME_PACKAGES_11_OR_GREATER
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Xunit;
using static Microsoft.Extensions.Caching.Hybrid.Tests.DistributedCacheTests;
using static Microsoft.Extensions.Caching.Hybrid.Tests.L2Tests;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

// Covers mutations the factory makes to the HybridCacheEntryContext it is handed.
public class FactoryOptionsTests(ITestOutputHelper log) : IClassFixture<TestEventListener>
{
    private static ServiceProvider GetDefaultCache(out DefaultHybridCache cache, Action<ServiceCollection>? config = null)
    {
        var services = new ServiceCollection();
        config?.Invoke(services);
        services.AddHybridCache();
        ServiceProvider provider = services.BuildServiceProvider();
        cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());
        return provider;
    }

    private static ServiceProvider BuildCacheWithL2(ITestOutputHelper log, out DefaultHybridCache cache, out CapturingCache localCache)
    {
        var captured = new CapturingCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        localCache = captured;
        return GetDefaultCache(out cache, services => services.AddSingleton<IDistributedCache>(new LoggingCache(log, captured)));
    }

    private static async Task WaitForBackgroundL2WriteAsync(CapturingCache cache, string key)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await cache.WaitForWriteAsync(key, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Assert.Fail($"Timed out waiting for background L2 write for key '{key}'.");
        }
    }

    [Fact]
    public async Task FactoryCanReEnableL2Write_ThatCallerDisabled()
    {
        // Caller disabled L2 writes; factory clears the flag — value must be persisted to L2.
        using var provider = BuildCacheWithL2(log, out var cache, out var localCache);
        string key = nameof(FactoryCanReEnableL2Write_ThatCallerDisabled);

        _ = await cache.GetOrCreateAsync(
            key,
            (entryOptions, _) =>
            {
                entryOptions.Flags = HybridCacheEntryFlags.None;
                return new ValueTask<Guid>(Guid.NewGuid());
            },
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite,
            });

        await WaitForBackgroundL2WriteAsync(localCache, key);
        Assert.NotNull(localCache.Get(key));
    }

    [Fact]
    public async Task FactoryCanDisableL2Write_ThatCallerEnabled()
    {
        // Symmetric tightening: caller allowed L2 writes (None), factory disables them.
        using var provider = BuildCacheWithL2(log, out var cache, out var localCache);
        string key = nameof(FactoryCanDisableL2Write_ThatCallerEnabled);

        _ = await cache.GetOrCreateAsync(
            key,
            (entryOptions, _) =>
            {
                entryOptions.Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite;
                return new ValueTask<Guid>(Guid.NewGuid());
            },
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                Flags = HybridCacheEntryFlags.None,
            });

        await Task.Delay(500);
        Assert.Null(localCache.Get(key));
    }

    [Fact]
    public async Task FactoryCanEnableL1Write_ThatCallerDisabled()
    {
        // L1 counterpart of FactoryCanReEnableL2Write: caller passed DisableLocalCacheWrite,
        // factory clears it. If the override sticks, a subsequent read returns the same value
        // from L1 without re-invoking the factory.
        using var provider = GetDefaultCache(out var cache);

        string key = nameof(FactoryCanEnableL1Write_ThatCallerDisabled);
        int factoryCalls = 0;

        var first = await cache.GetOrCreateAsync(
            key,
            (entryOptions, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                entryOptions.Flags = HybridCacheEntryFlags.None;
                return new ValueTask<Guid>(Guid.NewGuid());
            },
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                Flags = HybridCacheEntryFlags.DisableLocalCacheWrite,
            });

        Assert.Equal(1, factoryCalls);

        // Second call should be served from L1 — same Guid, factory not invoked again.
        var second = await cache.GetOrCreateAsync<Guid>(key, _ => new(Guid.NewGuid()));
        Assert.Equal(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task FactoryExpirationMutation_PropagatesToL2()
    {
        // Factory mutates Expiration; the L2 backend must receive DistributedCacheEntryOptions
        // whose AbsoluteExpirationRelativeToNow matches the factory-set value.
        var captured = new CapturingCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        using var provider = GetDefaultCache(out var cache, services => services.AddSingleton<IDistributedCache>(captured));

        var factoryExpiration = TimeSpan.FromMinutes(7);
        _ = await cache.GetOrCreateAsync(
            nameof(FactoryExpirationMutation_PropagatesToL2),
            (entryOptions, _) =>
            {
                entryOptions.Expiration = factoryExpiration;
                return new ValueTask<Guid>(Guid.NewGuid());
            },
            options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(1) });

        await WaitForBackgroundL2WriteAsync(captured, nameof(FactoryExpirationMutation_PropagatesToL2));
        Assert.Equal(factoryExpiration, captured.LastSetOptions?.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task FactoryLocalCacheExpirationMutation_ShortensL1Only()
    {
        // Factory sets LocalCacheExpiration tighter than Expiration. After the L1 entry expires
        // but before the overall entry does, the next call must re-fetch from L2 (factory not
        // invoked again), proving the factory-set L1 expiration was honored.
        var clock = new FakeTime();
        using var l1 = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var l2 = new LoggingCache(log, new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { Clock = clock })));

        using var provider = GetDefaultCache(out var cache, services =>
        {
            services.AddSingleton<ISystemClock>(clock);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IMemoryCache>(l1);
            services.AddSingleton<IDistributedCache>(l2);
        });

        string key = nameof(FactoryLocalCacheExpirationMutation_ShortensL1Only);
        int factoryCalls = 0;
        Func<HybridCacheEntryContext, CancellationToken, ValueTask<Guid>> factory = (entryContext, _) =>
        {
            Interlocked.Increment(ref factoryCalls);
            entryContext.LocalCacheExpiration = TimeSpan.FromSeconds(30);
            return new ValueTask<Guid>(Guid.NewGuid());
        };

        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) };

        var first = await cache.GetOrCreateAsync(key, factory, options);
        Assert.Equal(1, factoryCalls);

        // Past factory-set L1 expiration (30s) but well before overall expiration (5m).
        clock.Add(TimeSpan.FromSeconds(45));

        var second = await cache.GetOrCreateAsync(key, factory, options);

        // Same value (came from L2 round-trip), factory not invoked again.
        Assert.Equal(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task FactoryLocalSizeMutation_HonoredForL1SizeAccounting()
    {
        // Tight L1 SizeLimit + a payload large enough to exceed it. Without the override the
        // entry would be evicted from L1; the factory sets LocalSize = 1 to make the entry fit.
        using var provider = GetDefaultCache(out var cache, services => services.AddMemoryCache(options => options.SizeLimit = 5));

        string key = nameof(FactoryLocalSizeMutation_HonoredForL1SizeAccounting);
        int factoryCalls = 0;

        // Use a string payload large enough that its serialized size would otherwise exceed
        // the L1 SizeLimit (the default L1 size for a string is its byte length).
        string payload = new('x', 256);

        var first = await cache.GetOrCreateAsync(
            key,
            (entryOptions, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                entryOptions.LocalSize = 1;
                return new ValueTask<string>(payload);
            },
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                Flags = HybridCacheEntryFlags.DisableDistributedCache, // no L2; force L1-only path
            });

        Assert.Equal(1, factoryCalls);

        // Second call: served from L1 because the override kept the entry under SizeLimit.
        var second = await cache.GetOrCreateAsync<string>(key, _ => new(Guid.NewGuid().ToString()));
        Assert.Equal(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task FactoryReceivesUsableContext_WhenCallerPassedNull()
    {
        // The context-aware overload must hand the factory a real, mutable context even when
        // the caller did not supply options.
        using var provider = BuildCacheWithL2(log, out var cache, out var localCache);
        string key = nameof(FactoryReceivesUsableContext_WhenCallerPassedNull);
        int factoryCalls = 0;

        _ = await cache.GetOrCreateAsync<Guid>(
            key,
            (entryContext, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                Assert.NotNull(entryContext);
                entryContext.Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite;
                return new ValueTask<Guid>(Guid.NewGuid());
            });

        Assert.Equal(1, factoryCalls);

        // The factory's mutation must have taken effect — no value written to L2.
        await Task.Delay(500);
        Assert.Null(localCache.Get(key));
    }

    [Fact]
    public async Task FactoryLocalSize_PersistedInL2_AndReappliedOnL2Reload()
    {
        // The factory-set LocalSize must be persisted into the L2 payload so a *different* cache
        // instance reading from the shared L2 still gets the size override applied to its L1 entry.
        //
        // Setup:
        //   Cache A: unlimited L1 SizeLimit; factory sets LocalSize=1, payload is 256 bytes.
        //   Cache B: shares L2 with A, has SizeLimit=5. On its first read it must fetch from L2,
        //     and the persisted LocalSize=1 must be reapplied to its L1 entry so the (256-byte)
        //     value fits. A second read on B then comes from L1 — observable via the LoggingCache
        //     L2 op count not increasing.
        var sharedL2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        string key = nameof(FactoryLocalSize_PersistedInL2_AndReappliedOnL2Reload);
        string payload = new('x', 256);

        // ---- Cache A: writes with factory-set LocalSize override ----
        var capturingA = new CapturingCache(sharedL2);
        var servicesA = new ServiceCollection();
        servicesA.AddMemoryCache(); // unlimited
        servicesA.AddSingleton<IDistributedCache>(capturingA);
        servicesA.AddHybridCache();
        using (var providerA = servicesA.BuildServiceProvider())
        {
            var cacheA = providerA.GetRequiredService<HybridCache>();
            _ = await cacheA.GetOrCreateAsync(
                key,
                (entryOptions, _) =>
                {
                    entryOptions.LocalSize = 1;
                    return new ValueTask<string>(payload);
                },
                options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) });

            await WaitForBackgroundL2WriteAsync(capturingA, key);
        }

        // Confirm the value was actually written to L2 by Cache A.
        Assert.NotNull(sharedL2.Get(key));

        // ---- Cache B: shares L2, has a tight SizeLimit that the raw payload would exceed ----
        var loggingB = new LoggingCache(log, sharedL2);
        var servicesB = new ServiceCollection();
        servicesB.AddMemoryCache(options => options.SizeLimit = 5);
        servicesB.AddSingleton<IDistributedCache>(loggingB);
        servicesB.AddHybridCache();
        using var providerB = servicesB.BuildServiceProvider();
        var cacheB = providerB.GetRequiredService<HybridCache>();

        // First call: L1 miss, fetches from L2; persisted LocalSize override must let it fit in L1.
        var firstB = await cacheB.GetOrCreateAsync<string>(key, _ => new(Guid.NewGuid().ToString()));
        Assert.Equal(payload, firstB);
        int opsAfterFirst = loggingB.OpCount;

        // Second call must hit L1 — no further L2 traffic. If the LocalSize override
        // wasn't reapplied, the entry would have been evicted from L1 immediately and
        // this call would have to re-read L2.
        var secondB = await cacheB.GetOrCreateAsync<string>(key, _ => new(Guid.NewGuid().ToString()));
        Assert.Equal(payload, secondB);
        Assert.Equal(opsAfterFirst, loggingB.OpCount);
    }

    // Test-only IDistributedCache that wraps another IDistributedCache and adds:
    //   - LastSetOptions: the DistributedCacheEntryOptions from the most recent Set/SetAsync,
    //     for tests that assert on the options the HybridCache layer produced.
    //   - WaitForWriteAsync(key): a Task that completes after the (possibly background) write
    //     for that key has finished, so tests can wait deterministically instead of sleeping.
    private sealed class CapturingCache(IDistributedCache tail) : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _writes = new(StringComparer.Ordinal);

        public DistributedCacheEntryOptions? LastSetOptions { get; private set; }

        public Task WaitForWriteAsync(string key, CancellationToken cancellationToken = default)
        {
            var tcs = SignalFor(key);
            if (!cancellationToken.CanBeCanceled)
            {
                return tcs.Task;
            }

            return WaitWithCancellationAsync(tcs.Task, cancellationToken);

            static async Task WaitWithCancellationAsync(Task task, CancellationToken ct)
            {
                var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), cancelTcs))
                {
                    var completed = await Task.WhenAny(task, cancelTcs.Task).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                }
            }
        }

        public byte[]? Get(string key) => tail.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => tail.GetAsync(key, token);
        public void Refresh(string key) => tail.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => tail.RefreshAsync(key, token);
        public void Remove(string key) => tail.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => tail.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastSetOptions = options;
            tail.Set(key, value, options);
            _ = SignalFor(key).TrySetResult(true);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            LastSetOptions = options;
            await tail.SetAsync(key, value, options, token).ConfigureAwait(false);
            _ = SignalFor(key).TrySetResult(true);
        }

        private TaskCompletionSource<bool> SignalFor(string key)
            => _writes.GetOrAdd(key, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    [Fact]
    public void CreateOptionsFromContext_CopiesEveryPublicWritableContextProperty()
    {
        // Guards the context -> options mapping in DefaultHybridCache against silent data loss when
        // HybridCacheEntryContext gains a new writable property that CreateOptionsFromContext doesn't copy.
        var contextProps = typeof(HybridCacheEntryContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.NotEmpty(contextProps);

        var context = new HybridCacheEntryContext(null);
        var expected = new Dictionary<string, object?>(contextProps.Length);
        foreach (var prop in contextProps)
        {
            object? value = MakeDistinctiveValue(prop.PropertyType, prop.Name);
            prop.SetValue(context, value);
            expected[prop.Name] = value;
        }

        HybridCacheEntryOptions options = DefaultHybridCache.CreateOptionsFromContext(context);

        foreach (var prop in contextProps)
        {
            PropertyInfo optionsProp = typeof(HybridCacheEntryOptions).GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance)!;
            Assert.NotNull(optionsProp);
            Assert.Equal(expected[prop.Name], optionsProp.GetValue(options));
        }
    }

    private static object MakeDistinctiveValue(Type t, string propName)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;

        // Per-property unique values so cross-wired assignments
        // (e.g. Expiration <-> LocalCacheExpiration) are also caught.
        if (underlying == typeof(TimeSpan))
        {
            return TimeSpan.FromSeconds((StableHash(propName) % 3600) + 1);
        }

        if (underlying == typeof(long))
        {
            return (long)StableHash(propName) + 1;
        }

        if (underlying.IsEnum)
        {
            var nonDefault = Enum.GetValues(underlying).Cast<object>()
                .FirstOrDefault(v => !v.Equals(Activator.CreateInstance(underlying)));
            return nonDefault ?? Activator.CreateInstance(underlying)!;
        }

        throw new NotSupportedException(
            $"HybridCacheEntryContext has a new property '{propName}' of type {underlying.FullName}. " +
            $"Add a distinctive value generator here AND update DefaultHybridCache.CreateOptionsFromContext.");
    }

    private static int StableHash(string s)
    {
        // FNV-ish stable hash; avoids dependence on string.GetHashCode randomization.
        int h = 17;
        foreach (char c in s)
        {
            h = unchecked((h * 31) + c);
        }

        return Math.Abs(h);
    }

    [Fact]
    public async Task FactoryNegativeLocalSize_Throws()
    {
        using var provider = GetDefaultCache(out var cache);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrCreateAsync(
            nameof(FactoryNegativeLocalSize_Throws),
            (entryOptions, _) =>
            {
                entryOptions.LocalSize = -1;
                return new ValueTask<int>(0);
            }).AsTask());

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public async Task DefaultEntryOptionsLocalSize_AppliedWhenCallerOmitsIt()
    {
        // We prove that DefaultEntryOptions are honored by setting a tight
        // L1 SizeLimit + a default LocalSize=1 so a 256-byte payload (which would normally exceed
        // SizeLimit and be evicted) survives in L1. A second call must hit L1 (same value back,
        // factory not re-invoked) without the caller ever supplying per-call options.
        string key = nameof(DefaultEntryOptionsLocalSize_AppliedWhenCallerOmitsIt);
        int factoryCalls = 0;

        using var provider = GetDefaultCache(out var cache, services =>
        {
            services.AddMemoryCache(o => o.SizeLimit = 5);
            services.Configure<HybridCacheOptions>(o => o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalSize = 1,
                Flags = HybridCacheEntryFlags.DisableDistributedCache
            });
        });

        string payload = new('x', 256);
        var first = await cache.GetOrCreateAsync(key, _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new ValueTask<string>(payload);
        });
        Assert.Equal(1, factoryCalls);

        var second = await cache.GetOrCreateAsync(key, _ => new ValueTask<string>(Guid.NewGuid().ToString()));
        Assert.Equal(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void DefaultEntryOptionsNegativeLocalSize_ThrowsAtConstruction()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.Configure<HybridCacheOptions>(o => o.DefaultEntryOptions = new HybridCacheEntryOptions { LocalSize = -1 });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<HybridCache>());
        Assert.Equal("options", ex.ParamName);
    }
}
#endif
