// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class GlobalLogBufferingConfigureOptionsTests
{
    [Fact]
    public void Configure_WhenConfigurationIsNull_DoesNotModifyOptions()
    {
        // Arrange
        var options = new GlobalLogBufferingOptions();
        var configureOptions = new GlobalLogBufferingConfigureOptions(null!);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new GlobalLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WhenSectionDoesNotExist_DoesNotModifyOptions()
    {
        // Arrange
        var options = new GlobalLogBufferingOptions();
        var configuration = new ConfigurationBuilder().Build();
        var configureOptions = new GlobalLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new GlobalLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WhenSectionContainsInvalidPropertyNames_DoesNotModifyOptions()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["GlobalLogBuffering"] = "1",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new GlobalLogBufferingOptions();
        var configureOptions = new GlobalLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equivalent(new GlobalLogBufferingOptions(), options);
    }

    [Fact]
    public void Configure_WithValidConfiguration_UpdatesOptions()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["GlobalLogBuffering:MaxLogRecordSizeInBytes"] = "1024",
            ["GlobalLogBuffering:MaxBufferSizeInBytes"] = "4096",
            ["GlobalLogBuffering:IncludeScopes"] = "true",
            ["GlobalLogBuffering:Rules:0:CategoryName"] = "TestCategory",
            ["GlobalLogBuffering:Rules:0:LogLevel"] = "Information"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new GlobalLogBufferingOptions();
        var configureOptions = new GlobalLogBufferingConfigureOptions(configuration);

        // Act
        configureOptions.Configure(options);

        // Assert
        Assert.Equal(1024, options.MaxLogRecordSizeInBytes);
        Assert.Equal(4096, options.MaxBufferSizeInBytes);
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
            ["GlobalLogBuffering:Rules:0:CategoryName"] = "Category1",
            ["GlobalLogBuffering:Rules:0:LogLevel"] = "Warning",
            ["GlobalLogBuffering:Rules:1:CategoryName"] = "Category2",
            ["GlobalLogBuffering:Rules:1:LogLevel"] = "Error"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = new GlobalLogBufferingOptions();
        var configureOptions = new GlobalLogBufferingConfigureOptions(configuration);

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
