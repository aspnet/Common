// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

[MemoryDiagnoser]
[InvocationCount(1)]
public class SerializedExporterImpactBench
{
    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _strategyServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _strategyLoggers = null!;
    private LogBuffer? _strategyBuffer;
    private Activity? _activity;

    public enum ExportStrategy
    {
        RandomOnePercent,
        RandomByCategory,
        TraceRetain,
        TraceDrop,
        BottomKOnePercent
    }

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [Params(
        ExportStrategy.RandomOnePercent,
        ExportStrategy.RandomByCategory,
        ExportStrategy.TraceRetain,
        ExportStrategy.TraceDrop,
        ExportStrategy.BottomKOnePercent)]
    public ExportStrategy Strategy { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices();
        _strategyServices = CreateServices(Strategy, RecordsPerMinute);
        _baselineLoggers = CreateLoggers(_baselineServices);
        _strategyLoggers = CreateLoggers(_strategyServices);
        _strategyBuffer = _strategyServices.GetService<LogBuffer>();

        if (Strategy is ExportStrategy.TraceRetain or ExportStrategy.TraceDrop)
        {
            _activity = new Activity("SerializedExporterBenchmark")
            {
                ActivityTraceFlags = Strategy == ExportStrategy.TraceRetain
                    ? ActivityTraceFlags.Recorded
                    : ActivityTraceFlags.None
            };
            _activity.Start();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _activity?.Stop();
        _strategyServices.Dispose();
        _baselineServices.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void NoSampling()
    {
        LoggingBenchmarkWorkload.LogBatch(_baselineLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void WithStrategy()
    {
        LoggingBenchmarkWorkload.LogBatch(_strategyLoggers, RecordsPerMinute);
        _strategyBuffer?.Flush();
    }

    internal static ServiceProvider CreateServices(
        ExportStrategy? strategy = null,
        int recordCount = 0,
        SerializedExporterMetrics? metrics = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new SerializedExporterLoggerProvider(metrics));

            switch (strategy)
            {
                case ExportStrategy.RandomOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;

                case ExportStrategy.RandomByCategory:
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

                case ExportStrategy.TraceRetain:
                case ExportStrategy.TraceDrop:
                    builder.AddTraceBasedSampler();
                    break;

                case ExportStrategy.BottomKOnePercent:
                    builder.AddBottomKLogSampling(options =>
                    {
                        options.Capacity = Math.Max(
                            1,
                            recordCount / 100 / LoggingBenchmarkWorkload.CategoryCount);
                        options.PreserveCapacity = 0;
                        options.FlushInterval = TimeSpan.FromDays(1);
                        options.Rules.Add(new BottomKLogSamplingFilterRule());
                    });
                    break;
            }
        });

        return services.BuildServiceProvider();
    }

    private static ILogger[] CreateLoggers(ServiceProvider services)
        => LoggingBenchmarkWorkload.CreateLoggers(services.GetRequiredService<ILoggerFactory>());
}
