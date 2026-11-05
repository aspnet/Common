// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Disk.Test;

[SupportedOSPlatform("windows")]
public class WindowsDiskIoTimePerfCounterTests
{
    public WindowsDiskIoTimePerfCounterTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    private const string CategoryName = "LogicalDisk";

    [Fact]
    public void DiskReadsPerfCounter_Per60Seconds()
    {
        const string CounterName = WindowsDiskPerfCounterNames.DiskReadsCounter;
        var performanceCounterFactory = new Mock<IPerformanceCounterFactory>();
        var fakeTimeProvider = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromSeconds(60) };

        var perfCounters = new WindowsDiskIoTimePerfCounter(
            performanceCounterFactory.Object,
            fakeTimeProvider,
            CategoryName,
            CounterName,
            instanceNames: ["C:", "D:"]);

        // Set up
        var counterC = new FakePerformanceCounter("C:", [100, 100, 0, 99.5f]);
        var counterD = new FakePerformanceCounter("D:", [100, 99.9f, 88.8f, 66.6f]);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, CounterName, "C:")).Returns(counterC);
        performanceCounterFactory.Setup(x => x.Create(CategoryName, CounterName, "D:")).Returns(counterD);

        // Initialize the counters
        perfCounters.InitializeDiskCounters();
        Assert.Equal(2, perfCounters.TotalSeconds.Count);
        Assert.Equal(0, perfCounters.TotalSeconds["C:"]);
        Assert.Equal(0, perfCounters.TotalSeconds["D:"]);

        // Simulate the first tick
        perfCounters.UpdateDiskCounters();
        Assert.Equal(0, perfCounters.TotalSeconds["C:"], precision: 2); // (100-100)% * 60 = 0
        Assert.Equal(0.06, perfCounters.TotalSeconds["D:"], precision: 2); // (100-99.9)% * 60 = 0.06

        // Simulate the second tick
        perfCounters.UpdateDiskCounters();
        Assert.Equal(60, perfCounters.TotalSeconds["C:"], precision: 2); // 0 + (100-0)% * 60 = 60
        Assert.Equal(6.78, perfCounters.TotalSeconds["D:"], precision: 2); // 0.06 + (100-88.8)% * 60 = 6.78

        // Simulate the third tick
        perfCounters.UpdateDiskCounters();
        Assert.Equal(60.3, perfCounters.TotalSeconds["C:"], precision: 2); // 60 + (100-99.5)% * 60 = 60.3
        Assert.Equal(26.82, perfCounters.TotalSeconds["D:"], precision: 2); // 6.78 + (100-66.6)% * 60 = 6.78 + 20.04 = 26.82
    }
}
