// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Extensions.Diagnostics.Latency.Internal;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Latency.Test;

public class RequestLatencyTelemetryExtensionsTests
{
    [Fact]
    public void RequestLatencyExtensions_NullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
        RequestLatencyTelemetryServiceCollectionExtensions.AddRequestLatencyTelemetry(null!));
        Assert.Throws<ArgumentNullException>(() =>
        RequestLatencyTelemetryServiceCollectionExtensions.AddRequestLatencyTelemetry(new ServiceCollection(), configure: null!));
        Assert.Throws<ArgumentNullException>(() =>
        RequestLatencyTelemetryServiceCollectionExtensions.AddRequestLatencyTelemetry(new ServiceCollection(), section: null!));
        Assert.Throws<ArgumentNullException>(() =>
        RequestLatencyTelemetryApplicationBuilderExtensions.UseRequestLatencyTelemetry(null!));
    }

    [Fact]
    public void RequestLatencyExtensions_AddRequestLatency_AddsLatencyContext()
    {
        using var serviceProvider = new ServiceCollection()
            .AddLatencyContext()
            .AddRequestLatencyTelemetry()
            .BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ILatencyContext>());

        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        Assert.Equal(scope1.ServiceProvider.GetService<ILatencyContext>(),
            scope1.ServiceProvider.GetService<ILatencyContext>());
        Assert.NotEqual(scope1.ServiceProvider.GetService<ILatencyContext>(),
            scope2.ServiceProvider.GetService<ILatencyContext>());
    }

    [Fact]
    public void AddRequestLatencyTelemetry_LatencyContextNotRegistered_RegistersLatencyContext()
    {
        using var serviceProvider = new ServiceCollection()
            .AddRequestLatencyTelemetry()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<LatencyContextTokenIssuer>(serviceProvider.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.IsType<LatencyContextProvider>(serviceProvider.GetRequiredService<ILatencyContextProvider>());

        using var scope = serviceProvider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ILatencyContext>());
    }

    [Fact]
    public void AddRequestLatencyTelemetry_CustomLatencyContextRegisteredFirst_KeepsCustomLatencyContext()
    {
        var tokenIssuer = Mock.Of<ILatencyContextTokenIssuer>();
        var latencyContextProvider = Mock.Of<ILatencyContextProvider>();

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(tokenIssuer)
            .AddSingleton(latencyContextProvider)
            .AddRequestLatencyTelemetry()
            .BuildServiceProvider();

        Assert.Same(tokenIssuer, serviceProvider.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.Same(latencyContextProvider, serviceProvider.GetRequiredService<ILatencyContextProvider>());
    }

    [Fact]
    public void RequestLatencyExtensions_Add_BindsToConfigSection()
    {
        RequestLatencyTelemetryOptions expectedOptions = new()
        {
            LatencyDataExportTimeout = TimeSpan.FromSeconds(2)
        };
        var config = GetConfigSection(expectedOptions);

        using var serviceProvider = new ServiceCollection()
            .AddRequestLatencyTelemetry(config)
            .BuildServiceProvider();

        var actualOptions = serviceProvider.GetRequiredService<IOptions<RequestLatencyTelemetryOptions>>();

        Assert.True(actualOptions.Value.LatencyDataExportTimeout == expectedOptions.LatencyDataExportTimeout);
    }

    private static IConfigurationSection GetConfigSection(RequestLatencyTelemetryOptions options)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { $"{nameof(RequestLatencyTelemetryOptions)}:{nameof(options.LatencyDataExportTimeout)}", options.LatencyDataExportTimeout.ToString() },
            })
            .Build()
            .GetSection($"{nameof(RequestLatencyTelemetryOptions)}");
    }
}
