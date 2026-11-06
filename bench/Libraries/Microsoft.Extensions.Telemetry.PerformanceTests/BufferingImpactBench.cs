// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

[MemoryDiagnoser]
[InvocationCount(1)]
public class BufferingImpactBench
{
    private ServiceProvider _baselineServices = null!;
    private ServiceProvider _bufferedServices = null!;
    private ILogger[] _baselineLoggers = null!;
    private ILogger[] _bufferedLoggers = null!;
    private GlobalLogBuffer _buffer = null!;

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineServices = CreateServices(bufferingEnabled: false);
        _bufferedServices = CreateServices(bufferingEnabled: true);
        _baselineLoggers = LoggingBenchmarkWorkload.CreateLoggers(_baselineServices.GetRequiredService<ILoggerFactory>());
        _bufferedLoggers = LoggingBenchmarkWorkload.CreateLoggers(_bufferedServices.GetRequiredService<ILoggerFactory>());
        _buffer = _bufferedServices.GetRequiredService<GlobalLogBuffer>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _bufferedServices.Dispose();
        _baselineServices.Dispose();
    }

    [IterationCleanup]
    public void FlushBuffer()
    {
        _buffer.Flush();
    }

    [Benchmark(Baseline = true)]
    public void NoBuffering()
    {
        LoggingBenchmarkWorkload.LogBatch(_baselineLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BufferOnly()
    {
        LoggingBenchmarkWorkload.LogBatch(_bufferedLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BufferAndFlush()
    {
        LoggingBenchmarkWorkload.LogBatch(_bufferedLoggers, RecordsPerMinute);
        _buffer.Flush();
    }

    private static ServiceProvider CreateServices(bool bufferingEnabled)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new BenchLoggerProvider());

            if (bufferingEnabled)
            {
                builder.AddGlobalBuffer(options =>
                {
                    options.AutoFlushDuration = TimeSpan.Zero;
                    options.MaxBufferSizeInBytes = 512 * 1024 * 1024;
                    options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Information));
                });
            }
        });

        return services.BuildServiceProvider();
    }
}
