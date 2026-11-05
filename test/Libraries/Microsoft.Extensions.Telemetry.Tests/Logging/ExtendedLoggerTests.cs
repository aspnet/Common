// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
#if NET9_0_OR_GREATER
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.Buffering;
#endif

namespace Microsoft.Extensions.Logging.Test;

public static class ExtendedLoggerTests
{
    [Theory]
    [CombinatorialData]
    public static void FeatureEnablement(bool enableRedaction, bool enableEnrichment)
    {
        const string Category = "C1";

        using var provider = new Provider();
        var enricher = new ForcedEnricher(
            new[]
            {
                new KeyValuePair<string, object?>("EK1", "EV1"),
            });

        var staticEnricher = new ForcedEnricher(
            new[]
            {
                new KeyValuePair<string, object?>("SEK1", "SEV1"),
            });

        var redactorProvider = new FakeRedactorProvider(new FakeRedactorOptions
        {
            RedactionFormat = "REDACTED<{0}>",
        });

        var enrichmentOptions = enableEnrichment ? new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()) : null;
        var redactionOptions = enableRedaction ? new StaticOptionsMonitor<LoggerRedactionOptions>(new() { ApplyDiscriminator = false }) : null;

        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: enrichmentOptions,
            redactionOptions: redactionOptions,
            enrichers: new[] { enricher },
            staticEnrichers: new[] { staticEnricher },
            redactorProvider: redactorProvider,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger(Category);
        logger.LogWarning("MSG0");

        var lms = LoggerMessageHelper.ThreadLocalState;
        var index = lms.ReserveTagSpace(1);
        lms.TagArray[index] = new("PK2", "PV2");

