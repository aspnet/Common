// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Http.Resilience.Internal;
using Microsoft.Extensions.Http.Resilience.Test.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.Testing;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test;

public sealed partial class HttpClientBuilderExtensionsTests
{
    [Fact]
    public void UseBudgetedRetries_BuilderIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IHttpStandardResiliencePipelineBuilder)null!).UseBudgetedRetries());
    }

    [Fact]
    public void AddStandardResilienceHandler_BudgetedRetries_OnlyChangesRetryOptions()
    {
        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        var pipelineBuilder = builder.AddStandardResilienceHandler();
        pipelineBuilder.UseBudgetedRetries().Should().BeSameAs(pipelineBuilder);

        var options = GetStandardOptions(builder.Services);
        var defaults = new HttpStandardResilienceOptions();
        options.Retry.RetryBudget.Should().BeEquivalentTo(new HttpRetryBudgetOptions());
        options.Retry.MaxRetryAttempts.Should().Be(defaults.Retry.MaxRetryAttempts);
        options.CircuitBreaker.Should().BeEquivalentTo(defaults.CircuitBreaker);
        options.RateLimiter.Should().BeEquivalentTo(defaults.RateLimiter);
        options.TotalRequestTimeout.Should().BeEquivalentTo(defaults.TotalRequestTimeout);
        options.AttemptTimeout.Should().BeEquivalentTo(defaults.AttemptTimeout);

        var descriptor = GetPipeline(builder.Services, "test-standard").GetPipelineDescriptor();
        descriptor.Strategies.Should().HaveCount(6);
        descriptor.Strategies[0].Options.Should().BeOfType<HttpRateLimiterStrategyOptions>();
        descriptor.Strategies[1].Options.Should().BeOfType<HttpTimeoutStrategyOptions>();
        descriptor.Strategies[2].Options.Should().BeOfType<RetryBudgetStrategyOptions>();
        descriptor.Strategies[3].Options.Should().BeOfType<HttpRetryStrategyOptions>();
        descriptor.Strategies[4].Options.Should().BeOfType<HttpCircuitBreakerStrategyOptions>();
        descriptor.Strategies[5].Options.Should().BeOfType<HttpTimeoutStrategyOptions>();
    }

    [Fact]
    public void AddStandardResilienceHandler_BudgetedRetries_LaterConfigurationTakesPrecedence()
    {
        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.UseBudgetedRetries();
            o.Retry.MaxRetryAttempts = 5;
            o.Retry.RetryBudget!.RetryRatio = 0.2;
        });

        var options = GetStandardOptions(builder.Services);
        options.Retry.MaxRetryAttempts.Should().Be(5);
        options.Retry.RetryBudget!.RetryRatio.Should().Be(0.2);
    }

    [Fact]
    public void AddStandardResilienceHandler_BudgetedRetries_BudgetCanBeDisabled()
    {
        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler(o => o.Retry.UseBudgetedRetries()).Configure(o => o.Retry.RetryBudget = null);

        GetPipeline(builder.Services, "test-standard").GetPipelineDescriptor().Strategies.Should().HaveCount(5);
    }

    [Fact]
    public void AddStandardResilienceHandler_BudgetedRetries_ConfigurationOverridesBudget()
    {
        var section = ConfigurationStubFactory.Create(
            new Dictionary<string, string?>
            {
                { "standard:Retry:RetryBudget:RetryRatio", "0.25" },
                { "standard:Retry:RetryBudget:MinimumThroughput", "20" },
            }).GetSection("standard");

        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler().UseBudgetedRetries().Configure(section);

        var budget = GetStandardOptions(builder.Services).Retry.RetryBudget!;
        budget.RetryRatio.Should().Be(0.25);
        budget.MinimumThroughput.Should().Be(20);
        budget.SamplingDuration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AddStandardResilienceHandler_BudgetedRetriesAfterConfiguration_OverwritesBudgetOnly()
    {
        var section = ConfigurationStubFactory.Create(
            new Dictionary<string, string?>
            {
                { "standard:Retry:MaxRetryAttempts", "5" },
                { "standard:Retry:RetryBudget:RetryRatio", "0.25" },
            }).GetSection("standard");

        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler(section).UseBudgetedRetries();

        var retry = GetStandardOptions(builder.Services).Retry;
        retry.MaxRetryAttempts.Should().Be(5);
        retry.RetryBudget.Should().BeEquivalentTo(new HttpRetryBudgetOptions());
    }

    [Fact]
    public void AddStandardResilienceHandler_ConfigurationEnablesBudget()
    {
        var section = ConfigurationStubFactory.Create(
            new Dictionary<string, string?>
            {
                { "standard:Retry:RetryBudget:SamplingDuration", "00:00:20" },
            }).GetSection("standard");

        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler(section);

        var budget = GetStandardOptions(builder.Services).Retry.RetryBudget!;
        budget.SamplingDuration.Should().Be(TimeSpan.FromSeconds(20));
        budget.RetryRatio.Should().Be(0.1);

        GetPipeline(builder.Services, "test-standard").GetPipelineDescriptor().Strategies.Should().HaveCount(6);
    }

    [Fact]
    public void AddStandardResilienceHandler_InvalidBudget_Throws()
    {
        var builder = new ServiceCollection().AddLogging().AddMetrics().AddHttpClient("test");
        builder.AddStandardResilienceHandler(o => o.Retry.RetryBudget = new() { MinimumThroughput = 0 });

        Assert.Throws<OptionsValidationException>(() => GetPipeline(builder.Services, "test-standard"));
    }

    [Theory]
#if NET6_0_OR_GREATER
    [CombinatorialData]
#else
    [InlineData(true)]
#endif
    public async Task AddStandardResilienceHandler_RetryBudget_LimitsRetries(bool asynchronous = true)
    {
        var requests = 0;
        var config = ConfigurationStubFactory.Create(
            new()
            {
                { "standard:Retry:RetryBudget:RetryRatio", "0.5" },
                { "standard:Retry:RetryBudget:MinimumThroughput", "1" },
            },
            out var reloadAction).GetSection("standard");

        _builder.AddStandardResilienceHandler(o => o.Retry.UseBudgetedRetries()).Configure(config).Configure(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.Zero;
            options.CircuitBreaker.MinimumThroughput = 1000;
        });

        // A frozen clock keeps the sampling window from expiring when a slow agent stalls the test.
        _builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());

        _builder.AddHttpMessageHandler(() => new TestHandlerStub((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }));

        var client = CreateClient();

        // 1 request allows 1 retry (the floor), even though MaxRetryAttempts is 2.
        using (await SendRequest(client, "https://dummy", asynchronous))
        {
            requests.Should().Be(2);
        }

        // 2 requests allow 1 retry, which is already used.
        requests = 0;
        using (await SendRequest(client, "https://dummy", asynchronous))
        {
            requests.Should().Be(1);
        }

        // Reloading the options creates a new pipeline instance with a fresh budget.
        requests = 0;
        reloadAction(new()
        {
            { "standard:Retry:RetryBudget:RetryRatio", "0.5" },
            { "standard:Retry:RetryBudget:MinimumThroughput", "2" },
        });

        using (await SendRequest(client, "https://dummy", asynchronous))
        {
            requests.Should().Be(3);
        }
    }

    [Theory]
