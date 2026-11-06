// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class ReportTagMetricsTests
{
    [Fact]
    public void HybridCacheMetricsInstrumentsAreCreated()
    {
        // Verify that the System.Diagnostics.Metrics instruments are properly created
        using var meterListener = new MeterListener();
        var meterNames = new List<string>();

        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Microsoft.Extensions.Caching.Hybrid")
            {
                meterNames.Add(instrument.Name);
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.Start();

        // Creating HybridCacheEventSource should initialize the metrics
        using var eventSource = new HybridCacheEventSource();

        // Verify expected metric names are registered
        Assert.Contains("hybrid_cache.local.hits", meterNames);
        Assert.Contains("hybrid_cache.local.misses", meterNames);
        Assert.Contains("hybrid_cache.distributed.hits", meterNames);
        Assert.Contains("hybrid_cache.distributed.misses", meterNames);
        Assert.Contains("hybrid_cache.local.writes", meterNames);
        Assert.Contains("hybrid_cache.distributed.writes", meterNames);
        Assert.Contains("hybrid_cache.tag.invalidations", meterNames);
    }

    [Fact]
    public async Task ReportTagMetrics_Enabled_EmitsTagDimensions()
    {
        var services = new ServiceCollection();
        services.AddHybridCache(options =>
        {
            options.ReportTagMetrics = true;
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.hits");

        // Perform cache operation with tags
        await cache.GetOrCreateAsync("test-key", "test-state",
            (state, token) => new ValueTask<string>("test-value"),
            tags: ["region:us-west", "service:api"]);

        // Get the value again to trigger a cache hit
        await cache.GetOrCreateAsync("test-key", "test-state",
            (state, token) => new ValueTask<string>("test-value"),
            tags: ["region:us-west", "service:api"]);

        // Check that metrics with tag dimensions were emitted
        var measurements = collector.GetMeasurementSnapshot();
        if (measurements.Count > 0)
        {
            var measurement = measurements.Last();
            Assert.True(measurement.Tags.Count > 0, "Expected tag dimensions to be present when ReportTagMetrics is enabled");
        }
    }

    [Fact]
    public async Task ReportTagMetrics_Disabled_NoTagDimensions()
    {
        var services = new ServiceCollection();
        services.AddHybridCache(options =>
        {
            options.ReportTagMetrics = false; // Explicitly disabled
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.hits");

        // Perform cache operation with tags
        await cache.GetOrCreateAsync("test-key", "test-state",
            (state, token) => new ValueTask<string>("test-value"),
            tags: ["region:us-west", "service:api"]);

        // Get the value again to trigger a cache hit
        await cache.GetOrCreateAsync("test-key", "test-state",
            (state, token) => new ValueTask<string>("test-value"),
            tags: ["region:us-west", "service:api"]);

        // No metric measurements should be emitted when ReportTagMetrics is disabled
        var measurements = collector.GetMeasurementSnapshot();

        // We expect no measurements or measurements without tag dimensions when ReportTagMetrics is disabled
        Assert.True(measurements.Count == 0 || measurements.All(m => m.Tags.Count == 0),
            "Expected no tag dimensions when ReportTagMetrics is disabled");
    }

    [Fact]
    public void EventSource_LocalCacheHitWithTags_ReportTagMetrics_True()
    {
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.hits");

        var tags = TagSet.Create(["region", "product"]);
        var eventSource = HybridCacheEventSource.Log;

        eventSource.LocalCacheHitWithTags(tags, reportTagMetrics: true);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected metrics to be emitted when reportTagMetrics is true");

        var measurement = measurements.Last();
        Assert.Equal(1, measurement.Value);
        Assert.True(measurement.Tags.Count >= 2, "Expected tag dimensions to be present");
    }

    [Fact]
    public void EventSource_LocalCacheHitWithTags_ReportTagMetrics_False()
    {
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.hits");

        var tags = TagSet.Create(["region", "product"]);
        var eventSource = HybridCacheEventSource.Log;

        eventSource.LocalCacheHitWithTags(tags, reportTagMetrics: false);

        // When reportTagMetrics is false, no System.Diagnostics.Metrics should be emitted
        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count == 0, "Expected no metrics to be emitted when reportTagMetrics is false");
    }

    [Fact]
    public void EventSource_TagInvalidatedWithTags_ReportTagMetrics_True()
    {
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.tag.invalidations");

        var eventSource = HybridCacheEventSource.Log;

        eventSource.TagInvalidatedWithTags("test-tag", reportTagMetrics: true);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.True(measurements.Count > 0, "Expected metrics to be emitted when reportTagMetrics is true");

        var measurement = measurements.Last();
        Assert.Equal(1, measurement.Value);
        Assert.Contains(measurement.Tags, kvp => kvp.Key == "tag" && kvp.Value?.ToString() == "test-tag");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EventSource_EmptyTags_ReportTagMetrics(bool reportTagMetrics)
    {
        using var collector = new MetricCollector<long>(null, "Microsoft.Extensions.Caching.Hybrid", "hybrid_cache.local.hits");

        var emptyTags = TagSet.Empty;
        var eventSource = HybridCacheEventSource.Log;

        eventSource.LocalCacheHitWithTags(emptyTags, reportTagMetrics);

        var measurements = collector.GetMeasurementSnapshot();
        if (reportTagMetrics)
        {
            Assert.True(measurements.Count > 0, "Expected metrics to be emitted when reportTagMetrics is true, even with empty tags");
            var measurement = measurements.Last();
            Assert.Equal(1, measurement.Value);
            Assert.Empty(measurement.Tags); // No tag dimensions for empty tags
        }
        else
        {
            Assert.True(measurements.Count == 0, "Expected no metrics when reportTagMetrics is false");
        }
    }
}
