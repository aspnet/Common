// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Test;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class GlobalBufferIncludeScopesTests
{
    [Fact]
    public void AddsScopesToAttributesOfBufferedLogRecords()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = CreateLoggerFactory(provider, includeScopes: true);
        ILogger logger = factory.CreateLogger("my category");

        using (logger.BeginScope("plain scope"))
        using (logger.BeginScope(new Dictionary<string, string> { ["Tenant"] = "contoso" }))
        using (logger.BeginScope("Order {OrderId}", 42))
        {
            logger.LogWarning("buffered {Number}", 1);
        }

        GetBuffer(factory).Flush();

        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal(
            new KeyValuePair<string, string?>[] { new("Number", "1"), new("OrderId", "42"), new("{OriginalFormat}", "buffered {Number}") },
            record.StructuredState);
    }

    [Fact]
    public void WhenLogRecordHasNoOriginalFormat_AddsScopesAfterItsAttributes()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = CreateLoggerFactory(provider, includeScopes: true);
        ILogger logger = factory.CreateLogger("my category");

        using (logger.BeginScope("Order {OrderId}", 42))
        {
            logger.Log(LogLevel.Warning, default, new KeyValuePair<string, object?>[] { new("Number", 1) }, null, static (_, _) => "buffered");
        }

        GetBuffer(factory).Flush();

        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal(new KeyValuePair<string, string?>[] { new("Number", "1"), new("OrderId", "42") }, record.StructuredState);
    }

    [Fact]
    public void WhenIncludeScopesIsDisabled_DoesNotAddScopes()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = CreateLoggerFactory(provider, includeScopes: false);
        ILogger logger = factory.CreateLogger("my category");

        using (logger.BeginScope("Order {OrderId}", 42))
        {
            logger.LogWarning("buffered");
        }

        GetBuffer(factory).Flush();

        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal(new KeyValuePair<string, string?>[] { new("{OriginalFormat}", "buffered") }, record.StructuredState);
    }

    [Fact]
    public void AddsScopeValuesAsTheyAreWhenLogRecordIsBuffered()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = CreateLoggerFactory(provider, includeScopes: true);
        ILogger logger = factory.CreateLogger("my category");

        var scope = new MutableKeyValueScope { Value = "original" };
        using (logger.BeginScope(scope))
        {
            logger.LogWarning("buffered");
        }

        scope.Value = "changed";
        GetBuffer(factory).Flush();

        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal("original", record.GetStructuredStateValue("Key"));
    }

    [Fact]
    public void AddsActivityScope()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = CreateLoggerFactory(
            provider,
            includeScopes: true,
            builder => builder.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId));
        ILogger logger = factory.CreateLogger("my category");

        using var activity = new Activity("logging");
        activity.Start();
        logger.LogWarning("buffered");

        GetBuffer(factory).Flush();

        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal(activity.TraceId.ToHexString(), record.GetStructuredStateValue("TraceId"));
        Assert.Equal(activity.SpanId.ToHexString(), record.GetStructuredStateValue("SpanId"));
    }

    [Fact]
    public void ScopeValuesCountTowardMaxLogRecordSize()
    {
        using var provider = new FakeLoggerProvider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(builder =>
        {
            builder.AddProvider(provider);
            builder.AddGlobalBuffer(options =>
            {
                options.IncludeScopes = true;
                options.MaxLogRecordSizeInBytes = 1024;
                options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Warning));
            });
        });
        ILogger logger = factory.CreateLogger("my category");

        logger.LogWarning("buffered");
        using (logger.BeginScope("{Value}", new string('a', 1024)))
        {
            logger.LogWarning("too big to be buffered");
        }

        // The log record which exceeds the size limit together with its scopes is emitted right away.
        FakeLogRecord record = Assert.Single(provider.Collector.GetSnapshot());
        Assert.Equal("too big to be buffered", record.Message);
    }

    private static ILoggerFactory CreateLoggerFactory(ILoggerProvider provider, bool includeScopes, Action<ILoggingBuilder>? configure = null) =>
        Utils.CreateLoggerFactory(builder =>
        {
            builder.AddProvider(provider);
            builder.AddGlobalBuffer(options =>
            {
                options.IncludeScopes = includeScopes;
                options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Warning));
            });

            configure?.Invoke(builder);
        });

    private static GlobalLogBuffer GetBuffer(ILoggerFactory factory) =>
        ((Utils.DisposingLoggerFactory)factory).ServiceProvider.GetRequiredService<GlobalLogBuffer>();

    private sealed class MutableKeyValueScope : IEnumerable<KeyValuePair<string, object?>>
    {
        public string? Value { get; set; }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return new KeyValuePair<string, object?>("Key", Value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
#endif
