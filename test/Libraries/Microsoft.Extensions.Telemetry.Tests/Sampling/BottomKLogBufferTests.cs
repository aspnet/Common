// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Test;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Extensions.Telemetry.Sampling;

public class BottomKLogBufferTests
{
    private static readonly Func<IReadOnlyList<KeyValuePair<string, object?>>, Exception?, string> _formatter =
        static (_, _) => "message";

    [Fact]
    public void Admit_WhenIntervalElapsed_FlushesBeforeNewAdmission()
    {
        var timeProvider = new TestTimeProvider();
        var destination = new RecordingBufferedLogger();
        using var buffer = CreateBuffer(timeProvider);

        Enqueue(buffer, destination, [new("{OriginalFormat}", "message")]);
        Assert.Empty(destination.Records);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        _ = buffer.Admit("category", new EventId(1));

        Assert.Single(destination.Records);
    }

    [Fact]
    public void Dispose_FlushesRetainedRecords()
    {
        var destination = new RecordingBufferedLogger();
        using var buffer = CreateBuffer(new TestTimeProvider());

        Enqueue(buffer, destination, [new("{OriginalFormat}", "message")]);
        buffer.Dispose();

        Assert.Single(destination.Records);
    }

    [Fact]
    public void Flush_BeforeFirstAdmission_DoesNothing()
    {
        using var buffer = CreateBuffer(new TestTimeProvider());
        var destination = new RecordingBufferedLogger();

        buffer.Flush();

        Assert.Empty(destination.Records);
    }

    [Fact]
    public void TryEnqueue_WithUnsupportedState_Throws()
    {
        using var buffer = CreateBuffer(new TestTimeProvider());
        var destination = new RecordingBufferedLogger();
        var eventId = new EventId(1);
        Assert.True(buffer.Admit("category", eventId));
        var entry = new LogEntry<string>(
            LogLevel.Information,
            "category",
            eventId,
            "unsupported",
            null,
            static (state, _) => state);

        Assert.Throws<InvalidOperationException>(() => buffer.TryEnqueue(destination, entry));
    }

    [Fact]
    public void Flush_AddsSamplingCountBeforeOriginalFormat()
    {
        var destination = new RecordingBufferedLogger();
        using var buffer = CreateBuffer(new TestTimeProvider());

        Enqueue(
            buffer,
            destination,
            [
                new("property", 42),
                new("{OriginalFormat}", "message {property}"),
            ]);
        buffer.Flush();

        BufferedLogRecord record = Assert.Single(destination.Records);
        Assert.Equal("sampling.count", record.Attributes[^2].Key);
        Assert.Equal("{OriginalFormat}", record.Attributes[^1].Key);
        Assert.True(double.IsFinite(Assert.IsType<double>(record.Attributes[^2].Value)));
    }

    [Fact]
    public void Flush_ReplacesUserSuppliedSamplingCount()
    {
        var destination = new RecordingBufferedLogger();
        using var buffer = CreateBuffer(new TestTimeProvider());

        Enqueue(
            buffer,
            destination,
            [
                new("property", 42),
                new("sampling.count", -1.0),
                new("sampling.count", "ambiguous"),
                new("{OriginalFormat}", "message {property}"),
            ]);
        buffer.Flush();

        BufferedLogRecord record = Assert.Single(destination.Records);
        Assert.Collection(
            record.Attributes,
            attribute => Assert.Equal("property", attribute.Key),
            attribute =>
            {
                Assert.Equal("sampling.count", attribute.Key);
                Assert.True(Assert.IsType<double>(attribute.Value) >= 1.0);
            },
            attribute => Assert.Equal(new("{OriginalFormat}", "message {property}"), attribute));
    }

