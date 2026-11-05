// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Helpers;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Disk.Test;

[SupportedOSPlatform("windows")]
public class WindowsDiskMetricsTests
{
    public WindowsDiskMetricsTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    private const string CategoryName = "LogicalDisk";
    private readonly FakeLogger<WindowsDiskMetrics> _fakeLogger = new();

    [Fact]
    public void Creates_Meter_With_Correct_Name()
    {
        using var meterFactory = new TestMeterFactory();
        var performanceCounterFactoryMock = new Mock<IPerformanceCounterFactory>();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        _ = new WindowsDiskMetrics(
            _fakeLogger,
            meterFactory,
            performanceCounterFactoryMock.Object,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(options));

        Meter meter = meterFactory.Meters.Single();
        Assert.Equal(ResourceUtilizationInstruments.MeterName, meter.Name);
    }

    [Fact]
    public void DiskOperationMetricsTest()
    {
        using var meterFactory = new TestMeterFactory();
        var performanceCounterFactory = new Mock<IPerformanceCounterFactory>();
        var fakeTimeProvider = new FakeTimeProvider();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        // Set up
        const string ReadCounterName = WindowsDiskPerfCounterNames.DiskReadsCounter;
        const string WriteCounterName = WindowsDiskPerfCounterNames.DiskWritesCounter;
        var readCounterC = new FakePerformanceCounter("C:", [0, 1, 1.5f, 2, 2.5f]);
        var readCounterD = new FakePerformanceCounter("D:", [0, 2, 2.5f, 3, 3.5f]);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, ReadCounterName, "C:")).Returns(readCounterC);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, ReadCounterName, "D:")).Returns(readCounterD);
        var writeCounterC = new FakePerformanceCounter("C:", [0, 10, 15, 20, 25]);
        var writeCounterD = new FakePerformanceCounter("D:", [0, 20, 25, 30, 35]);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, WriteCounterName, "C:")).Returns(writeCounterC);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, WriteCounterName, "D:")).Returns(writeCounterD);
        performanceCounterFactory.Setup(x => x.GetCategoryInstances(CategoryName)).Returns(["_Total", "C:", "D:"]);

        _ = new WindowsDiskMetrics(
            _fakeLogger,
            meterFactory,
            performanceCounterFactory.Object,
            fakeTimeProvider,
            Options.Options.Create(options));
        Meter meter = meterFactory.Meters.Single();

        var readTag = new KeyValuePair<string, object?>("disk.io.direction", "read");
        var writeTag = new KeyValuePair<string, object?>("disk.io.direction", "write");
        var deviceTagC = new KeyValuePair<string, object?>("system.device", "C:");
        var deviceTagD = new KeyValuePair<string, object?>("system.device", "D:");

        using var operationCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskOperations);

        // 1st measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        IReadOnlyList<CollectedMeasurement<long>> measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(4, measurements.Count);
        Assert.Equal(60, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 1 * 60 = 60
        Assert.Equal(120, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 2 * 60 = 120
        Assert.Equal(600, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 10 * 60 = 600
        Assert.Equal(1200, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 20 * 60 = 1200

        // 2nd measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(150, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 60 + 1.5 * 60 = 150
        Assert.Equal(270, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 120 + 2.5 * 60 = 270
        Assert.Equal(1500, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 600 + 15 * 60 = 1500
        Assert.Equal(2700, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 1200 + 25 * 60 = 2700

        // 3rd measurement
        fakeTimeProvider.Advance(TimeSpan.FromSeconds(30));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(210, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 150 + 2 * 30 = 210
        Assert.Equal(360, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 270 + 3 * 30 = 360
        Assert.Equal(2100, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 1500 + 20 * 60 = 2100
        Assert.Equal(3600, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 2700 + 30 * 60 = 3600

        // 4th measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(360, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 210 + 2.5 * 60 = 360
        Assert.Equal(570, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 360 + 3.5 * 60 = 570
        Assert.Equal(3600, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 2100 + 25 * 60 = 3600
        Assert.Equal(5700, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 3600 + 35 * 60 = 5700
    }

    [Fact]
    public void DiskIoBytesMetricsTest()
    {
        using var meterFactory = new TestMeterFactory();
        var performanceCounterFactory = new Mock<IPerformanceCounterFactory>();
        var fakeTimeProvider = new FakeTimeProvider();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        // Set up
        const string ReadCounterName = WindowsDiskPerfCounterNames.DiskReadBytesCounter;
        const string WriteCounterName = WindowsDiskPerfCounterNames.DiskWriteBytesCounter;
        var readCounterC = new FakePerformanceCounter("C:", [0, 10, 15, 20, 25]);
        var readCounterD = new FakePerformanceCounter("D:", [0, 20, 25, 30, 35]);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, ReadCounterName, "C:")).Returns(readCounterC);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, ReadCounterName, "D:")).Returns(readCounterD);
        var writeCounterC = new FakePerformanceCounter("C:", [0, 100, 150, 200, 250]);
        var writeCounterD = new FakePerformanceCounter("D:", [0, 200, 250, 300, 350]);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, WriteCounterName, "C:")).Returns(writeCounterC);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, WriteCounterName, "D:")).Returns(writeCounterD);
        performanceCounterFactory.Setup(x => x.GetCategoryInstances(CategoryName)).Returns(["_Total", "C:", "D:"]);

        _ = new WindowsDiskMetrics(
            _fakeLogger,
            meterFactory,
            performanceCounterFactory.Object,
            fakeTimeProvider,
            Options.Options.Create(options));
        Meter meter = meterFactory.Meters.Single();

        var readTag = new KeyValuePair<string, object?>("disk.io.direction", "read");
        var writeTag = new KeyValuePair<string, object?>("disk.io.direction", "write");
        var deviceTagC = new KeyValuePair<string, object?>("system.device", "C:");
        var deviceTagD = new KeyValuePair<string, object?>("system.device", "D:");

        using var operationCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskIo);

        // 1st measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        IReadOnlyList<CollectedMeasurement<long>> measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(4, measurements.Count);
        Assert.Equal(600, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 10 * 60 = 600
        Assert.Equal(1200, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 20 * 60 = 1200
        Assert.Equal(6000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 100 * 60 = 6000
        Assert.Equal(12000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 200 * 60 = 12000

        // 2nd measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(1500, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 600 + 15 * 60 = 1500
        Assert.Equal(2700, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 1200 + 25 * 60 = 2700
        Assert.Equal(15000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 6000 + 150 * 60 = 15000
        Assert.Equal(27000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 12000 + 250 * 60 = 27000

        // 3rd measurement
        fakeTimeProvider.Advance(TimeSpan.FromSeconds(30));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(2100, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 1500 + 20 * 30 = 210
        Assert.Equal(3600, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 2700 + 30 * 30 = 360
        Assert.Equal(21000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 15000 + 200 * 60 = 21000
        Assert.Equal(36000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 27000 + 300 * 60 = 36000

        // 4th measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        operationCollector.RecordObservableInstruments();
        measurements = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(3600, measurements.Last(x => x.MatchesTags(readTag, deviceTagC)).Value); // 2100 + 25 * 60 = 3600
        Assert.Equal(5700, measurements.Last(x => x.MatchesTags(readTag, deviceTagD)).Value); // 3600 + 35 * 60 = 5700
        Assert.Equal(36000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagC)).Value); // 21000 + 250 * 60 = 36000
        Assert.Equal(57000, measurements.Last(x => x.MatchesTags(writeTag, deviceTagD)).Value); // 36000 + 350 * 60 = 57000
    }
}
