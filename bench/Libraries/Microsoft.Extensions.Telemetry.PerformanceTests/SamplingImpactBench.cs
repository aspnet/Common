// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

[MemoryDiagnoser]
[InvocationCount(LoggingBenchmarkWorkload.SamplingInvocationsPerIteration)]
public class SamplingImpactBench
{
    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _sampledServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _sampledLoggers = null!;
    private Activity? _activity;

    public enum SamplingScenario
    {
        RandomSampleAll,
        RandomSampleOnePercent,
        RandomDropAll,
        RandomByCategory,
        TraceSample,
        TraceDrop
    }

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [Params(
        SamplingScenario.RandomSampleAll,
        SamplingScenario.RandomSampleOnePercent,
        SamplingScenario.RandomDropAll,
        SamplingScenario.RandomByCategory,
        SamplingScenario.TraceSample,
        SamplingScenario.TraceDrop)]
    public SamplingScenario Scenario { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices();
        _sampledServices = CreateServices(Scenario);
        _baselineLoggers = LoggingBenchmarkWorkload.CreateLoggers(_baselineServices.GetRequiredService<ILoggerFactory>());
        _sampledLoggers = LoggingBenchmarkWorkload.CreateLoggers(_sampledServices.GetRequiredService<ILoggerFactory>());

        if (Scenario is SamplingScenario.TraceSample or SamplingScenario.TraceDrop)
        {
            _activity = new Activity("SamplingBenchmark")
            {
                ActivityTraceFlags = Scenario == SamplingScenario.TraceSample
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
        _sampledServices.Dispose();
        _baselineServices.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void NoSampling()
    {
        LoggingBenchmarkWorkload.LogBatch(_baselineLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void WithSampling()
    {
        LoggingBenchmarkWorkload.LogBatch(_sampledLoggers, RecordsPerMinute);
    }

    private static ServiceProvider CreateServices(SamplingScenario? scenario = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());

            switch (scenario)
            {
                case SamplingScenario.RandomSampleAll:
                    builder.AddRandomProbabilisticSampler(1.0);
                    break;
                case SamplingScenario.RandomSampleOnePercent:
                    builder.AddRandomProbabilisticSampler(0.01);
                    break;
                case SamplingScenario.RandomDropAll:
                    builder.AddRandomProbabilisticSampler(0.0);
                    break;
                case SamplingScenario.RandomByCategory:
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
                case SamplingScenario.TraceSample:
                case SamplingScenario.TraceDrop:
                    builder.AddTraceBasedSampler();
                    break;
            }
        });

        return services.BuildServiceProvider();
    }
}
