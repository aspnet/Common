// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Extensions.Diagnostics.Latency.Internal;
using Microsoft.Extensions.Http.Latency.Internal;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Latency.Test;

public class HttpClientLatencyTelemetryExtensionsTest
{
    [Fact]
    public void HttpClientLatencyTelemtry_NullParameter_ThrowsException()
    {
        var act = () => ((IServiceCollection)null!).AddHttpClientLatencyTelemetry();
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IServiceCollection>().AddHttpClientLatencyTelemetry((Action<HttpClientLatencyTelemetryOptions>)null!);
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IServiceCollection>().AddHttpClientLatencyTelemetry((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientLatencyTelemtry_AddToServiceCollection()
    {
        using var sp = new ServiceCollection()
            .AddHttpClient()
            .AddNullLatencyContext()
            .AddHttpClientLatencyTelemetry()
            .BuildServiceProvider();

        var listener = sp.GetRequiredService<HttpRequestLatencyListener>();
        Assert.NotNull(listener);

        var latencyContext = sp.GetRequiredService<HttpClientLatencyContext>();
        Assert.NotNull(latencyContext);
        Assert.Null(latencyContext.Get());

        var options = sp.GetRequiredService<IOptions<HttpClientLatencyTelemetryOptions>>().Value;
        Assert.NotNull(options);
        Assert.True(options.EnableDetailedLatencyBreakdown);

        var handler = sp.GetRequiredService<HttpLatencyTelemetryHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void HttpClientLatencyTelemtry_AddToServiceCollection_CreatesClientSuccessfully()
    {
        using var sp = new ServiceCollection()
            .AddNullLatencyContext()
            .AddHttpClient()
            .AddHttpClientLatencyTelemetry()
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public void HttpClientLatencyTelemtryExtensions_Add_InvokesConfig()
    {
        bool invoked = false;
        using var sp = new ServiceCollection()
            .AddNullLatencyContext()
            .AddHttpClientLatencyTelemetry(a =>
            {
                invoked = true;
                a.EnableDetailedLatencyBreakdown = false;
            })
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<HttpClientLatencyTelemetryOptions>>().Value;
        Assert.NotNull(options);
        Assert.False(options.EnableDetailedLatencyBreakdown);
        Assert.True(invoked);
    }

    [Fact]
    public void RequestLatencyExtensions_Add_BindsToConfigSection()
    {
        HttpClientLatencyTelemetryOptions expectedOptions = new()
        {
            EnableDetailedLatencyBreakdown = false
        };

        var config = GetConfigSection(expectedOptions);
        using var sp = new ServiceCollection()
          .AddNullLatencyContext()
          .AddHttpClientLatencyTelemetry(config)
          .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<HttpClientLatencyTelemetryOptions>>().Value;
        Assert.NotNull(options);
        Assert.Equal(expectedOptions.EnableDetailedLatencyBreakdown, options.EnableDetailedLatencyBreakdown);
    }

    [Fact]
    public void HttpClientLatencyTelemtry_LatencyContextNotRegistered_RegistersLatencyContext()
    {
        using var sp = new ServiceCollection()
            .AddHttpClient()
            .AddHttpClientLatencyTelemetry()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<LatencyContextTokenIssuer>(sp.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.IsType<LatencyContextProvider>(sp.GetRequiredService<ILatencyContextProvider>());
    }

    [Fact]
    public void HttpClientLatencyTelemtry_CustomLatencyContextRegisteredFirst_KeepsCustomLatencyContext()
    {
        using var sp = new ServiceCollection()
            .AddSingleton<ILatencyContextTokenIssuer, TestLatencyContextTokenIssuer>()
            .AddSingleton<ILatencyContextProvider, TestLatencyContextProvider>()
            .AddHttpClientLatencyTelemetry()
            .BuildServiceProvider();

        Assert.IsType<TestLatencyContextTokenIssuer>(sp.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.IsType<TestLatencyContextProvider>(sp.GetRequiredService<ILatencyContextProvider>());
    }

    private static IConfigurationSection GetConfigSection(HttpClientLatencyTelemetryOptions options)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { $"{nameof(HttpClientLatencyTelemetryOptions)}:{nameof(options.EnableDetailedLatencyBreakdown)}", options.EnableDetailedLatencyBreakdown.ToString(CultureInfo.InvariantCulture) },
            })
            .Build()
            .GetSection($"{nameof(HttpClientLatencyTelemetryOptions)}");
    }

    private sealed class TestLatencyContextTokenIssuer : ILatencyContextTokenIssuer
    {
        public TagToken GetTagToken(string name) => throw new NotSupportedException();
        public CheckpointToken GetCheckpointToken(string name) => throw new NotSupportedException();
        public MeasureToken GetMeasureToken(string name) => throw new NotSupportedException();
    }

    private sealed class TestLatencyContextProvider : ILatencyContextProvider
    {
        public ILatencyContext CreateContext() => throw new NotSupportedException();
    }
}