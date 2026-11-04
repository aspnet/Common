// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Extensions.Http.Resilience.Internal;

/// <summary>
/// Marks the validation of <see cref="HttpRetryStrategyOptions"/> done by a consumer that applies the retry budget.
/// </summary>
/// <remarks>
/// <see cref="RetryBudgetAppliedAttribute"/> rejects a retry budget that's validated outside this scope, for example by
/// Polly's <c>AddRetry</c>, which would otherwise ignore the budget silently. Validation is synchronous, so a thread-static
/// flag scopes it to the validation call without modifying the options.
/// </remarks>
internal static class RetryBudgetValidationScope
{
    [field: ThreadStatic]
    public static bool IsBudgetApplied { get; private set; }

    public static T ValidateWithBudgetApplied<T>(Func<T> validate)
    {
        var previous = IsBudgetApplied;
        IsBudgetApplied = true;

        try
        {
            return validate();
        }
        finally
        {
            IsBudgetApplied = previous;
        }
    }
}
