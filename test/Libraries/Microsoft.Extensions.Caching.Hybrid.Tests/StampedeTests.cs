// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class StampedeTests : IClassFixture<TestEventListener>
{
    private static ServiceProvider GetDefaultCache(out DefaultHybridCache cache)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedCache, InvalidCache>();
        services.AddSingleton<IMemoryCache, InvalidCache>();
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new()
            {
                Flags = HybridCacheEntryFlags.DisableDistributedCache | HybridCacheEntryFlags.DisableLocalCache
            };
        });
        ServiceProvider provider = services.BuildServiceProvider();
        cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());
        return provider;
    }

    public sealed class InvalidCache : IDistributedCache, IMemoryCache
    {
        public void Dispose()
        {
            // nothing to do
        }

        ICacheEntry IMemoryCache.CreateEntry(object key) => throw new NotSupportedException("Intentionally not provided");

        byte[]? IDistributedCache.Get(string key) => throw new NotSupportedException("Intentionally not provided");

        Task<byte[]?> IDistributedCache.GetAsync(string key, CancellationToken token) => throw new NotSupportedException("Intentionally not provided");

        void IDistributedCache.Refresh(string key) => throw new NotSupportedException("Intentionally not provided");

        Task IDistributedCache.RefreshAsync(string key, CancellationToken token) => throw new NotSupportedException("Intentionally not provided");

        void IDistributedCache.Remove(string key) => throw new NotSupportedException("Intentionally not provided");

        void IMemoryCache.Remove(object key) => throw new NotSupportedException("Intentionally not provided");

        Task IDistributedCache.RemoveAsync(string key, CancellationToken token) => throw new NotSupportedException("Intentionally not provided");

        void IDistributedCache.Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException("Intentionally not provided");

        Task IDistributedCache.SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token) => throw new NotSupportedException("Intentionally not provided");

        bool IMemoryCache.TryGetValue(object key, out object? value) => throw new NotSupportedException("Intentionally not provided");
    }

    [Fact]
    public void ToString_Key()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());

        var key = new DefaultHybridCache.StampedeKey("test_key", HybridCacheEntryFlags.DisableLocalCache);

        const string Expected = "test_key (DisableLocalCache)";

        Assert.Equal(Expected, key.ToString());

        var state = new DefaultHybridCache.StampedeState<string, string>(cache, in key, TagSet.Empty, true);
        Assert.Equal(Expected, state.ToString());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S5034:\"ValueTask\" should be consumed correctly", Justification = "False positive, is only awaited once")]
    public async Task MultipleCallsShareExecution_NoCancellation(int callerCount, bool canBeCanceled)
    {
        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        using var cts = canBeCanceled ? new CancellationTokenSource() : null;
        var token = cts?.Token ?? CancellationToken.None;

        int executeCount = 0;
        int cancelCount = 0;
        var results = new Task<Guid>[callerCount];
        for (var i = 0; i < callerCount; i++)
        {
            results[i] = cache.GetOrCreateAsync(Me(), async ct =>
            {
                using var reg = ct.Register(() => Interlocked.Increment(ref cancelCount));
                if (!await semaphore.WaitAsync(5_000, CancellationToken.None))
                {
                    throw new TimeoutException("Failed to activate");
                }

                Interlocked.Increment(ref executeCount);
                ct.ThrowIfCancellationRequested(); // assert not cancelled
                return Guid.NewGuid();
            }, cancellationToken: token).AsTask();
        }

        Assert.Equal(callerCount, cache.DebugGetCallerCount(Me()));

        // everyone is queued up; release the hounds and check
        // that we all got the same result
        Assert.Equal(0, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));
        semaphore.Release();
        var first = await results[0];
        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));
        foreach (var result in results)
        {
            Assert.Equal(first, await result);
        }

        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));

        // and do it a second time; we expect different results
        Volatile.Write(ref executeCount, 0);
        for (var i = 0; i < callerCount; i++)
        {
            results[i] = cache.GetOrCreateAsync(Me(), async ct =>
            {
                using var reg = ct.Register(() => Interlocked.Increment(ref cancelCount));
                if (!await semaphore.WaitAsync(5_000, CancellationToken.None))
                {
                    throw new TimeoutException("Failed to activate");
                }

                Interlocked.Increment(ref executeCount);
                ct.ThrowIfCancellationRequested(); // assert not cancelled
                return Guid.NewGuid();
            }, cancellationToken: token).AsTask();
        }

        Assert.Equal(callerCount, cache.DebugGetCallerCount(Me()));

        // everyone is queued up; release the hounds and check
        // that we all got the same result
        Assert.Equal(0, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));
        semaphore.Release();
        var second = await results[0];
        Assert.NotEqual(first, second);
        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));
        foreach (var result in results)
        {
            Assert.Equal(second, await result);
        }

        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task MultipleCallsShareExecution_EveryoneCancels(int callerCount)
    {
        // what we want to prove here is that everyone ends up cancelling promptly by
        // *their own* cancellation (not dependent on the shared task), and that
        // the shared task becomes cancelled (which can be later)

        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        int executeCount = 0;
        int cancelCount = 0;
        var results = new Task<Guid>[callerCount];
        var cancels = new CancellationTokenSource[callerCount];
        for (var i = 0; i < callerCount; i++)
        {
            cancels[i] = new CancellationTokenSource();
            results[i] = cache.GetOrCreateAsync(Me(), async ct =>
            {
                using var reg = ct.Register(() => Interlocked.Increment(ref cancelCount));
                if (!await semaphore.WaitAsync(5_000, CancellationToken.None))
                {
                    throw new TimeoutException("Failed to activate");
                }

                try
                {
                    Interlocked.Increment(ref executeCount);
                    ct.ThrowIfCancellationRequested();
                    return Guid.NewGuid();
                }
                finally
                {
                    semaphore.Release(); // handshake so we can check when available again
                }
            }, cancellationToken: cancels[i].Token).AsTask();
        }

        Assert.Equal(callerCount, cache.DebugGetCallerCount(Me()));

        // everyone is queued up; release the hounds and check
        // that we all got the same result
        foreach (var cancel in cancels)
        {
            cancel.Cancel();
        }

        // allow the shared underlying task time to enter its semaphore wait
        await Task.Delay(500);

        for (var i = 0; i < callerCount; i++)
        {
            var result = results[i];

            // cancellation happens on a worker; wait up to 5 seconds total for the caller's task to observe its cancel
            await WaitForCompletionAsync(result, 5_000);
            Assert.Equal(TaskStatus.Canceled, result.Status);
            var ex = Assert.Throws<OperationCanceledException>(() => result.GetAwaiter().GetResult());
            Assert.Equal(cancels[i].Token, ex.CancellationToken); // each gets the correct blame
        }

        Assert.Equal(0, Volatile.Read(ref executeCount));
        semaphore.Release();

        // wait for underlying task to hand back to us
        if (!await semaphore.WaitAsync(5_000))
        {
            throw new TimeoutException("Didn't get handshake back from task");
        }

        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(1, Volatile.Read(ref cancelCount));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(10, 0)]
    [InlineData(10, 1)]
    [InlineData(10, 7)]
    public async Task MultipleCallsShareExecution_MostCancel(int callerCount, int remaining)
    {
        Assert.True(callerCount >= 2); // "most" is not "one"

        // what we want to prove here is that everyone ends up cancelling promptly by
        // *their own* cancellation (not dependent on the shared task), and that
        // the shared task becomes cancelled (which can be later)

        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        int executeCount = 0;
        int cancelCount = 0;
        var results = new Task<Guid>[callerCount];
        var cancels = new CancellationTokenSource[callerCount];
        for (var i = 0; i < callerCount; i++)
        {
            cancels[i] = new CancellationTokenSource();
            results[i] = cache.GetOrCreateAsync(Me(), async ct =>
            {
                using var reg = ct.Register(() => Interlocked.Increment(ref cancelCount));
                if (!await semaphore.WaitAsync(5_000, CancellationToken.None))
                {
                    throw new TimeoutException("Failed to activate");
                }

                try
                {
                    Interlocked.Increment(ref executeCount);
                    ct.ThrowIfCancellationRequested();
                    return Guid.NewGuid();
                }
                finally
                {
                    semaphore.Release(); // handshake so we can check when available again
                }
            }, cancellationToken: cancels[i].Token).AsTask();
        }

        Assert.Equal(callerCount, cache.DebugGetCallerCount(Me()));

        // everyone is queued up; release the hounds and check
        // that we all got the same result
        for (var i = 0; i < callerCount; i++)
        {
            if (i != remaining)
            {
                cancels[i].Cancel();
            }
        }

        // allow the shared underlying task time to enter its semaphore wait
        await Task.Delay(500);

        for (var i = 0; i < callerCount; i++)
        {
            if (i != remaining)
            {
                var result = results[i];

                // cancellation happens on a worker; wait up to 5 seconds total for the caller's task to observe its cancel
                await WaitForCompletionAsync(result, 5_000);
                Assert.Equal(TaskStatus.Canceled, result.Status);
                var ex = Assert.Throws<OperationCanceledException>(() => result.GetAwaiter().GetResult());
                Assert.Equal(cancels[i].Token, ex.CancellationToken); // each gets the correct blame
            }
        }

        Assert.Equal(0, Volatile.Read(ref executeCount));
        semaphore.Release();

        // wait for underlying task to hand back to us
        if (!await semaphore.WaitAsync(5_000))
        {
            throw new TimeoutException("Didn't get handshake back from task");
        }

        Assert.Equal(1, Volatile.Read(ref executeCount));
        Assert.Equal(0, Volatile.Read(ref cancelCount)); // ran to completion
        await results[remaining];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S5034:\"ValueTask\" should be consumed correctly", Justification = "False positive, is only awaited once")]
    public async Task ImmutableTypesShareFinalTask(bool withCancelation)
    {
        using CancellationTokenSource? cts = withCancelation ? new() : null;
        var token = cts?.Token ?? CancellationToken.None;

        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        // note AsTask *in this scenario* fetches the underlying incomplete task
        var first = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return Guid.NewGuid();
        }, cancellationToken: token).AsTask();

        var second = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return Guid.NewGuid();
        }, cancellationToken: token).AsTask();

        if (withCancelation)
        {
            Assert.NotSame(first, second);
        }
        else
        {
            Assert.Same(first, second);
        }

        semaphore.Release();
        Assert.Equal(await first, await second);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S5034:\"ValueTask\" should be consumed correctly", Justification = "False positive, is only awaited once")]
    public async Task ImmutableCustomTypesShareFinalTask(bool withCancelation)
    {
        using var cts = withCancelation ? new CancellationTokenSource() : null;
        var token = cts?.Token ?? CancellationToken.None;

        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        // AsTask *in this scenario* fetches the underlying incomplete task
        var first = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return new Immutable(Guid.NewGuid());
        }, cancellationToken: token).AsTask();

        var second = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return new Immutable(Guid.NewGuid());
        }, cancellationToken: token).AsTask();

        if (withCancelation)
        {
            Assert.NotSame(first, second);
        }
        else
        {
            Assert.Same(first, second);
        }

        semaphore.Release();

        var x = await first;
        var y = await second;
        Assert.Equal(x.Value, y.Value);
        Assert.Same(x, y); // same instance regardless of whether the tasks were shared
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S5034:\"ValueTask\" should be consumed correctly", Justification = "False positive, is only awaited once")]
    public async Task MutableTypesNeverShareFinalTask(bool withCancelation)
    {
        using CancellationTokenSource? cts = withCancelation ? new() : null;
        var token = cts?.Token ?? CancellationToken.None;

        using var scope = GetDefaultCache(out var cache);
        using var semaphore = new SemaphoreSlim(0);

        // AsTask *in this scenario* fetches the underlying incomplete task
        var first = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return new Mutable(Guid.NewGuid());
        }, cancellationToken: token).AsTask();

        var second = cache.GetOrCreateAsync(Me(), async ct =>
        {
            await semaphore.WaitAsync(CancellationToken.None);
            semaphore.Release();
            return new Mutable(Guid.NewGuid());
        }, cancellationToken: token).AsTask();

        Assert.NotSame(first, second);
        semaphore.Release();

        var x = await first;
        var y = await second;
        Assert.Equal(x.Value, y.Value);
        Assert.NotSame(x, y);
    }

    [Fact]
    public void ValidatePartitioning()
    {
        // we just want to validate that key-level partitioning is
        // happening to some degree, i.e. it isn't fundamentally broken
        using var scope = GetDefaultCache(out var cache);
        Dictionary<object, int> counts = [];
        for (int i = 0; i < 1024; i++)
        {
            var key = new DefaultHybridCache.StampedeKey(Guid.NewGuid().ToString(), default);
            Assert.True(key.Equals(key), "typed equality self");
            Assert.True(key.Equals((object)key), "object equality self");
            var obj = cache.GetPartitionedSyncLock(in key);
            if (!counts.TryGetValue(obj, out var count))
            {
                count = 0;
            }

            counts[obj] = count + 1;
        }

        // We just want to prove that we got 8 non-empty partitions.
        // This is *technically* non-deterministic, but: we'd
        // need to be having a very bad day for the math gods
        // to conspire against us that badly - if this test
        // starts failing, maybe buy a lottery ticket?
        Assert.Equal(8, counts.Count);
        foreach (var pair in counts)
        {
            // the *median* should be 128 here; let's
            // not be aggressive about it, though
            Assert.True(pair.Value > 16);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public Task JoinedCallersWithoutUnderlyingDataBalanceReservations_Immutable(int callerCount)
        => JoinedCallersWithoutUnderlyingDataBalanceReservationsAsync<Immutable>(callerCount);

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public Task JoinedCallersWithoutUnderlyingDataBalanceReservations_Mutable(int callerCount)
        => JoinedCallersWithoutUnderlyingDataBalanceReservationsAsync<Mutable>(callerCount);

    private static async Task JoinedCallersWithoutUnderlyingDataBalanceReservationsAsync<T>(int callerCount)
        where T : class
    {
        // regression: a miss with DisableUnderlyingData used to complete every joined caller with one shared item
        // that held a single reservation, so each extra caller over-released it
        using var scope = GetDefaultCache(out var cache);
        const HybridCacheEntryFlags Flags = HybridCacheEntryFlags.DisableDistributedCache | HybridCacheEntryFlags.DisableLocalCache
            | HybridCacheEntryFlags.DisableUnderlyingData;

        Assert.True(cache.GetOrCreateStampedeState<int, T>(Me(), Flags, out var stampede, canBeCanceled: true, tags: null));
        for (int i = 1; i < callerCount; i++)
        {
            Assert.False(cache.GetOrCreateStampedeState<int, T>(Me(), Flags, out var joined, canBeCanceled: true, tags: null));
            Assert.Same(stampede, joined);
        }

        Assert.Equal(callerCount, cache.DebugGetCallerCount(Me(), Flags));

        await stampede.ExecuteDirectAsync(0, static (_, _) => throw new InvalidOperationException("Underlying data should not be invoked"), options: null);
        Assert.Equal(0, cache.DebugGetCallerCount(Me(), Flags)); // no longer an active operation

        // every caller must receive an item that holds a reservation for it
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
        DefaultHybridCache.CacheItem<T> item = await stampede.Task;
#pragma warning restore VSTHRD003
        Assert.Equal(callerCount, item.RefCount);

        for (int i = 0; i < callerCount; i++)
        {
            Assert.Null(await stampede.JoinAsync(NullLogger.Instance, CancellationToken.None));
        }

        Assert.Equal(0, item.RefCount);
    }

    private class Mutable(Guid value)
    {
        public Guid Value => value;
    }

    [ImmutableObject(true)]
    public sealed class Immutable(Guid value)
    {
        public Guid Value => value;
    }

    private static string Me([CallerMemberName] string caller = "") => caller;

    private static async Task WaitForCompletionAsync(
        Task task,
        int timeoutMs,
        [CallerArgumentExpression(nameof(task))] string? expression = null)
    {
        if (task.IsCompleted)
        {
            return;
        }

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
#pragma warning restore VSTHRD003
        if (completed != task)
        {
            throw new TimeoutException($"Task '{expression}' did not complete within {timeoutMs}ms");
        }
    }
}
