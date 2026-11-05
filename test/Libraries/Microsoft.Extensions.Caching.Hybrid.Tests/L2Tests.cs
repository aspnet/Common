// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class L2Tests(ITestOutputHelper log) : IClassFixture<TestEventListener>
{
    private static string CreateString(bool work = false)
    {
        Assert.True(work, "we didn't expect this to be invoked");
        return Guid.NewGuid().ToString();
    }

    private static readonly HybridCacheEntryOptions _expiry = new() { Expiration = TimeSpan.FromMinutes(3.5) };

    private static readonly HybridCacheEntryOptions _expiryNoL1 = new() { Flags = HybridCacheEntryFlags.DisableLocalCache, Expiration = TimeSpan.FromMinutes(3.5) };

    private ITestOutputHelper Log => log;

    private class Options<T>(T value) : IOptions<T>
        where T : class
    {
        T IOptions<T>.Value => value;
    }

    private ServiceProvider GetDefaultCache(bool buffers, out DefaultHybridCache cache)
    {
        var services = new ServiceCollection();
        var localCacheOptions = new Options<MemoryDistributedCacheOptions>(new());
        var localCache = new MemoryDistributedCache(localCacheOptions);
        services.AddSingleton<IDistributedCache>(buffers ? new BufferLoggingCache(Log, localCache) : new LoggingCache(Log, localCache));
        services.AddHybridCache();
        ServiceProvider provider = services.BuildServiceProvider();
        cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());
        return provider;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssertL2Operations_Immutable(bool buffers)
    {
        using var provider = GetDefaultCache(buffers, out var cache);
        var backend = Assert.IsAssignableFrom<LoggingCache>(cache.BackendCache);
        Log.WriteLine("Inventing key...");
        var s = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<string>(CreateString(true)));
        Assert.Equal(3, backend.OpCount); // (wildcard timstamp GET), GET, SET

        Log.WriteLine("Reading with L1...");
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<string>(CreateString()));
            Assert.Equal(s, x);
            Assert.Same(s, x);
        }

        Assert.Equal(3, backend.OpCount); // shouldn't be hit

        Log.WriteLine("Reading without L1...");
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<string>(CreateString()), _expiryNoL1);
            Assert.Equal(s, x);
            Assert.NotSame(s, x);
        }

        Assert.Equal(8, backend.OpCount); // should be read every time

        Log.WriteLine("Setting value directly");
        s = CreateString(true);
        await cache.SetAsync(Me(), s);
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<string>(CreateString()));
            Assert.Equal(s, x);
            Assert.Same(s, x);
        }

        Assert.Equal(9, backend.OpCount); // SET

        Log.WriteLine("Removing key...");
        await cache.RemoveAsync(Me());
        Assert.Equal(10, backend.OpCount); // DEL

        Log.WriteLine("Fetching new...");
        var t = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<string>(CreateString(true)));
        Assert.NotEqual(s, t);
        Assert.Equal(12, backend.OpCount); // GET, SET
    }

    public sealed class Foo
    {
        public string Value { get; set; } = "";
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssertL2Operations_Mutable(bool buffers)
    {
        using var provider = GetDefaultCache(buffers, out var cache);
        var backend = Assert.IsAssignableFrom<LoggingCache>(cache.BackendCache);
        Log.WriteLine("Inventing key...");
        var s = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<Foo>(new Foo { Value = CreateString(true) }), _expiry);
        Assert.Equal(3, backend.OpCount); // (wildcard timstamp GET), GET, SET

        Log.WriteLine("Reading with L1...");
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<Foo>(new Foo { Value = CreateString() }), _expiry);
            Assert.Equal(s.Value, x.Value);
            Assert.NotSame(s, x);
        }

        Assert.Equal(3, backend.OpCount); // shouldn't be hit

        Log.WriteLine("Reading without L1...");
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<Foo>(new Foo { Value = CreateString() }), _expiryNoL1);
            Assert.Equal(s.Value, x.Value);
            Assert.NotSame(s, x);
        }

        Assert.Equal(8, backend.OpCount); // should be read every time

        Log.WriteLine("Setting value directly");
        s = new Foo { Value = CreateString(true) };
        await cache.SetAsync(Me(), s);
        for (var i = 0; i < 5; i++)
        {
            var x = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<Foo>(new Foo { Value = CreateString() }), _expiry);
            Assert.Equal(s.Value, x.Value);
            Assert.NotSame(s, x);
        }

        Assert.Equal(9, backend.OpCount); // SET

        Log.WriteLine("Removing key...");
        await cache.RemoveAsync(Me());
        Assert.Equal(10, backend.OpCount); // DEL

        Log.WriteLine("Fetching new...");
        var t = await cache.GetOrCreateAsync(Me(), ct => new ValueTask<Foo>(new Foo { Value = CreateString(true) }), _expiry);
        Assert.NotEqual(s.Value, t.Value);
        Assert.Equal(12, backend.OpCount); // GET, SET
    }

    [Fact]
    public async Task PendingTagInvalidationUsesPayloadCreationTimestamp()
    {
        var clock = new DistributedCacheTests.FakeTime();
        var shared = new MemoryDistributedCache(new Options<MemoryDistributedCacheOptions>(new()));
        var delayed = new DelayedTagReadCache(shared, "tag");

        // Provider A doesn't need delayed behavior, but can't use the MemoryDistributedCache,
        // because HybridCache ignores it when combined with MemoryCache.
        using var providerA = CreateNode(delayed, clock);
        using var providerB = CreateNode(delayed, clock);
        var cacheA = providerA.GetRequiredService<HybridCache>();
        var cacheB = providerB.GetRequiredService<HybridCache>();

        await cacheA.SetAsync("key", "original", tags: ["tag"]);
        await delayed.EntryWritten;

        clock.Add(TimeSpan.FromSeconds(1));
        await cacheA.RemoveByTagAsync("tag");
        Assert.NotNull(await shared.GetAsync("__MSFT_HCT__tag"));
        clock.Add(TimeSpan.FromSeconds(1));

        bool factoryRan = false;
        ValueTask<string> read = cacheB.GetOrCreateAsync(
            "key",
            _ =>
            {
                factoryRan = true;
                return new ValueTask<string>("regenerated");
            },
            tags: ["tag"]);

        // This is guaranteed to succeed, because all GetOrCreateAsync code until the tag read is executed synchronously.
        Assert.True(delayed.TagReadStarted);

        Assert.False(read.IsCompleted);
        delayed.CompleteTagRead();

        Assert.Equal("regenerated", await read);
        Assert.True(factoryRan);

        static ServiceProvider CreateNode(IDistributedCache backend, TimeProvider clock)
        {
            var services = new ServiceCollection();
            services.AddSingleton(backend);
            services.AddSingleton(clock);
            services.AddHybridCache();
            return services.BuildServiceProvider();
        }
    }

    private class BufferLoggingCache : LoggingCache, IBufferDistributedCache
    {
        public BufferLoggingCache(ITestOutputHelper log, IDistributedCache tail)
            : base(log, tail)
        {
        }

        void IBufferDistributedCache.Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"Set (ROS-byte): {key}");
            Tail.Set(key, value.ToArray(), options);
        }

        ValueTask IBufferDistributedCache.SetAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"SetAsync (ROS-byte): {key}");
            return new(Tail.SetAsync(key, value.ToArray(), options, token));
        }

        bool IBufferDistributedCache.TryGet(string key, IBufferWriter<byte> destination)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"TryGet: {key}");
            var buffer = Tail.Get(key);
            if (buffer is null)
            {
                return false;
            }

            destination.Write(buffer);
            return true;
        }

        async ValueTask<bool> IBufferDistributedCache.TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"TryGetAsync: {key}");
            var buffer = await Tail.GetAsync(key, token);
            if (buffer is null)
            {
                return false;
            }

            destination.Write(buffer);
            return true;
        }
    }

    internal class LoggingCache(ITestOutputHelper log, IDistributedCache tail) : IDistributedCache
    {
        protected ITestOutputHelper Log => log;
        protected IDistributedCache Tail => tail;

        protected int ProtectedOpCount;

        public int OpCount => Volatile.Read(ref ProtectedOpCount);

        byte[]? IDistributedCache.Get(string key)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"Get: {key}");
            return Tail.Get(key);
        }

        Task<byte[]?> IDistributedCache.GetAsync(string key, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"GetAsync: {key}");
            return Tail.GetAsync(key, token);
        }

        void IDistributedCache.Refresh(string key)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"Refresh: {key}");
            Tail.Refresh(key);
        }

        Task IDistributedCache.RefreshAsync(string key, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"RefreshAsync: {key}");
            return Tail.RefreshAsync(key, token);
        }

        void IDistributedCache.Remove(string key)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"Remove: {key}");
            Tail.Remove(key);
        }

        Task IDistributedCache.RemoveAsync(string key, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"RemoveAsync: {key}");
            return Tail.RemoveAsync(key, token);
        }

        void IDistributedCache.Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"Set (byte[]): {key} (expiry: {options.AbsoluteExpirationRelativeToNow})");
            Tail.Set(key, value, options);
        }

        Task IDistributedCache.SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token)
        {
            Interlocked.Increment(ref ProtectedOpCount);
            Log.WriteLine($"SetAsync (byte[]): {key} (expiry: {options.AbsoluteExpirationRelativeToNow})");
            return Tail.SetAsync(key, value, options, token);
        }
    }

    private sealed class DelayedTagReadCache(IDistributedCache inner, string tag) : IDistributedCache
    {
        private readonly TaskCompletionSource<bool> _entryWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<byte[]?> _tagRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _tagKey = "__MSFT_HCT__" + tag;

        public Task EntryWritten => _entryWritten.Task;
        public bool TagReadStarted { get; private set; }

        public byte[]? Get(string key) => inner.Get(key);

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "VSTHRD003:Avoid awaiting or returning a Task representing work that was not started within your context",
            Justification = "The test controls completion.")]
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (key == _tagKey)
            {
                TagReadStarted = true;
                return _tagRead.Task;
            }

            // Complete entry reads synchronously so parsing reaches the gated tag check before returning to the test.
            return Task.FromResult(inner.Get(key));
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            await inner.SetAsync(key, value, options, token);

            if (key == "key")
            {
                _entryWritten.TrySetResult(true);
            }
        }

        public void CompleteTagRead() => _tagRead.SetResult(inner.Get(_tagKey));
    }

    private static string Me([CallerMemberName] string caller = "") => caller;
}
