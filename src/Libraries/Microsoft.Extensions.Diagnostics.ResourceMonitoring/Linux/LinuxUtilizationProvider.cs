// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Instruments;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux;

internal sealed class LinuxUtilizationProvider : ISnapshotProvider
{
    private const double One = 1.0;
    private const double Hundred = 100.0;
    private const long HundredLong = 100L;
    private const double NanosecondsInSecond = 1_000_000_000;

    private readonly object _cpuLocker = new();
    private readonly object _memoryLocker = new();
    private readonly ILogger<LinuxUtilizationProvider> _logger;
    private readonly ILinuxUtilizationParser _parser;
    private readonly long _cpuPeriodsInterval;
    private readonly TimeSpan _cpuRefreshInterval;
    private readonly TimeSpan _memoryRefreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly double _scaleRelativeToCpuRequestForTrackerApi;
    private readonly double _metricValueMultiplier;

    private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastFailure = DateTimeOffset.MinValue;
    private int _measurementsUnavailable;

    private double _memoryLimit;
    private double _cpuLimit;
#pragma warning disable S1450 // Private fields only used as local variables in methods should become local variables. This will be used once we bring relevant meters.
    private double _memoryRequest;
#pragma warning restore S1450 // Private fields only used as local variables in methods should become local variables
    private double _cpuRequest;

    private DateTimeOffset _refreshAfterCpu;
    private DateTimeOffset _refreshAfterMemory;
    private double _cpuPercentage = double.NaN;
    private double _lastCpuCoresUsed = double.NaN;
    private ulong _memoryUsage;
    private long _previousCgroupCpuTime;
    private long _previousHostCpuTime;
    private long _previousCgroupCpuPeriodCounter;
    public SystemResources Resources { get; }

