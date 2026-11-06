// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

internal static class RetainedMemoryMeasurement
{
    private enum Pipeline
    {
        NoSampling,
        RandomOnePercent,
        RandomByCategory,
        GlobalBuffer,
        BottomKAdaptive,
        BottomKRetainAll
    }

    public static void Run()
    {
        Console.WriteLine("| Pipeline | Records | Retained managed | Peak managed | Peak working set | Peak private bytes |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");

        foreach (int recordCount in new[] { 10_000, 20_000 })
        {
            foreach (Pipeline pipeline in Enum.GetValues<Pipeline>())
            {
                RunIsolatedWorker(pipeline, recordCount);
            }
        }
    }

    public static void RunWorker(string pipelineValue, string recordCountValue)
    {
        if (!Enum.TryParse(pipelineValue, out Pipeline pipeline) ||
            !int.TryParse(recordCountValue, NumberStyles.None, CultureInfo.InvariantCulture, out int recordCount))
        {
#pragma warning disable LA0001 // Worker argument validation is outside the measured path.
            throw new ArgumentException("Invalid retained-memory worker arguments.");
#pragma warning restore LA0001
        }

        Result result = Measure(pipeline, recordCount);
        Console.WriteLine(
            $"| {pipeline} | {recordCount:N0} | {FormatBytes(result.RetainedManagedBytes)} | " +
            $"{FormatBytes(result.PeakManagedBytes)} | {FormatBytes(result.PeakWorkingSetBytes)} | " +
            $"{FormatBytes(result.PeakPrivateBytes)} |");
    }

    private static Result Measure(Pipeline pipeline, int recordCount)
    {
        ForceFullCollection();

        using ServiceProvider services = CreateServices(pipeline, recordCount);
        ILogger[] loggers = LoggingBenchmarkWorkload.CreateLoggers(
            services.GetRequiredService<ILoggerFactory>());

        ForceFullCollection();

        using Process process = Process.GetCurrentProcess();
        process.Refresh();

        long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetBefore = process.WorkingSet64;
        long privateBytesBefore = process.PrivateMemorySize64;
        long peakManaged = managedBefore;
        long peakWorkingSet = workingSetBefore;
        long peakPrivateBytes = privateBytesBefore;

        LoggingBenchmarkWorkload.LogBatchWithObserver(loggers, recordCount, ObserveMemory);

        ForceFullCollection();
        long retainedManaged = GC.GetTotalMemory(forceFullCollection: false) - managedBefore;

        return new Result(
            retainedManaged,
            peakManaged - managedBefore,
            peakWorkingSet - workingSetBefore,
            peakPrivateBytes - privateBytesBefore);

        void ObserveMemory()
        {
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(forceFullCollection: false));
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
        }
    }

    private static ServiceProvider CreateServices(Pipeline pipeline, int recordCount)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());

            switch (pipeline)
            {
                case Pipeline.RandomOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;

                case Pipeline.RandomByCategory:
                    builder.AddRandomProbabilisticSampler(options =>
                    {
                        options.Rules.Add(new RandomProbabilisticSamplerFilterRule(
                            0.01,
                            categoryName: $"{LoggingBenchmarkWorkload.HighVolumeCategoryPrefix}*"));
                        options.Rules.Add(new RandomProbabilisticSamplerFilterRule(
                            1.0,
                            categoryName: $"{LoggingBenchmarkWorkload.CriticalCategoryPrefix}*"));
                        options.Rules.Add(new RandomProbabilisticSamplerFilterRule(0.1));
                    });
                    break;

                case Pipeline.GlobalBuffer:
                    builder.AddGlobalBuffer(options =>
                    {
                        options.AutoFlushDuration = TimeSpan.Zero;
                        options.MaxBufferSizeInBytes = 512 * 1024 * 1024;
                        options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Information));
                    });
                    break;

                case Pipeline.BottomKAdaptive:
                    AddBottomK(builder, capacity: 128);
                    break;

                case Pipeline.BottomKRetainAll:
                    AddBottomK(
                        builder,
                        capacity: recordCount / LoggingBenchmarkWorkload.CategoryCount);
                    break;
            }
        });

        return services.BuildServiceProvider();
    }

    private static void AddBottomK(ILoggingBuilder builder, int capacity)
    {
        builder.AddBottomKLogSampling(options =>
        {
            options.Capacity = capacity;
            options.PreserveCapacity = 0;
            options.FlushInterval = TimeSpan.FromDays(1);
            options.Rules.Add(new BottomKLogSamplingFilterRule());
        });
    }

    private static void ForceFullCollection()
    {
#pragma warning disable S1215 // Full collections establish the live-memory baseline for this diagnostic.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#pragma warning restore S1215
    }

    private static void RunIsolatedWorker(Pipeline pipeline, int recordCount)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current executable.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(
                Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Unable to locate the entry assembly."));
        }

        startInfo.ArgumentList.Add("--retained-memory-worker");
        startInfo.ArgumentList.Add(pipeline.ToString());
#pragma warning disable LA0002 // Worker process setup is outside the measured path.
        startInfo.ArgumentList.Add(recordCount.ToString(CultureInfo.InvariantCulture));
#pragma warning restore LA0002

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the retained-memory worker.");
        string output = worker.StandardOutput.ReadToEnd();
        string error = worker.StandardError.ReadToEnd();
        worker.WaitForExit();

        if (worker.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Retained-memory worker failed with exit code {worker.ExitCode}: {error}");
        }

        Console.Write(output);
    }

    private static string FormatBytes(long bytes)
    {
        const double BytesPerMiB = 1024 * 1024;
        return $"{bytes / BytesPerMiB:N2} MiB";
    }

    private readonly record struct Result(
        long RetainedManagedBytes,
        long PeakManagedBytes,
        long PeakWorkingSetBytes,
        long PeakPrivateBytes);
}
