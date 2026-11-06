// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// Defines a rule used to determine whether matching log messages are processed by bottom-K sampling.
/// </summary>
[Experimental(DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
public class BottomKLogSamplingFilterRule : ILogSamplingFilterRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BottomKLogSamplingFilterRule"/> class.
    /// </summary>
    /// <param name="categoryName">The category name to use in this filter rule.</param>
    /// <param name="logLevel">The maximum <see cref="LogLevel"/> this rule applies to.</param>
    /// <param name="eventId">The event ID this rule applies to.</param>
    /// <param name="eventName">The event name this rule applies to.</param>
    public BottomKLogSamplingFilterRule(
        string? categoryName = null,
        LogLevel? logLevel = null,
        int? eventId = null,
        string? eventName = null)
    {
        CategoryName = categoryName;
        LogLevel = logLevel;
        EventId = eventId;
        EventName = eventName;
    }

    /// <inheritdoc/>
    public string? CategoryName { get; }

    /// <inheritdoc/>
    public LogLevel? LogLevel { get; }

    /// <inheritdoc/>
    public int? EventId { get; }

    /// <inheritdoc/>
    public string? EventName { get; }
}
#endif
