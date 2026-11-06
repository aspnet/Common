// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

[MemoryDiagnoser]
[InvocationCount(1)]
public class MultithreadedSamplingImpactBench
{
    private const int CategoryCount = 100;
    private const int RecordsPerMinute = 20_000;

    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _strategyServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _strategyLoggers = null!;
    private LogBuffer? _strategyBuffer;
    private ParallelOptions _parallelOptions = null!;

    public enum SamplingStrategy
    {
        RandomOnePercent,
        BottomKOnePercent
    }

    [Params(1, 4, 8)]
    public int WorkerCount { get; set; }

    [Params(SamplingStrategy.RandomOnePercent, SamplingStrategy.BottomKOnePercent)]
    public SamplingStrategy Strategy { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices();
        _strategyServices = CreateServices(Strategy);
        _baselineLoggers = CreateLoggers(_baselineServices);
        _strategyLoggers = CreateLoggers(_strategyServices);
        _strategyBuffer = _strategyServices.GetService<LogBuffer>();
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = WorkerCount };
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
        LogConcurrently(_baselineLoggers);
    }

    [Benchmark]
    public void WithStrategy()
    {
        LogConcurrently(_strategyLoggers);
        _strategyBuffer?.Flush();
    }

    private static ServiceProvider CreateServices(SamplingStrategy? strategy = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());

            switch (strategy)
            {
                case SamplingStrategy.RandomOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;

                case SamplingStrategy.BottomKOnePercent:
                    builder.AddBottomKLogSampling(options =>
                    {
                        options.Capacity = RecordsPerMinute / 100 / CategoryCount;
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
        => LoggingBenchmarkWorkload.CreateLoggers(
            services.GetRequiredService<ILoggerFactory>(),
            CategoryCount);

    private void LogConcurrently(ILogger[] loggers)
    {
        _ = Parallel.For(
            0,
            WorkerCount,
            _parallelOptions,
            workerIndex => LoggingBenchmarkWorkload.LogInterleaved(
                loggers,
                workerIndex,
                WorkerCount,
                RecordsPerMinute));
    }
}
