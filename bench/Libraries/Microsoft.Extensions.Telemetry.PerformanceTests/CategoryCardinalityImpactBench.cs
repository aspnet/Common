// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

[MemoryDiagnoser]
[InvocationCount(1)]
public class CategoryCardinalityImpactBench
{
    private const int RecordsPerMinute = 20_000;

    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _strategyServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _strategyLoggers = null!;
    private LogBuffer? _strategyBuffer;

    public enum SamplingStrategy
    {
        RandomOnePercent,
        BottomKOnePercent
    }

    [Params(50, 100, 200)]
    public int CategoryCount { get; set; }

    [Params(SamplingStrategy.RandomOnePercent, SamplingStrategy.BottomKOnePercent)]
    public SamplingStrategy Strategy { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices();
        _strategyServices = CreateServices(Strategy, CategoryCount);
        _baselineLoggers = CreateLoggers(_baselineServices);
        _strategyLoggers = CreateLoggers(_strategyServices);
        _strategyBuffer = _strategyServices.GetService<LogBuffer>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
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

    private static ServiceProvider CreateServices(
        SamplingStrategy? strategy = null,
        int categoryCount = 1)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new SerializedExporterLoggerProvider());

            switch (strategy)
            {
                case SamplingStrategy.RandomOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;

                case SamplingStrategy.BottomKOnePercent:
                    builder.AddBottomKLogSampling(options =>
                    {
                        options.Capacity = RecordsPerMinute / 100 / categoryCount;
                        options.PreserveCapacity = 0;
                        options.FlushInterval = TimeSpan.FromDays(1);
                        options.Rules.Add(new BottomKLogSamplingFilterRule());
                    });
                    break;
            }
        });

        return services.BuildServiceProvider();
    }

    private ILogger[] CreateLoggers(ServiceProvider services)
        => LoggingBenchmarkWorkload.CreateLoggers(
            services.GetRequiredService<ILoggerFactory>(),
            CategoryCount);
}