#if NET6_0_OR_GREATER
    [CombinatorialData]
#else
    [InlineData(true)]
#endif
    public async Task AddStandardResilienceHandler_RetryBudgetExhausted_EmitsMetric(bool asynchronous = true)
    {
        const string ClientName = "retry-budget-metric";
        using var metricCollector = new MetricCollector<int>(null, "Polly", "resilience.polly.strategy.events");

        var client = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddHttpClient(ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new TestHandlerStub(HttpStatusCode.InternalServerError))
            .AddStandardResilienceHandler()
            .Configure(options =>
            {
                options.Retry.UseBudgetedRetries();
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.RetryBudget!.RetryRatio = 0.5;
                options.Retry.RetryBudget.MinimumThroughput = 1;
                options.CircuitBreaker.MinimumThroughput = 1000;
            })
            .Services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClientName);

        // The floor of 1 allows the first retry; the second retry (MaxRetryAttempts is 2) is denied by the budget.
        using (await SendRequest(client, "https://dummy", asynchronous))
        {
            var measurement = GetBudgetExhaustedMeasurements().Should().ContainSingle().Subject;
            measurement.Value.Should().Be(1);
            measurement.Tags["event.severity"].Should().Be("Warning");
            measurement.Tags["strategy.name"].Should().Be("Standard-Retry");
            measurement.Tags["error.type"].Should().Be("500");
        }

        // The budget is spent, so the next request is denied its first retry.
        using (await SendRequest(client, "https://dummy", asynchronous))
        {
            GetBudgetExhaustedMeasurements().Should().HaveCount(2);
        }

        IEnumerable<CollectedMeasurement<int>> GetBudgetExhaustedMeasurements()
            => metricCollector.GetMeasurementSnapshot().Where(m =>
                m.Tags.TryGetValue("pipeline.name", out var pipeline) && Equals(pipeline, $"{ClientName}-standard") &&
                m.Tags.TryGetValue("event.name", out var name) && Equals(name, RetryBudgetStrategy.BudgetExhaustedEventName));
    }

    private static HttpStandardResilienceOptions GetStandardOptions(IServiceCollection services)
    {
        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get("test-standard");
    }
}