        index = lms.ReserveClassifiedTagSpace(2);
        lms.ClassifiedTagArray[index] = new("PK3", "PV3", FakeTaxonomy.PrivateData);
        lms.ClassifiedTagArray[index + 1] = new("PK4", null, FakeTaxonomy.PrivateData);

        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), lms, null, (_, _) => "MSG2");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(2, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);

        if (enableEnrichment)
        {
            Assert.Equal("SEV1", snap[0].GetStructuredStateValue("SEK1"));
            Assert.Equal("EV1", snap[0].GetStructuredStateValue("EK1"));
        }
        else
        {
            Assert.Null(snap[0].GetStructuredStateValue("SEK1"));
            Assert.Null(snap[0].GetStructuredStateValue("EK1"));
        }

        Assert.Equal(Category, snap[1].Category);
        Assert.Null(snap[1].Exception);
        Assert.Equal(new EventId(2, "ID2"), snap[1].Id);
        Assert.Equal("MSG2", snap[1].Message);
        Assert.Equal("PV2", snap[1].GetStructuredStateValue("PK2"));

        if (enableRedaction)
        {
            Assert.Equal("REDACTED<PV3>", snap[1].GetStructuredStateValue("PK3"));
        }
        else
        {
            Assert.Equal("PV3", snap[1].GetStructuredStateValue("PK3"));
        }

        Assert.Null(snap[1].GetStructuredStateValue("PK4"));

        if (enableEnrichment)
        {
            Assert.Equal("SEV1", snap[1].GetStructuredStateValue("SEK1"));
            Assert.Equal("EV1", snap[1].GetStructuredStateValue("EK1"));
        }
        else
        {
            Assert.Null(snap[1].GetStructuredStateValue("SEK1"));
            Assert.Null(snap[1].GetStructuredStateValue("EK1"));
        }
    }

    [Fact]
    public static void Sampling()
    {
        const string Category = "C1";

        RandomProbabilisticSamplerOptions options = new();
        options.Rules.Add(new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Warning));
        LogSamplingRuleSelector<RandomProbabilisticSamplerFilterRule> ruleSelector = new();
        using var sampler = new RandomProbabilisticSampler(ruleSelector, new StaticOptionsMonitor<RandomProbabilisticSamplerOptions>(options));

        using var provider = new Provider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(
             builder =>
             {
                 builder.AddProvider(provider);
                 builder.AddRandomProbabilisticSampler(0, LogLevel.Warning);
             });
        ILogger logger = factory.CreateLogger(Category);

        // Act
        // 1, no state (legacy path)
        logger.LogWarning("MSG0");

        // 2, with Modern state
        LoggerMessageState lms = LoggerMessageHelper.ThreadLocalState;
        int index = lms.ReserveTagSpace(1);
        lms.TagArray[index] = new("PK2", "PV2");
        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), lms, null, (_, _) => "MSG2");

        Assert.Equal(0, provider.Logger!.Collector.Count);
    }

    [Theory]
    [CombinatorialData]
    public static void BagAndJoiner(bool objectVersion)
    {
        const string Category = "C1";

        using var provider = new Provider();
        var enricher = new FancyEnricher(
            new[]
            {
                new KeyValuePair<string, object?>("EK1", "EV1"),
                new KeyValuePair<string, object?>("EK2", "EV2"),
            }, objectVersion);

        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: new[] { enricher },
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: null,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger(Category);
        logger.LogWarning("MSG0");

        var lms = LoggerMessageHelper.ThreadLocalState;
        var index = lms.ReserveTagSpace(1);
        lms.TagArray[index] = new("PK2", "PV2");
        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), lms, null, (_, _) => "MSG2");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(2, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);
        Assert.Equal("EV1", snap[0].GetStructuredStateValue("EK1"));
        Assert.Equal("EV2", snap[0].GetStructuredStateValue("EK2"));

        Assert.Equal(Category, snap[1].Category);
        Assert.Null(snap[1].Exception);
        Assert.Equal(new EventId(2, "ID2"), snap[1].Id);
        Assert.Equal("MSG2", snap[1].Message);
        Assert.Equal("PV2", snap[1].GetStructuredStateValue("PK2"));
        Assert.Equal("EV1", snap[1].GetStructuredStateValue("EK1"));
        Assert.Equal("EV2", snap[1].GetStructuredStateValue("EK2"));
    }

    [Fact]
    public static void NullStateObject()
    {
        const string Category = "C1";

        using var provider = new Provider();
        var enricher = new ForcedEnricher(
            new[]
            {
                new KeyValuePair<string, object?>("EK1", "EV1"),
            });

        var staticEnricher = new ForcedEnricher(
            new[]
            {
                new KeyValuePair<string, object?>("SEK1", "SEV1"),
            });

        var redactorProvider = new FakeRedactorProvider(new FakeRedactorOptions
        {
            RedactionFormat = "REDACTED<{0}>",
        });

        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: new[] { enricher },
            staticEnrichers: new[] { staticEnricher },
            redactorProvider: redactorProvider,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger(Category);
        logger.Log<object?>(LogLevel.Error, new EventId(0, "ID0"), null, null, (_, _) => "MSG0");
        logger.Log<object?>(LogLevel.Error, new EventId(0, "ID0b"), null, null, (_, _) => "MSG0b");

        var lms = LoggerMessageHelper.ThreadLocalState;
        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), (LoggerMessageState?)null, null, (_, _) => "MSG2");
        logger.Log(LogLevel.Warning, new EventId(2, "ID2b"), (LoggerMessageState?)null, null, (_, _) => "MSG2b");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(4, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0, "ID0"), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);
        Assert.Equal("EV1", snap[0].GetStructuredStateValue("EK1"));
        Assert.Equal("SEV1", snap[0].GetStructuredStateValue("SEK1"));

        Assert.Equal(Category, snap[1].Category);
        Assert.Null(snap[1].Exception);
        Assert.Equal(new EventId(0, "ID0b"), snap[1].Id);
        Assert.Equal("MSG0b", snap[1].Message);
        Assert.Equal("EV1", snap[1].GetStructuredStateValue("EK1"));
        Assert.Equal("SEV1", snap[1].GetStructuredStateValue("SEK1"));

        Assert.Equal(Category, snap[2].Category);
        Assert.Null(snap[2].Exception);
        Assert.Equal(new EventId(2, "ID2"), snap[2].Id);
        Assert.Equal("MSG2", snap[2].Message);
        Assert.Equal("EV1", snap[2].GetStructuredStateValue("EK1"));
        Assert.Equal("SEV1", snap[2].GetStructuredStateValue("SEK1"));

        Assert.Equal(Category, snap[3].Category);
        Assert.Null(snap[3].Exception);
        Assert.Equal(new EventId(2, "ID2b"), snap[3].Id);
        Assert.Equal("MSG2b", snap[3].Message);
        Assert.Equal("EV1", snap[3].GetStructuredStateValue("EK1"));
        Assert.Equal("SEV1", snap[3].GetStructuredStateValue("SEK1"));
    }

    [Fact]
    public static void EnumerableStateObject()
    {
        const string Category = "C1";

        using var provider = new Provider();
        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: Array.Empty<ILogEnricher>(),
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: null,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger(Category);

        var a = new[] { new KeyValuePair<string, object?>("K1", "V1") };
        var e = a.Where(_ => true);

        logger.Log(LogLevel.Warning, new EventId(0, "ID0"), e, null, (_, _) => "MSG0");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(1, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0, "ID0"), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);
        Assert.Equal("V1", snap[0].GetStructuredStateValue("K1"));
    }

    [Fact]
    public static void Filtering()
    {
        const string FilteredCategory = "C1";
        const string UnfilteredCategory = "C2";

        var filterOptions = new LoggerFilterOptions();
        filterOptions.Rules.Add(new LoggerFilterRule(null, FilteredCategory, null, (_, _, _) => false));

        using var provider = new Provider();
        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(filterOptions),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: Array.Empty<ILogEnricher>(),
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: null,
            scopeProvider: null,
            factoryOptions: null);

        var filteredLogger = lf.CreateLogger(FilteredCategory);
        var unfilteredLogger = lf.CreateLogger(UnfilteredCategory);

        Assert.False(filteredLogger.IsEnabled(LogLevel.Warning));
        Assert.True(unfilteredLogger.IsEnabled(LogLevel.Warning));

        var fake = provider.Logger!;
        fake.ControlLevel(LogLevel.Warning, false);

        Assert.False(filteredLogger.IsEnabled(LogLevel.Warning));
        Assert.False(unfilteredLogger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public static void StringStateObject()
    {
        const string Category = "C1";

        using var provider = new Provider();
        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: Array.Empty<ILogEnricher>(),
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: null,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger(Category);

        logger.Log(LogLevel.Warning, new EventId(0, "ID0"), "PAYLOAD", null, (_, _) => "MSG0");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(1, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0, "ID0"), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);
        Assert.Equal("PAYLOAD", snap[0].GetStructuredStateValue("{OriginalFormat}"));
    }

    [Fact]
    public static void StateToStringWorks()
    {
        using var provider = new CapturingProvider();
        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: Array.Empty<ILogEnricher>(),
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: null,
            scopeProvider: null,
            factoryOptions: null);

        var logger = lf.CreateLogger("FOO");

        logger.Log(LogLevel.Information, new EventId(0, "ID0"), "PAYLOAD", null, (_, _) => "MSG0");

        Assert.Equal("PAYLOAD", provider.State!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public static void Exceptions(bool includeExceptionMessage)
    {
        const string Category = "C1";

        using var provider = new Provider();
        var stackTraceOptions = new LoggerEnrichmentOptions
        {
            CaptureStackTraces = true,
            UseFileInfoForStackTraces = true,
            MaxStackTraceLength = 4096,
            IncludeExceptionMessage = includeExceptionMessage,
        };

        using var lf = new ExtendedLoggerFactory(
            providers: new[] { provider },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()),
            enrichmentOptions: new StaticOptionsMonitor<LoggerEnrichmentOptions>(stackTraceOptions),
            redactionOptions: new StaticOptionsMonitor<LoggerRedactionOptions>(new()),
            enrichers: Array.Empty<ILogEnricher>(),
            staticEnrichers: Array.Empty<IStaticLogEnricher>(),
            redactorProvider: new FakeRedactorProvider(),
            scopeProvider: null,
            factoryOptions: null);

        Exception ex;
        try
        {
            List<Exception> exceptions = [];
            try
            {
                throw new ArgumentNullException("EM1", (Exception?)null);
            }
            catch (ArgumentNullException e)
            {
                exceptions.Add(e);
            }

            try
            {
                throw new ArgumentOutOfRangeException("EM2", (Exception?)null);
            }
            catch (ArgumentOutOfRangeException e)
            {
                exceptions.Add(e);
            }

            try
            {
                throw new InvalidOperationException("EM3");
            }
            catch (InvalidOperationException e)
            {
                exceptions.Add(e);
            }

            throw new AggregateException("EM4", exceptions);
        }
        catch (AggregateException e)
        {
            ex = e;
        }

        var logger = lf.CreateLogger(Category);
        logger.Log<object?>(LogLevel.Error, new EventId(0, "ID0"), null, null, (_, _) => "MSG0");
        logger.Log<object?>(LogLevel.Error, new EventId(0, "ID0b"), null, ex, (_, _) => "MSG0b");

        var lms = LoggerMessageHelper.ThreadLocalState;
        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), lms, null, (_, _) => "MSG2");
        logger.Log(LogLevel.Warning, new EventId(2, "ID2b"), lms, ex, (_, _) => "MSG2b");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(Category, sink.Category);
        Assert.Equal(4, collector.Count);

        var snap = collector.GetSnapshot();

        Assert.Equal(Category, snap[0].Category);
        Assert.Null(snap[0].Exception);
        Assert.Equal(new EventId(0, "ID0"), snap[0].Id);
        Assert.Equal("MSG0", snap[0].Message);

        Assert.Equal(Category, snap[1].Category);
        Assert.NotNull(snap[1].Exception);
        Assert.Equal(new EventId(0, "ID0b"), snap[1].Id);
        Assert.Equal("MSG0b", snap[1].Message);

        Assert.Equal(Category, snap[2].Category);
        Assert.Null(snap[2].Exception);
        Assert.Equal(new EventId(2, "ID2"), snap[2].Id);
        Assert.Equal("MSG2", snap[2].Message);

        Assert.Equal(Category, snap[3].Category);
        Assert.NotNull(snap[3].Exception);
        Assert.Equal(new EventId(2, "ID2b"), snap[3].Id);
        Assert.Equal("MSG2b", snap[3].Message);

        var exceptionType = snap[3].GetStructuredStateValue("exception.type")!;
        Assert.Equal("System.AggregateException", exceptionType);

        var stackTrace = snap[3].GetStructuredStateValue("exception.stacktrace")!;
        Assert.Contains("AggregateException", stackTrace);
        Assert.Contains("ArgumentNullException", stackTrace);
        Assert.Contains("ArgumentOutOfRangeException", stackTrace);
        Assert.Contains("InvalidOperationException", stackTrace);

        if (includeExceptionMessage)
        {
            var exceptionMessage = snap[3].GetStructuredStateValue("exception.message");
#if NETFRAMEWORK
            // On .NET Framework, AggregateException.Message does not include inner exception messages
            Assert.Equal("EM4", exceptionMessage);
#else
            Assert.Equal("EM4 (EM1) (EM2) (EM3)", exceptionMessage);
#endif

            Assert.Contains("EM1", stackTrace);
            Assert.Contains("EM2", stackTrace);
            Assert.Contains("EM3", stackTrace);
            Assert.Contains("EM4", stackTrace);
        }
        else
        {
            var state = snap[3].StructuredState;
            Assert.DoesNotContain(state!, x => x.Key == "exception.message");

            Assert.DoesNotContain("EM1", stackTrace);
            Assert.DoesNotContain("EM2", stackTrace);
            Assert.DoesNotContain("EM3", stackTrace);
            Assert.DoesNotContain("EM4", stackTrace);
        }
    }

