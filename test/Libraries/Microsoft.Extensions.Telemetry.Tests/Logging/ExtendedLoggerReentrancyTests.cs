// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.Logging.Test;

[Collection("Serial Tests")]
public static partial class ExtendedLoggerReentrancyTests
{
    private const string OuterCategory = "Outer";
    private const string NestedCategory = "Nested";

    public enum NestedLogThread
    {
        // The provider emits the nested record on the thread logging the outer one.
        Same,

        // The provider emits the nested record on a thread pool thread and waits for it.
        OtherWaited,

        // The provider emits the nested record on a thread pool thread without waiting for it.
        OtherNotWaited,
    }

    [Theory]
    [CombinatorialData]
    public static void NestedLogFromProvider_PreservesBothRecords(bool extendedFactory, bool generatedOuter, bool generatedNested)
    {
        var reentrantCollector = new FakeLogCollector();
        var downstreamCollector = new FakeLogCollector();
        using var reentrantProvider = new ReentrantProvider(reentrantCollector);
        using var downstreamProvider = new FakeLoggerProvider(downstreamCollector);
        using var errors = new LoggingErrorListener();

        Action<ILoggingBuilder> configure = builder => builder.AddProvider(reentrantProvider).AddProvider(downstreamProvider);
        using ILoggerFactory factory = extendedFactory ? Utils.CreateLoggerFactory(configure) : LoggerFactory.Create(configure);

        var nestedLogger = factory.CreateLogger(NestedCategory);
        reentrantProvider.OnOuterRecord = () =>
        {
            if (generatedNested)
            {
                NestedRecord(nestedLogger, "nested-value");
            }
            else
            {
                nestedLogger.LogInformation("Nested record {value}", "nested-value");
            }
        };

        var outerLogger = factory.CreateLogger(OuterCategory);
        var exception = new InvalidOperationException("Outer failure");
        if (generatedOuter)
        {
            OuterRecord(outerLogger, exception, "outer-value");
        }
        else
        {
            outerLogger.LogError(exception, "Outer record {value}", "outer-value");
        }

        Assert.Empty(errors.Errors);
        AssertRecords(reentrantCollector, exception, extendedFactory);
        AssertRecords(downstreamCollector, exception, extendedFactory);
    }

