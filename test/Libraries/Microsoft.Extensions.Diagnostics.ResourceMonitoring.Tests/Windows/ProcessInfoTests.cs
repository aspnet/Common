// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Interop;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

/// <summary>
/// Process Info Interop Tests.
/// </summary>
/// <remarks>These tests are added for coverage reasons, but the code doesn't have
/// the necessary environment predictability to really test it.</remarks>
public sealed class ProcessInfoTests
{
    [Fact]
    public void GetCurrentProcessMemoryUsage()
    {
        var workingSet64 = new ProcessInfo().GetCurrentProcessMemoryUsage();
        Assert.True(workingSet64 > 0);
    }
}
