// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience.Test.Hedging;
using Polly;
using Polly.Retry;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Polly;

public class HttpRetryStrategyOptionsTests
{
#pragma warning disable S2330
    public static readonly IEnumerable<object[]> HandledExceptionsClassified = new[]
    {
        new object[] { new InvalidCastException(), null!, false },
        [new HttpRequestException(), null!, true],
        [new OperationCanceledExceptionMock(new TimeoutException()), null!, true],
        [new OperationCanceledExceptionMock(new TimeoutException()), default(CancellationToken), true],
        [new OperationCanceledExceptionMock(new InvalidOperationException()), default(CancellationToken), false],
        [new OperationCanceledExceptionMock(new TimeoutException()), new CancellationToken(canceled: true), false],
        [new OperationCanceledException("<unknown>", new TimeoutException()) { Source = "<unknown>" }, default(CancellationToken), true],
        [new TaskCanceledException("Connection timed out", new TimeoutException()), default(CancellationToken), true],
    };

    private readonly HttpRetryStrategyOptions _testClass = new();

    [Fact]
    public void Ctor_Defaults()
    {
        var options = new HttpRetryStrategyOptions();

        options.BackoffType.Should().Be(DelayBackoffType.Exponential);
        options.UseJitter.Should().BeTrue();
        options.MaxRetryAttempts.Should().Be(3);
        options.Delay.Should().Be(TimeSpan.FromSeconds(2));
        options.ShouldHandle.Should().NotBeNull();
        options.OnRetry.Should().BeNull();
        options.ShouldRetryAfterHeader.Should().BeTrue();
        options.DelayGenerator.Should().NotBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    public async Task ShouldHandleResultAsError_DefaultValue_ShouldClassify(HttpStatusCode statusCode, bool expectedCondition)
    {
        var response = new HttpResponseMessage { StatusCode = statusCode };
        var isTransientFailure = await _testClass.ShouldHandle(CreateArgs(Outcome.FromResult(response)));

        Assert.Equal(expectedCondition, isTransientFailure);
        response.Dispose();
    }

    [Theory]
    [MemberData(nameof(HandledExceptionsClassified))]
    public async Task ShouldHandleException_DefaultValue_ShouldClassify(Exception exception, CancellationToken? token, bool expectedToHandle)
    {
        var args = CreateArgs(Outcome.FromException<HttpResponseMessage>(exception), token ?? default);
        var shouldHandle = await _testClass.ShouldHandle(args);
        Assert.Equal(expectedToHandle, shouldHandle);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    public async Task ShouldHandleResultAsError_DefaultInstance_ShouldClassify(HttpStatusCode statusCode, bool expectedCondition)
    {
        var response = new HttpResponseMessage { StatusCode = statusCode };
        var isTransientFailure = await new HttpRetryStrategyOptions().ShouldHandle(CreateArgs(Outcome.FromResult(response)));
        Assert.Equal(expectedCondition, isTransientFailure);
        response.Dispose();
    }

    [Theory]
    [MemberData(nameof(HandledExceptionsClassified))]
    public async Task ShouldHandleException_DefaultInstance_ShouldClassify(Exception exception, CancellationToken? token, bool expectedToHandle)
    {
        var args = CreateArgs(Outcome.FromException<HttpResponseMessage>(exception), token ?? default);
        var shouldHandle = await new HttpRetryStrategyOptions().ShouldHandle(args);
        Assert.Equal(expectedToHandle, shouldHandle);
    }

    [Fact]
    public async Task ShouldRetryAfterHeader_InvalidOutcomes_ShouldReturnNull()
    {
        var options = new HttpRetryStrategyOptions { ShouldRetryAfterHeader = true };
        using var responseMessage = new HttpResponseMessage();

        Assert.NotNull(options.DelayGenerator);

        var result = await options.DelayGenerator(
            new(ResilienceContextPool.Shared.Get(),
            Outcome.FromResult(responseMessage),
            0));

        result.Should().BeNull();

        result = await options.DelayGenerator(
            new(ResilienceContextPool.Shared.Get(),
            Outcome.FromResult<HttpResponseMessage>(null),
            0));
        result.Should().BeNull();

        result = await options.DelayGenerator(
            new(ResilienceContextPool.Shared.Get(),
            Outcome.FromException<HttpResponseMessage>(new InvalidOperationException()),
            0));
        result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldRetryAfterHeader_WhenResponseContainsRetryAfterHeader_ShouldReturnTimeSpan()
    {
        var options = new HttpRetryStrategyOptions { ShouldRetryAfterHeader = true };
        using var responseMessage = new HttpResponseMessage
        {
            Headers =
            {
                RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10))
            }
        };

        var result = await options.DelayGenerator!(
            new(ResilienceContextPool.Shared.Get(),
            Outcome.FromResult(responseMessage),
            0));

        Assert.Equal(result, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetDelayGenerator_ShouldGetBasedOnShouldRetryAfterHeader(bool shouldRetryAfterHeader)
    {
        var options = new HttpRetryStrategyOptions
        {
            ShouldRetryAfterHeader = shouldRetryAfterHeader
        };

        Assert.Equal(shouldRetryAfterHeader, options.DelayGenerator != null);
    }

    private static RetryPredicateArguments<HttpResponseMessage> CreateArgs(
        Outcome<HttpResponseMessage> outcome,
        CancellationToken cancellationToken = default)
            => new(ResilienceContextPool.Shared.Get(cancellationToken), outcome, 0);
}