    [Theory]
    [CombinatorialData]
    public static async Task NestedLogFromProvider_PreservesAllRecordsWhenLoggingConcurrently(
        bool extendedFactory, bool generatedOuter, bool generatedNested, NestedLogThread nestedLogThread)
    {
        const int ThreadCount = 8;
        const int RecordsPerThread = 1_000;

        var reentrantCollector = new FakeLogCollector();
        var downstreamCollector = new FakeLogCollector();
        using var reentrantProvider = new ReentrantProvider(reentrantCollector);
        using var downstreamProvider = new FakeLoggerProvider(downstreamCollector);
        using var outerValue = new ThreadLocal<string?>();

        Action<ILoggingBuilder> configure = builder => builder.AddProvider(reentrantProvider).AddProvider(downstreamProvider);
        using ILoggerFactory factory = extendedFactory ? Utils.CreateLoggerFactory(configure) : LoggerFactory.Create(configure);

        var nestedLogger = factory.CreateLogger(NestedCategory);
        var notWaitedNestedLogs = new ConcurrentQueue<Task>();
        reentrantProvider.OnOuterRecord = () =>
        {
            var nestedValue = GetNestedValue(outerValue.Value!);
            void LogNested()
            {
                if (generatedNested)
                {
                    NestedRecord(nestedLogger, nestedValue);
                }
                else
                {
                    nestedLogger.LogInformation("Nested record {value}", nestedValue);
                }
            }

            switch (nestedLogThread)
            {
                case NestedLogThread.Same:
                    LogNested();
                    break;
                case NestedLogThread.OtherWaited:
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                    Task.Run(LogNested).Wait();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                    break;
                default:
                    notWaitedNestedLogs.Enqueue(Task.Run(LogNested));
                    break;
            }
        };

        var outerLogger = factory.CreateLogger(OuterCategory);
        var exception = new InvalidOperationException("Outer failure");
        using var start = new Barrier(ThreadCount);

        // Each long-running task gets a dedicated thread, so all of them log at the same time.
        var threads = Enumerable.Range(0, ThreadCount).Select(threadIndex => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait();
                for (int i = 0; i < RecordsPerThread; i++)
                {
                    var value = GetOuterValue(threadIndex, i);
                    outerValue.Value = value;
                    if (generatedOuter)
                    {
                        OuterRecord(outerLogger, exception, value);
                    }
                    else
                    {
                        outerLogger.LogError(exception, "Outer record {value}", value);
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToList();

        await Task.WhenAll(threads);
        await Task.WhenAll(notWaitedNestedLogs);

        var outerExceptionTag = extendedFactory ? $"exception.type={typeof(InvalidOperationException)};" : string.Empty;
        var expected = new List<string>();
        for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
        {
            for (int i = 0; i < RecordsPerThread; i++)
            {
                var value = GetOuterValue(threadIndex, i);
                var nestedValue = GetNestedValue(value);
                expected.Add($"{NestedCategory}|Nested record {nestedValue}||value={nestedValue};{{OriginalFormat}}=Nested record {{value}}");
                expected.Add($"{OuterCategory}|Outer record {value}|{exception.Message}|{outerExceptionTag}value={value};{{OriginalFormat}}=Outer record {{value}}");
            }
        }

        AssertSameRecords(expected, reentrantCollector);
        AssertSameRecords(expected, downstreamCollector);

        static string GetOuterValue(int threadIndex, int recordIndex) => $"thread{threadIndex}-record{recordIndex}";
        static string GetNestedValue(string value) => $"nested-in-{value}";
    }

    private static void AssertSameRecords(List<string> expected, FakeLogCollector collector)
    {
        var actual = collector.GetSnapshot()
            .Select(record => $"{record.Category}|{record.Message}|{record.Exception?.Message}|{string.Join(";", GetTags(record))}")
            .ToList();

        var missing = expected.Except(actual, StringComparer.Ordinal).Take(3).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).Take(3).ToList();
        Assert.True(
            actual.Count == expected.Count && missing.Count == 0 && unexpected.Count == 0,
            $"Expected {expected.Count} records but got {actual.Count}. Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
    }

    private static void AssertRecords(FakeLogCollector collector, Exception exception, bool extendedFactory)
    {
        var records = collector.GetSnapshot();
        Assert.Equal(2, records.Count);

        var nested = records[0];
        Assert.Equal(NestedCategory, nested.Category);
        Assert.Equal("Nested record nested-value", nested.Message);
        Assert.Null(nested.Exception);
        Assert.Equal(new[] { "value=nested-value", "{OriginalFormat}=Nested record {value}" }, GetTags(nested));

        var outer = records[1];
        Assert.Equal(OuterCategory, outer.Category);
        Assert.Equal("Outer record outer-value", outer.Message);
        Assert.Same(exception, outer.Exception);

        var expectedOuterTags = new List<string>();
        if (extendedFactory)
        {
            expectedOuterTags.Add($"exception.type={typeof(InvalidOperationException)}");
        }

        expectedOuterTags.Add("value=outer-value");
        expectedOuterTags.Add("{OriginalFormat}=Outer record {value}");
        Assert.Equal(expectedOuterTags, GetTags(outer));
    }

    private static IEnumerable<string> GetTags(FakeLogRecord record) => record.StructuredState!.Select(tag => $"{tag.Key}={tag.Value}");

    [LoggerMessage(LogLevel.Error, "Outer record {value}")]
    private static partial void OuterRecord(ILogger logger, Exception exception, string value);

    [LoggerMessage(LogLevel.Information, "Nested record {value}")]
    private static partial void NestedRecord(ILogger logger, string value);

    // Emits a log record of its own while handling a record of the outer category, before reading that record.
    private sealed class ReentrantProvider : ILoggerProvider
    {
        private readonly FakeLogCollector _collector;

        public ReentrantProvider(FakeLogCollector collector)
        {
            _collector = collector;
        }

        public Action? OnOuterRecord { get; set; }

        public ILogger CreateLogger(string categoryName) => new ReentrantLogger(this, new FakeLogger(_collector, categoryName));

        public void Dispose()
        {
            // Nothing to dispose.
        }

        private sealed class ReentrantLogger : ILogger
        {
            private readonly ReentrantProvider _provider;
            private readonly FakeLogger _logger;

            public ReentrantLogger(ReentrantProvider provider, FakeLogger logger)
            {
                _provider = provider;
                _logger = logger;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => _logger.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (_logger.Category == OuterCategory)
                {
                    _provider.OnOuterRecord?.Invoke();
                }

                _logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    // Collects the exceptions that logging swallows and reports through its event source on the current thread.
    private sealed class LoggingErrorListener : EventListener
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;

        public LoggingErrorListener()
        {
            EnableEvents(LoggingEventSource.Instance, EventLevel.Error);
        }

        public List<string> Errors { get; } = [];

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (Environment.CurrentManagedThreadId == _threadId)
            {
                Errors.Add(eventData.Payload?[0]?.ToString() ?? string.Empty);
            }
        }
    }
}
