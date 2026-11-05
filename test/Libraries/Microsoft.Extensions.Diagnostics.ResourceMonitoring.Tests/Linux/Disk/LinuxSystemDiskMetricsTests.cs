// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Helpers;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Disk.Test;

public class LinuxSystemDiskMetricsTests
{
    public LinuxSystemDiskMetricsTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");
    }

    private static readonly string[] _skipDevicePrefixes = new[] { "ram", "loop", "dm-" };
    private readonly FakeLogger<LinuxSystemDiskMetrics> _fakeLogger = new();

    [Fact]
    public void Creates_Meter_With_Correct_Name()
    {
        using var meterFactory = new TestMeterFactory();
        var diskStatsReaderMock = new Mock<IDiskStatsReader>();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        _ = new LinuxSystemDiskMetrics(
            _fakeLogger,
            meterFactory,
            Options.Options.Create(options),
            TimeProvider.System,
            diskStatsReaderMock.Object);

        Meter meter = meterFactory.Meters.Single();
        Assert.Equal(ResourceUtilizationInstruments.MeterName, meter.Name);
    }

    [Fact]
    public void Test_MetricValues()
    {
        using var meterFactory = new TestMeterFactory();
        var fakeTimeProvider = new FakeTimeProvider();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        // Set up
        var diskStatsReader = new FakeDiskStatsReader(new Dictionary<string, List<DiskStats>>
        {
            {
                "sda", [
                    new DiskStats
                    {
                        DeviceName = "sda",
                        SectorsRead = 0,
                        SectorsWritten = 0,
                        ReadsCompleted = 0,
                        WritesCompleted = 0,
                        TimeIoMs = 0
                    },
                    new DiskStats
                    {
                        DeviceName = "sda",
                        SectorsRead = 500,
                        SectorsWritten = 1000,
                        ReadsCompleted = 600,
                        WritesCompleted = 1200,
                        TimeIoMs = 1234
                    },
                    new DiskStats
                    {
                        DeviceName = "sda",
                        SectorsRead = 700,
                        SectorsWritten = 1100,
                        ReadsCompleted = 800,
                        WritesCompleted = 1300,
                        TimeIoMs = 2234
                    },
                    new DiskStats
                    {
                        DeviceName = "sda",
                        SectorsRead = 1000,
                        SectorsWritten = 1600,
                        ReadsCompleted = 1300,
                        WritesCompleted = 1350,
                        TimeIoMs = 4444
                    }
                ]
            },
            {
                "sdb", [
                    new DiskStats
                    {
                        DeviceName = "sdb",
                        SectorsRead = 200,
                        SectorsWritten = 300,
                        ReadsCompleted = 400,
                        WritesCompleted = 500,
                        TimeIoMs = 6000
                    },
                    new DiskStats
                    {
                        DeviceName = "sdb",
                        SectorsRead = 350,
                        SectorsWritten = 450,
                        ReadsCompleted = 550,
                        WritesCompleted = 650,
                        TimeIoMs = 7500
                    },
                    new DiskStats
                    {
                        DeviceName = "sdb",
                        SectorsRead = 400,
                        SectorsWritten = 500,
                        ReadsCompleted = 600,
                        WritesCompleted = 700,
                        TimeIoMs = 7500
                    },
                        new DiskStats
                    {
                        DeviceName = "sdb",
                        SectorsRead = 550,
                        SectorsWritten = 650,
                        ReadsCompleted = 750,
                        WritesCompleted = 850,
                        TimeIoMs = 9500
                    }
                ]
            },
        });

        _ = new LinuxSystemDiskMetrics(
            _fakeLogger,
            meterFactory,
            Options.Options.Create(options),
            fakeTimeProvider,
            diskStatsReader);
        Meter meter = meterFactory.Meters.Single();

        var readTag = new KeyValuePair<string, object?>("disk.io.direction", "read");
        var writeTag = new KeyValuePair<string, object?>("disk.io.direction", "write");
        var deviceTagSda = new KeyValuePair<string, object?>("system.device", "sda");
        var deviceTagSdb = new KeyValuePair<string, object?>("system.device", "sdb");

        using var diskIoCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskIo);
        using var operationCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskOperations);
        using var ioTimeCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.SystemDiskIoTime);

        // 1st measurement
        diskIoCollector.RecordObservableInstruments();
        operationCollector.RecordObservableInstruments();
        ioTimeCollector.RecordObservableInstruments();

        // Assert the 1st measurement
        var diskIoMeasurement = diskIoCollector.GetMeasurementSnapshot();
        Assert.Equal(4, diskIoMeasurement.Count);
        Assert.Equal(256_000, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // (500 - 0) * 512 = 256000
        Assert.Equal(76_800, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // (350 - 200) * 512 = 76800
        Assert.Equal(512_000, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // (1000 - 0) * 512 = 512000
        Assert.Equal(76_800, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // (450 - 300) * 512 = 76800
        var operationMeasurement = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(4, operationMeasurement.Count);
        Assert.Equal(600, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // 600 - 0 = 600
        Assert.Equal(150, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // 550 - 400 = 150
        Assert.Equal(1200, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // 1200 - 0 = 1200
        Assert.Equal(150, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // 650 - 500 = 150
        var ioTimeMeasurement = ioTimeCollector.GetMeasurementSnapshot();
        Assert.Equal(2, ioTimeMeasurement.Count);
        Assert.Equal(1.234, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSda)).Value, 0.01); // (1234 - 0) / 1000 = 1.234
        Assert.Equal(1.5, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSdb)).Value, 0.01); // (7500 - 6000) / 1000 = 6.0

        // 2nd measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        diskIoCollector.RecordObservableInstruments();
        operationCollector.RecordObservableInstruments();
        ioTimeCollector.RecordObservableInstruments();

        // Assert the 2nd measurement
        diskIoMeasurement = diskIoCollector.GetMeasurementSnapshot();
        Assert.Equal(358_400, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // (700 - 0) * 512 = 358400
        Assert.Equal(102_400, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // (400 - 200) * 512 = 102400
        Assert.Equal(563_200, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // (1100 - 0) * 512 = 563200
        Assert.Equal(102_400, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // (500 - 300) * 512 = 102400
        operationMeasurement = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(800, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // 800 - 0 = 800
        Assert.Equal(200, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // 600 - 400 = 200
        Assert.Equal(1300, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // 1300 - 0 = 1300
        Assert.Equal(200, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // 700 - 500 = 200
        ioTimeMeasurement = ioTimeCollector.GetMeasurementSnapshot();
        Assert.Equal(2.234, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSda)).Value, 0.01); // (2234 - 0) / 1000 = 2.234
        Assert.Equal(1.5, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSdb)).Value, 0.01); // (7500 - 6000) / 1000 = 1.5

        // 3rd measurement
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        diskIoCollector.RecordObservableInstruments();
        operationCollector.RecordObservableInstruments();
        ioTimeCollector.RecordObservableInstruments();

        // Assert the 3rd measurement
        diskIoMeasurement = diskIoCollector.GetMeasurementSnapshot();
        Assert.Equal(512_000, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // (1000 - 0) * 512 = 512000
        Assert.Equal(179_200, diskIoMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // (550 - 200) * 512 = 179200
        Assert.Equal(819_200, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // (1600 - 0) * 512 = 819200
        Assert.Equal(179_200, diskIoMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // (650 - 300) * 512 = 179200
        operationMeasurement = operationCollector.GetMeasurementSnapshot();
        Assert.Equal(1300, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSda)).Value); // 1300 - 0 = 1300
        Assert.Equal(350, operationMeasurement.Last(x => x.MatchesTags(readTag, deviceTagSdb)).Value); // 750 - 400 = 350
        Assert.Equal(1350, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSda)).Value); // 1350 - 0 = 1350
        Assert.Equal(350, operationMeasurement.Last(x => x.MatchesTags(writeTag, deviceTagSdb)).Value); // 850 - 500 = 350
        ioTimeMeasurement = ioTimeCollector.GetMeasurementSnapshot();
        Assert.Equal(4.444, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSda)).Value, 0.01); // (4444 - 0) / 1000 = 4.444
        Assert.Equal(3.5, ioTimeMeasurement.Last(x => x.MatchesTags(deviceTagSdb)).Value, 0.01); // (9500 - 6000) / 1000 = 3.5
    }

    [Fact]
    public void GetAllDiskStats_RetriesAfterFailureInterval()
    {
        using var meterFactory = new TestMeterFactory();
        var fakeTimeProvider = new FakeTimeProvider();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };

        var diskStats = new DiskStats
        {
            DeviceName = "sda",
            SectorsRead = 100,
            SectorsWritten = 200,
            ReadsCompleted = 10,
            WritesCompleted = 20,
            TimeIoMs = 1000
        };

        var diskStatsReaderMock = new Mock<IDiskStatsReader>();
        diskStatsReaderMock.Setup(r => r.ReadAll(_skipDevicePrefixes)).Throws<FileNotFoundException>();

        var metrics = new LinuxSystemDiskMetrics(
            _fakeLogger,
            meterFactory,
            Options.Options.Create(options),
            fakeTimeProvider,
            diskStatsReaderMock.Object);

        using var ioCollector = new MetricCollector<long>(meterFactory.Meters.Single(), ResourceUtilizationInstruments.SystemDiskIo);

        ioCollector.RecordObservableInstruments();
        Assert.Empty(ioCollector.GetMeasurementSnapshot());
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Once);

        ioCollector.RecordObservableInstruments();
        Assert.Empty(ioCollector.GetMeasurementSnapshot());
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Once);

        fakeTimeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        ioCollector.RecordObservableInstruments();
        Assert.Empty(ioCollector.GetMeasurementSnapshot());
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Exactly(2));

        diskStatsReaderMock.Reset();
        diskStatsReaderMock.Setup(r => r.ReadAll(_skipDevicePrefixes)).Returns(new DiskStats[] { diskStats });

        fakeTimeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        ioCollector.RecordObservableInstruments();
        var measurements = ioCollector.GetMeasurementSnapshot();
        Assert.NotEmpty(measurements);
        Assert.Contains(measurements, m => m.Tags.Any(t => t.Value?.ToString() == "sda"));
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Once);

        fakeTimeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        ioCollector.RecordObservableInstruments();
        measurements = ioCollector.GetMeasurementSnapshot();
        Assert.NotEmpty(measurements);
        Assert.Contains(measurements, m => m.Tags.Any(t => t.Value?.ToString() == "sda"));
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Exactly(2));
    }

    [Fact]
    public void Metrics_Are_Not_Created_When_ReadAll_Throws_FileNotFoundException()
    {
        using var meterFactory = new TestMeterFactory();
        var options = new ResourceMonitoringOptions { EnableSystemDiskIoMetrics = true };
        var diskStatsReaderMock = new Mock<IDiskStatsReader>();
        diskStatsReaderMock.Setup(r => r.ReadAll(_skipDevicePrefixes)).Throws<FileNotFoundException>();

        var fakeTimeProvider = new FakeTimeProvider();

        _ = new LinuxSystemDiskMetrics(
            _fakeLogger,
            meterFactory,
            Options.Options.Create(options),
            fakeTimeProvider,
            diskStatsReaderMock.Object);

        Meter meter = meterFactory.Meters.Single();

        using var diskIoCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskIo);
        using var operationCollector = new MetricCollector<long>(meter, ResourceUtilizationInstruments.SystemDiskOperations);
        using var ioTimeCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.SystemDiskIoTime);

        diskIoCollector.RecordObservableInstruments();
        operationCollector.RecordObservableInstruments();
        ioTimeCollector.RecordObservableInstruments();

        Assert.Empty(diskIoCollector.GetMeasurementSnapshot());
        Assert.Empty(operationCollector.GetMeasurementSnapshot());
        Assert.Empty(ioTimeCollector.GetMeasurementSnapshot());
        diskStatsReaderMock.Verify(r => r.ReadAll(_skipDevicePrefixes), Times.Once);
    }
}
