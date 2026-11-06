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
/// Measures the incremental cost of evaluating bottom-K category sampling rules.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class BottomKCategoryPolicyBench
{
    private const int AdaptiveCapacity = 128;

    private ServiceProvider _withoutCategoryPolicyServices = null!;
    private ServiceProvider _withTwoCategoryPoliciesServices = null!;
    private ServiceProvider _withHundredCategoryPoliciesServices = null!;
    private ServiceProvider _withHundredExactCategoriesServices = null!;
    private ILogger[] _withoutCategoryPolicyLoggers = null!;
    private ILogger[] _withTwoCategoryPoliciesLoggers = null!;
    private ILogger[] _withHundredCategoryPoliciesLoggers = null!;
    private ILogger[] _withHundredExactCategoriesLoggers = null!;
    private LogBuffer _withoutCategoryPolicyBuffer = null!;
    private LogBuffer _withTwoCategoryPoliciesBuffer = null!;
    private LogBuffer _withHundredCategoryPoliciesBuffer = null!;
    private LogBuffer _withHundredExactCategoriesBuffer = null!;

    [Params(10_000, 20_000)]
    public int RecordsPerMinute { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _withoutCategoryPolicyServices = CreateServices(categoryPolicyCount: 0);
        _withTwoCategoryPoliciesServices = CreateServices(categoryPolicyCount: 2);
        _withHundredCategoryPoliciesServices = CreateServices(categoryPolicyCount: 100);
        _withHundredExactCategoriesServices = CreateServices(categoryPolicyCount: 100, useWildcards: false);
        _withoutCategoryPolicyLoggers = CreateLoggers(_withoutCategoryPolicyServices);
        _withTwoCategoryPoliciesLoggers = CreateLoggers(_withTwoCategoryPoliciesServices);
        _withHundredCategoryPoliciesLoggers = CreateLoggers(_withHundredCategoryPoliciesServices);
        _withHundredExactCategoriesLoggers = CreateLoggers(_withHundredExactCategoriesServices);
        _withoutCategoryPolicyBuffer = _withoutCategoryPolicyServices.GetRequiredService<LogBuffer>();
        _withTwoCategoryPoliciesBuffer = _withTwoCategoryPoliciesServices.GetRequiredService<LogBuffer>();
        _withHundredCategoryPoliciesBuffer = _withHundredCategoryPoliciesServices.GetRequiredService<LogBuffer>();
        _withHundredExactCategoriesBuffer = _withHundredExactCategoriesServices.GetRequiredService<LogBuffer>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _withHundredExactCategoriesServices.Dispose();
        _withHundredCategoryPoliciesServices.Dispose();
        _withTwoCategoryPoliciesServices.Dispose();
        _withoutCategoryPolicyServices.Dispose();
    }

    [IterationCleanup]
    public void FlushBuffers()
    {
        _withoutCategoryPolicyBuffer.Flush();
        _withTwoCategoryPoliciesBuffer.Flush();
        _withHundredCategoryPoliciesBuffer.Flush();
        _withHundredExactCategoriesBuffer.Flush();
    }

    [Benchmark(Baseline = true)]
    public void BottomKWithoutCategoryPolicy()
    {
        LoggingBenchmarkWorkload.LogBatch(_withoutCategoryPolicyLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKWithTwoCategoryPolicyMisses()
    {
        LoggingBenchmarkWorkload.LogBatch(_withTwoCategoryPoliciesLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKWithHundredCategoryPolicyMisses()
    {
        LoggingBenchmarkWorkload.LogBatch(_withHundredCategoryPoliciesLoggers, RecordsPerMinute);
    }

    [Benchmark]
    public void BottomKWithHundredExactCategoryMisses()
    {
        LoggingBenchmarkWorkload.LogBatch(_withHundredExactCategoriesLoggers, RecordsPerMinute);
    }

    private static ServiceProvider CreateServices(int categoryPolicyCount, bool useWildcards = true)
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

                for (int i = 0; i < categoryPolicyCount; i++)
                {
                    string categoryName = useWildcards
                        ? $"Contoso.Protected{i:D3}.*"
                        : $"Contoso.Protected{i:D3}.Component";
                    options.Rules.Add(new BottomKLogSamplingFilterRule(
                        categoryName: categoryName));
                }
            });
        });

        return services.BuildServiceProvider();
    }

    private static ILogger[] CreateLoggers(ServiceProvider services)
        => LoggingBenchmarkWorkload.CreateLoggers(services.GetRequiredService<ILoggerFactory>());
}
