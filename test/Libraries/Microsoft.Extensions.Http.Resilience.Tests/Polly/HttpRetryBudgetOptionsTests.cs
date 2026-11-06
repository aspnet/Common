// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience.Internal;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Polly;

public class HttpRetryBudgetOptionsTests
{
    [Fact]
    public void Ctor_Defaults()
    {
        var options = new HttpRetryBudgetOptions();

        options.RetryRatio.Should().Be(0.1);
        options.SamplingDuration.Should().Be(TimeSpan.FromSeconds(10));
        options.MinimumThroughput.Should().Be(10);
    }

    [Fact]
    public void HttpRetryStrategyOptions_RetryBudget_DefaultsToNull()
    {
        new HttpRetryStrategyOptions().RetryBudget.Should().BeNull();
    }

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        new HttpRetryBudgetOptionsValidator().Validate(null, new HttpRetryBudgetOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0, 1, 1, true)]
    [InlineData(1.0, 1, 1, true)]
    [InlineData(0.5, 86_400, int.MaxValue, true)]
    [InlineData(-0.01, 30, 100, false)]
    [InlineData(1.01, 30, 100, false)]
    [InlineData(double.NaN, 30, 100, false)]
    [InlineData(0.1, 0, 100, false)]
    [InlineData(0.1, 86_401, 100, false)]
    [InlineData(0.1, 30, 0, false)]
    [InlineData(0.1, 30, -1, false)]
    public void Validate_Ranges(double retryRatio, int samplingDurationSeconds, int minimumThroughput, bool valid)
    {
        var options = new HttpRetryBudgetOptions
        {
            RetryRatio = retryRatio,
            SamplingDuration = TimeSpan.FromSeconds(samplingDurationSeconds),
            MinimumThroughput = minimumThroughput,
        };

        new HttpRetryBudgetOptionsValidator().Validate(null, options).Succeeded.Should().Be(valid);
    }
}
