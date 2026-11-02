// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using Polly;
using Polly.Timeout;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Polly;

public class HttpClientResiliencePredicatesTests
{
#pragma warning disable S2330 // Array covariance should not be used
    public static readonly IEnumerable<object[]> HandledExceptionsClassified = new[]
    {
        new object[] { new InvalidCastException(), false },
        new object[] { new HttpRequestException(), true },
        new object[] { new TimeoutRejectedException(), true },
    };
#pragma warning restore S2330 // Array covariance should not be used

    [Fact]
    public void IsTransientHttpException_NullException_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HttpClientResiliencePredicates.IsTransientHttpException(null!));
    }

    [Theory]
    [MemberData(nameof(HandledExceptionsClassified))]
    public void IsTransientHttpException_Exception_ShouldClassify(Exception ex, bool expectedCondition)
    {
        var isTransientHttpException = HttpClientResiliencePredicates.IsTransientHttpException(ex);
        Assert.Equal(expectedCondition, isTransientHttpException);

        isTransientHttpException = HttpClientResiliencePredicates.IsTransient(Outcome.FromException<HttpResponseMessage>(ex));
        Assert.Equal(expectedCondition, isTransientHttpException);
    }

#pragma warning disable EXTEXP0001
    [Theory]
    [InlineData("System.Private.CoreLib", true, true)]
    [InlineData("<unknown>", true, true)]
    [InlineData(null, true, true)]
    [InlineData("CustomAssembly", true, true)]
    [InlineData("<unknown>", false, false)]
    public void IsTransient_WithCancellationToken_HandlesConnectionTimeout(string? source, bool notCanceled, bool expected)
    {
        var ex = new OperationCanceledException("Connection timed out", new TimeoutException())
        {
            Source = source
        };
        var outcome = Outcome.FromException<HttpResponseMessage>(ex);
        using var cts = new CancellationTokenSource();
        if (!notCanceled)
        {
            cts.Cancel();
        }

        var result = HttpClientResiliencePredicates.IsTransient(outcome, cts.Token);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTransient_WithCancellationToken_NonTimeoutInnerException_ReturnsFalse()
    {
        var ex = new OperationCanceledException("Canceled", new InvalidOperationException());
        var outcome = Outcome.FromException<HttpResponseMessage>(ex);

        var result = HttpClientResiliencePredicates.IsTransient(outcome, CancellationToken.None);
        Assert.False(result);
    }
#pragma warning restore EXTEXP0001

    public class HttpResponseMessageExtensionsTests
    {
        [Theory]
        [InlineData(HttpStatusCode.OK, false)]
        [InlineData(HttpStatusCode.BadRequest, false)]
        [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
        [InlineData(HttpStatusCode.InternalServerError, true)]
        [InlineData(HttpStatusCode.HttpVersionNotSupported, true)]
        [InlineData(HttpStatusCode.RequestTimeout, true)]
        [InlineData((HttpStatusCode)429, true)]
        public void IsTransientFailure_ShouldClassify(HttpStatusCode statusCode, bool expectedCondition)
        {
            var response = new HttpResponseMessage { StatusCode = statusCode };
            var isTransientFailure = HttpClientResiliencePredicates.IsTransientHttpFailure(response);
            Assert.Equal(expectedCondition, isTransientFailure);

            isTransientFailure = HttpClientResiliencePredicates.IsTransient(Outcome.FromResult(response));
            Assert.Equal(expectedCondition, isTransientFailure);

            response.Dispose();
        }

        [Fact]
        public void IsTransientFailure_NullResponse_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => HttpClientResiliencePredicates.IsTransientHttpFailure(null!));
        }
    }
}
