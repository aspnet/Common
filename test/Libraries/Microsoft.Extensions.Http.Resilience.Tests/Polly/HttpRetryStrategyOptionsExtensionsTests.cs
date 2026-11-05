// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Polly;

public class HttpRetryStrategyOptionsExtensionsTests
{
    [Fact]
    public void DisableFor_RetryOptionsIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((HttpRetryStrategyOptions)null!).DisableFor(HttpMethod.Get));
    }

    [Fact]
    public void DisableFor_HttpMethodsIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpRetryStrategyOptions().DisableFor(null!));
    }

    [Fact]
    public void DisableFor_HttpMethodsIsEmptry_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HttpRetryStrategyOptions().DisableFor([]));
    }

    [Fact]
    public void DisableFor_ShouldHandleIsNull_Throws()
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = null! };
        Assert.Throws<ArgumentException>(() => options.DisableFor(HttpMethod.Get));
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("DELETE", false)]
    [InlineData("GET", true)]
    public async Task DisableFor_PositiveScenario(string httpMethod, bool shouldHandle)
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = _ => PredicateResult.True() };
        options.DisableFor(HttpMethod.Post, HttpMethod.Delete);

        using var request = new HttpRequestMessage { Method = new HttpMethod(httpMethod) };
        using var response = new HttpResponseMessage { RequestMessage = request };

        Assert.Equal(shouldHandle, await options.ShouldHandle(CreatePredicateArguments(response)));
    }

    [Fact]
    public async Task DisableFor_RespectsOriginalShouldHandlePredicate()
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = _ => PredicateResult.False() };
        options.DisableFor(HttpMethod.Post);

        using var request = new HttpRequestMessage { Method = HttpMethod.Get };
        using var response = new HttpResponseMessage { RequestMessage = request };

        Assert.False(await options.ShouldHandle(CreatePredicateArguments(response)));
    }

    [Fact]
    public async Task DisableFor_ResponseMessageIsNull_RetrievesRequestMessageFromContext()
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = _ => PredicateResult.True() };
        options.DisableFor(HttpMethod.Post);

        using var request = new HttpRequestMessage { Method = HttpMethod.Post };
        var context = ResilienceContextPool.Shared.Get();
        context.SetRequestMessage(request);

        Assert.False(await options.ShouldHandle(CreatePredicateArguments(null, context)));
    }

    [Fact]
    public async Task DisableFor_RequestMessageIsNull_DoesNotDisableRetries()
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = _ => PredicateResult.True() };
        options.DisableFor(HttpMethod.Post);

        using var response = new HttpResponseMessage { RequestMessage = null };
        var context = ResilienceContextPool.Shared.Get();
        context.SetRequestMessage(null);

        Assert.True(await options.ShouldHandle(CreatePredicateArguments(response, context)));
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("DELETE", false)]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("CONNECT", false)]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("TRACE", true)]
    [InlineData("OPTIONS", true)]
    public async Task DisableForUnsafeHttpMethods_PositiveScenario(string httpMethod, bool shouldHandle)
    {
        var options = new HttpRetryStrategyOptions { ShouldHandle = _ => PredicateResult.True() };
        options.DisableForUnsafeHttpMethods();

        using var request = new HttpRequestMessage { Method = new HttpMethod(httpMethod) };
        using var response = new HttpResponseMessage { RequestMessage = request };

        Assert.Equal(shouldHandle, await options.ShouldHandle(CreatePredicateArguments(response)));
    }

    [Fact]
    public void UseBudgetedRetries_RetryOptionsIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((HttpRetryStrategyOptions)null!).UseBudgetedRetries());
    }

    [Fact]
    public void UseBudgetedRetries_OverwritesBudget_PreservesOtherOptions()
    {
        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            RetryBudget = new HttpRetryBudgetOptions { RetryRatio = 0.5 },
        };
        options.DisableForUnsafeHttpMethods();
        var shouldHandle = options.ShouldHandle;

        options.UseBudgetedRetries();

        Assert.Equal(5, options.MaxRetryAttempts);
        Assert.Equal(new HttpRetryBudgetOptions().RetryRatio, options.RetryBudget!.RetryRatio);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Delay);
        Assert.Same(shouldHandle, options.ShouldHandle);
    }

    private static RetryPredicateArguments<HttpResponseMessage> CreatePredicateArguments(HttpResponseMessage? response, ResilienceContext? context = null)
    {
        return new RetryPredicateArguments<HttpResponseMessage>(
            context ?? ResilienceContextPool.Shared.Get(),
            Outcome.FromResult(response),
            attemptNumber: 1);
    }
}
