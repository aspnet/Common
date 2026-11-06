// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Extensions.Http.Resilience.Internal;

/// <summary>
/// Rejects a retry budget that's validated by a consumer that can't apply it, such as Polly's <c>AddRetry</c>,
/// so the budget isn't silently ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class RetryBudgetAppliedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || RetryBudgetValidationScope.IsBudgetApplied)
        {
            return ValidationResult.Success;
        }

        var memberName = validationContext.MemberName ?? nameof(HttpRetryStrategyOptions.RetryBudget);

        return new ValidationResult(
            $"The {memberName} is only applied by the standard resilience handler and by AddRetryWithBudget. " +
            $"Use AddRetryWithBudget instead of AddRetry, or remove the {memberName}.",
            [memberName]);
    }
}
