// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

// validate HC stability when the L2 is unreliable
public class UnreliableL2Tests(ITestOutputHelper testLog) : IClassFixture<TestEventListener>
{
    [Theory]
    [InlineData(BreakType.None)]
    [InlineData(BreakType.Synchronous, Log.IdCacheBackendWriteFailure)]
    [InlineData(BreakType.Asynchronous, Log.IdCacheBackendWriteFailure)]
    [InlineData(BreakType.AsynchronousYield, Log.IdCacheBackendWriteFailure)]
    [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Intentional; tracking for out-of-band support only")]
    public async Task WriteFailureInvisible(BreakType writeBreak, params int[] errorIds)
    {
        using (GetServices(out var hc, out var l1, out var l2, out var log))
        using (log)
        {
            // normal behaviour when working fine
            var x = await hc.GetOrCreateAsync("x", NewGuid);
            Assert.Equal(x, await hc.GetOrCreateAsync("x", NewGuid));
            Assert.NotNull(l2.Tail.Get("x")); // exists

            l2.WriteBreak = writeBreak;
            var y = await hc.GetOrCreateAsync("y", NewGuid);
            Assert.Equal(y, await hc.GetOrCreateAsync("y", NewGuid));
            if (writeBreak == BreakType.None)
            {
                Assert.NotNull(l2.Tail.Get("y")); // exists
            }
            else
            {
                Assert.Null(l2.Tail.Get("y")); // does not exist
            }

            await l2.LastWrite; // allows out-of-band write to complete

            // Wait for logs to be written (with timeout to avoid hanging on failure)
            await log.WaitForLogsAsync(errorIds, TimeSpan.FromSeconds(5));

            log.WriteTo(testLog);
            log.AssertErrors(errorIds);
        }
    }

    [Theory]
    [InlineData(BreakType.None)]
    [InlineData(BreakType.Synchronous, Log.IdCacheBackendReadFailure, Log.IdCacheBackendReadFailure)]
    [InlineData(BreakType.Asynchronous, Log.IdCacheBackendReadFailure, Log.IdCacheBackendReadFailure)]
    [InlineData(BreakType.AsynchronousYield, Log.IdCacheBackendReadFailure, Log.IdCacheBackendReadFailure)]
    public async Task ReadFailureInvisible(BreakType readBreak, params int[] errorIds)
    {
        using (GetServices(out var hc, out var l1, out var l2, out var log))
        using (log)
        {
            // create two new values via HC; this should go down to l2
            var x = await hc.GetOrCreateAsync("x", NewGuid);
            var y = await hc.GetOrCreateAsync("y", NewGuid);

            // this should be reliable and repeatable
            Assert.Equal(x, await hc.GetOrCreateAsync("x", NewGuid));
            Assert.Equal(y, await hc.GetOrCreateAsync("y", NewGuid));

            // even if we clean L1, causing new L2 fetches
            l1.Clear();
            Assert.Equal(x, await hc.GetOrCreateAsync("x", NewGuid));
            Assert.Equal(y, await hc.GetOrCreateAsync("y", NewGuid));

            // now we break L2 in some predictable way, *without* clearing L1 - the
            // values should still be available via L1
            l2.ReadBreak = readBreak;
            Assert.Equal(x, await hc.GetOrCreateAsync("x", NewGuid));
            Assert.Equal(y, await hc.GetOrCreateAsync("y", NewGuid));

            // but if we clear L1 to force L2 hits, we anticipate problems
            l1.Clear();
            if (readBreak == BreakType.None)
            {
                Assert.Equal(x, await hc.GetOrCreateAsync("x", NewGuid));
                Assert.Equal(y, await hc.GetOrCreateAsync("y", NewGuid));
            }
            else
            {
                // because L2 is unavailable and L1 is empty, we expect the callback
                // to be used again, generating new values
                var a = await hc.GetOrCreateAsync("x", NewGuid, NoL2Write);
                var b = await hc.GetOrCreateAsync("y", NewGuid, NoL2Write);

                Assert.NotEqual(x, a);
                Assert.NotEqual(y, b);

                // but those *new* values are at least reliable inside L1
                Assert.Equal(a, await hc.GetOrCreateAsync("x", NewGuid));
                Assert.Equal(b, await hc.GetOrCreateAsync("y", NewGuid));
            }

            log.WriteTo(testLog);
            log.AssertErrors(errorIds);
        }
    }

    private static HybridCacheEntryOptions NoL2Write { get; } = new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite };

