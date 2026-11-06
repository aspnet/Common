// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public sealed class ReportTagMetricsIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly HybridCache _cache;
    private bool _disposed;

    public ReportTagMetricsIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache(options =>
        {
            options.ReportTagMetrics = true;
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WithTags_EmitsTaggedMetrics()
    {
        // Arrange
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.misses");

        // Act - first call should miss
        var result1 = await _cache.GetOrCreateAsync("test-key", "initial-state",
            (state, token) => new ValueTask<string>($"value-for-{state}"),
            tags: ["region:us-west", "service:test"]);

        // Act - second call should hit
        var result2 = await _cache.GetOrCreateAsync("test-key", "second-state",
            (state, token) => new ValueTask<string>($"value-for-{state}"),
            tags: ["region:us-west", "service:test"]);

        // Assert
        Assert.Equal("value-for-initial-state", result1);
        Assert.Equal("value-for-initial-state", result2); // Should get cached value

        // Verify metrics were emitted
        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected cache miss metrics to be emitted");

        var latestMeasurement = measurements.Last();
        Assert.Equal(1, latestMeasurement.Value);
        Assert.True(latestMeasurement.Tags.Count >= 2, "Expected tag dimensions in metrics");
    }

    [Fact]
    public async Task SetAsync_WithTags_EmitsTaggedWriteMetrics()
    {
        // Arrange
        using var writeCollector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.writes");

        // Act
        await _cache.SetAsync("set-key", "set-value", tags: ["operation:set", "category:test"]);

        // Assert
        var measurements = writeCollector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected cache write metrics to be emitted");

        var latestMeasurement = measurements.Last();
        Assert.Equal(1, latestMeasurement.Value);
        Assert.True(latestMeasurement.Tags.Count >= 2, "Expected tag dimensions in write metrics");
    }

    [Fact]
    public async Task RemoveByTagAsync_EmitsTagInvalidationMetrics()
    {
        // Arrange
        using var invalidationCollector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.tag.invalidations");

        // Setup - add some data first
        await _cache.SetAsync("tagged-key", "tagged-value", tags: ["invalidation-test"]);

        // Act
        await _cache.RemoveByTagAsync("invalidation-test");

        // Assert
        var measurements = invalidationCollector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected tag invalidation metrics to be emitted");

        var latestMeasurement = measurements.Last();
        Assert.Equal(1, latestMeasurement.Value);
        Assert.Contains(latestMeasurement.Tags, kvp => kvp.Key == "tag" && kvp.Value?.ToString() == "invalidation-test");
    }

    [Fact]
    public async Task CacheOperations_WithoutTags_EmitsMetricsWithoutDimensions()
    {
        // Arrange
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.misses");

        // Act - cache operation without tags
        var result = await _cache.GetOrCreateAsync("no-tags-key", "state",
            (state, token) => new ValueTask<string>($"value-{state}"));

        // Assert
        Assert.Equal("value-state", result);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected metrics to be emitted even without tags");

        var latestMeasurement = measurements.Last();
        Assert.Equal(1, latestMeasurement.Value);
        Assert.Empty(latestMeasurement.Tags);// No dimensions when no tags
    }

    [Theory]
    [InlineData("single-tag")]
    [InlineData("tag1", "tag2")]
    [InlineData("tag1", "tag2", "tag3", "tag4", "tag5")]
    public async Task CacheOperations_WithVariousTagCounts_EmitsCorrectDimensions(params string[] tags)
    {
        // Arrange
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.misses");

        // Act
        var result = await _cache.GetOrCreateAsync($"key-{string.Join("-", tags)}", "state",
            (state, token) => new ValueTask<string>($"value-{state}"),
            tags: tags);

        // Assert
        Assert.Equal("value-state", result);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected metrics to be emitted");

        var latestMeasurement = measurements.Last();
        Assert.Equal(1, latestMeasurement.Value);
        Assert.Equal(tags.Length, latestMeasurement.Tags.Count); // Should have one dimension per tag
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _serviceProvider.Dispose();
        _disposed = true;
    }
}
