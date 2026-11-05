// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.AI;

/// <summary>Shared metric instrument factories for the OpenTelemetry* clients.</summary>
internal static class OtelMetricHelpers
{
    /// <summary>Creates the standard <c>gen_ai.client.token.usage</c> histogram on <paramref name="meter"/>.</summary>
    public static Histogram<int> CreateGenAITokenUsageHistogram(Meter meter) =>
        meter.CreateHistogram<int>(
            OpenTelemetryConsts.GenAI.Client.TokenUsage.Name,
            OpenTelemetryConsts.TokensUnit,
            OpenTelemetryConsts.GenAI.Client.TokenUsage.Description,
            advice: new() { HistogramBucketBoundaries = OpenTelemetryConsts.GenAI.Client.TokenUsage.ExplicitBucketBoundaries });

    /// <summary>Creates the standard <c>gen_ai.client.operation.duration</c> histogram on <paramref name="meter"/>.</summary>
    public static Histogram<double> CreateGenAIOperationDurationHistogram(Meter meter) =>
        meter.CreateHistogram<double>(
            OpenTelemetryConsts.GenAI.Client.OperationDuration.Name,
            OpenTelemetryConsts.SecondsUnit,
            OpenTelemetryConsts.GenAI.Client.OperationDuration.Description,
            advice: new() { HistogramBucketBoundaries = OpenTelemetryConsts.GenAI.Client.OperationDuration.ExplicitBucketBoundaries });
}