#if false
    [Fact]
    public void Log_IgnoresExceptionInIntermediateLoggersAndThrowsAggregateException()
    {
        // Arrange
        var store = new List<string>();
        var loggerFactory = TestLoggerBuilder.Create(builder => builder
            .AddProvider(new CustomLoggerProvider("provider1", ThrowExceptionAt.None, store))
            .AddProvider(new CustomLoggerProvider("provider2", ThrowExceptionAt.Log, store))
            .AddProvider(new CustomLoggerProvider("provider3", ThrowExceptionAt.None, store)));

        var logger = loggerFactory.CreateLogger("Test");

        // Act
        var aggregateException = Assert.Throws<AggregateException>(() => logger.LogInformation("Hello!"));

        // Assert
        Assert.Equal(new[] { "provider1.Test-Hello!", "provider3.Test-Hello!" }, store);
        Assert.NotNull(aggregateException);
        Assert.StartsWith("An error occurred while writing to logger(s).", aggregateException.Message);
        Assert.Single(aggregateException.InnerExceptions);
        var exception = aggregateException.InnerExceptions[0];
        Assert.Equal("provider2.Test-Error occurred while logging data.", exception.Message);
    }

    [Fact]
    public static void BeginScope_IgnoresExceptionInIntermediateLoggersAndThrowsAggregateException()
    {
        // Arrange
        var store = new List<string>();
        var loggerFactory = TestLoggerBuilder.Create(builder => builder
            .AddProvider(new CustomLoggerProvider("provider1", ThrowExceptionAt.None, store))
            .AddProvider(new CustomLoggerProvider("provider2", ThrowExceptionAt.BeginScope, store))
            .AddProvider(new CustomLoggerProvider("provider3", ThrowExceptionAt.None, store)));

        var logger = loggerFactory.CreateLogger("Test");

        // Act
        var aggregateException = Assert.Throws<AggregateException>(() => logger.BeginScope("Scope1"));

        // Assert
        Assert.Equal(new[] { "provider1.Test-Scope1", "provider3.Test-Scope1" }, store);
        Assert.NotNull(aggregateException);
        Assert.StartsWith("An error occurred while writing to logger(s).", aggregateException.Message);
        Assert.Single(aggregateException.InnerExceptions);
        var exception = aggregateException.InnerExceptions[0];
        Assert.Equal("provider2.Test-Error occurred while creating scope.", exception.Message);
    }

    [Fact]
    public static void IsEnabled_IgnoresExceptionInIntermediateLoggers()
    {
        // Arrange
        var store = new List<string>();
        var loggerFactory = TestLoggerBuilder.Create(builder => builder
            .AddProvider(new CustomLoggerProvider("provider1", ThrowExceptionAt.None, store))
            .AddProvider(new CustomLoggerProvider("provider2", ThrowExceptionAt.IsEnabled, store))
            .AddProvider(new CustomLoggerProvider("provider3", ThrowExceptionAt.None, store)));

        var logger = loggerFactory.CreateLogger("Test");

        // Act
        var aggregateException = Assert.Throws<AggregateException>(() => logger.LogInformation("Hello!"));

        // Assert
        Assert.Equal(new[] { "provider1.Test-Hello!", "provider3.Test-Hello!" }, store);
        Assert.NotNull(aggregateException);
        Assert.StartsWith("An error occurred while writing to logger(s).", aggregateException.Message);
        Assert.Single(aggregateException.InnerExceptions);
        var exception = aggregateException.InnerExceptions[0];
        Assert.Equal("provider2.Test-Error occurred while checking if logger is enabled.", exception.Message);
    }

    [Fact]
    public static void Log_AggregatesExceptionsFromMultipleLoggers()
    {
        // Arrange
        var store = new List<string>();
        var loggerFactory = TestLoggerBuilder.Create(builder => builder
            .AddProvider(new CustomLoggerProvider("provider1", ThrowExceptionAt.Log, store))
            .AddProvider(new CustomLoggerProvider("provider2", ThrowExceptionAt.Log, store)));

        var logger = loggerFactory.CreateLogger("Test");

        // Act
        var aggregateException = Assert.Throws<AggregateException>(() => logger.LogInformation("Hello!"));

        // Assert
        Assert.Empty(store);
        Assert.NotNull(aggregateException);
        Assert.StartsWith("An error occurred while writing to logger(s).", aggregateException.Message);
        var exceptions = aggregateException.InnerExceptions;
        Assert.Equal(2, exceptions.Count);
        Assert.Equal("provider1.Test-Error occurred while logging data.", exceptions[0].Message);
        Assert.Equal("provider2.Test-Error occurred while logging data.", exceptions[1].Message);
    }
