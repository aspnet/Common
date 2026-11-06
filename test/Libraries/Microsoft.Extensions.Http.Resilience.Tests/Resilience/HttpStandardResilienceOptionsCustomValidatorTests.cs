// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
#if NET6_0_OR_GREATER
using System.Linq;
using Microsoft;
using Microsoft.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
#endif
using Microsoft.Extensions.Http.Resilience.Internal.Validators;
using Polly;
using Polly.Retry;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Resilience;

public class HttpStandardResilienceOptionsCustomValidatorTests
{
    [Fact]
    public void Validate_InvalidOptions_EnsureValidationErrors()
    {
        HttpStandardResilienceOptions options = new();
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(1);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(1);

        var validationResult = new HttpStandardResilienceOptionsCustomValidator().Validate(string.Empty, options);

        Assert.True(validationResult.Failed);

#if NET6_0_OR_GREATER
        Assert.Equal(2, validationResult.Failures.Count());
#endif
    }

    [Fact]
    public void Validate_ValidOptions_NoValidationErrors()
    {
        HttpStandardResilienceOptions options = new();

        var validationResult = new HttpStandardResilienceOptionsCustomValidator().Validate(string.Empty, options);

        Assert.True(validationResult.Succeeded);
    }

    public static IEnumerable<object[]> GetOptions_ValidOptions_EnsureNoErrors_Data
    {
        get
        {
            var options = new HttpStandardResilienceOptions();
            options.AttemptTimeout.Timeout = options.TotalRequestTimeout.Timeout;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(options.AttemptTimeout.Timeout.TotalMilliseconds * 2);
            yield return new object[] { options };

            options = new HttpStandardResilienceOptions();
            options.AttemptTimeout.Timeout = options.TotalRequestTimeout.Timeout;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(options.AttemptTimeout.Timeout.TotalMilliseconds * 2) + TimeSpan.FromMilliseconds(10);
            yield return new object[] { options };

            options = new HttpStandardResilienceOptions();
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.BackoffType = DelayBackoffType.Linear;
            options.Retry.Delay = options.TotalRequestTimeout.Timeout;
            yield return new object[] { options };
        }
    }

    [MemberData(nameof(GetOptions_ValidOptions_EnsureNoErrors_Data))]
    [Theory]
    public void Validate_ValidOptions_EnsureNoErrors(HttpStandardResilienceOptions options)
    {
        var validationResult = new HttpStandardResilienceOptionsCustomValidator().Validate(string.Empty, options);

        Assert.False(validationResult.Failed);
    }

    public static IEnumerable<object[]> GetOptions_InvalidOptions_EnsureErrors_Data
    {
        get
        {
            var options = new HttpStandardResilienceOptions();
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
            yield return new object[] { options };

            options = new HttpStandardResilienceOptions();
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
            yield return new object[] { options };

            options = new HttpStandardResilienceOptions();
            options.AttemptTimeout.Timeout = options.TotalRequestTimeout.Timeout;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(options.AttemptTimeout.Timeout.TotalMilliseconds / 2);
            yield return new object[] { options };
        }
    }

    [MemberData(nameof(GetOptions_InvalidOptions_EnsureErrors_Data))]
    [Theory]
    public void Validate_InvalidOptions_EnsureErrors(HttpStandardResilienceOptions options)
    {
        var validationResult = new HttpStandardResilienceOptionsCustomValidator().Validate(string.Empty, options);

        Assert.True(validationResult.Failed);
    }

    [Fact]
    public void Validate_RetryBudget_OnlyAllowedByBudgetAwareValidator()
    {
        var options = new HttpStandardResilienceOptions();
        options.Retry.UseBudgetedRetries();

        Assert.True(new HttpStandardResilienceOptionsValidator().Validate(string.Empty, options).Failed);
        Assert.True(new HttpStandardResilienceOptionsBudgetAwareValidator().Validate(string.Empty, options).Succeeded);
    }
}
