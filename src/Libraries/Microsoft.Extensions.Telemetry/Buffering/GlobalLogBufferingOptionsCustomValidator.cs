// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Diagnostics.Buffering;

internal sealed class GlobalLogBufferingOptionsCustomValidator : IValidateOptions<GlobalLogBufferingOptions>
{
    private const char WildcardChar = '*';

    public ValidateOptionsResult Validate(string? name, GlobalLogBufferingOptions options)
    {
        ValidateOptionsResultBuilder resultBuilder = new();
        foreach (LogBufferingFilterRule rule in options.Rules)
        {
            if (rule.CategoryName is null)
            {
                continue;
            }

            int wildcardIndex = rule.CategoryName.IndexOf(WildcardChar, StringComparison.Ordinal);
            if (wildcardIndex >= 0 && rule.CategoryName.IndexOf("*", wildcardIndex + 1, StringComparison.Ordinal) >= 0)
            {
                resultBuilder.AddError("Only one wildcard character is allowed in category name.", nameof(options.Rules));
            }
        }

        return resultBuilder.Build();
    }
}
#endif
