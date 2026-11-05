// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Xunit;
using static Microsoft.Extensions.Caching.Hybrid.Tests.DistributedCacheTests;
using static Microsoft.Extensions.Caching.Hybrid.Tests.L2Tests;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class ExpirationTests(ITestOutputHelper log)
{
    [Fact]
    public async Task ExpirationRespected()
    {
        // we want set up separate cache instances with a shared L2 to show relative expiration
        // being respected
        var clock = new FakeTime();
        using var l1 = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var l2 = new LoggingCache(log, new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { Clock = clock })));

        Guid guid0;
        string key = nameof(ExpirationRespected);
        Func<CancellationToken, ValueTask<Guid>> callback = static _ => new(Guid.NewGuid());
        HybridCacheEntryOptions options = new() { Expiration = TimeSpan.FromMinutes(2), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

        ServiceCollection services = new();
        services.AddSingleton<ISystemClock>(clock);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IMemoryCache>(l1);
        services.AddSingleton<IDistributedCache>(l2);
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        guid0 = await cache.GetOrCreateAsync(key, callback, options);

        // should be fine immediately
        Assert.Equal(guid0, await cache.GetOrCreateAsync(key, callback, options));

        clock.Add(TimeSpan.FromSeconds(45)); // should still be fine from L1 (L1 has 1 minute expiration)
        Assert.Equal(guid0, await cache.GetOrCreateAsync(key, callback, options));

        clock.Add(TimeSpan.FromSeconds(45)); // should still be fine from L2 (L1 now expired, but fetches from L2 and detects 0:30 remaining, which limits L1 to 0:30)
        Assert.Equal(guid0, await cache.GetOrCreateAsync(key, callback, options));

        clock.Add(TimeSpan.FromSeconds(45)); // should now be expired
        Assert.NotEqual(guid0, await cache.GetOrCreateAsync(key, callback, options));
    }
}
