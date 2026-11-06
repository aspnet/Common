// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

/// <summary>
/// Measures the incremental cost of evaluating bottom-K EventId sampling rules.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class BottomKEventIdPolicyBench
{
    private const int AdaptiveCapacity = 128;

    private ServiceProvider _withoutEventIdPolicyServices = null!;
    private ServiceProvider _withEventIdPolicyServices = null!;
    private ILogger[] _withoutEventIdPolicyLoggers = null!;
    private ILogger[] _withEventIdPolicyLoggers = null!;
    private LogBuffer _withoutEventIdPolicyBuffer = null!;
    private LogBuffer _withEventIdPolicyBuffer = null!;

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _withoutEventIdPolicyServices = CreateServices(retainProtectedEventIds: false);
        _withEventIdPolicyServices = CreateServices(retainProtectedEventIds: true);
        _withoutEventIdPolicyLoggers = CreateLoggers(_withoutEventIdPolicyServices);
        _withEventIdPolicyLoggers = CreateLoggers(_withEventIdPolicyServices);
        _withoutEventIdPolicyBuffer = _withoutEventIdPolicyServices.GetRequiredService<LogBuffer>();
        _withEventIdPolicyBuffer = _withEventIdPolicyServices.GetRequiredService<LogBuffer>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _withEventIdPolicyServices.Dispose();
        _withoutEventIdPolicyServices.Dispose();
    }

    [IterationCleanup]
    public void FlushBuffers()
    {
        _withoutEventIdPolicyBuffer.Flush();
        _withEventIdPolicyBuffer.Flush();
    }

    [Benchmark(Baseline = true)]
    public void BottomKWithoutEventIdPolicy()
    {
        LoggingBenchmarkWorkload.LogBatch(_withoutEventIdPolicyLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKWithEventIdPolicyMiss()
    {
        LoggingBenchmarkWorkload.LogBatch(_withEventIdPolicyLoggers, RecordsPerMinute);
    }

    private static ServiceProvider CreateServices(bool retainProtectedEventIds)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());
            builder.AddBottomKLogSampling(options =>
            {
                options.Capacity = AdaptiveCapacity;
                options.PreserveCapacity = 0;
                options.FlushInterval = TimeSpan.FromDays(1);
                options.Rules.Clear();
                options.Rules.Add(new BottomKLogSamplingFilterRule(logLevel: LogLevel.Critical));

                if (retainProtectedEventIds)
                {
                    options.Rules.Add(new BottomKLogSamplingFilterRule(eventId: 10_001));
                    options.Rules.Add(new BottomKLogSamplingFilterRule(eventId: 10_002));
                }
            });
        });

        return services.BuildServiceProvider();
    }

    private static ILogger[] CreateLoggers(ServiceProvider services)
        => LoggingBenchmarkWorkload.CreateLoggers(services.GetRequiredService<ILoggerFactory>());
}
