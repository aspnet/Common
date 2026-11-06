// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
#if NET9_0_OR_GREATER
using Microsoft.Extensions.Diagnostics.Buffering;
#endif
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

internal sealed class ExtendedLoggerFactory : ILoggerFactory
{
    private readonly Dictionary<string, ExtendedLogger> _loggers = new(StringComparer.Ordinal);
    private readonly List<ProviderRegistration> _providerRegistrations = [];
    private readonly object _sync = new();
    private readonly IDisposable? _filterOptionsChangeTokenRegistration;
    private readonly LoggerFactoryOptions _factoryOptions;
    private readonly IDisposable? _enrichmentOptionsChangeTokenRegistration;
    private readonly IDisposable? _redactionOptionsChangeTokenRegistration;
    private readonly Action<IEnrichmentTagCollector>[] _enrichers;
    private readonly LoggingSampler? _sampler;
#if NET9_0_OR_GREATER
    private readonly LogBuffer? _logBuffer;
#endif
    private readonly KeyValuePair<string, object?>[] _staticTags;
    private readonly Func<DataClassificationSet, Redactor> _redactorProvider;
    private volatile bool _disposed;
    private LoggerFilterOptions _filterOptions;
    private IExternalScopeProvider? _scopeProvider;

    public ExtendedLoggerFactory(
        IEnumerable<ILoggerProvider> providers,
        IEnumerable<ILogEnricher> enrichers,
        IEnumerable<IStaticLogEnricher> staticEnrichers,
        IOptionsMonitor<LoggerFilterOptions> filterOptions,
        LoggingSampler? sampler = null,
        IOptions<LoggerFactoryOptions>? factoryOptions = null,
        IExternalScopeProvider? scopeProvider = null,
        IOptionsMonitor<LoggerEnrichmentOptions>? enrichmentOptions = null,
        IOptionsMonitor<LoggerRedactionOptions>? redactionOptions = null,
#if NET9_0_OR_GREATER
        IRedactorProvider? redactorProvider = null,
        LogBuffer? logBuffer = null)
#else
        IRedactorProvider? redactorProvider = null)
