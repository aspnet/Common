// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// Validates bottom-K configuration constraints that cannot be expressed with data annotations.
/// </summary>
internal sealed class BottomKLogSamplingOptionsCustomValidator : IValidateOptions<BottomKLogSamplingOptions>
{
    public ValidateOptionsResult Validate(string? name, BottomKLogSamplingOptions options)
    {
        ValidateOptionsResultBuilder result = new();

        if (options.FlushInterval <= TimeSpan.Zero)
        {
            result.AddError("FlushInterval must be greater than zero.", nameof(options.FlushInterval));
        }

        if (!Enum.IsDefined(options.UnseenWeightMode))
        {
            result.AddError("UnseenWeightMode must be a defined value.", nameof(options.UnseenWeightMode));
        }

        ValidateRules(options.Rules, result);

        return result.Build();
    }

    private static void ValidateRules(
        IList<BottomKLogSamplingFilterRule>? rules,
        ValidateOptionsResultBuilder result)
    {
        if (rules is null)
        {
            return;
        }

        foreach (BottomKLogSamplingFilterRule? rule in rules)
        {
            if (rule is null)
            {
                result.AddError("Rules cannot contain null values.", nameof(BottomKLogSamplingOptions.Rules));
                continue;
            }

            if (rule.LogLevel is { } logLevel && !Enum.IsDefined(logLevel))
            {
                result.AddError("Rule LogLevel must be a defined value.", nameof(BottomKLogSamplingOptions.Rules));
            }

            string? categoryName = rule.CategoryName;
            if (categoryName is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                result.AddError("Rule category names cannot be empty.", nameof(BottomKLogSamplingOptions.Rules));
                continue;
            }

            int wildcard = categoryName.IndexOf("*", StringComparison.Ordinal);
            if (wildcard >= 0 && categoryName.IndexOf("*", wildcard + 1, StringComparison.Ordinal) >= 0)
            {
                result.AddError(
                    "Only one wildcard character is allowed in a rule category name.",
                    nameof(BottomKLogSamplingOptions.Rules));
            }
        }
    }
}
#endif
