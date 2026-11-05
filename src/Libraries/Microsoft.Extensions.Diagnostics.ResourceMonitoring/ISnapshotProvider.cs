// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

/// <summary>
/// An interface to be implemented by a provider that represents an underlying system and gets resources data about it.
/// </summary>
[Obsolete(DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiMessage,
    DiagnosticId = DiagnosticIds.Obsoletions.NonObservableResourceMonitoringApiDiagId,
    UrlFormat = DiagnosticIds.UrlFormat)]
public interface ISnapshotProvider
{
    /// <summary>
    /// Gets the static values of CPU and memory limitations defined by the system.
    /// </summary>
    SystemResources Resources { get; }

    /// <summary>
    /// Get a snapshot of the resource utilization of the system.
    /// </summary>
    /// <returns>An appropriate sample.</returns>
    Snapshot GetSnapshot();
}