#endif
    {
        _scopeProvider = scopeProvider;
#if NET9_0_OR_GREATER
        _logBuffer = logBuffer;
#endif
        _sampler = sampler;

        _factoryOptions = factoryOptions == null || factoryOptions.Value == null ? new LoggerFactoryOptions() : factoryOptions.Value;

        const ActivityTrackingOptions ActivityTrackingOptionsMask = ~(ActivityTrackingOptions.SpanId | ActivityTrackingOptions.TraceId | ActivityTrackingOptions.ParentId |
                                                                      ActivityTrackingOptions.TraceFlags | ActivityTrackingOptions.TraceState | ActivityTrackingOptions.Tags
                                                                      | ActivityTrackingOptions.Baggage);

        if ((_factoryOptions.ActivityTrackingOptions & ActivityTrackingOptionsMask) != 0)
        {
            Throw.ArgumentException($"{_factoryOptions.ActivityTrackingOptions} is invalid ActivityTrackingOptions value.", nameof(factoryOptions));
        }

        foreach (ILoggerProvider p in providers)
        {
            AddProviderRegistration(p, dispose: false);
        }

        _filterOptionsChangeTokenRegistration = filterOptions.OnChange(RefreshFilters);
        RefreshFilters(filterOptions.CurrentValue);

        if (enrichmentOptions is null)
        {
            // enrichmentOptions is only present if EnableEnrichment was called, so if it's null
            // then ignore all the supplied enrichers, we're not doing enrichment
#pragma warning disable S1226
            enrichers = [];
            staticEnrichers = [];
#pragma warning restore S1226
        }

        _enrichers = enrichers.Select<ILogEnricher, Action<IEnrichmentTagCollector>>(e => e.Enrich).ToArray();
        _enrichmentOptionsChangeTokenRegistration = enrichmentOptions?.OnChange(UpdateEnrichmentOptions);
        _redactionOptionsChangeTokenRegistration = redactionOptions?.OnChange(UpdateRedactionOptions);

        var provider = redactionOptions != null && redactorProvider != null
            ? redactorProvider
            : NullRedactorProvider.Instance;
        _redactorProvider = provider.GetRedactor;

        var tags = new List<KeyValuePair<string, object?>>();
        var collector = new ExtendedLogger.EnrichmentTagCollector(tags);
        foreach (var enricher in staticEnrichers)
        {
            enricher.Enrich(collector);
        }

        _staticTags = [.. tags];
        Config = ComputeConfig(enrichmentOptions?.CurrentValue ?? new(), redactionOptions?.CurrentValue ?? new() { ApplyDiscriminator = false });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            _filterOptionsChangeTokenRegistration?.Dispose();
            _enrichmentOptionsChangeTokenRegistration?.Dispose();
            _redactionOptionsChangeTokenRegistration?.Dispose();

            foreach (ProviderRegistration registration in _providerRegistrations)
            {
                try
                {
                    if (registration.ShouldDispose)
                    {
                        registration.Provider.Dispose();
                    }
                }
                catch
                {
                    // Swallow exceptions on dispose
                }
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        _ = Throw.IfNull(categoryName);

        if (CheckDisposed())
        {
            throw new ObjectDisposedException(nameof(LoggerFactory));
        }

        lock (_sync)
        {
            if (!_loggers.TryGetValue(categoryName, out ExtendedLogger? logger))
            {
                logger = new ExtendedLogger(this, categoryName, CreateLoggers(categoryName));

                (logger.MessageLoggers, logger.ScopeLoggers) = ApplyFilters(logger.Loggers);

                _loggers[categoryName] = logger;
            }

            return logger;
        }
    }

    public void AddProvider(ILoggerProvider provider)
    {
        _ = Throw.IfNull(provider);

        if (CheckDisposed())
        {
            throw new ObjectDisposedException(nameof(LoggerFactory));
        }

        lock (_sync)
        {
            AddProviderRegistration(provider, dispose: true);

            foreach (KeyValuePair<string, ExtendedLogger> existingLogger in _loggers)
            {
                ExtendedLogger logger = existingLogger.Value;
                LoggerInformation[] loggerInformation = logger.Loggers;

                int newLoggerIndex = loggerInformation.Length;
                Array.Resize(ref loggerInformation, loggerInformation.Length + 1);
                loggerInformation[newLoggerIndex] = new LoggerInformation(provider, existingLogger.Key);

                logger.Loggers = loggerInformation;
                (logger.MessageLoggers, logger.ScopeLoggers) = ApplyFilters(logger.Loggers);
            }
        }
    }

    [MemberNotNull(nameof(_filterOptions))]
    private void RefreshFilters(LoggerFilterOptions filterOptions)
    {
        lock (_sync)
        {
            _filterOptions = filterOptions;
            foreach (KeyValuePair<string, ExtendedLogger> registeredLogger in _loggers)
            {
                ExtendedLogger logger = registeredLogger.Value;
                (logger.MessageLoggers, logger.ScopeLoggers) = ApplyFilters(logger.Loggers);
            }
        }
    }

    private void AddProviderRegistration(ILoggerProvider provider, bool dispose)
    {
        _providerRegistrations.Add(new ProviderRegistration
        {
            Provider = provider,
            ShouldDispose = dispose
        });

        if (provider is ISupportExternalScope supportsExternalScope)
        {
            _scopeProvider ??= new LoggerFactoryScopeProvider(_factoryOptions.ActivityTrackingOptions);

            supportsExternalScope.SetScopeProvider(_scopeProvider);
        }
    }

    private LoggerInformation[] CreateLoggers(string categoryName)
    {
        var loggers = new List<LoggerInformation>(_providerRegistrations.Count);
        for (int i = 0; i < _providerRegistrations.Count; i++)
        {
            var loggerInformation = new LoggerInformation(_providerRegistrations[i].Provider, categoryName);

            // We do not need to check for NullLogger<T>.Instance as no provider would reasonably return it (the <T> handling is at
            // outer loggers level, not inner level loggers in Logger/LoggerProvider).
            if (loggerInformation.Logger != NullLogger.Instance)
            {
                loggers.Add(loggerInformation);
            }
        }

        return loggers.ToArray();
    }

    private (MessageLogger[] messageLoggers, ScopeLogger[] scopeLoggers) ApplyFilters(LoggerInformation[] loggers)
    {
        var messageLoggers = new List<MessageLogger>();
        List<ScopeLogger>? scopeLoggers = _filterOptions.CaptureScopes ? [] : null;

        foreach (LoggerInformation loggerInformation in loggers)
        {
            LoggerRuleSelector.Select(_filterOptions,
                loggerInformation.ProviderType,
                loggerInformation.Category,
                out LogLevel? minLevel,
                out Func<string?, string?, LogLevel, bool>? filter);

            if (minLevel is > LogLevel.Critical)
            {
                continue;
            }

            messageLoggers.Add(new MessageLogger(loggerInformation.Logger, loggerInformation.Category, loggerInformation.ProviderType.FullName, minLevel, filter));

            if (!loggerInformation.ExternalScope)
            {
                scopeLoggers?.Add(new ScopeLogger(logger: loggerInformation.Logger, externalScopeProvider: null));
            }
        }

        if (_scopeProvider != null)
        {
            scopeLoggers?.Add(new ScopeLogger(logger: null, externalScopeProvider: _scopeProvider));
        }

        return (messageLoggers.ToArray(), scopeLoggers?.ToArray() ?? Array.Empty<ScopeLogger>());
    }

    private bool CheckDisposed() => _disposed;

    /// <summary>
    /// Gets the current config state that loggers should use.
    /// </summary>
    /// <remarks>
    /// This gets replaced whenever option monitors trigger. The loggers should sample this value
    /// and use it for an entire call to ILogger.Log so as to get a consistent view of config for the
    /// execution span of the function.
    /// </remarks>
    internal LoggerConfig Config { get; private set; }

    private LoggerConfig ComputeConfig(LoggerEnrichmentOptions? enrichmentOptions, LoggerRedactionOptions? redactionOptions)
    {
        if (enrichmentOptions == null)
        {
            enrichmentOptions = new LoggerEnrichmentOptions
            {
                CaptureStackTraces = Config.CaptureStackTraces,
                UseFileInfoForStackTraces = Config.UseFileInfoForStackTraces,
                IncludeExceptionMessage = Config.IncludeExceptionMessage,
                MaxStackTraceLength = Config.MaxStackTraceLength,
            };
        }

        if (redactionOptions == null)
        {
            redactionOptions = new LoggerRedactionOptions
            {
                ApplyDiscriminator = Config.AddRedactionDiscriminator,
            };
        }

        return new(_staticTags,
                _enrichers,
                _sampler,
                enrichmentOptions.CaptureStackTraces,
                enrichmentOptions.UseFileInfoForStackTraces,
                enrichmentOptions.IncludeExceptionMessage,
                enrichmentOptions.MaxStackTraceLength,
                _redactorProvider,
#if NET9_0_OR_GREATER
                redactionOptions.ApplyDiscriminator,
                _logBuffer);
#else
                redactionOptions.ApplyDiscriminator);
#endif
    }

    private void UpdateEnrichmentOptions(LoggerEnrichmentOptions enrichmentOptions) => Config = ComputeConfig(enrichmentOptions, null);
    private void UpdateRedactionOptions(LoggerRedactionOptions redactionOptions) => Config = ComputeConfig(null, redactionOptions);

    public struct ProviderRegistration
    {
        public ILoggerProvider Provider;
        public bool ShouldDispose;
    }
}
