// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Interop;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

/// <summary>
/// System Info Interop Tests.
/// </summary>
/// <remarks>These tests are added for coverage reasons, but the code doesn't have
/// the necessary environment predictability to really test it.</remarks>
public sealed class SystemInfoTests
{
    public SystemInfoTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    /// <summary>
    /// Get basic system info.
    /// </summary>
    [Fact]
    public void GetSystemInfo()
    {
        var sysInfo = new SystemInfo().GetSystemInfo();
        Assert.True(sysInfo.NumberOfProcessors > 0);
    }
}
