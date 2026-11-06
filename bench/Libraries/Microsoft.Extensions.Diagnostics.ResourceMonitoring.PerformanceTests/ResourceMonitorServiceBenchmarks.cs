// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Bench;

/// <summary>
/// Benchmarks for the work <see cref="ResourceMonitorService"/> does every sampling interval to publish resource utilization.
/// </summary>
[MemoryDiagnoser]
public class ResourceMonitorServiceBenchmarks
{
    private ResourceMonitorService _noPublishers = null!;
    private ResourceMonitorService _onePublisher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ResourceMonitoringOptions());

        _noPublishers = new ResourceMonitorService(
            new FakeSnapshotProvider(),
            NullLogger<ResourceMonitorService>.Instance,
            options,
            publishers: []);

        _onePublisher = new ResourceMonitorService(
            new FakeSnapshotProvider(),
            NullLogger<ResourceMonitorService>.Instance,
            options,
            publishers: [new NoOpPublisher()]);
    }

    [Benchmark]
    public Task PublishUtilization_NoPublishers()
        => _noPublishers.PublishUtilizationAsync(CancellationToken.None);

    [Benchmark]
    public Task PublishUtilization_OnePublisher()
        => _onePublisher.PublishUtilizationAsync(CancellationToken.None);

    private sealed class FakeSnapshotProvider : ISnapshotProvider
    {
        public SystemResources Resources { get; } = new(1.0, 2.0, 1024UL * 1024 * 1024, 2048UL * 1024 * 1024);

        public Snapshot GetSnapshot()
            => new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 512UL * 1024 * 1024);
    }

    private sealed class NoOpPublisher : IResourceUtilizationPublisher
    {
        public ValueTask PublishAsync(ResourceUtilization utilization, CancellationToken cancellationToken)
            => default;
    }
}
