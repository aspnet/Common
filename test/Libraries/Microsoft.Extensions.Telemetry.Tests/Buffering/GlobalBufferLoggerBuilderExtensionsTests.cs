// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Test;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class GlobalBufferLoggerBuilderExtensionsTests
{
    [Fact]
    public void WithLogLevel_RegistersInDI()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            builder.AddGlobalBuffer(LogLevel.Warning);
        });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        var bufferManager = serviceProvider.GetService<GlobalLogBuffer>();

        Assert.NotNull(bufferManager);
        Assert.IsAssignableFrom<GlobalLogBufferManager>(bufferManager);
    }

    [Fact]
    public void AddGlobalBuffer_WhenCustomLogBufferIsRegistered_PreservesCustomBuffer()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            builder.Services.AddSingleton<LogBuffer, StubLogBuffer>();
            builder.AddGlobalBuffer();
        });

        using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        Assert.IsType<StubLogBuffer>(serviceProvider.GetRequiredService<LogBuffer>());
        Assert.NotNull(serviceProvider.GetRequiredService<GlobalLogBuffer>());
    }

    [Fact]
    public void WhenArgumentNull_Throws()
    {
        ILoggingBuilder? builder = null;
        IConfiguration? configuration = null;

        Assert.Throws<ArgumentNullException>(() => builder!.AddGlobalBuffer(LogLevel.Warning));
        Assert.Throws<ArgumentNullException>(() => builder!.AddGlobalBuffer(configuration!));
    }

    [Fact]
    public void WithConfiguration_RegistersInDI()
    {
        List<LogBufferingFilterRule> expectedData =
        [
            new ("Program.MyLogger",  LogLevel.Information, 1, "number one", [new("region", "westus2"), new ("priority", 1)]),
            new (logLevel: LogLevel.Information),
        ];
        ConfigurationBuilder configBuilder = new ConfigurationBuilder();
        configBuilder.AddJsonFile("appsettings.json");
        IConfigurationRoot configuration = configBuilder.Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            builder.AddGlobalBuffer(configuration);
            builder.Services.Configure<GlobalLogBufferingOptions>(options =>
            {
                options.MaxLogRecordSizeInBytes = 33;
            });
        });
        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptionsMonitor<GlobalLogBufferingOptions>>();
        Assert.NotNull(options);
        Assert.NotNull(options.CurrentValue);
        Assert.Equal(33, options.CurrentValue.MaxLogRecordSizeInBytes); // value comes from the Configure<GlobalLogBufferingOptions>()  call
        Assert.Equal(1000, options.CurrentValue.MaxBufferSizeInBytes); // value comes from appsettings.json
        Assert.Equal(TimeSpan.FromSeconds(30), options.CurrentValue.AutoFlushDuration); // value comes from default
        Assert.Equivalent(expectedData, options.CurrentValue.Rules);
    }

    [Fact]
    public void WhenConfigUpdated_PicksUpConfigChanges()
    {
        List<LogBufferingFilterRule> initialData =
        [
            new(categoryName: "Program.MyLogger", logLevel: LogLevel.Information, eventId: 1, eventName: "number one"),
            new(logLevel : LogLevel.Information),
        ];
        List<LogBufferingFilterRule> updatedData =
        [
            new(logLevel: LogLevel.Information),
        ];
        string jsonConfig =
            @"
{
    ""GlobalLogBuffering"": {
     ""Rules"": [
       {
         ""CategoryName"": ""Program.MyLogger"",
         ""LogLevel"": ""Information"",
         ""EventId"": 1,
         ""EventName"": ""number one"",
       },
       {
         ""LogLevel"": ""Information"",
       },
     ]
    }
}
";

        using ConfigurationRoot config = TestConfiguration.Create(() => jsonConfig);

        using ExtendedLoggerTests.Provider provider = new ExtendedLoggerTests.Provider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(
            builder =>
            {
                builder.AddProvider(provider);
                builder.AddGlobalBuffer(config);
            });
        ILogger logger = factory.CreateLogger("Program.MyLogger");
        Utils.DisposingLoggerFactory dlf = (Utils.DisposingLoggerFactory)factory;
        var bufferManager = dlf.ServiceProvider.GetRequiredService<GlobalLogBuffer>() as GlobalLogBufferManager;

        IOptionsMonitor<GlobalLogBufferingOptions>? options = dlf.ServiceProvider.GetService<IOptionsMonitor<GlobalLogBufferingOptions>>();
        Assert.NotNull(options);
        Assert.NotNull(options.CurrentValue);
        Assert.Equivalent(initialData, options.CurrentValue.Rules);

        // this is just to trigger creating an internal buffer:
        logger.LogInformation(new EventId(1, "number one"), null);

        jsonConfig =
@"
{
    ""GlobalLogBuffering"": {
     ""Rules"": [
       {
         ""LogLevel"": ""Information"",
       },
     ]
    }
}
";
        config.Reload();

        Assert.NotNull(bufferManager);
        Assert.NotEmpty(bufferManager.Buffers);
        foreach (GlobalBuffer buffer in bufferManager.Buffers.Values)
        {
            Assert.Equivalent(updatedData, buffer.LastKnownGoodFilterRules, strict: true);
        }
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
}
#endif