    public LinuxUtilizationProvider(
        IOptions<ResourceMonitoringOptions> options,
        ILinuxUtilizationParser parser,
        IMeterFactory meterFactory,
        ResourceQuotaProvider resourceQuotaProvider,
        ILogger<LinuxUtilizationProvider>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _parser = parser;
        _logger = logger ?? NullLogger<LinuxUtilizationProvider>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _cpuRefreshInterval = options.Value.CpuConsumptionRefreshInterval;
        _memoryRefreshInterval = options.Value.MemoryConsumptionRefreshInterval;
        _metricValueMultiplier = options.Value.UseZeroToOneRangeForLinuxMetrics ? One : Hundred;
        _refreshAfterCpu = now;
        _refreshAfterMemory = now;
        _previousHostCpuTime = _parser.GetHostCpuUsageInNanoseconds();
        _previousCgroupCpuTime = _parser.GetCgroupCpuUsageInNanoseconds();

        var quota = resourceQuotaProvider.GetResourceQuota();
        _cpuLimit = quota.MaxCpuInCores;
        _memoryLimit = quota.MaxMemoryInBytes;
        _cpuRequest = quota.BaselineCpuInCores;
        _memoryRequest = quota.BaselineMemoryInBytes;

        float hostCpus = _parser.GetHostCpuCount();
        double scaleRelativeToCpuLimit = hostCpus / _cpuLimit;
        double scaleRelativeToCpuRequest = hostCpus / _cpuRequest;
        _scaleRelativeToCpuRequestForTrackerApi = hostCpus; // the division by cpuRequest is performed later on in the ResourceUtilization class

#pragma warning disable CA2000 // Dispose objects before losing scope
        // We don't dispose the meter because IMeterFactory handles that
        // An issue on analyzer side: https://github.com/dotnet/roslyn-analyzers/issues/6912
        // Related documentation: https://github.com/dotnet/docs/pull/37170
        var meter = meterFactory.Create(ResourceUtilizationInstruments.MeterName);
#pragma warning restore CA2000 // Dispose objects before losing scope

        if (options.Value.UseLinuxCalculationV2)
        {
            // Get Cpu periods interval from cgroup
            _cpuPeriodsInterval = _parser.GetCgroupPeriodsIntervalInMicroSecondsV2();
            (_previousCgroupCpuTime, _previousCgroupCpuPeriodCounter) = _parser.GetCgroupCpuUsageInNanosecondsAndCpuPeriodsV2();

            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ContainerCpuLimitUtilization,
                observeValues: () => GetMeasurementWithRetry(() => CpuUtilizationLimit(_cpuLimit)),
                unit: "1",
                description: "The CPU consumption of the running containerized application relative to resource limit.");

            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ContainerCpuRequestUtilization,
                observeValues: () => GetMeasurementWithRetry(() => CpuUtilizationRequest(_cpuRequest)),
                unit: "1",
                description: "The CPU consumption of the running containerized application relative to resource request.");

            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ContainerCpuTime,
                observeValues: GetCpuTime,
                unit: "1",
                description: "CPU time used by the container.");
        }
        else
        {
            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ContainerCpuLimitUtilization,
                observeValues: () => GetMeasurementWithRetry(() => CpuUtilization() * scaleRelativeToCpuLimit),
                unit: "1",
                description: "The CPU consumption of the running containerized application relative to resource limit.");

            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ContainerCpuRequestUtilization,
                observeValues: () => GetMeasurementWithRetry(() => CpuUtilization() * scaleRelativeToCpuRequest),
                unit: "1",
                description: "The CPU consumption of the running containerized application relative to resource request.");

            _ = meter.CreateObservableGauge(
                name: ResourceUtilizationInstruments.ProcessCpuUtilization,
                observeValues: () => GetMeasurementWithRetry(() => CpuUtilization() * scaleRelativeToCpuRequest),
                unit: "1",
                description: "The CPU consumption of the running application.");
        }

        _ = meter.CreateObservableGauge(
            name: ResourceUtilizationInstruments.ContainerMemoryLimitUtilization,
            observeValues: () => GetMeasurementWithRetry(() => MemoryPercentageLimit()),
            unit: "1",
            description: "The memory consumption of the running containerized application relative to resource limit.");

        _ = meter.CreateObservableGauge(
            name: ResourceUtilizationInstruments.ContainerMemoryRequestUtilization,
            observeValues: () => GetMeasurementWithRetry(() => MemoryPercentageRequest()),
            unit: "1",
            description: "The memory consumption of the running containerized application relative to resource request.");

        _ = meter.CreateObservableUpDownCounter(
            name: ResourceUtilizationInstruments.ContainerMemoryUsage,
            observeValues: () => GetMeasurementWithRetry(() => (long)MemoryUsage()),
            unit: "By",
            description: "Memory usage of the container.");

        _ = meter.CreateObservableGauge(
            name: ResourceUtilizationInstruments.ProcessMemoryUtilization,
            observeValues: () => GetMeasurementWithRetry(() => MemoryPercentageLimit()),
            unit: "1",
            description: "The memory consumption of the running application.");

        Resources = new SystemResources(_cpuRequest, _cpuLimit, quota.BaselineMemoryInBytes, quota.MaxMemoryInBytes);
        _logger.SystemResourcesInfo(_cpuLimit, _cpuRequest, quota.MaxMemoryInBytes, quota.BaselineMemoryInBytes);
    }

    public double CpuUtilizationV2()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_cpuLocker)
        {
            if (now <= _refreshAfterCpu)
            {
                return _lastCpuCoresUsed;
            }
        }

        (long cpuUsageTime, long cpuPeriodCounter) = _parser.GetCgroupCpuUsageInNanosecondsAndCpuPeriodsV2();
        lock (_cpuLocker)
        {
            if (now <= _refreshAfterCpu)
            {
                return _lastCpuCoresUsed;
            }

            long deltaCgroup = cpuUsageTime - _previousCgroupCpuTime;
            long deltaPeriodCount = cpuPeriodCounter - _previousCgroupCpuPeriodCounter;

            if (deltaCgroup <= 0 || deltaPeriodCount <= 0)
            {
                return _lastCpuCoresUsed;
            }

            long deltaCpuPeriodInNanoseconds = deltaPeriodCount * _cpuPeriodsInterval * 1000;
            double coresUsed = deltaCgroup / (double)deltaCpuPeriodInNanoseconds;

            _logger.CpuUsageDataV2(cpuUsageTime, _previousCgroupCpuTime, deltaCpuPeriodInNanoseconds, coresUsed);

            _lastCpuCoresUsed = coresUsed;
            _refreshAfterCpu = now.Add(_cpuRefreshInterval);
            _previousCgroupCpuTime = cpuUsageTime;
            _previousCgroupCpuPeriodCounter = cpuPeriodCounter;
        }

        return _lastCpuCoresUsed;
    }

    public double CpuUtilization()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_cpuLocker)
        {
            if (now <= _refreshAfterCpu)
            {
                return _cpuPercentage;
            }
        }

        long hostCpuTime = _parser.GetHostCpuUsageInNanoseconds();
        long cgroupCpuTime = _parser.GetCgroupCpuUsageInNanoseconds();

        lock (_cpuLocker)
        {
            if (now <= _refreshAfterCpu)
            {
                return _cpuPercentage;
            }

            long deltaHost = hostCpuTime - _previousHostCpuTime;
            long deltaCgroup = cgroupCpuTime - _previousCgroupCpuTime;

            if (deltaHost <= 0 || deltaCgroup <= 0)
            {
                return _cpuPercentage;
            }

            double percentage = Math.Min(_metricValueMultiplier, (double)deltaCgroup / deltaHost * _metricValueMultiplier);

            _logger.CpuUsageData(cgroupCpuTime, hostCpuTime, _previousCgroupCpuTime, _previousHostCpuTime, percentage);

            _cpuPercentage = percentage;
            _refreshAfterCpu = now.Add(_cpuRefreshInterval);
            _previousCgroupCpuTime = cgroupCpuTime;
            _previousHostCpuTime = hostCpuTime;
        }

        return _cpuPercentage;
    }

    public ulong MemoryUsage()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_memoryLocker)
        {
            if (now < _refreshAfterMemory)
            {
                return _memoryUsage;
            }
        }

        ulong memoryUsage = _parser.GetMemoryUsageInBytes();

        lock (_memoryLocker)
        {
            if (now >= _refreshAfterMemory)
            {
                _memoryUsage = memoryUsage;
                _refreshAfterMemory = now.Add(_memoryRefreshInterval);
            }
        }

        _logger.MemoryUsageData(_memoryUsage);

        return _memoryUsage;
    }

    /// <remarks>
    /// Not adding caching, to preserve original semantics of the code.
    /// The snapshot provider is called in intervals configured by the tracker.
    /// We multiply by scale to make hardcoded algorithm in tracker's calculator to produce right results.
    /// </remarks>
    public Snapshot GetSnapshot()
    {
        long hostTime = _parser.GetHostCpuUsageInNanoseconds();
        long cgroupTime = _parser.GetCgroupCpuUsageInNanoseconds();
        ulong memoryUsed = _parser.GetMemoryUsageInBytes();

        return new Snapshot(
            totalTimeSinceStart: TimeSpan.FromTicks(hostTime / HundredLong),
            kernelTimeSinceStart: TimeSpan.Zero,
            userTimeSinceStart: TimeSpan.FromTicks((long)(cgroupTime / HundredLong * _scaleRelativeToCpuRequestForTrackerApi)),
            memoryUsageInBytes: memoryUsed);
    }

    private double MemoryPercentageLimit()
    {
        ulong memoryUsage = MemoryUsage();
        double memoryPercentage = Math.Min(_metricValueMultiplier, memoryUsage / _memoryLimit * _metricValueMultiplier);

        _logger.MemoryPercentageLimit(memoryUsage, _memoryLimit, memoryPercentage);
        return memoryPercentage;
    }

    private double MemoryPercentageRequest()
    {
        ulong memoryUsage = MemoryUsage();
        double memoryPercentage = Math.Min(_metricValueMultiplier, memoryUsage / _memoryRequest * _metricValueMultiplier);

        _logger.MemoryPercentageRequest(memoryUsage, _memoryRequest, memoryPercentage);
        return memoryPercentage;
    }

    private Measurement<T>[] GetMeasurementWithRetry<T>(Func<T> func)
        where T : struct
    {
        if (!TryGetValueWithRetry(func, out T value))
        {
            return Array.Empty<Measurement<T>>();
        }

        return new[] { new Measurement<T>(value) };
    }

    private bool TryGetValueWithRetry<T>(Func<T> func, out T value)
        where T : struct
    {
        value = default;
        if (Volatile.Read(ref _measurementsUnavailable) == 1 &&
            _timeProvider.GetUtcNow() - _lastFailure < _retryInterval)
        {
            return false;
        }

        try
        {
            value = func();
            _ = Interlocked.CompareExchange(ref _measurementsUnavailable, 0, 1);

            return true;
        }
        catch (Exception ex) when (
            ex is System.IO.FileNotFoundException ||
            ex is System.IO.DirectoryNotFoundException ||
            ex is System.UnauthorizedAccessException)
        {
            _lastFailure = _timeProvider.GetUtcNow();
            _ = Interlocked.Exchange(ref _measurementsUnavailable, 1);

            return false;
        }
    }

    // Math.Min() is used below to mitigate margin errors and various kinds of precisions losses
    // due to the fact that the calculation itself is not an atomic operation:
    private double CpuUtilizationRequest(double cpuRequest) => Math.Min(_metricValueMultiplier, CpuUtilizationV2() / cpuRequest * _metricValueMultiplier);
    private double CpuUtilizationLimit(double cpuLimit) => Math.Min(_metricValueMultiplier, CpuUtilizationV2() / cpuLimit * _metricValueMultiplier);

    private IEnumerable<Measurement<double>> GetCpuTime()
    {
        if (TryGetValueWithRetry(_parser.GetHostCpuUsageInNanoseconds, out long systemCpuTime))
        {
            yield return new Measurement<double>(systemCpuTime / NanosecondsInSecond, [new KeyValuePair<string, object?>("cpu.mode", "system")]);
        }

        if (TryGetValueWithRetry(CpuUtilizationV2, out double userCpuTime))
        {
            yield return new Measurement<double>(userCpuTime, [new KeyValuePair<string, object?>("cpu.mode", "user")]);
        }
    }
}
