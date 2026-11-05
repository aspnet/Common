// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// A snapshot of CPU and memory usage taken periodically over time.
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Comparing instances is not an expected scenario")]
[Obsolete(DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiMessage,
    DiagnosticId = DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiDiagId,
    UrlFormat = DiagnosticIds.UrlFormat)]
public readonly struct Snapshot
{
    /// <summary>
    /// Gets the total CPU time that has elapsed since startup.
    /// </summary>
    public TimeSpan TotalTimeSinceStart { get; }

    /// <summary>
    /// Gets the amount of kernel time that has elapsed since startup.
    /// </summary>
    public TimeSpan KernelTimeSinceStart { get; }

    /// <summary>
    /// Gets the amount of user time that has elapsed since startup.
    /// </summary>
    public TimeSpan UserTimeSinceStart { get; }

    /// <summary>
    /// Gets the memory usage within the system in bytes.
    /// </summary>
    public ulong MemoryUsageInBytes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Snapshot"/> struct.
    /// </summary>
    /// <param name="totalTimeSinceStart">The time at which the snapshot was taken.</param>
    /// <param name="kernelTimeSinceStart">The amount of kernel time that has elapsed since startup.</param>
    /// <param name="userTimeSinceStart">The amount of user time that has elapsed since startup.</param>
    /// <param name="memoryUsageInBytes">The memory usage within the system in bytes.</param>
    public Snapshot(
        TimeSpan totalTimeSinceStart,
        TimeSpan kernelTimeSinceStart,
        TimeSpan userTimeSinceStart,
        ulong memoryUsageInBytes)
    {
        _ = Throw.IfLessThan(memoryUsageInBytes, 0);
        _ = Throw.IfLessThan(kernelTimeSinceStart.Ticks, 0);
        _ = Throw.IfLessThan(userTimeSinceStart.Ticks, 0);

        TotalTimeSinceStart = totalTimeSinceStart;
        KernelTimeSinceStart = kernelTimeSinceStart;
        UserTimeSinceStart = userTimeSinceStart;
        MemoryUsageInBytes = memoryUsageInBytes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Snapshot"/> struct.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="kernelTimeSinceStart">The amount of kernel time that has elapsed since startup.</param>
    /// <param name="userTimeSinceStart">The amount of user time that has elapsed since startup.</param>
    /// <param name="memoryUsageInBytes">The memory usage within the system in bytes.</param>
    /// <remarks>This is a internal constructor to be used in unit tests only.</remarks>
    internal Snapshot(
        TimeProvider timeProvider,
        TimeSpan kernelTimeSinceStart,
        TimeSpan userTimeSinceStart,
        ulong memoryUsageInBytes)
    {
        _ = Throw.IfLessThan(memoryUsageInBytes, 0);
        _ = Throw.IfLessThan(kernelTimeSinceStart.Ticks, 0);
        _ = Throw.IfLessThan(userTimeSinceStart.Ticks, 0);

        TotalTimeSinceStart = TimeSpan.FromTicks(timeProvider.GetUtcNow().Ticks);
        KernelTimeSinceStart = kernelTimeSinceStart;
        UserTimeSinceStart = userTimeSinceStart;
        MemoryUsageInBytes = memoryUsageInBytes;
    }
}
