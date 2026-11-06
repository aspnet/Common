// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

internal static class SerializedExporterVolumeMeasurement
{
    public static void Run()
    {
        Console.WriteLine("| Strategy | Input | Emitted | Retention | UTF-8 bytes | Bytes/record |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");

        foreach (int recordCount in new[] { 10_000, 20_000 })
        {
            Measure(strategy: null, recordCount);
            foreach (SerializedExporterImpactBench.ExportStrategy strategy
                in Enum.GetValues<SerializedExporterImpactBench.ExportStrategy>())
            {
                Measure(strategy, recordCount);
            }
        }
    }

    private static void Measure(SerializedExporterImpactBench.ExportStrategy? strategy, int recordCount)
    {
        var metrics = new SerializedExporterMetrics();
        using ServiceProvider services =
            SerializedExporterImpactBench.CreateServices(strategy, recordCount, metrics);
        ILogger[] loggers = LoggingBenchmarkWorkload.CreateLoggers(
            services.GetRequiredService<ILoggerFactory>());
        using Activity? activity = StartActivity(strategy);

        LoggingBenchmarkWorkload.LogBatch(loggers, recordCount);
        services.GetService<LogBuffer>()?.Flush();

        double retention = (double)metrics.RecordsEmitted / recordCount;
        double bytesPerRecord = metrics.RecordsEmitted == 0
            ? 0
            : (double)metrics.BytesEmitted / metrics.RecordsEmitted;

        Console.WriteLine(
            $"| {strategy?.ToString() ?? "NoSampling"} | {recordCount:N0} | " +
            $"{metrics.RecordsEmitted:N0} | {retention:P2} | {metrics.BytesEmitted:N0} | " +
            $"{bytesPerRecord:N1} |");
    }

    private static Activity? StartActivity(SerializedExporterImpactBench.ExportStrategy? strategy)
    {
        if (strategy is not (
            SerializedExporterImpactBench.ExportStrategy.TraceRetain or
            SerializedExporterImpactBench.ExportStrategy.TraceDrop))
        {
            return null;
        }

        var activity = new Activity("SerializedExporterVolume")
        {
            ActivityTraceFlags = strategy == SerializedExporterImpactBench.ExportStrategy.TraceRetain
                ? ActivityTraceFlags.Recorded
                : ActivityTraceFlags.None
        };
        activity.Start();
        return activity;
    }
}