    [Fact]
    public void LoggingPipeline_PreservesEnrichmentAndSupportsOrdinaryProvider()
    {
        using var provider = new CapturingProvider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(builder =>
        {
            builder.AddProvider(provider);
            builder.Services.AddSingleton<ILogEnricher>(new TestEnricher());
            builder.AddBottomKLogSampling(options =>
            {
                options.Capacity = 1;
                options.PreserveCapacity = 0;
                options.Rules.Add(new BottomKLogSamplingFilterRule());
            });
        });

        ILogger logger = factory.CreateLogger("category");
        logger.LogInformation("message {property}", 42);

        var disposingFactory = Assert.IsType<Utils.DisposingLoggerFactory>(factory);
        disposingFactory.ServiceProvider.GetRequiredService<LogBuffer>().Flush();

        IReadOnlyList<KeyValuePair<string, object?>> state = Assert.Single(provider.States);
        Assert.Contains(state, pair => pair.Key == "enriched" && Equals(pair.Value, "value"));
        Assert.Contains(state, pair => pair.Key == "sampling.count" && pair.Value is double weight && weight >= 1.0);
        Assert.Equal("{OriginalFormat}", state[^1].Key);
    }

    [Fact]
    public void AfterDispose_RecordsBypassBottomKBuffer()
    {
        var destination = new RecordingBufferedLogger();
        var buffer = CreateBuffer(new TestTimeProvider());
        buffer.Dispose();

        var eventId = new EventId(1);
        Assert.True(buffer.Admit("category", eventId));

        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "category",
            eventId,
            [new("{OriginalFormat}", "message")],
            null,
            _formatter);

