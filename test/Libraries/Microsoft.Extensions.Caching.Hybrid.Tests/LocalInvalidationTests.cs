// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Microsoft.Extensions.Caching.Hybrid.Tests.L2Tests;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class LocalInvalidationTests(ITestOutputHelper log) : IClassFixture<TestEventListener>
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

    [Fact]
    public async Task GlobalInvalidateNoTags()
    {
        using var services = GetDefaultCache(out var cache);
        var value = await cache.GetOrCreateAsync<Guid>("abc", ct => new(Guid.NewGuid()));

        // should work immediately as-is
        Assert.Equal(value, await cache.GetOrCreateAsync<Guid>("abc", ct => new(Guid.NewGuid())));

        // invalidating a normal tag should have no effect
        await cache.RemoveByTagAsync("foo");
        Assert.Equal(value, await cache.GetOrCreateAsync<Guid>("abc", ct => new(Guid.NewGuid())));

        // invalidating everything should force a re-fetch
        await cache.RemoveByTagAsync("*");
        var newValue = await cache.GetOrCreateAsync<Guid>("abc", ct => new(Guid.NewGuid()));
        Assert.NotEqual(value, newValue);

        // which should now be repeatable again
        Assert.Equal(newValue, await cache.GetOrCreateAsync<Guid>("abc", ct => new(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TagBasedInvalidate(bool withL2, bool withExtraTag)
    {
        using IMemoryCache l1 = new MemoryCache(new MemoryCacheOptions());
        IDistributedCache? l2 = null;
        if (withL2)
        {
            MemoryDistributedCacheOptions options = new();
            MemoryDistributedCache mdc = new(Options.Create(options));
            l2 = new LoggingCache(log, mdc);
        }

        Guid lastValue = Guid.Empty;

        // loop because we want to test pre-existing L1/L2 impact
        for (int i = 0; i < 3; i++)
        {
            using var services = GetDefaultCache(out var cache, svc =>
            {
                svc.AddSingleton(l1);
                if (l2 is not null)
                {
                    svc.AddSingleton(l2);
                }
            });
            var clock = services.GetRequiredService<TimeProvider>();

            string key = "mykey";
            string tag = "abc";
            string[] tags = withExtraTag ? [tag, "other"] : [tag];
            var value = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"First value: {value}");
            if (lastValue != Guid.Empty)
            {
                Assert.Equal(lastValue, value);
            }

            // should work immediately as-is
            var tmp = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"Second value: {tmp} (should be {value})");
            Assert.Equal(value, tmp);

            // invalidating a normal tag should have no effect
            await cache.RemoveByTagAsync("foo");
            tmp = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"Value after invalidating tag foo: {tmp} (should be {value})");
            Assert.Equal(value, tmp);

            // invalidating a tag we have should force a re-fetch
            await cache.RemoveByTagAsync(tag);
            var newValue = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"Value after invalidating tag {tag}: {newValue} (should not be {value})");
            Assert.NotEqual(value, newValue);

            // which should now be repeatable again
            tmp = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"And repeating: {tmp} (should be {newValue})");
            Assert.Equal(newValue, tmp);
            value = newValue;

            // invalidating everything should force a re-fetch
            await cache.RemoveByTagAsync("*");
            newValue = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"Value after invalidating tag *: {newValue} (should not be {value})");
            Assert.NotEqual(value, newValue);

            // which should now be repeatable again
            tmp = await cache.GetOrCreateAsync<Guid>(key, ct => new(Guid.NewGuid()), tags: tags);
            log.WriteLine($"And repeating: {tmp} (should be {newValue})");
            Assert.Equal(newValue, tmp);
            lastValue = newValue;

            var now = clock.GetUtcNow().UtcTicks;
            do
            {
                await Task.Delay(10);
            }
            while (clock.GetUtcNow().UtcTicks == now);
        }
    }
}
