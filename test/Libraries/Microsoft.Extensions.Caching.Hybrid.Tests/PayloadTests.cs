// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Microsoft.Extensions.Caching.Hybrid.Tests.DistributedCacheTests;
using static Microsoft.Extensions.Caching.Hybrid.Tests.L2Tests;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class PayloadTests(ITestOutputHelper log) : IClassFixture<TestEventListener>
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

    [Theory]
    [InlineData("", 1054, 0)]
    [InlineData("some_tag", 1063, 1)]
    [InlineData("some_tag,another_tag", 1075, 2)]
    public void RoundTrip_Success(string delimitedTags, int expectedLength, int tagCount)
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = string.IsNullOrEmpty(delimitedTags)
            ? TagSet.Empty : TagSet.Create(delimitedTags.Split(','));
        Assert.Equal(tagCount, tags.Count);

        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");

        Assert.Equal(expectedLength, actualLength);

        clock.Add(TimeSpan.FromSeconds(10));
        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out var parsed, out _);
        log.WriteLine($"Entropy: {parsed.Entropy}; Flags: {parsed.Flags}");
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void RoundTrip_LocalCacheSize(long? localCacheSize)
    {
        using var provider = GetDefaultCache(out var cache);

        byte[] bytes = new byte[64];
        new Random().NextBytes(bytes);

        string key = "k";
        TagSet tags = TagSet.Empty;
        int maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        byte[] oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes), localCacheSize: localCacheSize);

        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache,
            out var parsed, out _);

        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.True(parsed.PendingTags.IsEmpty);
        Assert.Equal(localCacheSize, parsed.LocalCacheSize);
        Assert.Equal(localCacheSize is not null, (parsed.Flags & HybridCachePayload.PayloadFlags.HasLocalSize) != 0);

        ArrayPool<byte>.Shared.Return(oversized);
    }

    // Guards the v1 wire format. This test pins a literal v1 byte sequence and asserts the reader
    // still accepts it.
    //
    // The bytes below were produced by `HybridCachePayload.Write` with:
    //   key="frozen-key", tags=Empty, payload=[0x01..0x08],
    //   creationTime = 638000000000000000 ticks (~2023-01-30 UTC),
    //   duration = TimeSpan.FromDays(36500) (100 years headroom so the payload stays valid).
    [Fact]
    public void V1_FrozenBytes_StillReadable()
    {
        using var provider = GetDefaultCache(out var cache);

        byte[] frozen = Convert.FromBase64String(
            "AwGPlwAAs6aeodoIAAiAgIztsrqCOAAKZnJvemVuLWtleQECAwQFBgcIAwE=");

        var result = HybridCachePayload.TryParse(
            new(frozen), "frozen-key", TagSet.Empty, cache,
            out var parsed, out _);

        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.AsSpan().SequenceEqual<byte>([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]));
        Assert.True(parsed.PendingTags.IsEmpty);
        Assert.Null(parsed.LocalCacheSize);
        Assert.Equal(HybridCachePayload.PayloadFlags.None, parsed.Flags & HybridCachePayload.PayloadFlags.HasLocalSize);
        Assert.True(parsed.RemainingTime > TimeSpan.Zero, "v1 frozen entry should not be expired (100-year duration).");
    }

    [Fact]
    public void RoundTrip_SelfExpiration()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        clock.Add(TimeSpan.FromSeconds(58));
        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.True(parsed.PendingTags.IsEmpty);

        clock.Add(TimeSpan.FromSeconds(4));
        result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.ExpiredByEntry, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Fact]
    public async Task RoundTrip_WildcardExpiration()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        clock.Add(TimeSpan.FromSeconds(2));
        await cache.RemoveByTagAsync("*");

        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.ExpiredByWildcard, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Fact]
    public async Task RoundTrip_TagExpiration()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        clock.Add(TimeSpan.FromSeconds(2));
        await cache.RemoveByTagAsync("other_tag");

        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.True(parsed.PendingTags.IsEmpty);

        await cache.RemoveByTagAsync("some_tag");
        result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.ExpiredByTag, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Fact]
    public async Task RoundTrip_TagExpiration_Pending()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        var creation = cache.CurrentTimestamp();
        int actualLength = HybridCachePayload.Write(oversized, key, creation, TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        clock.Add(TimeSpan.FromSeconds(2));

        var tcs = new TaskCompletionSource<long>();
        cache.DebugInvalidateTag("some_tag", tcs.Task);
        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.Equal(1, parsed.PendingTags.Count);
        Assert.Equal("some_tag", parsed.PendingTags[0]);
        Assert.Equal(creation, parsed.CreationTime);

        tcs.SetResult(cache.CurrentTimestamp());
        Assert.True(await cache.IsAnyTagExpiredAsync(parsed.PendingTags, creation));
    }

    [Fact]
    public void Gibberish()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        var result = HybridCachePayload.TryParse(new(bytes), "whatever", TagSet.Empty, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.FormatNotRecognized, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Fact]
    public void RoundTrip_TruncatedAtEveryLength_NeverThrows()
    {
        // Truncating the payload at *every* prefix length must surface as a clean parse result
        // (never an exception that becomes ParseFault). In particular, a buffer that ends in the
        // middle of a varint must not read past the end of the span.
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        for (int truncatedLength = 0; truncatedLength < actualLength; truncatedLength++)
        {
            var result = HybridCachePayload.TryParse(
                new(oversized, 0, truncatedLength), key, tags, cache,
                out var parsed, out var fault);

            Assert.Null(fault);
            Assert.NotEqual(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
            Assert.Equal(0, parsed.Payload.Count);
            Assert.True(parsed.PendingTags.IsEmpty);
        }

        ArrayPool<byte>.Shared.Return(oversized);
    }

    [Fact]
    public void RoundTrip_Oversized()
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        var tags = TagSet.Create(["some_tag"]);
        var maxLen = HybridCachePayload.GetMaxBytes(key, tags, bytes.Length) + 1;
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, tags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");
        Assert.Equal(1063, actualLength);

        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength + 1), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.InvalidData, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
    }

    [Fact]
    public async Task MalformedKeyDetected()
    {
        using var collector = new LogCollector();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            var localCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            config.AddSingleton<IDistributedCache>(new LoggingCache(log, localCache));
            config.AddLogging(options =>
            {
                options.ClearProviders();
                options.AddProvider(collector);
            });
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my\uD801\uD802key"; // malformed
        string[] tags = ["mytag"];

        _ = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);

        collector.WriteTo(log);
        collector.AssertErrors([Log.IdKeyInvalidUnicode]);
    }

    [Fact]
    public async Task MalformedTagDetected()
    {
        using var collector = new LogCollector();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            var localCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            config.AddSingleton<IDistributedCache>(new LoggingCache(log, localCache));
            config.AddLogging(options =>
            {
                options.ClearProviders();
                options.AddProvider(collector);
            });
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key"; // malformed
        string[] tags = ["my\uD801\uD802tag"];

        _ = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);

        collector.WriteTo(log);
        collector.AssertErrors([Log.IdTagInvalidUnicode]);
    }

    public enum FactoryMutation
    {
        MutateNonFlags,
        SetFlagsToNone,
        SetFlagsReadSideOnly,
        CallerDisabledL2Write_FactoryClears,
    }

    [Theory]
    [InlineData(FactoryMutation.MutateNonFlags)]
    [InlineData(FactoryMutation.SetFlagsToNone)]
    [InlineData(FactoryMutation.SetFlagsReadSideOnly)]
    [InlineData(FactoryMutation.CallerDisabledL2Write_FactoryClears)]
    public async Task MalformedKey_DoesNotWriteToL2_EvenWhenFactoryMutatesOptions(FactoryMutation mutation)
    {
        // When the key fails unicode validation, make sure the (corrupted) key/tags are not persisted to L2.
        using var collector = new LogCollector();
        MemoryDistributedCache? localCache = null;
        using var provider = GetDefaultCache(out var cache, config =>
        {
            localCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            config.AddSingleton<IDistributedCache>(new LoggingCache(log, localCache));
            config.AddLogging(options =>
            {
                options.ClearProviders();
                options.AddProvider(collector);
            });
        });

        Assert.NotNull(localCache);

        string key = "my\uD801\uD802key"; // malformed unicode
        string[] tags = ["mytag"];

        HybridCacheEntryFlags callerFlags = mutation == FactoryMutation.CallerDisabledL2Write_FactoryClears
            ? HybridCacheEntryFlags.DisableDistributedCacheWrite
            : HybridCacheEntryFlags.None;

        _ = await cache.GetOrCreateAsync(
            key,
            (entryOptions, _) =>
            {
                switch (mutation)
                {
                    case FactoryMutation.MutateNonFlags:
                        entryOptions.LocalSize = 1234;
                        break;
                    case FactoryMutation.SetFlagsToNone:
                        entryOptions.Flags = HybridCacheEntryFlags.None;
                        break;
                    case FactoryMutation.SetFlagsReadSideOnly:
                        entryOptions.Flags = HybridCacheEntryFlags.DisableLocalCacheRead;
                        break;
                    case FactoryMutation.CallerDisabledL2Write_FactoryClears:
                        entryOptions.Flags = HybridCacheEntryFlags.None;
                        break;
                }

                return new ValueTask<Guid>(Guid.NewGuid());
            },
            options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(1), Flags = callerFlags },
            tags: tags);

        // Wait until the unicode-validation log fires (synchronous, inside BackgroundFetchAsync,
        // just before the L2 write decision); then give the background work a brief moment
        // to flush any erroneous L2 SetAsync.
        await collector.WaitForLogsAsync([Log.IdKeyInvalidUnicode], TimeSpan.FromSeconds(5));
        await Task.Delay(500);

        collector.WriteTo(log);
        collector.AssertErrors([Log.IdKeyInvalidUnicode]);

        // The corrupted key must NOT have been persisted to L2. (Note: unrelated
        // tag-invalidation reads against valid tag keys may still appear in the backend.)
        Assert.Null(localCache.Get(key));
    }

    [Theory]
    [InlineData("tag1,tag2", 2)]
    [InlineData("tag1,tag2,tag3", 3)]
    public void RoundTrip_WithPendingTags_WhenKnownTagsMismatch(string delimitedTags, int tagCount)
    {
        var clock = new FakeTime();
        using var provider = GetDefaultCache(out var cache, config =>
        {
            config.AddSingleton<TimeProvider>(clock);
        });

        byte[] bytes = new byte[1024];
        new Random().NextBytes(bytes);

        string key = "my key";
        string[] tagsArray = delimitedTags.Split(',');
        var writeTags = TagSet.Create(tagsArray);
        Assert.Equal(tagCount, writeTags.Count);

        var maxLen = HybridCachePayload.GetMaxBytes(key, writeTags, bytes.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(maxLen);

        int actualLength = HybridCachePayload.Write(oversized, key, cache.CurrentTimestamp(), TimeSpan.FromMinutes(1), 0, writeTags, new(bytes));
        log.WriteLine($"bytes written: {actualLength}");

        clock.Add(TimeSpan.FromSeconds(10));

        // Inject non-completed tasks for each tag so IsTagExpired returns isPending=true
        foreach (string tag in tagsArray)
        {
            cache.DebugInvalidateTag(tag, new TaskCompletionSource<long>().Task);
        }

        // Parse with empty knownTags to force all tags into pendingTags via the rented buffer path
        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength), key, TagSet.Empty, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.Success, result);
        Assert.True(parsed.Payload.SequenceEqual(bytes));
        Assert.Equal(tagCount, parsed.PendingTags.Count);
    }
}