#endif

    [Fact]
    public static void LoggerCanGetProviderAfterItIsCreated()
    {
        // Arrange
        var store = new List<string>();
        using var loggerFactory = new LoggerFactory();
        var logger = loggerFactory.CreateLogger("Test");
        using var provider = new CustomLoggerProvider("provider1", ThrowExceptionAt.None, store);

        loggerFactory.AddProvider(provider);

        // Act
        logger.LogInformation("Hello");

        // Assert
        Assert.Equal(new[] { "provider1.Test-Hello" }, store);
    }

    [Fact]
    public static void ScopesAreNotCreatedForDisabledLoggers()
    {
        var provider = new Mock<ILoggerProvider>();
        var logger = new Mock<ILogger>();

        provider.Setup(loggerProvider => loggerProvider.CreateLogger(It.IsAny<string>()))
            .Returns(logger.Object);

        using var factory = Utils.CreateLoggerFactory(
            builder =>
            {
                builder.AddProvider(provider.Object);

                // Disable all logs
                builder.AddFilter(null, LogLevel.None);
            });

        var newLogger = factory.CreateLogger("Logger");
        using (newLogger.BeginScope("Scope"))
        {
            // nop
        }

        provider.Verify(p => p.CreateLogger("Logger"), Times.Once);
        logger.Verify(l => l.BeginScope(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public static void ScopesAreNotCreatedWhenScopesAreDisabled()
    {
        var provider = new Mock<ILoggerProvider>();
        var logger = new Mock<ILogger>();

        provider.Setup(loggerProvider => loggerProvider.CreateLogger(It.IsAny<string>()))
            .Returns(logger.Object);

        using var factory = Utils.CreateLoggerFactory(
            builder =>
            {
                builder.AddProvider(provider.Object);
                builder.Services.Configure<LoggerFilterOptions>(options => options.CaptureScopes = false);
            });

        var newLogger = factory.CreateLogger("Logger");
        using (newLogger.BeginScope("Scope"))
        {
            // nop
        }

        provider.Verify(p => p.CreateLogger("Logger"), Times.Once);
        logger.Verify(l => l.BeginScope(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public static void ScopesAreNotCreatedInIScopeProviderWhenScopesAreDisabled()
    {
        var provider = new Mock<ILoggerProvider>();
        var logger = new Mock<ILogger>();

        IExternalScopeProvider? externalScopeProvider = null;

        provider.Setup(loggerProvider => loggerProvider.CreateLogger(It.IsAny<string>()))
            .Returns(logger.Object);
        provider.As<ISupportExternalScope>().Setup(scope => scope.SetScopeProvider(It.IsAny<IExternalScopeProvider>()))
            .Callback((IExternalScopeProvider scopeProvider) => externalScopeProvider = scopeProvider);

        using var factory = Utils.CreateLoggerFactory(
            builder =>
            {
                builder.AddProvider(provider.Object);
                builder.Services.Configure<LoggerFilterOptions>(options => options.CaptureScopes = false);
            });

        var newLogger = factory.CreateLogger("Logger");
        int scopeCount = 0;

        using (newLogger.BeginScope("Scope"))
        {
            externalScopeProvider!.ForEachScope<object>((_, _) => scopeCount++, null!);
        }

        provider.Verify(p => p.CreateLogger("Logger"), Times.Once);
        logger.Verify(l => l.BeginScope(It.IsAny<object>()), Times.Never);
        Assert.Equal(0, scopeCount);
    }

    [Fact]
    public static void CaptureScopesIsReadFromConfiguration()
    {
        var provider = new Mock<ILoggerProvider>();
        var logger = new Mock<ILogger>();
        var json = @"{ ""CaptureScopes"": ""false"" }";

        using var config = TestConfiguration.Create(() => json);
        IExternalScopeProvider? externalScopeProvider = null;

        provider.Setup(loggerProvider => loggerProvider.CreateLogger(It.IsAny<string>()))
            .Returns(logger.Object);
        provider.As<ISupportExternalScope>().Setup(scope => scope.SetScopeProvider(It.IsAny<IExternalScopeProvider>()))
            .Callback((IExternalScopeProvider scopeProvider) => externalScopeProvider = scopeProvider);

        using var factory = Utils.CreateLoggerFactory(
            builder =>
            {
                builder.AddProvider(provider.Object);
                builder.AddConfiguration(config);
            });

        var newLogger = factory.CreateLogger("Logger");
        int scopeCount = 0;

        using (newLogger.BeginScope("Scope"))
        {
            externalScopeProvider!.ForEachScope<object>((_, _) => scopeCount++, null!);
            Assert.Equal(0, scopeCount);
        }

        json = @"{ ""CaptureScopes"": ""true"" }";
        config.Reload();

        scopeCount = 0;
        using (newLogger.BeginScope("Scope"))
        {
            externalScopeProvider.ForEachScope<object>((_, _) => scopeCount++, null!);
            Assert.Equal(1, scopeCount);
        }
    }

    [Theory]
    [CombinatorialData]
    public static void ModernLogging_OriginalFormatMustBeLastInTheListOfStateProperties(
        bool enableRedaction, bool enableEnrichment, bool logException)
    {
        using var provider = new Provider();

        var enricher = new ForcedEnricher(new[]
        {
            new KeyValuePair<string, object?>("K1", "V1"),
        });
        var staticEnricher = new ForcedEnricher(new[]
        {
            new KeyValuePair<string, object?>("K2", "V2"),
        });
        var redactorProvider = new FakeRedactorProvider(new FakeRedactorOptions
        {
            RedactionFormat = "REDACTED<{0}>",
        });

        var enrichmentOptions = enableEnrichment ? new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()) : null;
        var redactionOptions = enableRedaction ? new StaticOptionsMonitor<LoggerRedactionOptions>(new() { ApplyDiscriminator = false }) : null;

        using var factory = new ExtendedLoggerFactory(
            providers: new[] { provider },
            enrichmentOptions: enrichmentOptions,
            redactionOptions: redactionOptions,
            enrichers: new[] { enricher },
            staticEnrichers: new[] { staticEnricher },
            redactorProvider: redactorProvider,
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()));

        var logger = factory.CreateLogger("logger");

        var state = LoggerMessageHelper.ThreadLocalState;
        var index = state.ReserveTagSpace(2);
        state.TagArray[index] = new("K3", "V3");
        state.TagArray[index + 1] = new("{OriginalFormat}", "V4");

        index = state.ReserveClassifiedTagSpace(1);
        state.ClassifiedTagArray[index] = new("K5", "V5", FakeTaxonomy.PrivateData);

        var exception = logException ? new InvalidOperationException() : null;

        logger.Log(LogLevel.Warning, new EventId(1, "ID1"), state, exception, (_, _) => "MSG");

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(1, collector.Count);

        var record = collector.GetSnapshot().Single();
        var property = record.StructuredState!.Last();
        Assert.Equal("{OriginalFormat}", property.Key);
        Assert.Equal("V4", property.Value);
    }

    [Theory]
    [CombinatorialData]
    public static void LegacyLogging_OriginalFormatMustBeLastInTheListOfStateProperties(
        bool enableEnrichment, bool logException, LegacyStateType stateType)
    {
        using var provider = new Provider();

        var enricher = new ForcedEnricher(new[]
        {
            new KeyValuePair<string, object?>("K1", "V1"),
        });
        var staticEnricher = new ForcedEnricher(new[]
        {
            new KeyValuePair<string, object?>("K2", "V2"),
        });

        var enrichmentOptions = enableEnrichment ? new StaticOptionsMonitor<LoggerEnrichmentOptions>(new()) : null;

        using var factory = new ExtendedLoggerFactory(
            providers: new[] { provider },
            enrichmentOptions: enrichmentOptions,
            enrichers: new[] { enricher },
            staticEnrichers: new[] { staticEnricher },
            filterOptions: new StaticOptionsMonitor<LoggerFilterOptions>(new()));

        var logger = factory.CreateLogger("logger");
        var exception = logException ? new InvalidOperationException() : null;

        switch (stateType)
        {
            case LegacyStateType.ReadOnlyList:
                List<KeyValuePair<string, object?>> list =
                [
                    new("K3", "V3"),
                    new("{OriginalFormat}", "V4")
                ];
                logger.Log(LogLevel.Warning, new EventId(1, "ID1"), list, exception, (_, _) => "MSG");
                break;

            case LegacyStateType.Enumerable:
                IEnumerable<KeyValuePair<string, object?>> enumerable =
                    new List<KeyValuePair<string, object?>>
                    {
                        new("K3", "V3"),
                        new("{OriginalFormat}", "V4")
                    }
                    .Select(x => x);
                logger.Log(LogLevel.Warning, new EventId(1, "ID1"), enumerable, exception, (_, _) => "MSG");
                break;

            case LegacyStateType.String:
                logger.Log(LogLevel.Warning, new EventId(1, "ID1"), "V4", exception, (_, _) => "MSG");
                break;

            default:
                throw new InvalidOperationException($"Uknown state type: {stateType}");
        }

        var sink = provider.Logger!;
        var collector = sink.Collector;
        Assert.Equal(1, collector.Count);

        var record = collector.GetSnapshot().Single();
        var property = record.StructuredState!.Last();
        Assert.Equal("{OriginalFormat}", property.Key);
        Assert.Equal("V4", property.Value);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public static void GlobalBuffering_CanonicalUsecase()
    {
        using var provider = new Provider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(
             builder =>
             {
                 builder.AddProvider(provider);
                 builder.AddGlobalBuffer(LogLevel.Warning);
             });

        ILogger logger = factory.CreateLogger("my category");
        logger.LogWarning("MSG0");
        logger.Log(LogLevel.Warning, new EventId(2, "ID2"), "some state", null, (_, _) => "MSG2");

        // nothing is logged because the buffer not flushed yet
        Assert.Equal(0, provider.Logger!.Collector.Count);

        // instead of this, users would get LogBuffer from DI and call Flush on it
        Utils.DisposingLoggerFactory dlf = (Utils.DisposingLoggerFactory)factory;
        GlobalLogBuffer buffer = dlf.ServiceProvider.GetRequiredService<GlobalLogBuffer>();

        buffer.Flush();

        // 2 log records emitted because the buffer has been flushed
        Assert.Equal(2, provider.Logger!.Collector.Count);
    }

    [Fact]
    public static void GlobalBuffering_ParallelLogging()
    {
        using var provider = new Provider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(
             builder =>
             {
                 builder.AddProvider(provider);
                 builder.AddGlobalBuffer(LogLevel.Warning);
             });

        ILogger logger = factory.CreateLogger("my category");

        // 1000 threads logging at the same time
        Parallel.For(0, 1000, _ =>
        {
            logger.LogWarning("MSG0");
            logger.Log(LogLevel.Warning, new EventId(2, "ID2"), "some state", null, (_, _) => "MSG2");
        });

        // nothing is logged because the buffer not flushed yet
        Assert.Equal(0, provider.Logger!.Collector.Count);

        Utils.DisposingLoggerFactory dlf = (Utils.DisposingLoggerFactory)factory;
        GlobalLogBuffer buffer = dlf.ServiceProvider.GetRequiredService<GlobalLogBuffer>();

        buffer.Flush();

        // 2000 log records emitted because the buffer has been flushed
        Assert.Equal(2000, provider.Logger!.Collector.Count);
    }

    [Fact]
    public static async Task GlobalBuffering_ParallelLoggingAndFlushing()
    {
        // Arrange
        using var provider = new Provider();
        using ILoggerFactory factory = Utils.CreateLoggerFactory(
             builder =>
             {
                 builder.AddProvider(provider);
                 builder.AddGlobalBuffer(options =>
                 {
                     options.AutoFlushDuration = TimeSpan.Zero;
                     options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Warning));
                 });
             });

        ILogger logger = factory.CreateLogger("my category");
        Utils.DisposingLoggerFactory dlf = (Utils.DisposingLoggerFactory)factory;
        GlobalLogBuffer buffer = dlf.ServiceProvider.GetRequiredService<GlobalLogBuffer>();

        // Act - Run logging and flushing operations in parallel
        await Task.Run(() =>
        {
            Parallel.For(0, 100, i =>
            {
                logger.LogWarning("MSG0");
                logger.LogWarning("MSG1");
                logger.LogWarning("MSG2");
                logger.LogWarning("MSG3");
                logger.LogWarning("MSG4");
                logger.LogWarning("MSG5");
                logger.LogWarning("MSG6");
                logger.LogWarning("MSG7");
                logger.LogWarning("MSG8");
                logger.LogWarning("MSG9");
                buffer.Flush();
            });
        });

        buffer.Flush();
        Assert.Equal(1000, provider.Logger!.Collector.Count);
    }

#endif

    private sealed class CustomLoggerProvider : ILoggerProvider
    {
        private readonly string _providerName;
        private readonly ThrowExceptionAt _throwExceptionAt;
        private readonly List<string> _store;

        public CustomLoggerProvider(string providerName, ThrowExceptionAt throwExceptionAt, List<string> store)
        {
            _providerName = providerName;
            _throwExceptionAt = throwExceptionAt;
            _store = store;
        }

        public ILogger CreateLogger(string name)
        {
            return new CustomLogger($"{_providerName}.{name}", _throwExceptionAt, _store);
        }

        public void Dispose()
        {
            // nop[
        }
    }

    private sealed class CustomLogger : ILogger
    {
        private readonly string _name;
        private readonly ThrowExceptionAt _throwExceptionAt;
        private readonly List<string> _store;

        public CustomLogger(string name, ThrowExceptionAt throwExceptionAt, List<string> store)
        {
            _name = name;
            _throwExceptionAt = throwExceptionAt;
            _store = store;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (_throwExceptionAt == ThrowExceptionAt.BeginScope)
            {
                throw new InvalidOperationException($"{_name}-Error occurred while creating scope.");
            }

            _store.Add($"{_name}-{state}");

            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_throwExceptionAt == ThrowExceptionAt.IsEnabled)
            {
                throw new InvalidOperationException($"{_name}-Error occurred while checking if logger is enabled.");
            }

            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (_throwExceptionAt == ThrowExceptionAt.Log)
            {
                throw new InvalidOperationException($"{_name}-Error occurred while logging data.");
            }

            _store.Add($"{_name}-{state}");
        }
    }

    private enum ThrowExceptionAt
    {
        None,
        BeginScope,
        Log,
        IsEnabled
    }

    internal sealed class Provider : ILoggerProvider
    {
        public FakeLogger? Logger { get; private set; }

        public ILogger CreateLogger(string categoryName)
        {
            Logger = new FakeLogger((FakeLogCollector?)null, categoryName);
            return Logger;
        }

        public void Dispose()
        {
            // nothing to do
        }
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public object? State { get; private set; }
        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
            // nothing to do
        }

        private sealed class Logger : ILogger
        {
            private readonly CapturingProvider _provider;

            public Logger(CapturingProvider provider)
            {
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => throw new NotSupportedException();

            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => _provider.State = state?.ToString();
        }
    }

    private sealed class ForcedEnricher : ILogEnricher, IStaticLogEnricher
    {
        private readonly KeyValuePair<string, object?>[] _values;

        public ForcedEnricher(KeyValuePair<string, object?>[] values)
        {
            _values = values;
        }

        public void Enrich(IEnrichmentTagCollector enrichmentPropertyBag)
        {
            foreach (var kvp in _values)
            {
                enrichmentPropertyBag.Add(kvp.Key, kvp.Value!);
            }
        }
    }

    private sealed class FancyEnricher : ILogEnricher
    {
        private readonly KeyValuePair<string, object?>[] _values;
        private readonly bool _objectVersion;

        public FancyEnricher(KeyValuePair<string, object?>[] values, bool objectVersion)
        {
            _values = values;
            _objectVersion = objectVersion;
        }

        public void Enrich(IEnrichmentTagCollector collector)
        {
            if (_objectVersion)
            {
                var p = (KeyValuePair<string, object>[])(object)_values;
                foreach (var kvp in p)
                {
                    collector.Add(kvp.Key, kvp.Value);
                }
            }
            else
            {
                var a = new KeyValuePair<string, string>[_values.Length];
                int i = 0;
                foreach (var kvp in _values)
                {
                    a[i++] = new(kvp.Key, (string)kvp.Value!);
                }

                foreach (var kvp in a)
                {
                    collector.Add(kvp.Key, kvp.Value);
                }
            }
        }
    }

    public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public IDisposable? OnChange(Action<T, string> listener) => null;
        public T Get(string? name) => CurrentValue;
        public T CurrentValue { get; }
    }

    public enum LegacyStateType
    {
        ReadOnlyList,
        Enumerable,
        String
    }
}
