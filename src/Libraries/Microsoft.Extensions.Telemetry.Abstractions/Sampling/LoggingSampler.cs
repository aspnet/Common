// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Controls the number of samples of log records collected and sent to the backend.
/// </summary>
public abstract class LoggingSampler
{
    /// <summary>
    /// Determines whether a log record should be created for the specified category and level.
    /// </summary>
    /// <param name="categoryName">The category name of the log record.</param>
    /// <param name="logLevel">The level of the log record.</param>
    /// <returns>
    /// <see langword="true"/> if the log record should be created; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This method is called from <see cref="ILogger.IsEnabled(LogLevel)"/> before log state, enrichment,
    /// redaction, and exception information are processed. Implementations must make the decision using
    /// only the supplied values and ambient state. It may be called more than once for the same log record,
    /// so implementations should return a stable decision. The default implementation enables the log record.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public virtual bool ShouldSample(string categoryName, LogLevel logLevel) => true;

    /// <summary>
    /// Makes a sampling decision for the provided <paramref name="logEntry"/>.
    /// </summary>
    /// <param name="logEntry">The log entry used to make the sampling decision for.</param>
    /// <typeparam name="TState">The type of the log entry state.</typeparam>
    /// <returns><see langword="true" /> if the log record should be sampled; otherwise, <see langword="false" />.</returns>
    public abstract bool ShouldSample<TState>(in LogEntry<TState> logEntry);
}
