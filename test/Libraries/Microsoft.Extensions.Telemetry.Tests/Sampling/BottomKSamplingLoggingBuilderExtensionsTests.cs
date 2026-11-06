// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Extensions.Telemetry.Sampling;

public class BottomKSamplingLoggingBuilderExtensionsTests
{
    [Fact]
    public void AddBottomKLogSampling_RegistersSharedBufferAndSampler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBottomKLogSampling());

        using ServiceProvider provider = services.BuildServiceProvider();

        BottomKLogBuffer bottomKBuffer = provider.GetRequiredService<BottomKLogBuffer>();
        Assert.Same(bottomKBuffer, provider.GetRequiredService<LogBuffer>());
        Assert.IsType<BottomKLoggingSampler>(provider.GetRequiredService<LoggingSampler>());
        BottomKLogSamplingOptions options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>().CurrentValue;
        Assert.Empty(options.Rules);
    }

    [Fact]
    public void AddBottomKLogSampling_WithDelegate_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBottomKLogSampling(options =>
        {
            options.Capacity = 17;
            options.UnseenWeightMode = BottomKUnseenWeightMode.RarestSeen;
        }));

        using ServiceProvider provider = services.BuildServiceProvider();
        BottomKLogSamplingOptions options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>().CurrentValue;

        Assert.Equal(17, options.Capacity);
        Assert.Equal(BottomKUnseenWeightMode.RarestSeen, options.UnseenWeightMode);
    }

    [Fact]
    public void AddBottomKLogSampling_WithConfigurationSection_BindsOptions()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:BottomK:Capacity"] = "23",
                ["Logging:BottomK:Enabled"] = "false",
                ["Logging:BottomK:Rules:0:CategoryName"] = "Sampled.*",
                ["Logging:BottomK:Rules:0:LogLevel"] = "Information",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
            builder.AddBottomKLogSampling(configuration.GetSection("Logging:BottomK")));

        using ServiceProvider provider = services.BuildServiceProvider();
        BottomKLogSamplingOptions options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>().CurrentValue;

        Assert.Equal(23, options.Capacity);
        Assert.False(options.Enabled);
        BottomKLogSamplingFilterRule rule = Assert.Single(options.Rules);
        Assert.Equal("Sampled.*", rule.CategoryName);
        Assert.Equal(LogLevel.Information, rule.LogLevel);
    }

    [Fact]
    public void AddBottomKLogSampling_WhenOptionsInvalid_ValidationFails()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
            builder.AddBottomKLogSampling(options => options.FlushInterval = TimeSpan.Zero));

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<BottomKLogSamplingOptions> options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.CurrentValue);
    }

    [Fact]
    public void CustomValidator_RejectsEveryInvalidPolicyShape()
    {
        var options = new BottomKLogSamplingOptions
        {
            FlushInterval = TimeSpan.Zero,
            UnseenWeightMode = (BottomKUnseenWeightMode)int.MaxValue,
            Rules =
            [
                new(logLevel: (LogLevel)int.MaxValue),
                new(categoryName: ""),
                new(categoryName: "one*two*three"),
                null!,
            ],
        };
        var validator = new BottomKLogSamplingOptionsCustomValidator();

        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(6, result.Failures.Count());
    }

    [Fact]
    public void CustomValidator_AllowsNullCollectionsForDataAnnotationValidator()
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = null!,
        };
        var validator = new BottomKLogSamplingOptionsCustomValidator();

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void AddBottomKLogSampling_WhenCalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddBottomKLogSampling();
            builder.AddBottomKLogSampling();
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<BottomKLoggingSampler>(provider.GetRequiredService<LoggingSampler>());
    }

    [Fact]
    public void AddBottomKLogSampling_DefaultThenDelegate_UsesExplicitRulesOnly()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddBottomKLogSampling();
            builder.AddBottomKLogSampling(options =>
                options.Rules.Add(new BottomKLogSamplingFilterRule(categoryName: "Explicit.Category")));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        BottomKLogSamplingOptions options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>().CurrentValue;

        BottomKLogSamplingFilterRule rule = Assert.Single(options.Rules);
        Assert.Equal("Explicit.Category", rule.CategoryName);
    }

    [Fact]
    public void AddBottomKLogSampling_DelegateThenDefault_KeepsExplicitRules()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddBottomKLogSampling(options =>
                options.Rules.Add(new BottomKLogSamplingFilterRule(categoryName: "Explicit.Category")));
            builder.AddBottomKLogSampling();
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        BottomKLogSamplingOptions options =
            provider.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>().CurrentValue;

        BottomKLogSamplingFilterRule rule = Assert.Single(options.Rules);
        Assert.Equal("Explicit.Category", rule.CategoryName);
    }

    [Fact]
    public void AddBottomKLogSampling_WhenAnotherBufferIsRegistered_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.Services.AddSingleton<LogBuffer, StubLogBuffer>();
                builder.AddBottomKLogSampling();
            }));

        Assert.Contains("another log buffer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBottomKLogSampling_WhenAnotherSamplerIsRegistered_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.AddTraceBasedSampler();
                builder.AddBottomKLogSampling();
            }));

        Assert.Contains("another logging sampler", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBottomKLogSampling_WhenKeyedSamplerIsRegistered_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<LoggingSampler, StubSampler>("key");

        services.AddLogging(builder => builder.AddBottomKLogSampling());

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<BottomKLoggingSampler>(provider.GetRequiredService<LoggingSampler>());
        Assert.IsType<StubSampler>(provider.GetRequiredKeyedService<LoggingSampler>("key"));
    }

    [Fact]
    public void AddBottomKLogSampling_WhenRejected_DoesNotRegisterOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LoggingSampler, StubSampler>();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
                builder.AddBottomKLogSampling(options => options.Capacity = 17)));

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IConfigureOptions<BottomKLogSamplingOptions>));
    }

    [Fact]
    public void AddSamplerType_WhenBottomKIsRegistered_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.AddBottomKLogSampling();
                builder.AddSampler<StubSampler>();
            }));

        Assert.Contains("cannot be combined", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddSamplerInstance_WhenBottomKIsRegistered_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.AddBottomKLogSampling();
                builder.AddSampler(new StubSampler());
            }));

        Assert.Contains("cannot be combined", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddRandomSampler_WhenBottomKIsRegistered_DoesNotRegisterOptions()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.AddBottomKLogSampling();
                builder.AddRandomProbabilisticSampler(options => options.Rules.Clear());
            }));

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IConfigureOptions<RandomProbabilisticSamplerOptions>));
    }

    [Fact]
    public void AddGlobalBuffer_WhenBottomKIsRegistered_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddLogging(builder =>
            {
                builder.AddBottomKLogSampling();
                builder.AddGlobalBuffer();
            }));

        Assert.Contains("Bottom-K log sampling", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubLogBuffer : LogBuffer
    {
        public override void Flush()
        {
        }

        public override bool TryEnqueue<TState>(
            IBufferedLogger bufferedLogger,
            in LogEntry<TState> logEntry) => false;
    }

    private sealed class StubSampler : LoggingSampler
    {
        public override bool ShouldSample<TState>(in LogEntry<TState> logEntry) => true;
    }
}
#endif
