// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Extensions.Diagnostics.Latency.Internal;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Latency.Test;

public class RequestCheckpointExtensionsTests
{
    [Fact]
    public void AddRequestCheckpoint_Throws_WhenNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => Extensions.DependencyInjection.RequestLatencyTelemetryServiceCollectionExtensions.AddRequestCheckpoint(null!));
    }

    [Fact]
    public void UseRequestCheckpoint_Throws_WhenNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => Builder.RequestLatencyTelemetryApplicationBuilderExtensions.UseRequestCheckpoint(null!));
    }

    [Fact]
    public void AddPipelineEntryCheckpoint_Throws_WhenNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => Extensions.DependencyInjection.RequestLatencyTelemetryServiceCollectionExtensions.AddPipelineEntryCheckpoint(null!));
    }

    [Fact]
    public void AddRequestCheckpoint_LatencyContextNotRegistered_RegistersLatencyContext()
    {
        using var serviceProvider = new ServiceCollection()
            .AddRequestCheckpoint()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<LatencyContextTokenIssuer>(serviceProvider.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.IsType<LatencyContextProvider>(serviceProvider.GetRequiredService<ILatencyContextProvider>());

        using var scope = serviceProvider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ILatencyContext>());
    }

    [Fact]
    public void AddRequestCheckpoint_CustomLatencyContextRegisteredFirst_KeepsCustomLatencyContext()
    {
        var tokenIssuer = Mock.Of<ILatencyContextTokenIssuer>();
        var latencyContextProvider = Mock.Of<ILatencyContextProvider>();
        var latencyContext = Mock.Of<ILatencyContext>();

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(tokenIssuer)
            .AddSingleton(latencyContextProvider)
            .AddScoped(_ => latencyContext)
            .AddRequestCheckpoint()
            .BuildServiceProvider();

        Assert.Same(tokenIssuer, serviceProvider.GetRequiredService<ILatencyContextTokenIssuer>());
        Assert.Same(latencyContextProvider, serviceProvider.GetRequiredService<ILatencyContextProvider>());

        using var scope = serviceProvider.CreateScope();
        Assert.Same(latencyContext, scope.ServiceProvider.GetRequiredService<ILatencyContext>());
    }
}
