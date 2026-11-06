// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.Data.Validation;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Options for the retry budget that limits the number of retries relative to the number of requests.
/// </summary>
/// <remarks>
/// The retry budget tracks the number of requests and retries over a sliding window of <see cref="SamplingDuration"/>.
/// Once the window contains at least <see cref="MinimumThroughput"/> requests, a retry is only allowed while the number
/// of retries in the window is below <see cref="RetryRatio"/> multiplied by the number of requests, rounded down.
/// At least one retry per window is always allowed, unless <see cref="RetryRatio"/> is 0, which suppresses every retry.
/// When the budget is exhausted, the outcome that would have been retried is returned to the caller instead and the
/// <c>OnRetryBudgetExhausted</c> resilience event is reported with <see cref="OnRetryBudgetExhaustedArguments"/>.
/// <para>
/// The budget prevents retry storms: when a dependency is degraded, retries can multiply the load on it by up to
/// <c>MaxRetryAttempts + 1</c>. A budget caps that amplification to roughly <c>1 + RetryRatio</c>.
/// </para>
/// <para>
/// A budget instance is shared by all requests that use the same resilience pipeline instance. Each named client
/// (and each authority when <c>SelectPipelineByAuthority</c> is used) has its own budget.
/// The budget is reset when the pipeline is reloaded due to options changes.
/// </para>
/// </remarks>
[Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
public class HttpRetryBudgetOptions
{
    private const double DefaultRetryRatio = 0.1;
    private const int DefaultMinimumThroughput = 10;
    private static readonly TimeSpan _defaultSamplingDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum ratio of retries to requests within the sampling window.
    /// </summary>
    /// <value>
    /// A value between 0 and 1, inclusive. The default value is 0.1, which allows retries to add at most 10% extra load.
    /// A value of 0 suppresses every retry, regardless of <see cref="MinimumThroughput"/>.
    /// </value>
    [Range(0.0, 1.0)]
    public double RetryRatio { get; set; } = DefaultRetryRatio;

    /// <summary>
    /// Gets or sets the duration of the sliding window used to track requests and retries.
    /// </summary>
    /// <value>
    /// The default value is 10 seconds. The value must be between 1 second and 1 day.
    /// </value>
    [TimeSpan("00:00:01", "1.00:00:00")]
    public TimeSpan SamplingDuration { get; set; } = _defaultSamplingDuration;

    /// <summary>
    /// Gets or sets the minimum number of requests within the sampling window before the budget is enforced.
    /// </summary>
    /// <value>
    /// The default value is 10. The value must be 1 or greater.
    /// </value>
    /// <remarks>
    /// Below this throughput, retries are not limited by the budget, unless <see cref="RetryRatio"/> is 0. This keeps
    /// low-traffic clients from having their retries denied because of a small number of requests.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MinimumThroughput { get; set; } = DefaultMinimumThroughput;
}
