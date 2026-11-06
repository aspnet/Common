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
public class BottomKImpactBench
{
    private const int AdaptiveCapacity = 128;

    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _disabledServices = null!;
    private ServiceProvider _retainAllPolicyServices = null!;
    private ServiceProvider _retainAllServices = null!;
    private ServiceProvider _adaptiveServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _disabledLoggers = null!;
    private ILogger[] _retainAllPolicyLoggers = null!;
    private ILogger[] _retainAllLoggers = null!;
    private ILogger[] _adaptiveLoggers = null!;
    private LogBuffer _retainAllBuffer = null!;
    private LogBuffer _adaptiveBuffer = null!;

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices();
        _disabledServices = CreateServices(options =>
        {
            options.Enabled = false;
            options.Rules.Add(new BottomKLogSamplingFilterRule());
        });
        _retainAllPolicyServices = CreateServices(options =>
            options.Rules.Add(new BottomKLogSamplingFilterRule(categoryName: "Does.Not.Match")));
        _retainAllServices = CreateServices(RecordsPerMinute / LoggingBenchmarkWorkload.CategoryCount);
        _adaptiveServices = CreateServices(AdaptiveCapacity);

        _baselineLoggers = CreateLoggers(_baselineServices);
        _disabledLoggers = CreateLoggers(_disabledServices);
        _retainAllPolicyLoggers = CreateLoggers(_retainAllPolicyServices);
        _retainAllLoggers = CreateLoggers(_retainAllServices);
        _adaptiveLoggers = CreateLoggers(_adaptiveServices);
        _retainAllBuffer = _retainAllServices.GetRequiredService<LogBuffer>();
        _adaptiveBuffer = _adaptiveServices.GetRequiredService<LogBuffer>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _adaptiveServices.Dispose();
        _retainAllServices.Dispose();
        _retainAllPolicyServices.Dispose();
        _disabledServices.Dispose();
        _baselineServices.Dispose();
    }

    [IterationCleanup(Target = nameof(BottomKRetainAll))]
    public void FlushRetainAllBuffer() => _retainAllBuffer.Flush();

    [IterationCleanup(Target = nameof(BottomKAdaptive))]
    public void FlushAdaptiveBuffer() => _adaptiveBuffer.Flush();

    [Benchmark(Baseline = true)]
    public void NoSampling()
    {
        LoggingBenchmarkWorkload.LogBatch(_baselineLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKDisabled()
    {
        LoggingBenchmarkWorkload.LogBatch(_disabledLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKRetainAllPolicy()
    {
        LoggingBenchmarkWorkload.LogBatch(_retainAllPolicyLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKRetainAll()
    {
        LoggingBenchmarkWorkload.LogBatch(_retainAllLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKRetainAllAndFlush()
    {
        LoggingBenchmarkWorkload.LogBatch(_retainAllLoggers, RecordsPerMinute);
        _retainAllBuffer.Flush();
    }

    [Benchmark]
    public void BottomKAdaptive()
    {
        LoggingBenchmarkWorkload.LogBatch(_adaptiveLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKAdaptiveAndFlush()
    {
        LoggingBenchmarkWorkload.LogBatch(_adaptiveLoggers, RecordsPerMinute);
        _adaptiveBuffer.Flush();
    }

    private static ServiceProvider CreateServices(int? capacity = null)
    {
        if (!capacity.HasValue)
        {
            return CreateServices((Action<BottomKLogSamplingOptions>?)null);
        }

        return CreateServices(options =>
        {
            options.Capacity = capacity.Value;
            options.PreserveCapacity = 0;
            options.FlushInterval = TimeSpan.FromDays(1);
            options.Rules.Add(new BottomKLogSamplingFilterRule());
        });
    }

    private static ServiceProvider CreateServices(Action<BottomKLogSamplingOptions>? configure)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());

            if (configure is not null)
            {
                builder.AddBottomKLogSampling(configure);
            }
        });

        return services.BuildServiceProvider();
    }

    private static ILogger[] CreateLoggers(ServiceProvider services)
        => LoggingBenchmarkWorkload.CreateLoggers(services.GetRequiredService<ILoggerFactory>());
}
