// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

[SupportedOSPlatform("windows")]
public class PerformanceCounterFactoryTests
{
    public PerformanceCounterFactoryTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    [Fact]
    public void GetInstanceNameTest()
    {
        var performanceCounterFactory = new PerformanceCounterFactory();
        IPerformanceCounter performanceCounter = performanceCounterFactory.Create("Processor", "% Processor Time", "_Total");

        Assert.IsType<PerformanceCounterWrapper>(performanceCounter);
        Assert.Equal("_Total", performanceCounter.InstanceName);
    }
}
