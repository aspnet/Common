// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.ComponentModel;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class SizeTests(ITestOutputHelper log) : IClassFixture<TestEventListener>
{
    [Theory]
    [InlineData("abc", null, true, null, null)] // does not enforce size limits
    [InlineData("", null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData("  ", null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData(null, null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData("abc", 8L, false, null, null)] // unreasonably small limit; chosen because our test string has length 12 - hence no expectation to find the second time
    [InlineData("abc", 1024L, true, null, null)] // reasonable size limit
    [InlineData("abc", 1024L, true, 8L, null, Log.IdMaximumPayloadBytesExceeded)] // reasonable size limit, small HC quota
    [InlineData("abc", null, false, null, 2, Log.IdMaximumKeyLengthExceeded, Log.IdMaximumKeyLengthExceeded)] // key limit exceeded
    [InlineData("a\u0000c", null, false, null, null, Log.IdKeyInvalidContent, Log.IdKeyInvalidContent)] // invalid key
    [InlineData("a\u001Fc", null, false, null, null, Log.IdKeyInvalidContent, Log.IdKeyInvalidContent)] // invalid key
    [InlineData("a\u0020c", null, true, null, null)] // fine (this is just space)
    public async Task ValidateSizeLimit_Immutable(string? key, long? sizeLimit, bool expectFromL1, long? maximumPayloadBytes, int? maximumKeyLength,
        params int[] errorIds)
    {
        using var collector = new LogCollector();
        var services = new ServiceCollection();
        services.AddMemoryCache(options => options.SizeLimit = sizeLimit);
        services.AddHybridCache(options =>
        {
            if (maximumKeyLength.HasValue)
            {
                options.MaximumKeyLength = maximumKeyLength.GetValueOrDefault();
            }

            if (maximumPayloadBytes.HasValue)
            {
                options.MaximumPayloadBytes = maximumPayloadBytes.GetValueOrDefault();
            }
        });
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddProvider(collector);
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        var cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());

        // this looks weird; it is intentionally not a const - we want to check
        // same instance without worrying about interning from raw literals
        string expected = new("simple value".ToArray());
        var actual = await cache.GetOrCreateAsync<string>(key!, ct => new(expected));

        // expect same contents
        Assert.Equal(expected, actual);

        // expect same instance, because string is special-cased as a type
        // that doesn't need defensive copies
        Assert.Same(expected, actual);

        // rinse and repeat, to check we get the value from L1
        actual = await cache.GetOrCreateAsync<string>(key!, ct => new(Guid.NewGuid().ToString()));

        if (expectFromL1)
        {
            // expect same contents from L1
            Assert.Equal(expected, actual);

            // expect same instance, because string is special-cased as a type
            // that doesn't need defensive copies
            Assert.Same(expected, actual);
        }
        else
        {
            // L1 cache not used
            Assert.NotEqual(expected, actual);
        }

        collector.WriteTo(log);
        collector.AssertErrors(errorIds);
    }

    [Theory]
    [InlineData("abc", null, true, null, null)] // does not enforce size limits
    [InlineData("", null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData("  ", null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData(null, null, false, null, null, Log.IdKeyEmptyOrWhitespace, Log.IdKeyEmptyOrWhitespace)] // invalid key
    [InlineData("abc", 8L, false, null, null)] // unreasonably small limit; chosen because our test string has length 12 - hence no expectation to find the second time
    [InlineData("abc", 1024L, true, null, null)] // reasonable size limit
    [InlineData("abc", 1024L, true, 8L, null, Log.IdMaximumPayloadBytesExceeded)] // reasonable size limit, small HC quota
    [InlineData("abc", null, false, null, 2, Log.IdMaximumKeyLengthExceeded, Log.IdMaximumKeyLengthExceeded)] // key limit exceeded
    public async Task ValidateSizeLimit_Mutable(string? key, long? sizeLimit, bool expectFromL1, long? maximumPayloadBytes, int? maximumKeyLength,
        params int[] errorIds)
    {
        using var collector = new LogCollector();
        var services = new ServiceCollection();
        services.AddMemoryCache(options => options.SizeLimit = sizeLimit);
        services.AddHybridCache(options =>
        {
            if (maximumKeyLength.HasValue)
            {
                options.MaximumKeyLength = maximumKeyLength.GetValueOrDefault();
            }

            if (maximumPayloadBytes.HasValue)
            {
                options.MaximumPayloadBytes = maximumPayloadBytes.GetValueOrDefault();
            }
        });
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddProvider(collector);
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        var cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());

        string expected = "simple value";
        var actual = await cache.GetOrCreateAsync<MutablePoco>(key!, ct => new(new MutablePoco { Value = expected }));

        // expect same contents
        Assert.Equal(expected, actual.Value);

        // rinse and repeat, to check we get the value from L1
        actual = await cache.GetOrCreateAsync<MutablePoco>(key!, ct => new(new MutablePoco { Value = Guid.NewGuid().ToString() }));

        if (expectFromL1)
        {
            // expect same contents from L1
            Assert.Equal(expected, actual.Value);
        }
        else
        {
            // L1 cache not used
            Assert.NotEqual(expected, actual.Value);
        }

        collector.WriteTo(log);
        collector.AssertErrors(errorIds);
    }

    [Theory]
    [InlineData("some value", false, 1, 1, 2, false)]
    [InlineData("read fail", false, 1, 1, 1, true, Log.IdDeserializationFailure)]
    [InlineData("write fail", true, 1, 1, 0, true, Log.IdSerializationFailure)]
    public async Task BrokenSerializer_Mutable(string value, bool same, int runCount, int serializeCount, int deserializeCount, bool expectKnownFailure, params int[] errorIds)
    {
        using var collector = new LogCollector();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton<IDistributedCache, NullDistributedCache>();
        var serializer = new MutablePoco.Serializer();
        services.AddHybridCache().AddSerializer(serializer);
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddProvider(collector);
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        var cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());

        int actualRunCount = 0;
        Func<CancellationToken, ValueTask<MutablePoco>> func = _ =>
        {
            Interlocked.Increment(ref actualRunCount);
            return new(new MutablePoco { Value = value });
        };

        if (expectKnownFailure)
        {
            await Assert.ThrowsAsync<KnownFailureException>(async () => await cache.GetOrCreateAsync("key", func));
        }
        else
        {
            var first = await cache.GetOrCreateAsync("key", func);
            var second = await cache.GetOrCreateAsync("key", func);
            Assert.Equal(value, first.Value);
            Assert.Equal(value, second.Value);

            if (same)
            {
                Assert.Same(first, second);
            }
            else
            {
                Assert.NotSame(first, second);
            }
        }

        Assert.Equal(runCount, Volatile.Read(ref actualRunCount));
        Assert.Equal(serializeCount, serializer.WriteCount);
        Assert.Equal(deserializeCount, serializer.ReadCount);
        collector.WriteTo(log);
        collector.AssertErrors(errorIds);
    }

    [Theory]
    [InlineData("some value", true, 1, 1, 0, false, true)]
    [InlineData("read fail", true, 1, 1, 0, false, true)]
    [InlineData("write fail", true, 1, 1, 0, true, true, Log.IdSerializationFailure)]

    // without L2, we only need the serializer for sizing purposes (L1), not used for deserialize
    [InlineData("some value", true, 1, 1, 0, false, false)]
    [InlineData("read fail", true, 1, 1, 0, false, false)]
    [InlineData("write fail", true, 1, 1, 0, true, false, Log.IdSerializationFailure)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Test scenario range; reducing duplication")]
    public async Task BrokenSerializer_Immutable(string value, bool same, int runCount, int serializeCount, int deserializeCount, bool expectKnownFailure, bool withL2,
        params int[] errorIds)
    {
        using var collector = new LogCollector();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        if (withL2)
        {
            services.AddSingleton<IDistributedCache, NullDistributedCache>();
        }

        var serializer = new ImmutablePoco.Serializer();
        services.AddHybridCache().AddSerializer(serializer);
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddProvider(collector);
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        var cache = Assert.IsType<DefaultHybridCache>(provider.GetRequiredService<HybridCache>());

        int actualRunCount = 0;
        Func<CancellationToken, ValueTask<ImmutablePoco>> func = _ =>
        {
            Interlocked.Increment(ref actualRunCount);
            return new(new ImmutablePoco(value));
        };

        if (expectKnownFailure)
        {
            await Assert.ThrowsAsync<KnownFailureException>(async () => await cache.GetOrCreateAsync("key", func));
        }
        else
        {
            var first = await cache.GetOrCreateAsync("key", func);
            var second = await cache.GetOrCreateAsync("key", func);
            Assert.Equal(value, first.Value);
            Assert.Equal(value, second.Value);

            if (same)
            {
                Assert.Same(first, second);
            }
            else
            {
                Assert.NotSame(first, second);
            }
        }

        Assert.Equal(runCount, Volatile.Read(ref actualRunCount));
        Assert.Equal(serializeCount, serializer.WriteCount);
        Assert.Equal(deserializeCount, serializer.ReadCount);
        collector.WriteTo(log);
        collector.AssertErrors(errorIds);
    }

    public class KnownFailureException : Exception
    {
        public KnownFailureException(string message)
            : base(message)
        {
        }
    }

    public class MutablePoco
    {
        public string Value { get; set; } = "";

        public sealed class Serializer : IHybridCacheSerializer<MutablePoco>
        {
            private int _readCount;
            private int _writeCount;

            public int ReadCount => Volatile.Read(ref _readCount);
            public int WriteCount => Volatile.Read(ref _writeCount);

            public MutablePoco Deserialize(ReadOnlySequence<byte> source)
            {
                Interlocked.Increment(ref _readCount);
                var value = InbuiltTypeSerializer.DeserializeString(source);
                if (value == "read fail")
                {
                    throw new KnownFailureException("read failure");
                }

                return new MutablePoco { Value = value };
            }

            public void Serialize(MutablePoco value, IBufferWriter<byte> target)
            {
                Interlocked.Increment(ref _writeCount);
                if (value.Value == "write fail")
                {
                    throw new KnownFailureException("write failure");
                }

                InbuiltTypeSerializer.SerializeString(value.Value, target);
            }
        }
    }

    [ImmutableObject(true)]
    public sealed class ImmutablePoco
    {
        public ImmutablePoco(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public sealed class Serializer : IHybridCacheSerializer<ImmutablePoco>
        {
            private int _readCount;
            private int _writeCount;

            public int ReadCount => Volatile.Read(ref _readCount);
            public int WriteCount => Volatile.Read(ref _writeCount);

            public ImmutablePoco Deserialize(ReadOnlySequence<byte> source)
            {
                Interlocked.Increment(ref _readCount);
                var value = InbuiltTypeSerializer.DeserializeString(source);
                if (value == "read fail")
                {
                    throw new KnownFailureException("read failure");
                }

                return new ImmutablePoco(value);
            }

            public void Serialize(ImmutablePoco value, IBufferWriter<byte> target)
            {
                Interlocked.Increment(ref _writeCount);
                if (value.Value == "write fail")
                {
                    throw new KnownFailureException("write failure");
                }

                InbuiltTypeSerializer.SerializeString(value.Value, target);
            }
        }
    }
}
