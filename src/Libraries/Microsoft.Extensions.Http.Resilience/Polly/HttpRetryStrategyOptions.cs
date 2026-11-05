// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Http.Resilience.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Shared.DiagnosticIds;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Implementation of the <see cref="RetryStrategyOptions{TResult}"/> for <see cref="HttpResponseMessage"/> results.
/// </summary>
public class HttpRetryStrategyOptions : RetryStrategyOptions<HttpResponseMessage>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRetryStrategyOptions"/> class.
    /// </summary>
    /// <remarks>
    /// By default, the options are configured to handle only transient failures.
    /// Specifically, this includes HTTP status codes 408, 429, 500 and above, 
    /// as well as <see cref="HttpRequestException"/> and <see cref="TimeoutRejectedException"/> exceptions.
    /// </remarks>
    public HttpRetryStrategyOptions()
    {
        ShouldHandle = args => new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome, args.Context.CancellationToken));
        BackoffType = DelayBackoffType.Exponential;
        ShouldRetryAfterHeader = true;
        UseJitter = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to use the <c>Retry-After</c> header for the retry delays.
    /// </summary>
    /// <value>
    /// Defaults to <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// If the property is set to <see langword="true"/> then the generator will resolve the delay
    /// based on the <c>Retry-After</c> header rules, otherwise it will return <see langword="null"/> and the retry strategy
    /// delay will generate the delay based on the configured options.
    /// </remarks>
    public bool ShouldRetryAfterHeader
    {
        get;
        set
        {
            field = value;

            if (field)
            {
                DelayGenerator = args => args.Outcome.Result switch
                {
                    HttpResponseMessage response when RetryAfterHelper.TryParse(response, TimeProvider.System, out var retryAfter) => new ValueTask<TimeSpan?>(retryAfter),
                    _ => default
                };
            }
            else
            {
                DelayGenerator = null;
            }
        }
    }

    /// <summary>
    /// Gets or sets the retry budget options.
    /// </summary>
    /// <value>
    /// The default value is <see langword="null"/>, which means retries are not limited by a budget.
    /// </value>
    /// <remarks>
    /// The budget is applied by the standard resilience handler and by
    /// <see cref="HttpRetryResiliencePipelineBuilderExtensions.AddRetryWithBudget(ResiliencePipelineBuilder{HttpResponseMessage}, HttpRetryStrategyOptions)"/>.
    /// <see cref="RetryResiliencePipelineBuilderExtensions.AddRetry{TResult}(ResiliencePipelineBuilder{TResult}, RetryStrategyOptions{TResult})"/>
    /// can't apply the budget, so it throws a <see cref="ValidationException"/> when the budget is set, instead of ignoring it.
    /// See <see cref="HttpRetryBudgetOptions"/> for more details.
    /// </remarks>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
    [ValidateObjectMembers]
    [RetryBudgetApplied]
    public HttpRetryBudgetOptions? RetryBudget { get; set; }
}
