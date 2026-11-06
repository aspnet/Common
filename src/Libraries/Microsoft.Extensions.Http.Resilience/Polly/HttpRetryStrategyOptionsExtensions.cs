// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using Polly;
using Polly.Retry;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Extensions for <see cref="HttpRetryStrategyOptions"/>.
/// </summary>
[Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
public static class HttpRetryStrategyOptionsExtensions
{
#if !NET8_0_OR_GREATER
    private static readonly HttpMethod _connect = new("CONNECT");
    private static readonly HttpMethod _patch = new("PATCH");
#endif

    /// <summary>
    /// Enables a retry budget with default values to reduce the extra load that retries put on a degraded dependency.
    /// </summary>
    /// <param name="options">The retry strategy options.</param>
    /// <remarks>
    /// This method sets <see cref="HttpRetryStrategyOptions.RetryBudget"/> to a new <see cref="HttpRetryBudgetOptions"/> instance
    /// with default values, overwriting any existing budget. Other retry options are not changed.
    /// Values set after calling this method take precedence. Use
    /// <see cref="HttpRetryResiliencePipelineBuilderExtensions.AddRetryWithBudget(ResiliencePipelineBuilder{HttpResponseMessage}, HttpRetryStrategyOptions)"/>
    /// to apply the retry budget in custom pipelines. Polly's <c>AddRetry</c> can't apply it and throws a
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/> instead.
    /// </remarks>
    public static void UseBudgetedRetries(this HttpRetryStrategyOptions options)
    {
        _ = Throw.IfNull(options);

        options.RetryBudget = new HttpRetryBudgetOptions();
    }

    /// <summary>
    /// Disables retry attempts for POST, PATCH, PUT, DELETE, and CONNECT HTTP methods.
    /// </summary>
    /// <param name="options">The retry strategy options.</param>
    public static void DisableForUnsafeHttpMethods(this HttpRetryStrategyOptions options)
    {
        options.DisableFor(
            HttpMethod.Delete, HttpMethod.Post, HttpMethod.Put,
#if !NET8_0_OR_GREATER
            _connect, _patch);
#else
            HttpMethod.Connect, HttpMethod.Patch);
#endif
    }

    /// <summary>
    /// Disables retry attempts for the given list of HTTP methods.
    /// </summary>
    /// <param name="options">The retry strategy options.</param>
    /// <param name="methods">The list of HTTP methods.</param>
    public static void DisableFor(this HttpRetryStrategyOptions options, params HttpMethod[] methods)
    {
        _ = Throw.IfNullOrEmpty(methods);

        var shouldHandle = Throw.IfNullOrMemberNull(options, options?.ShouldHandle);

        options.ShouldHandle = async args =>
        {
            var result = await shouldHandle(args).ConfigureAwait(args.Context.ContinueOnCapturedContext);

            if (result && GetRequestMessage(args) is HttpRequestMessage request)
            {
                return !methods.Contains(request.Method);
            }

            return result;
        };
    }

    private static HttpRequestMessage? GetRequestMessage(RetryPredicateArguments<HttpResponseMessage> args) =>
        args.Outcome.Result?.RequestMessage ?? args.Context.GetRequestMessage();
}