        Assert.False(buffer.TryEnqueue(destination, entry));
        Assert.Empty(destination.Records);
    }

    [Fact]
    public void EmptyRules_ApplyBottomKToEveryLevel()
    {
        AssertUsesBottomK(
            new BottomKLogSamplingOptions(),
            "category",
            LogLevel.Critical,
            new EventId(1));
    }

    [Fact]
    public void ExplicitLogLevelRule_BypassesHigherLevels()
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule(logLevel: LogLevel.Warning)],
        };

        AssertBypasses(options, "category", LogLevel.Error, new EventId(1));
    }

    [Fact]
    public void EventIdRule_AppliesBottomK()
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule(eventId: 42)],
        };

        AssertUsesBottomK(options, "category", LogLevel.Information, new EventId(42));
    }

    [Fact]
    public void EventNameWithoutOtherMatchingCriteria_DoesNotAffectSelection()
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule(eventName: "event")],
        };

        AssertUsesBottomK(options, "category", LogLevel.Information, new EventId(1, "different"));
    }

    [Theory]
    [InlineData("Contoso.Security.Component", "contoso.security.component")]
    [InlineData("Contoso.Security.Component", "contoso.security.*")]
    [InlineData("Prefix.Component.Suffix", "prefix*suffix")]
    public void CategoryRule_AppliesBottomK(string category, string pattern)
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule(categoryName: pattern)],
        };

        AssertUsesBottomK(options, category, LogLevel.Information, new EventId(1));
    }

    [Fact]
    public void CategoryOutsideSampledAllowlist_BypassesBottomKBuffer()
    {
        var options = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule(categoryName: "Sampled.*")],
        };

        AssertBypasses(options, "Retained.Category", LogLevel.Information, new EventId(1));
    }

    [Fact]
    public void OptionsChange_AppliesPolicyToNextAdmission()
    {
        var initial = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        var monitor = new MutableOptionsMonitor(initial);
        using var buffer = new BottomKLogBuffer(monitor, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();

        Assert.True(buffer.Admit("category", LogLevel.Information, new EventId(1)));

        monitor.Set(new BottomKLogSamplingOptions { Enabled = false });

        Assert.True(buffer.Admit("category", LogLevel.Information, new EventId(2)));
        Assert.False(TryEnqueue(buffer, destination, new EventId(2)));
    }

    [Fact]
    public void OptionsChange_RebuildsAllCompiledBypassPolicies()
    {
        var initial = new BottomKLogSamplingOptions
        {
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        var monitor = new MutableOptionsMonitor(initial);
        using var buffer = new BottomKLogBuffer(monitor, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();

        var updated = new BottomKLogSamplingOptions
        {
            Rules =
            [
                new(categoryName: "Sampled.Exact", logLevel: LogLevel.Information),
                new(categoryName: "Sampled.*", logLevel: LogLevel.Information),
                new(eventId: 42),
            ],
        };
        monitor.Set(updated);

        AssertBypasses(buffer, destination, "category", LogLevel.Warning, new EventId(1));
        AssertBypasses(buffer, destination, "exact.protected", LogLevel.Information, new EventId(1));
        AssertBypasses(buffer, destination, "Wildcard.Component", LogLevel.Information, new EventId(1));
        AssertBypasses(buffer, destination, "Outside.Allowlist", LogLevel.Information, new EventId(1));

        var eventRuleEventId = new EventId(42);
        Assert.True(buffer.Admit("category", LogLevel.Information, eventRuleEventId));
        Assert.True(TryEnqueue(
            buffer,
            destination,
            "category",
            LogLevel.Information,
            eventRuleEventId));

        var sampledEventId = new EventId(2);
        Assert.True(buffer.Admit("sampled.exact", LogLevel.Information, sampledEventId));
        Assert.True(TryEnqueue(
            buffer,
            destination,
            "sampled.exact",
            LogLevel.Information,
            sampledEventId));

        var replacement = new BottomKLogSamplingOptions
        {
            Rules = [new(categoryName: "Replacement.Sampled")],
        };
        monitor.Set(replacement);

        var oldCategoryEventId = new EventId(3);
        AssertBypasses(
            buffer,
            destination,
            "Exact.Protected",
            LogLevel.Information,
            oldCategoryEventId);
        Assert.True(buffer.Admit(
            "replacement.sampled",
            LogLevel.Information,
            new EventId(4)));
        Assert.True(TryEnqueue(
            buffer,
            destination,
            "replacement.sampled",
            LogLevel.Information,
            new EventId(4)));
    }

    [Fact]
    public void AlgorithmOptionsChange_FlushesRecordsRetainedUnderPreviousConfiguration()
    {
        var initial = new BottomKLogSamplingOptions
        {
            Capacity = 1,
            PreserveCapacity = 0,
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        var monitor = new MutableOptionsMonitor(initial);
        using var buffer = new BottomKLogBuffer(monitor, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();
        Enqueue(buffer, destination, [new("{OriginalFormat}", "message")]);

        var updated = new BottomKLogSamplingOptions
        {
            Capacity = 2,
            PreserveCapacity = 0,
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        monitor.Set(updated);
        _ = buffer.Admit("category", LogLevel.Information, new EventId(2));

        Assert.Single(destination.Records);
    }

    [Fact]
    public async Task AlgorithmOptionsChange_InvalidatesAdmissionFromPreviousGeneration()
    {
        var initial = new BottomKLogSamplingOptions
        {
            Capacity = 1,
            PreserveCapacity = 0,
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        var monitor = new MutableOptionsMonitor(initial);
        using var buffer = new BottomKLogBuffer(monitor, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();
        using var admitted = new ManualResetEventSlim();
        using var continueInsertion = new ManualResetEventSlim();

        Task<bool> insertion = Task.Run(() =>
        {
            bool result = buffer.Admit("category", LogLevel.Information, new EventId(1));
            admitted.Set();
            continueInsertion.Wait();
            return result && TryEnqueue(buffer, destination, new EventId(1));
        });

        admitted.Wait();
        var updated = new BottomKLogSamplingOptions
        {
            Capacity = 2,
            PreserveCapacity = 0,
            Rules = [new BottomKLogSamplingFilterRule()],
        };
        monitor.Set(updated);
        _ = buffer.Admit("category", LogLevel.Information, new EventId(2));
        continueInsertion.Set();

        Assert.True(await insertion);
        Assert.Empty(destination.Records);
    }

    [Fact]
    public async Task Dispose_RacingWithInsertion_EitherFlushesOrBypassesRecord()
    {
        for (int i = 0; i < 100; i++)
        {
            using var buffer = CreateBuffer(new TestTimeProvider());
            var destination = new RecordingBufferedLogger();
            using var ready = new Barrier(2);
            var eventId = new EventId(i);

            Task<bool> insertion = Task.Run(() =>
            {
                Assert.True(buffer.Admit("category", eventId));
                ready.SignalAndWait();
                return TryEnqueue(buffer, destination, eventId);
            });
            Task disposal = Task.Run(() =>
            {
                ready.SignalAndWait();
                buffer.Dispose();
            });

            bool buffered = await insertion;
            await disposal;

            Assert.Equal(buffered ? 1 : 0, destination.Records.Count);
        }
    }

    [Fact]
    public void AutomaticFlush_WhenOneDestinationThrows_ContinuesFlushingOtherCategories()
    {
        var timeProvider = new TestTimeProvider();
        using var buffer = CreateBuffer(timeProvider);
        var throwingDestination = new ThrowingBufferedLogger();
        var recordingDestination = new RecordingBufferedLogger();
        Enqueue(buffer, throwingDestination, "throwing-category", new EventId(1));
        Enqueue(buffer, recordingDestination, "recording-category", new EventId(2));

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Exception? exception = Record.Exception(() =>
            buffer.Admit("trigger-category", LogLevel.Information, new EventId(3)));
        Assert.Null(exception);
        Assert.Single(recordingDestination.Records);
    }

    [Fact]
    public void ServiceProviderDispose_FlushesBeforeProviderDisposal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposalAwareProvider>();
        services.AddSingleton<ILoggerProvider>(
            static provider => provider.GetRequiredService<DisposalAwareProvider>());
        services.AddLogging(builder =>
        {
            builder.AddBottomKLogSampling(options =>
            {
                options.Capacity = 1;
                options.PreserveCapacity = 0;
                options.Rules.Add(new BottomKLogSamplingFilterRule());
            });
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        DisposalAwareProvider provider = serviceProvider.GetRequiredService<DisposalAwareProvider>();
        ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("category");
        logger.LogInformation("message");
        Assert.Empty(provider.States);

        serviceProvider.Dispose();

        Assert.Single(provider.States);
        Assert.True(provider.IsDisposed);
    }

    private static BottomKLogBuffer CreateBuffer(TimeProvider timeProvider)
        => new(
            new BottomKLogSamplingOptions
            {
                Capacity = 1,
                PreserveCapacity = 0,
                FlushInterval = TimeSpan.FromSeconds(1),
                Rules = [new BottomKLogSamplingFilterRule()],
            },
            timeProvider);

    private static void Enqueue(
        BottomKLogBuffer buffer,
        IBufferedLogger destination,
        IReadOnlyList<KeyValuePair<string, object?>> state)
    {
        var eventId = new EventId(1);
        Assert.True(buffer.Admit("category", eventId));

        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "category",
            eventId,
            state,
            null,
            _formatter);

        Assert.True(buffer.TryEnqueue(destination, entry));
    }

    private static void Enqueue(
        BottomKLogBuffer buffer,
        IBufferedLogger destination,
        string category,
        EventId eventId)
    {
        Assert.True(buffer.Admit(category, eventId));

        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            category,
            eventId,
            [new("{OriginalFormat}", "message")],
            null,
            _formatter);

        Assert.True(buffer.TryEnqueue(destination, entry));
    }

    private static bool TryEnqueue(
        BottomKLogBuffer buffer,
        IBufferedLogger destination,
        EventId eventId)
        => TryEnqueue(buffer, destination, "category", LogLevel.Information, eventId);

    private static bool TryEnqueue(
        BottomKLogBuffer buffer,
        IBufferedLogger destination,
        string category,
        LogLevel logLevel,
        EventId eventId)
    {
        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            logLevel,
            category,
            eventId,
            [new("{OriginalFormat}", "message")],
            null,
            _formatter);

        return buffer.TryEnqueue(destination, entry);
    }

    private static void AssertBypasses(
        BottomKLogBuffer buffer,
        IBufferedLogger destination,
        string category,
        LogLevel logLevel,
        EventId eventId)
    {
        Assert.True(buffer.Admit(category, logLevel, eventId));
        Assert.False(TryEnqueue(buffer, destination, category, logLevel, eventId));
    }

    private static void AssertBypasses(
        BottomKLogSamplingOptions options,
        string category,
        LogLevel logLevel,
        EventId eventId)
    {
        using var buffer = new BottomKLogBuffer(options, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();
        Assert.True(buffer.Admit(category, logLevel, eventId));

        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            logLevel,
            category,
            eventId,
            [new("{OriginalFormat}", "message")],
            null,
            _formatter);

        Assert.False(buffer.TryEnqueue(destination, entry));
        Assert.Empty(destination.Records);
    }

    private static void AssertUsesBottomK(
        BottomKLogSamplingOptions options,
        string category,
        LogLevel logLevel,
        EventId eventId)
    {
        using var buffer = new BottomKLogBuffer(options, new TestTimeProvider());
        var destination = new RecordingBufferedLogger();
        Assert.True(buffer.Admit(category, logLevel, eventId));
        Assert.True(TryEnqueue(buffer, destination, category, logLevel, eventId));
    }

    private sealed class RecordingBufferedLogger : IBufferedLogger
    {
        public List<BufferedLogRecord> Records { get; } = [];

        public void LogRecords(IEnumerable<BufferedLogRecord> records)
            => Records.AddRange(records);
    }

    private sealed class ThrowingBufferedLogger : IBufferedLogger
    {
        public void LogRecords(IEnumerable<BufferedLogRecord> records)
            => throw new InvalidOperationException("Test failure.");
    }

    private sealed class MutableOptionsMonitor : IOptionsMonitor<BottomKLogSamplingOptions>
    {
        private Action<BottomKLogSamplingOptions, string?>? _listener;

        public MutableOptionsMonitor(BottomKLogSamplingOptions value)
        {
            CurrentValue = value;
        }

        public BottomKLogSamplingOptions CurrentValue { get; private set; }

        public BottomKLogSamplingOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<BottomKLogSamplingOptions, string?> listener)
        {
            _listener = listener;
            return new ChangeToken(this);
        }

        public void Set(BottomKLogSamplingOptions value)
        {
            CurrentValue = value;
            _listener?.Invoke(value, string.Empty);
        }

        private sealed class ChangeToken : IDisposable
        {
            private MutableOptionsMonitor? _owner;

            public ChangeToken(MutableOptionsMonitor owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner is { } owner)
                {
                    owner._listener = null;
                    _owner = null;
                }
            }
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class TestEnricher : ILogEnricher
    {
        public void Enrich(IEnrichmentTagCollector collector)
            => collector.Add("enriched", "value");
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<IReadOnlyList<KeyValuePair<string, object?>>> States { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingProvider _provider;

            public CapturingLogger(CapturingProvider provider)
            {
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (state is IReadOnlyList<KeyValuePair<string, object?>> attributes)
                {
                    _provider.States.Add(attributes);
                }
            }
        }
    }

    private sealed class DisposalAwareProvider : ILoggerProvider
    {
        public bool IsDisposed { get; private set; }

        public List<IReadOnlyList<KeyValuePair<string, object?>>> States { get; } = [];

        public ILogger CreateLogger(string categoryName) => new DisposalAwareLogger(this);

        public void Dispose() => IsDisposed = true;

        private sealed class DisposalAwareLogger : ILogger
        {
            private readonly DisposalAwareProvider _provider;

            public DisposalAwareLogger(DisposalAwareProvider provider)
            {
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Assert.False(_provider.IsDisposed);
                if (state is IReadOnlyList<KeyValuePair<string, object?>> attributes)
                {
                    _provider.States.Add(attributes);
                }
            }
        }
    }
}
#endif
