// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// The implementation of <see cref="IResourceMonitor"/> that computes average resource utilization over a configured period of time.
/// </summary>
/// <remarks>
/// The class also acts as a hosted singleton, intended to be used to manage the
/// background process of periodically inspecting and monitoring the utilization
/// of an enclosing system.
/// </remarks>
internal sealed class ResourceMonitorService : BackgroundService, IResourceMonitor
{
    /// <summary>
    /// The data source.
    /// </summary>
    private readonly ISnapshotProvider _provider;

    /// <summary>
    /// The publishers to use with the data we are tracking.
    /// </summary>
    private readonly IResourceUtilizationPublisher[] _publishers;

    /// <summary>
    /// Logger to be used in this class.
    /// </summary>
    private readonly ILogger<ResourceMonitorService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Circular buffer for storing samples.
    /// </summary>
    private readonly CircularBuffer<Snapshot> _snapshotsStore;

    private readonly TimeSpan _samplingInterval;

    private readonly TimeSpan _publishingWindow;

    private readonly TimeSpan _collectionWindow;

    public ResourceMonitorService(
        ISnapshotProvider provider,
        ILogger<ResourceMonitorService> logger,
        IOptions<ResourceMonitoringOptions> options,
        IEnumerable<IResourceUtilizationPublisher> publishers)
        : this(provider, logger, options, publishers, TimeProvider.System)
    {
    }

    internal ResourceMonitorService(
        ISnapshotProvider provider,
        ILogger<ResourceMonitorService> logger,
        IOptions<ResourceMonitoringOptions> options,
        IEnumerable<IResourceUtilizationPublisher> publishers,
        TimeProvider timeProvider)
    {
        _provider = provider;
        _logger = logger;
        _timeProvider = timeProvider;
        var optionsValue = Throw.IfMemberNull(options, options.Value);
        _publishingWindow = optionsValue.PublishingWindow;
        _samplingInterval = optionsValue.SamplingInterval;
        _collectionWindow = optionsValue.CollectionWindow;

        _publishers = publishers.ToArray();

        var bufferSize = (int)(_collectionWindow.TotalMilliseconds / _samplingInterval.TotalMilliseconds);

        var firstSnapshot = _provider.GetSnapshot();

        _snapshotsStore = new CircularBuffer<Snapshot>(bufferSize + 1, firstSnapshot);
    }

    /// <inheritdoc />
    public ResourceUtilization GetUtilization(TimeSpan window)
    {
        _ = Throw.IfLessThanOrEqual(window.Ticks, 0);
        _ = Throw.IfGreaterThan(window.Ticks, _collectionWindow.Ticks);

        var samplesToRead = (int)(window.Ticks / _samplingInterval.Ticks) + 1;
        (Snapshot first, Snapshot last) t;

        lock (_snapshotsStore)
        {
            t = _snapshotsStore.GetFirstAndLastFromWindow(samplesToRead);
        }

        return Calculator.CalculateUtilization(t.first, t.last, _provider.Resources);
    }

    internal async Task PublishUtilizationAsync(CancellationToken cancellationToken)
    {
        if (_publishers.Length == 0)
        {
            // Nothing to publish to, so avoid calculating the utilization (which is otherwise side-effect free).
            return;
        }

        var u = GetUtilization(_publishingWindow);
        foreach (var publisher in _publishers)
        {
            try
            {
                await publisher.PublishAsync(u, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // By Design: Swallow the exception, as they're non-actionable in this code path.
                // Prioritize app reliability over error visibility
                _logger.HandlePublishUtilizationException(e, publisher.GetType().FullName!);
            }
        }
    }

    [SuppressMessage("Blocker Bug", "S2190:Loops and recursions should not be infinite", Justification = "Terminate when Delay throws an exception on cancellation")]
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _timeProvider.Delay(_samplingInterval, cancellationToken).ConfigureAwait(false);

            try
            {
                var snapshot = _provider.GetSnapshot();
                _snapshotsStore.Add(snapshot);

                _logger.SnapshotReceived(snapshot.TotalTimeSinceStart, snapshot.KernelTimeSinceStart, snapshot.UserTimeSinceStart, snapshot.MemoryUsageInBytes);
            }
            catch (Exception e)
            {
                // By Design: Swallow the exception, as they're non-actionable in this code path.
                // Prioritize app reliability over error visibility
                _logger.HandledGatherStatisticsException(e);
            }

            await PublishUtilizationAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
