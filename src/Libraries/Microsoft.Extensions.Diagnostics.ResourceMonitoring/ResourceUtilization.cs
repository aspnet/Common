// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// Captures resource usage at a given point in time.
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Comparing instances is not an expected scenario")]
[Obsolete(DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiMessage,
    DiagnosticId = DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiDiagId,
    UrlFormat = DiagnosticIds.UrlFormat)]
public readonly struct ResourceUtilization
{
    private const double Hundred = 100.0;

    /// <summary>
    /// Gets the CPU utilization percentage.
    /// </summary>
    /// <remarks>
    /// This percentage is calculated relative to the <see cref="SystemResources.GuaranteedCpuUnits"/>.
    /// </remarks>
    public double CpuUsedPercentage { get; }

    /// <summary>
    /// Gets the memory utilization percentage.
    /// </summary>
    /// <remarks>
    /// This percentage is calculated relative to the <see cref="SystemResources.GuaranteedMemoryInBytes"/>.
    /// </remarks>
    public double MemoryUsedPercentage { get; }

    /// <summary>
    /// Gets the total memory used.
    /// </summary>
    public ulong MemoryUsedInBytes { get; }

    /// <summary>
    /// Gets the CPU and memory limits defined by the underlying system.
    /// </summary>
    public SystemResources SystemResources { get; }

    /// <summary>
    /// Gets the latest snapshot of the resource utilization of the system.
    /// </summary>
#pragma warning disable LA0004 // Experimental member on an already-obsolete containing type is intentional
    [Experimental(diagnosticId: DiagnosticIds.Experiments.ResourceMonitoring, UrlFormat = DiagnosticIds.UrlFormat)]
    public Snapshot Snapshot { get; } = default;
#pragma warning restore LA0004

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUtilization"/> struct.
    /// </summary>
    /// <param name="cpuUsedPercentage">CPU utilization.</param>
    /// <param name="memoryUsedInBytes">Memory used in bytes (instantaneous).</param>
    /// <param name="systemResources">CPU and memory limits.</param>
    public ResourceUtilization(double cpuUsedPercentage, ulong memoryUsedInBytes, SystemResources systemResources)
    {
        double guaranteedCpuUnits = systemResources.GuaranteedCpuUnits;
        if (guaranteedCpuUnits <= 0)
        {
            guaranteedCpuUnits = 1;
        }

        CpuUsedPercentage = Math.Min(Hundred, Throw.IfLessThan(cpuUsedPercentage / guaranteedCpuUnits, 0.0));
        MemoryUsedInBytes = Throw.IfLessThan(memoryUsedInBytes, 0);
        SystemResources = systemResources;
        MemoryUsedPercentage = Math.Min(Hundred, (double)MemoryUsedInBytes / systemResources.GuaranteedMemoryInBytes * Hundred);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUtilization"/> struct.
    /// </summary>
    /// <param name="cpuUsedPercentage">CPU utilization.</param>
    /// <param name="memoryUsedInBytes">Memory used in bytes (instantaneous).</param>
    /// <param name="systemResources">CPU and memory limits.</param>
    /// <param name="snapShot">Latest ResourceUtilizationSnapshot.</param>
    internal ResourceUtilization(double cpuUsedPercentage, ulong memoryUsedInBytes, SystemResources systemResources, Snapshot snapShot)
        : this(cpuUsedPercentage, memoryUsedInBytes, systemResources)
    {
        Snapshot = snapShot;
    }
}
