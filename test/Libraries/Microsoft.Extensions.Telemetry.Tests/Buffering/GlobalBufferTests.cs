// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class GlobalBufferTests
{
    [Fact]
    public void Flush_EmitsStateOfThreadThatCreatedRecord()
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = CreateServices(provider);
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        ActivityTraceId expectedTraceId = default;
        ActivitySpanId expectedSpanId = default;
        int loggingThreadId = RunOnDedicatedThread(() =>
        {
            using Activity activity = new Activity("logging");
            activity.SetIdFormat(ActivityIdFormat.W3C).Start();
            expectedTraceId = activity.TraceId;
            expectedSpanId = activity.SpanId;

            logger.LogWarning("Hello {Name}", "World");
        });

        using (Activity flushingActivity = new Activity("flushing"))
        {
            flushingActivity.SetIdFormat(ActivityIdFormat.W3C).Start();
            services.GetRequiredService<GlobalLogBuffer>().Flush();
        }

        CapturedRecord record = Assert.Single(provider.Records);
        Assert.Equal(expectedTraceId, record.ActivityTraceId);
        Assert.Equal(expectedSpanId, record.ActivitySpanId);
        Assert.Equal(loggingThreadId, record.ManagedThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, record.ManagedThreadId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flush_EmitsNoTraceContext_WhenRecordCreatedWithoutW3CActivity(bool useHierarchicalActivity)
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = CreateServices(provider);
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        _ = RunOnDedicatedThread(() =>
        {
            // ignore any ambient activity flowing from the test runner
            Activity.Current = null;
            using Activity? activity = useHierarchicalActivity ? new Activity("logging") : null;
            activity?.SetIdFormat(ActivityIdFormat.Hierarchical).Start();

            logger.LogWarning("Hello {Name}", "World");
        });

        // the activity of the flushing thread must not be attributed to the record
        using (Activity flushingActivity = new Activity("flushing"))
        {
            flushingActivity.SetIdFormat(ActivityIdFormat.W3C).Start();
            services.GetRequiredService<GlobalLogBuffer>().Flush();
        }

        CapturedRecord record = Assert.Single(provider.Records);
        Assert.Null(record.ActivityTraceId);
        Assert.Null(record.ActivitySpanId);
    }

    [Fact]
    public void Flush_EmitsMessageTemplate()
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = CreateServices(provider);
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        logger.LogWarning("Hello {Name}", "World");

        services.GetRequiredService<GlobalLogBuffer>().Flush();

        Assert.Equal("Hello {Name}", Assert.Single(provider.Records).MessageTemplate);
    }

    [Fact]
    public void Flush_EmitsNoMessageTemplate_WhenStateIsAnObject()
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = CreateServices(provider);
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        // the logger records state which isn't a list of name/value pairs as "{OriginalFormat}"
        logger.Log(LogLevel.Warning, new EventId(1), new ObjectState(), null, static (_, _) => "Hello World");

        services.GetRequiredService<GlobalLogBuffer>().Flush();

        CapturedRecord record = Assert.Single(provider.Records);
        Assert.Contains(new KeyValuePair<string, object?>("{OriginalFormat}", "object state"), record.Attributes);
        Assert.Null(record.MessageTemplate);
    }

    [Fact]
    public void Flush_EmitsException_OnlyForRecordsLoggedWithException()
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = CreateServices(provider);
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        logger.LogWarning("Without exception");
        logger.LogWarning(new InvalidOperationException("Something failed"), "With exception");

        services.GetRequiredService<GlobalLogBuffer>().Flush();

        Assert.Null(Assert.Single(provider.Records, record => record.FormattedMessage == "Without exception").Exception);
        Assert.Equal("Something failed", Assert.Single(provider.Records, record => record.FormattedMessage == "With exception").Exception);
    }

    [Fact]
    public void Flush_EmitsNoDataOfPreviousRecords_WhenTrimmedRecordsAreReused()
    {
        using var provider = new CapturingProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(builder => builder
                .AddProvider(provider)
                .AddGlobalBuffer(options =>
                {
                    // small enough for most records to be trimmed, so that their instances get reused
                    options.MaxBufferSizeInBytes = 4096;
                    options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Warning));
                }))
            .BuildServiceProvider();
        ILogger logger = services.GetRequiredService<ILogger<GlobalBufferTests>>();

        _ = RunOnDedicatedThread(() =>
        {
            // ignore any ambient activity flowing from the test runner
            Activity.Current = null;

            // records with a trace context and a message template, which are trimmed and then reused...
            using (Activity activity = new Activity("logging"))
            {
                activity.SetIdFormat(ActivityIdFormat.W3C).Start();
                for (int i = 0; i < 100; i++)
                {
                    logger.LogWarning("Record {Index}", i);
                }
            }

            // ...for records without them
            for (int i = 0; i < 100; i++)
            {
                var state = new List<KeyValuePair<string, object?>> { new("Index", i) };
                logger.Log(LogLevel.Warning, new EventId(1), state, null, (_, _) => $"Other record {i}");
            }
        });

        services.GetRequiredService<GlobalLogBuffer>().Flush();

        Assert.InRange(provider.Records.Count, 2, 50);
        Assert.All(provider.Records, record =>
        {
            Assert.StartsWith("Other record", record.FormattedMessage);
            Assert.Equal("Index", Assert.Single(record.Attributes).Key);
            Assert.Null(record.ActivityTraceId);
            Assert.Null(record.ActivitySpanId);
            Assert.Null(record.MessageTemplate);
        });
    }

    private static ServiceProvider CreateServices(ILoggerProvider provider) =>
        new ServiceCollection()
            .AddLogging(builder => builder
                .AddProvider(provider)
                .AddGlobalBuffer(LogLevel.Warning))
            .BuildServiceProvider();

    private static int RunOnDedicatedThread(Action action)
    {
        int threadId = 0;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            threadId = Environment.CurrentManagedThreadId;
            try
            {
                action();
            }
#pragma warning disable CA1031 // Do not catch general exception types - the exception is reported on the test thread
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                exception = ex;
            }
        });

        thread.Start();
        thread.Join();

        Assert.Null(exception);
        return threadId;
    }

    private sealed class CapturingProvider : ILoggerProvider, ILogger, IBufferedLogger
    {
        public List<CapturedRecord> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // only buffered log records are of interest
        }

        public void LogRecords(IEnumerable<BufferedLogRecord> records)
        {
            // records must not be accessed after this method returns, hence copying them
            Records.AddRange(records.Select(record => new CapturedRecord(record)));
        }

        public void Dispose()
        {
            // nothing to dispose
        }
    }

    private sealed class ObjectState
    {
        public override string ToString() => "object state";
    }

    private sealed class CapturedRecord
    {
        public CapturedRecord(BufferedLogRecord record)
        {
            ActivityTraceId = record.ActivityTraceId;
            ActivitySpanId = record.ActivitySpanId;
            ManagedThreadId = record.ManagedThreadId;
            MessageTemplate = record.MessageTemplate;
            FormattedMessage = record.FormattedMessage;
            Exception = record.Exception;
            Attributes = record.Attributes.ToArray();
        }

        public ActivityTraceId? ActivityTraceId { get; }
        public ActivitySpanId? ActivitySpanId { get; }
        public int? ManagedThreadId { get; }
        public string? MessageTemplate { get; }
        public string? FormattedMessage { get; }
        public string? Exception { get; }
        public KeyValuePair<string, object?>[] Attributes { get; }
    }
}
#endif
