// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Helpers;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Interop;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using VerifyXunit;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

public sealed class WindowsSnapshotProviderTests
{
    private const string VerifiedDataDirectory = "Verified";

    private readonly Mock<IMeterFactory> _meterFactoryMock;
    private readonly FakeLogger<WindowsSnapshotProvider> _fakeLogger;
    private readonly IOptions<ResourceMonitoringOptions> _options;

    public WindowsSnapshotProviderTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        _options = Options.Options.Create<ResourceMonitoringOptions>(new());
        using var meter = new Meter(nameof(BasicConstructor));
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        _fakeLogger = new FakeLogger<WindowsSnapshotProvider>();
    }

    [Fact]
    public void BasicConstructor()
    {
        var provider = new WindowsSnapshotProvider(_fakeLogger, _meterFactoryMock.Object, _options);
        var memoryStatus = new MemoryInfo().GetMemoryStatus();

        Assert.Equal(Environment.ProcessorCount, provider.Resources.GuaranteedCpuUnits);
        Assert.Equal(Environment.ProcessorCount, provider.Resources.MaximumCpuUnits);
        Assert.Equal(memoryStatus.TotalPhys, provider.Resources.GuaranteedMemoryInBytes);
        Assert.Equal(memoryStatus.TotalPhys, provider.Resources.MaximumMemoryInBytes);
    }

    [Fact]
    public void GetSnapshot_DoesNotThrowExceptions()
    {
        var provider = new WindowsSnapshotProvider(_fakeLogger, _meterFactoryMock.Object, _options);

        var exception = Record.Exception(() => provider.GetSnapshot());
        Assert.Null(exception);
    }

    [Fact]
    public Task SnapshotProvider_EmitsLogRecord()
    {
        var provider = new WindowsSnapshotProvider(_fakeLogger, _meterFactoryMock.Object, _options);
        var logRecords = _fakeLogger.Collector.GetSnapshot();
        Assert.Equal(2, logRecords.Count);
        Assert.StartsWith("System resources information", logRecords[1].Message);

        return Verifier.Verify(logRecords[0]).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [CombinatorialData]
    public void SnapshotProvider_EmitsCpuMetrics(bool useZeroToOneRange)
    {
        var fakeClock = new FakeTimeProvider();
        var cpuTicks = 500L;
        var options = new ResourceMonitoringOptions
        {
            CpuConsumptionRefreshInterval = TimeSpan.FromMilliseconds(2),
            UseZeroToOneRangeForMetrics = useZeroToOneRange
        };
        var multiplier = useZeroToOneRange ? 1 : 100;
        using var meter = new Meter(nameof(SnapshotProvider_EmitsCpuMetrics));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>())).Returns(meter);

        var snapshotProvider = new WindowsSnapshotProvider(_fakeLogger, meterFactoryMock.Object, options, fakeClock,
            static () => 2, () => cpuTicks, static () => 0L, static () => 1UL);

        cpuTicks = 1_500L;

        using var metricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ProcessCpuUtilization, fakeClock);

        // Step #0 - state in the beginning:
        metricCollector.RecordObservableInstruments();
        Assert.NotNull(metricCollector.LastMeasurement);
        Assert.True(double.IsNaN(metricCollector.LastMeasurement.Value));

        // Step #1 - simulate 1 millisecond passing and collect metrics again:
        fakeClock.Advance(TimeSpan.FromMilliseconds(1));
        metricCollector.RecordObservableInstruments();
        Assert.Equal(0.05 * multiplier, metricCollector.LastMeasurement?.Value); // Consuming 5% of the CPU (2 CPUs, 1000 ticks, 1ms).

        // Step #2 - simulate another 1 millisecond passing and collect metrics again:
        fakeClock.Advance(TimeSpan.FromMilliseconds(1));
        metricCollector.RecordObservableInstruments();

        // CPU usage should be the same as before, as we're not simulating any CPU usage:
        Assert.Equal(0.05 * multiplier, metricCollector.LastMeasurement?.Value); // Still consuming 5% of the CPU
    }

    [Theory]
    [CombinatorialData]
    public void SnapshotProvider_EmitsMemoryMetrics(bool useZeroToOneRange)
    {
        var fakeClock = new FakeTimeProvider();
        long memoryUsed = 300L;
        var options = new ResourceMonitoringOptions
        {
            MemoryConsumptionRefreshInterval = TimeSpan.FromMilliseconds(2),
            UseZeroToOneRangeForMetrics = useZeroToOneRange
        };
        var multiplier = useZeroToOneRange ? 1 : 100;
        using var meter = new Meter(nameof(SnapshotProvider_EmitsMemoryMetrics));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        var snapshotProvider = new WindowsSnapshotProvider(_fakeLogger, meterFactoryMock.Object, options, fakeClock, static () => 1, static () => 0, () => memoryUsed, static () => 3000UL);

        using var metricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ProcessMemoryUtilization, fakeClock);

        // Step #0 - state in the beginning:
        metricCollector.RecordObservableInstruments();
        Assert.NotNull(metricCollector.LastMeasurement);
        Assert.Equal(0.1 * multiplier, metricCollector.LastMeasurement.Value); // Consuming 5% of the memory initially

        memoryUsed = 900L; // Simulate 30% memory usage.

        // Step #1 - simulate 1 millisecond passing and collect metrics again:
        fakeClock.Advance(TimeSpan.FromMilliseconds(1));
        metricCollector.RecordObservableInstruments();

        Assert.Equal(0.1 * multiplier, metricCollector.LastMeasurement.Value); // Still consuming 10% as gauge wasn't updated.

        // Step #2 - simulate 1 millisecond passing and collect metrics again:
        fakeClock.Advance(TimeSpan.FromMilliseconds(1));
        metricCollector.RecordObservableInstruments();

        Assert.Equal(0.3 * multiplier, metricCollector.LastMeasurement.Value); // Consuming 30% of the memory afterwards

        memoryUsed = 3_100L; // Simulate more than 100% memory usage

        // Step #3 - simulate 1 millisecond passing and collect metrics again:
        fakeClock.Advance(options.MemoryConsumptionRefreshInterval);
        metricCollector.RecordObservableInstruments();

        // Memory usage should be the same as before, as we're not simulating any CPU usage:
        Assert.Equal(1 * multiplier, Math.Round(metricCollector.LastMeasurement.Value)); // Consuming 100% of the memory
    }

    [Fact]
    public void Provider_Returns_MemoryConsumption()
    {
        // This is a synthetic test to have full test coverage:
        var usage = WindowsSnapshotProvider.GetMemoryUsageInBytes();
        Assert.InRange(usage, 0, long.MaxValue);
    }

    [Fact]
    public void Provider_Creates_Meter_With_Correct_Name()
    {
        using var meterFactory = new TestMeterFactory();

        _ = new WindowsSnapshotProvider(_fakeLogger, meterFactory, _options);

        var meter = meterFactory.Meters.Single();
        Assert.Equal(ResourceUtilizationInstruments.MeterName, meter.Name);
    }

    [Fact]
    public void Provider_Registers_Instruments_With_Descriptions()
    {
        using var meterFactory = new TestMeterFactory();

        _ = new WindowsSnapshotProvider(_fakeLogger, meterFactory, _options);

        var meter = meterFactory.Meters.Single();
        var instruments = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (ReferenceEquals(meter, instrument.Meter))
                {
                    instruments.Add(instrument);
                }
            }
        };

        listener.Start();

        Assert.NotEmpty(instruments);
        Assert.All(instruments, instrument => Assert.False(string.IsNullOrWhiteSpace(instrument.Description), $"Instrument '{instrument.Name}' has no description."));
    }
}
