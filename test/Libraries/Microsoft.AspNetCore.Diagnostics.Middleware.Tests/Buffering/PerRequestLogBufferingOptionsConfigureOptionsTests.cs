// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System.Collections.Generic;
using Microsoft.AspNetCore.Diagnostics.Buffering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class PerRequestLogBufferingOptionsConfigureOptionsTests
{
    [Fact]
    public void Configure_WhenConfigurationIsNull_DoesNotModifyOptions()
    {
        // Arrange
        var options = new PerRequestLogBufferingOptions();
        var configureOptions = new PerRequestLogBufferingConfigureOptions(null!);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new PerRequestLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WhenSectionDoesNotExist_DoesNotModifyOptions()
    {
        // Arrange
        var options = new PerRequestLogBufferingOptions();
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        var configureOptions = new PerRequestLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new PerRequestLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WhenSectionContainsInvalidPropertyNames_DoesNotModifyOptions()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["GlobalLogBuffering"] = "1",
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new PerRequestLogBufferingOptions();
        var configureOptions = new PerRequestLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new PerRequestLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WithValidConfiguration_UpdatesOptions()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["PerIncomingRequestLogBuffering:MaxLogRecordSizeInBytes"] = "1024",
            ["PerIncomingRequestLogBuffering:MaxPerRequestBufferSizeInBytes"] = "4096",
            ["PerIncomingRequestLogBuffering:IncludeScopes"] = "true",
            ["PerIncomingRequestLogBuffering:Rules:0:CategoryName"] = "TestCategory",
            ["PerIncomingRequestLogBuffering:Rules:0:LogLevel"] = "Information"
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new PerRequestLogBufferingOptions();
        var configureOptions = new PerRequestLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equal(1024, options.MaxLogRecordSizeInBytes);
        Assert.Equal(4096, options.MaxPerRequestBufferSizeInBytes);
        Assert.True(options.IncludeScopes);
        Assert.Single(options.Rules);
        Assert.Equal("TestCategory", options.Rules[0].CategoryName);
        Assert.Equal(LogLevel.Information, options.Rules[0].LogLevel);
    }

    [Fact]
    public void Configure_WithMultipleRules_AddsAllRules()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["PerIncomingRequestLogBuffering:Rules:0:CategoryName"] = "Category1",
            ["PerIncomingRequestLogBuffering:Rules:0:LogLevel"] = "Warning",
            ["PerIncomingRequestLogBuffering:Rules:1:CategoryName"] = "Category2",
            ["PerIncomingRequestLogBuffering:Rules:1:LogLevel"] = "Error"
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new PerRequestLogBufferingOptions();
        var configureOptions = new PerRequestLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equal(2, options.Rules.Count);
        Assert.Equal("Category1", options.Rules[0].CategoryName);
        Assert.Equal(LogLevel.Warning, options.Rules[0].LogLevel);
        Assert.Equal("Category2", options.Rules[1].CategoryName);
        Assert.Equal(LogLevel.Error, options.Rules[1].LogLevel);
    }
}
#endif
