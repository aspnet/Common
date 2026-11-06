// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// Provides configuration for adaptive bottom-K log sampling.
/// </summary>
[Experimental(DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class BottomKLogSamplingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether bottom-K sampling is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the per-period reservoir capacity (<c>T</c>).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Capacity { get; set; } = 128;

    /// <summary>
    /// Gets or sets the per-period novelty-preserve capacity (<c>R</c>). <c>0</c> disables the preserve.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PreserveCapacity { get; set; } = 128;

    /// <summary>
    /// Gets or sets the minimum prior-period arrival count below which the frozen frequency table is
    /// discarded and the next period is treated as warmup.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long MinPeriodCount { get; set; } = 32;

    /// <summary>
    /// Gets or sets the period length. When this much time has elapsed the reservoir is flushed and a
    /// new period begins.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the strategy used to weight callsites unseen in the frozen table.
    /// </summary>
    public BottomKUnseenWeightMode UnseenWeightMode { get; set; } = BottomKUnseenWeightMode.Chao1;

    /// <summary>
    /// Gets or sets the rules used to determine which log messages are processed by bottom-K sampling.
    /// </summary>
    /// <remarks>
    /// The most specific matching rule is applied. If no rule matches, the log message is emitted normally.
    /// An empty collection applies bottom-K sampling to every log message.
    /// </remarks>
    [Required]
    public IList<BottomKLogSamplingFilterRule> Rules { get; set; } = [];
}
#endif
