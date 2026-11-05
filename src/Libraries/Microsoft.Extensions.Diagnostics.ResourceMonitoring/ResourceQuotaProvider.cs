// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// Provides resource quota information for resource monitoring purposes.
/// </summary>
/// <remarks>
/// This abstract class defines a contract for retrieving resource quotas, which include
/// memory and CPU maximum and baseline allocations that may be imposed by container orchestrators,
/// resource management systems, or other runtime constraints.
/// </remarks>
#pragma warning disable S1694 // An abstract class should have both abstract and concrete methods. It's for better .NET Framework support, to have more flexible API if we add more methods here.
public abstract class ResourceQuotaProvider
#pragma warning restore S1694 // An abstract class should have both abstract and concrete methods
{
    /// <summary>
    /// Gets the current resource quota containing memory and CPU maximum and baseline allocations.
    /// The returned <see cref="ResourceQuota"/> is used in resource monitoring calculations.
    /// </summary>
    /// <returns>
    /// A <see cref="ResourceQuota"/> instance containing the current resource constraints
    /// including maximum memory, maximum CPU, baseline memory, and baseline CPU.
    /// </returns>
    public abstract ResourceQuota GetResourceQuota();
}