    public enum BreakType
    {
        None, // async API works correctly
        Synchronous, // async API faults directly rather than return a faulted task
        Asynchronous, // async API returns a completed asynchronous fault
        AsynchronousYield, // async API returns an incomplete asynchronous fault
    }

    private static ValueTask<Guid> NewGuid(CancellationToken cancellationToken) => new(Guid.NewGuid());

    private static IDisposable GetServices(out HybridCache hc, out MemoryCache l1,
        out UnreliableDistributedCache l2, out LogCollector log)
    {
        // we need an entirely separate MC for the dummy backend, not connected to our
        // "real" services
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var backend = services.BuildServiceProvider().GetRequiredService<IDistributedCache>();

        // now create the "real" services
        l2 = new UnreliableDistributedCache(backend);
        var collector = new LogCollector();
        log = collector;
        services = new ServiceCollection();
        services.AddSingleton<IDistributedCache>(l2);
        services.AddHybridCache();
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddProvider(collector);
        });
        var lifetime = services.BuildServiceProvider();
        hc = lifetime.GetRequiredService<HybridCache>();
        l1 = Assert.IsType<MemoryCache>(lifetime.GetRequiredService<IMemoryCache>());
        return lifetime;
    }

    private sealed class UnreliableDistributedCache : IDistributedCache
    {
        public UnreliableDistributedCache(IDistributedCache tail)
        {
            Tail = tail;
        }

        public IDistributedCache Tail { get; }
        public BreakType ReadBreak { get; set; }
        public BreakType WriteBreak { get; set; }

        public Task LastWrite { get; private set; } = Task.CompletedTask;

        public byte[]? Get(string key) => throw new NotSupportedException(); // only async API in use

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => TrackLast(ThrowIfBrokenAsync<byte[]?>(ReadBreak) ?? Tail.GetAsync(key, token));

        public void Refresh(string key) => throw new NotSupportedException(); // only async API in use

        public Task RefreshAsync(string key, CancellationToken token = default)
            => TrackLast(ThrowIfBrokenAsync(WriteBreak) ?? Tail.RefreshAsync(key, token));

        public void Remove(string key) => throw new NotSupportedException(); // only async API in use

        public Task RemoveAsync(string key, CancellationToken token = default)
            => TrackLast(ThrowIfBrokenAsync(WriteBreak) ?? Tail.RemoveAsync(key, token));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException(); // only async API in use

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => TrackLast(ThrowIfBrokenAsync(WriteBreak) ?? Tail.SetAsync(key, value, options, token));

        [DoesNotReturn]
        private static void Throw() => throw new IOException("L2 offline");

        private static async Task<T> ThrowAsync<T>(bool yield)
        {
            if (yield)
            {
                await Task.Yield();
            }

            Throw();
            return default; // never reached
        }

        private static Task? ThrowIfBrokenAsync(BreakType breakType) => ThrowIfBrokenAsync<int>(breakType);

        [SuppressMessage("Critical Bug", "S4586:Non-async \"Task/Task<T>\" methods should not return null", Justification = "Intentional for propagation")]
        private static Task<T>? ThrowIfBrokenAsync<T>(BreakType breakType)
        {
            switch (breakType)
            {
                case BreakType.Asynchronous:
                    return ThrowAsync<T>(false);
                case BreakType.AsynchronousYield:
                    return ThrowAsync<T>(true);
                case BreakType.None:
                    return null;
                default:
                    // includes BreakType.Synchronous and anything unknown
                    Throw();
                    break;
            }

            return null;
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Intentional; tracking for out-of-band support only")]
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "We don't need the failure type - just the timing")]
        private static Task IgnoreFailure(Task task)
        {
            return task.Status == TaskStatus.RanToCompletion
                ? Task.CompletedTask : IgnoreAsync(task);

            static async Task IgnoreAsync(Task task)
            {
                try
                {
                    await task;
                }
                catch
                {
                    // we only care about the "when"; failure is fine
                }
            }
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Intentional; tracking for out-of-band support only")]
        private Task TrackLast(Task lastWrite)
        {
            LastWrite = IgnoreFailure(lastWrite);
            return lastWrite;
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Intentional; tracking for out-of-band support only")]
        private Task<T> TrackLast<T>(Task<T> lastWrite)
        {
            LastWrite = IgnoreFailure(lastWrite);
            return lastWrite;
        }
    }
}
