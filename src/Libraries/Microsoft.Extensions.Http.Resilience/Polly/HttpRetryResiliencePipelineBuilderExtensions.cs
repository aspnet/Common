// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Microsoft.Extensions.Http.Resilience.Internal;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using Polly;
using Polly.Retry;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Extensions for adding an HTTP retry strategy to <see cref="ResiliencePipelineBuilder{TResult}"/>.
/// </summary>
[Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
public static class HttpRetryResiliencePipelineBuilderExtensions
{
    private static readonly HttpRetryBudgetOptionsValidator _budgetValidator = new();

    /// <summary>
    /// Adds a retry strategy limited by a retry budget to the builder. Use this method instead of
    /// <see cref="RetryResiliencePipelineBuilderExtensions.AddRetry{TResult}(ResiliencePipelineBuilder{TResult}, RetryStrategyOptions{TResult})"/>
    /// in custom pipelines, because <c>AddRetry</c> can't apply <see cref="HttpRetryStrategyOptions.RetryBudget"/> and rejects
    /// options that set it.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The retry strategy options.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// The budget needs a strategy that counts every request sent through the pipeline, in addition to the retry strategy.
    /// This method adds both. The budget is configured by <see cref="HttpRetryStrategyOptions.RetryBudget"/>. When it's
    /// <see langword="null"/>, a budget with default <see cref="HttpRetryBudgetOptions"/> values is used, without modifying
    /// <paramref name="options"/>. Other retry options, such as <see cref="RetryStrategyOptions{TResult}.MaxRetryAttempts"/>,
    /// are used as is. See <see cref="HttpRetryBudgetOptions"/> for more details.
    /// </remarks>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    public static ResiliencePipelineBuilder<HttpResponseMessage> AddRetryWithBudget(this ResiliencePipelineBuilder<HttpResponseMessage> builder, HttpRetryStrategyOptions options)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(options);

        return AddBudgetedRetry(builder, options, options.RetryBudget ?? new HttpRetryBudgetOptions());
    }

    // Used by the standard resilience handler, where a null budget means that retries are not limited.
    internal static ResiliencePipelineBuilder<HttpResponseMessage> AddRetryWithOptionalBudget(this ResiliencePipelineBuilder<HttpResponseMessage> builder, HttpRetryStrategyOptions options)
    {
        return options.RetryBudget is null
            ? RetryResiliencePipelineBuilderExtensions.AddRetry(builder, options)
            : AddBudgetedRetry(builder, options, options.RetryBudget);
    }

    // The options instance can be shared by multiple pipelines (for example, with SelectPipelineByAuthority),
    // so the wrapped predicate goes into a copy instead of mutating the caller's instance.
    internal static HttpRetryStrategyOptions CreateBudgetedCopy(HttpRetryStrategyOptions options, HttpRetryBudgetOptions budgetOptions)
    {
        return new HttpRetryStrategyOptions
        {
            Name = options.Name,
            MaxRetryAttempts = options.MaxRetryAttempts,
            BackoffType = options.BackoffType,
            UseJitter = options.UseJitter,
            Delay = options.Delay,
            MaxDelay = options.MaxDelay,

            // The ShouldRetryAfterHeader setter overwrites DelayGenerator, so it must be assigned first.
            ShouldRetryAfterHeader = options.ShouldRetryAfterHeader,
            DelayGenerator = options.DelayGenerator,
            OnRetry = options.OnRetry,
            Randomizer = options.Randomizer,
            RetryBudget = budgetOptions,

            // A null predicate is kept as null so that Polly's options validation rejects it at registration.
            ShouldHandle = options.ShouldHandle is null ? null! : RetryBudgetStrategy.WrapShouldHandle(options.ShouldHandle, options.MaxRetryAttempts),
        };
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All members of the budget strategy options are preserved by the DynamicDependency attribute.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RetryBudgetStrategyOptions))]
    private static ResiliencePipelineBuilder<HttpResponseMessage> AddBudgetedRetry(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        HttpRetryStrategyOptions options,
        HttpRetryBudgetOptions budgetOptions)
    {
        var validation = _budgetValidator.Validate($"{options.Name}.{nameof(HttpRetryStrategyOptions.RetryBudget)}", budgetOptions);
        if (validation.Failed)
        {
            throw new ValidationException(validation.FailureMessage);
        }

        // Snapshot the values so that later changes to the options instance don't affect the built pipelines.
        var retryRatio = budgetOptions.RetryRatio;
        var samplingDuration = budgetOptions.SamplingDuration;
        var minimumThroughput = budgetOptions.MinimumThroughput;

        _ = builder.AddStrategy(
            context => new RetryBudgetStrategy(new RetryBudget(retryRatio, samplingDuration, minimumThroughput, context.TimeProvider), context.Telemetry),
            new RetryBudgetStrategyOptions { Name = options.Name });

        var budgetedOptions = CreateBudgetedCopy(options, budgetOptions);

        return RetryBudgetValidationScope.ValidateWithBudgetApplied(() => RetryResiliencePipelineBuilderExtensions.AddRetry(builder, budgetedOptions));
    }
}
