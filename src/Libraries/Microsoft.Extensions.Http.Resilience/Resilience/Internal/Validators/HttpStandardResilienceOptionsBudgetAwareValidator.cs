// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Http.Resilience.Internal.Validators;

// The standard resilience handler applies the retry budget, so a configured budget is valid here.
internal sealed class HttpStandardResilienceOptionsBudgetAwareValidator : IValidateOptions<HttpStandardResilienceOptions>
{
    private readonly HttpStandardResilienceOptionsValidator _validator = new();

    public ValidateOptionsResult Validate(string? name, HttpStandardResilienceOptions options)
        => RetryBudgetValidationScope.ValidateWithBudgetApplied(() => _validator.Validate(name, options));
}
