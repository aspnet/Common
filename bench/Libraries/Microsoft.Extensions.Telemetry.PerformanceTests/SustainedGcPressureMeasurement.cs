// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

internal static class SustainedGcPressureMeasurement
{
    private const int CategoryCount = 4;
    private const int DefaultLogsPerMinute = 10_000;
    private const int WarmupRecordCount = 1_000;
    private static readonly TimeSpan _defaultDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _defaultFlushInterval = TimeSpan.FromSeconds(30);

    private enum Strategy
    {
        RandomOnePercent,
        BottomKOnePercent
    }

    public static void Run()
        => Run(DefaultLogsPerMinute, _defaultFlushInterval);

    public static void Run(string logsPerMinuteValue)
        => Run(logsPerMinuteValue, _defaultFlushInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture));

    public static void Run(string logsPerMinuteValue, string flushIntervalSecondsValue)
    {
        if (!int.TryParse(
                logsPerMinuteValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int logsPerMinute) ||
            logsPerMinute <= 0)
        {
#pragma warning disable LA0001 // Command-line argument validation is outside the measured path.
            throw new ArgumentException("Logs per minute must be a positive integer.", nameof(logsPerMinuteValue));
#pragma warning restore LA0001
        }

        if (!double.TryParse(
                flushIntervalSecondsValue,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double flushIntervalSeconds) ||
            flushIntervalSeconds <= 0)
        {
#pragma warning disable LA0001 // Command-line argument validation is outside the measured path.
            throw new ArgumentException(
                "Flush interval seconds must be positive.",
                nameof(flushIntervalSecondsValue));
#pragma warning restore LA0001
        }

        Run(logsPerMinute, TimeSpan.FromSeconds(flushIntervalSeconds));
    }

    private static void Run(int logsPerMinute, TimeSpan flushInterval)
    {
        Console.WriteLine(
            $"Sustained logging: {logsPerMinute:N0} logs/min, {_defaultDuration.TotalMinutes:N0} min, " +
            $"{flushInterval.TotalSeconds:N1} s bottom-K flush interval");
        Console.WriteLine();
        Console.WriteLine(
            "| Strategy | Input | Emitted | Retention | Export batches | Allocated | Gen0 | Gen1 | Gen2 | " +
            "GC pause | CPU time | CPU/wall | Peak managed | Retained managed |");
        Console.WriteLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (Strategy strategy in Enum.GetValues<Strategy>())
        {
            RunIsolatedWorker(strategy, _defaultDuration, logsPerMinute, flushInterval);
        }
    }

    public static void RunWorker(
        string strategyValue,
        string durationSecondsValue,
        string logsPerMinuteValue,
        string flushIntervalSecondsValue)
    {
        bool validStrategy = Enum.TryParse(strategyValue, out Strategy strategy);
        bool validDuration = double.TryParse(
            durationSecondsValue,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double durationSeconds);
        bool validRate = int.TryParse(
            logsPerMinuteValue,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int logsPerMinute);
        bool validFlushInterval = double.TryParse(
            flushIntervalSecondsValue,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double flushIntervalSeconds);

        if (!validStrategy || !validDuration || !validRate || !validFlushInterval)
        {
            ThrowInvalidWorkerArguments();
        }

        if (durationSeconds <= 0 || logsPerMinute <= 0 || flushIntervalSeconds <= 0)
        {
            ThrowInvalidWorkerArguments();
        }

        Result result = Measure(
            strategy,
            TimeSpan.FromSeconds(durationSeconds),
            logsPerMinute,
            TimeSpan.FromSeconds(flushIntervalSeconds));
        Console.WriteLine(
            $"| {strategy} | {result.InputRecords:N0} | {result.EmittedRecords:N0} | " +
            $"{result.Retention:P2} | {result.ExportBatches:N0} | {FormatBytes(result.AllocatedBytes)} | " +
            $"{result.Gen0Collections:N0} | {result.Gen1Collections:N0} | {result.Gen2Collections:N0} | " +
            $"{result.GcPause.TotalMilliseconds:N1} ms | {result.CpuTime.TotalMilliseconds:N0} ms | " +
            $"{result.CpuPercent:N1}% | {FormatBytes(result.PeakManagedBytes)} | " +
            $"{FormatBytes(result.RetainedManagedBytes)} |");
    }

    private static Result Measure(
        Strategy strategy,
        TimeSpan duration,
        int logsPerMinute,
        TimeSpan flushInterval)
    {
        var metrics = new SerializedExporterMetrics();
        using ServiceProvider services = CreateServices(strategy, metrics, logsPerMinute, flushInterval);
        ILogger[] loggers = LoggingBenchmarkWorkload.CreateLoggers(
            services.GetRequiredService<ILoggerFactory>(),
            CategoryCount);
        LogBuffer? buffer = services.GetService<LogBuffer>();

        LoggingBenchmarkWorkload.LogBatch(loggers, WarmupRecordCount);
        buffer?.Flush();
        metrics.Reset();
        ForceFullCollection();

        using Process process = Process.GetCurrentProcess();

        long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long peakManaged = managedBefore;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        TimeSpan pauseBefore = GC.GetTotalPauseDuration();
        TimeSpan cpuBefore = process.TotalProcessorTime;

        int targetRecordCount = checked((int)Math.Round(
            logsPerMinute * duration.TotalMinutes,
            MidpointRounding.AwayFromZero));
        int inputRecords = 0;
        var stopwatch = Stopwatch.StartNew();
        TimeSpan nextMemoryObservation = TimeSpan.Zero;

        while (stopwatch.Elapsed < duration)
        {
            int expectedRecords = Math.Min(
                targetRecordCount,
                (int)(logsPerMinute * stopwatch.Elapsed.TotalMinutes));

            if (expectedRecords > inputRecords)
            {
                LoggingBenchmarkWorkload.LogInterleaved(
                    loggers,
                    inputRecords,
                    recordStride: 1,
                    expectedRecords);
                inputRecords = expectedRecords;
            }

            if (stopwatch.Elapsed >= nextMemoryObservation)
            {
                ObserveMemory();
                nextMemoryObservation += TimeSpan.FromSeconds(1);
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }

        if (inputRecords < targetRecordCount)
        {
            LoggingBenchmarkWorkload.LogInterleaved(
                loggers,
                inputRecords,
                recordStride: 1,
                targetRecordCount);
            inputRecords = targetRecordCount;
        }

        buffer?.Flush();
        ObserveMemory();
        stopwatch.Stop();

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        int gen0Collections = GC.CollectionCount(0) - gen0Before;
        int gen1Collections = GC.CollectionCount(1) - gen1Before;
        int gen2Collections = GC.CollectionCount(2) - gen2Before;
        TimeSpan gcPause = GC.GetTotalPauseDuration() - pauseBefore;
        TimeSpan cpuTime = process.TotalProcessorTime - cpuBefore;
        double cpuPercent = cpuTime.TotalMilliseconds / stopwatch.Elapsed.TotalMilliseconds * 100;

        ForceFullCollection();
        long retainedManaged = GC.GetTotalMemory(forceFullCollection: false) - managedBefore;

        return new Result(
            inputRecords,
            metrics.RecordsEmitted,
            metrics.BatchesEmitted,
            allocatedBytes,
            gen0Collections,
            gen1Collections,
            gen2Collections,
            gcPause,
            cpuTime,
            cpuPercent,
            peakManaged - managedBefore,
            retainedManaged);

        void ObserveMemory()
            => peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(forceFullCollection: false));
    }

    private static ServiceProvider CreateServices(
        Strategy strategy,
        SerializedExporterMetrics metrics,
        int logsPerMinute,
        TimeSpan flushInterval)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new SerializedExporterLoggerProvider(metrics));

            switch (strategy)
            {
                case Strategy.RandomOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;

                case Strategy.BottomKOnePercent:
                    builder.AddBottomKLogSampling(options =>
                    {
                        options.Capacity = Math.Max(
                            1,
                            (int)(logsPerMinute * flushInterval.TotalMinutes / 100 / CategoryCount));
                        options.PreserveCapacity = 0;
                        options.FlushInterval = flushInterval;
                        options.Rules.Add(new BottomKLogSamplingFilterRule());
                    });
                    break;
            }
        });

        return services.BuildServiceProvider();
    }

    private static void ForceFullCollection()
    {
#pragma warning disable S1215 // Full collections establish comparable worker baselines.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#pragma warning restore S1215
    }

    private static void ThrowInvalidWorkerArguments()
    {
#pragma warning disable LA0001 // Worker argument validation is outside the measured path.
        throw new ArgumentException("Invalid sustained-GC worker arguments.");
#pragma warning restore LA0001
    }

    private static void RunIsolatedWorker(
        Strategy strategy,
        TimeSpan duration,
        int logsPerMinute,
        TimeSpan flushInterval)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current executable.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["DOTNET_TieredCompilation"] = "0";

        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(
                Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Unable to locate the entry assembly."));
        }

        startInfo.ArgumentList.Add("--sustained-gc-worker");
        startInfo.ArgumentList.Add(strategy.ToString());
        startInfo.ArgumentList.Add(duration.TotalSeconds.ToString(CultureInfo.InvariantCulture));
#pragma warning disable LA0002 // Worker process setup is outside the measured path.
        startInfo.ArgumentList.Add(logsPerMinute.ToString(CultureInfo.InvariantCulture));
#pragma warning restore LA0002
        startInfo.ArgumentList.Add(flushInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the sustained-GC worker.");
        string output = worker.StandardOutput.ReadToEnd();
        string error = worker.StandardError.ReadToEnd();
        worker.WaitForExit();

        if (worker.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Sustained-GC worker failed with exit code {worker.ExitCode}: {error}");
        }

        Console.Write(output);
    }

    private static string FormatBytes(long bytes)
    {
        const double BytesPerMiB = 1024 * 1024;
        return $"{bytes / BytesPerMiB:N2} MiB";
    }

    private readonly record struct Result(
        int InputRecords,
        long EmittedRecords,
        long ExportBatches,
        long AllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        TimeSpan GcPause,
        TimeSpan CpuTime,
        double CpuPercent,
        long PeakManagedBytes,
        long RetainedManagedBytes)
    {
        public double Retention => (double)EmittedRecords / InputRecords;
    }
}
