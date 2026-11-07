// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Microsoft.Extensions.Diagnostics.HealthChecks;

internal static partial class Metric
{
    [Counter(
        "dotnet.health_check.status",
        Name = "dotnet.health_check.reports",
        Unit = "{report}",
        Description = "Number of times a health report reported the health status of an application.")]
    public static partial HealthCheckReportCounter CreateHealthCheckReportCounter(Meter meter);

    [Counter(
        "dotnet.health_check.name",
        "dotnet.health_check.status",
        Name = "dotnet.health_check.unhealthy_checks",
        Unit = "{unhealthy_check}",
        Description = "Number of times a health check reported the health status of an application as Degraded or Unhealthy.")]
    public static partial UnhealthyHealthCheckCounter CreateUnhealthyHealthCheckCounter(Meter meter);

    public static void RecordMetric(this HealthCheckReportCounter counterMetric, HealthStatus status)
        => counterMetric.Add(1, status.ToString());

    public static void RecordMetric(this UnhealthyHealthCheckCounter counterMetric, string name, HealthStatus status)
        => counterMetric.Add(1, name, status.ToString());
}
