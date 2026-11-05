// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// Represents resource quota information for a process or container, including CPU and memory constraints,
/// that are used as denominators in CPU and memory utilization calculations.
/// Maximum values define the upper limits of resource usage, while baseline values specify 
/// the minimum assured resource allocations, usually based on Kubernetes requests or Linux shares and weights distribution.
/// </summary>
/// <remarks>
/// Max values will be emitted by limit metrics, and baseline values will be emmited by request metrics.
/// </remarks>
public sealed class ResourceQuota
{
    /// <summary>
    /// Gets or sets the maximum memory that can be used in bytes.
    /// </summary>
    public ulong MaxMemoryInBytes { get; set; }

    /// <summary>
    /// Gets or sets the maximum CPU that can be used in cores.
    /// </summary>
    public double MaxCpuInCores { get; set; }

    /// <summary>
    /// Gets or sets the baseline memory allocation in bytes.
    /// </summary>
    public ulong BaselineMemoryInBytes { get; set; }

    /// <summary>
    /// Gets or sets the baseline CPU allocation in cores.
    /// </summary>
    public double BaselineCpuInCores { get; set; }
}
