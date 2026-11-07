// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Polly.Telemetry;

namespace Microsoft.Extensions.Http.Resilience.Internal;

/// <summary>
/// Records one request per execution in the retry budget and exposes the budget to the retry strategy that it wraps.
/// </summary>
/// <remarks>
/// This strategy is added immediately before (outside) the retry strategy. While the execution is in progress,
/// the strategy is stored in <see cref="ResilienceContext.Properties"/> so that the retry predicate created by
/// <see cref="WrapShouldHandle"/> can consult the budget of the pipeline instance that is executing.
/// </remarks>
internal sealed class RetryBudgetStrategy : ResilienceStrategy
{
    internal const string BudgetExhaustedEventName = "OnRetryBudgetExhausted";

    private static readonly ResiliencePropertyKey<RetryBudgetStrategy?> _budgetKey = new("Resilience.Http.RetryBudget");
    private static readonly ResilienceEvent _budgetExhaustedEvent = new(ResilienceEventSeverity.Warning, BudgetExhaustedEventName);

    private readonly RetryBudget _budget;
    private readonly ResilienceStrategyTelemetry _telemetry;

    public RetryBudgetStrategy(RetryBudget budget, ResilienceStrategyTelemetry telemetry)
    {
        _budget = budget;
        _telemetry = telemetry;
    }

    public static Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> WrapShouldHandle(
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> shouldHandle,
        int maxRetryAttempts)
    {
        return args =>
        {
            var handled = shouldHandle(args);

            if (handled.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD002 // The value task has already completed, so reading its result doesn't block.
                return new ValueTask<bool>(EvaluateBudget(handled.Result, args, maxRetryAttempts));
#pragma warning restore VSTHRD002
            }

            return EvaluateBudgetAsync(handled, args, maxRetryAttempts);
        };

        static async ValueTask<bool> EvaluateBudgetAsync(ValueTask<bool> handled, RetryPredicateArguments<HttpResponseMessage> args, int maxRetryAttempts)
            => EvaluateBudget(await handled.ConfigureAwait(args.Context.ContinueOnCapturedContext), args, maxRetryAttempts);
    }

    protected override async ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        TState state)
    {
        _budget.RecordRequest();

        // Restore the previous value so that nested pipelines with their own budget don't leak into this one.
        _ = context.Properties.TryGetValue(_budgetKey, out var previous);
        context.Properties.Set(_budgetKey, this);

        try
        {
            return await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
        finally
        {
            context.Properties.Set(_budgetKey, previous);
        }
    }

    private static bool EvaluateBudget(bool handled, RetryPredicateArguments<HttpResponseMessage> args, int maxRetryAttempts)
    {
        // Polly evaluates the predicate after the last attempt too; that evaluation must not consume the budget
        // because no retry follows it.
        if (!handled || args.AttemptNumber >= maxRetryAttempts)
        {
            return handled;
        }

        if (!args.Context.Properties.TryGetValue(_budgetKey, out var strategy) || strategy is null)
        {
            return true;
        }

        return strategy.TryAcquireRetry(args);
    }

    private bool TryAcquireRetry(RetryPredicateArguments<HttpResponseMessage> args)
    {
        if (_budget.TryAcquireRetry())
        {
            return true;
        }

        _telemetry.Report(_budgetExhaustedEvent, args.Context, args.Outcome, new OnRetryBudgetExhaustedArguments(args.AttemptNumber));
        return false;
    }
}
