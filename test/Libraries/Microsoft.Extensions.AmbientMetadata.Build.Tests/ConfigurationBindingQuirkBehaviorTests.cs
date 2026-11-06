// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Extensions.AmbientMetadata.Test;

public class ConfigurationBindingQuirkBehaviorTests
{
    [Theory]
    [InlineData("ambientmetadata:build")]
    [InlineData("customSection:ambientmetadata:build")]
    public void GivenMetadata_RegistersOptions_HostBuilder(string sectionName)
    {
        // .NET 11 binds empty strings for HostBuilder too; earlier runtimes leave these properties null.
#if NET11_0_OR_GREATER
        var defaultMetadata = new BuildMetadata
        {
            BuildId = string.Empty,
            BuildNumber = string.Empty,
            SourceBranchName = string.Empty,
            SourceVersion = string.Empty
        };
#else
        var defaultMetadata = new BuildMetadata();
#endif

        using var host = CreateUsingHostBuilder(sectionName);

        // Earlier runtimes leave the properties null despite the empty strings in configuration.
        // Related issue: https://github.com/dotnet/runtime/issues/62532
        host.Services.GetRequiredService<IOptions<BuildMetadata>>().Value.Should().BeEquivalentTo(defaultMetadata);
    }

    [Theory]
    [InlineData("ambientmetadata:build")]
    [InlineData("customSection:ambientmetadata:build")]
    public void GivenMetadata_RegistersOptions_HostApplicationBuilder(string sectionName)
    {
        // When configuration is not available, values are initialized to an empty string
        var metadataWithEmptyStrings = new BuildMetadata
        {
            BuildId = string.Empty,
            BuildNumber = string.Empty,
            SourceBranchName = string.Empty,
            SourceVersion = string.Empty
        };

        using var host = CreateUsingHostApplicationBuilder(sectionName);

        // We get a BuildMetadata instance with all properties populated with empty strings, as expected
        host.Services.GetRequiredService<IOptions<BuildMetadata>>().Value.Should().BeEquivalentTo(metadataWithEmptyStrings);
    }

    private static IConfigurationBuilder ConfigureInMemoryCollection(IConfigurationBuilder configuration, string sectionName)
    {
        return configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { $"{sectionName}:BuildId", string.Empty },
            { $"{sectionName}:BuildNumber", string.Empty },
            { $"{sectionName}:SourceBranchName", string.Empty },
            { $"{sectionName}:SourceVersion", string.Empty }
        });
    }

    private static IHost CreateUsingHostBuilder(string sectionName)
    {
        return FakeHost.CreateBuilder()
            .ConfigureHostConfiguration(configBuilder =>
            {
                _ = ConfigureInMemoryCollection(configBuilder, sectionName);
            })
            .ConfigureServices((context, services) =>
            {
                _ = services.AddBuildMetadata(context.Configuration.GetSection(sectionName));
            })
            .Build();
    }

    private static IHost CreateUsingHostApplicationBuilder(string sectionName)
    {
        var hostBuilder = Host.CreateEmptyApplicationBuilder(new());
        _ = ConfigureInMemoryCollection(hostBuilder.Configuration, sectionName);
        _ = hostBuilder.Services.AddBuildMetadata(hostBuilder.Configuration.GetSection(sectionName));
        return hostBuilder.Build();
    }
}
