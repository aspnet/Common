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
    public void RoundTrip_Truncated()
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

        var result = HybridCachePayload.TryParse(new(oversized, 0, actualLength - 1), key, tags, cache, out var parsed, out _);
        Assert.Equal(HybridCachePayload.HybridCachePayloadParseResult.InvalidData, result);
        Assert.Equal(0, parsed.Payload.Count);
        Assert.True(parsed.PendingTags.IsEmpty);
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
