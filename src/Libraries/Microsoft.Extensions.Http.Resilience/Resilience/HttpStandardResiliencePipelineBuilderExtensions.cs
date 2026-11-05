// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience.Internal;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Extensions for <see cref="IHttpStandardResiliencePipelineBuilder"/>.
/// </summary>
public static class HttpStandardResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="HttpStandardResilienceOptions"/> for the standard resilience pipeline.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="section">The section that the options will bind against.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static IHttpStandardResiliencePipelineBuilder Configure(this IHttpStandardResiliencePipelineBuilder builder, IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        if (!section.GetChildren().Any())
        {
            Throw.ArgumentException(nameof(section), "Configuration section cannot be empty.");
        }

        _ = builder.Services.Configure<HttpStandardResilienceOptions>(
            builder.PipelineName,
            section,
            o => o.ErrorOnUnknownConfiguration = true);

        return builder;
    }

    /// <summary>
    /// Configures the <see cref="HttpStandardResilienceOptions"/> for the standard resilience pipeline.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="configure">The configure method.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
#pragma warning disable S3872 // Parameter names should not duplicate the names of their methods
    public static IHttpStandardResiliencePipelineBuilder Configure(this IHttpStandardResiliencePipelineBuilder builder, Action<HttpStandardResilienceOptions> configure)
#pragma warning restore S3872 // Parameter names should not duplicate the names of their methods
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        return builder.Configure((options, _) => configure(options));
    }

    /// <summary>
    /// Configures the <see cref="HttpStandardResilienceOptions"/> for the standard resilience pipeline.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="configure">The configure method.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
#pragma warning disable S3872 // Parameter names should not duplicate the names of their methods
    public static IHttpStandardResiliencePipelineBuilder Configure(this IHttpStandardResiliencePipelineBuilder builder, Action<HttpStandardResilienceOptions, IServiceProvider> configure)
#pragma warning restore S3872 // Parameter names should not duplicate the names of their methods
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder.Services.AddOptions<HttpStandardResilienceOptions>(builder.PipelineName).Configure(configure);

        return builder;
    }

    /// <summary>
    /// Enables a retry budget with default values for the standard resilience pipeline.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// This method applies <see cref="HttpRetryStrategyOptionsExtensions.UseBudgetedRetries(HttpRetryStrategyOptions)"/> to
    /// <see cref="HttpStandardResilienceOptions.Retry"/>. Options are configured in registration order, so a
    /// <see cref="HttpRetryStrategyOptions.RetryBudget"/> configured before this call, including through configuration sections
    /// passed to <c>AddStandardResilienceHandler</c>, is overwritten. Configuration registered after this call takes precedence.
    /// </remarks>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHttpStandardResiliencePipelineBuilder UseBudgetedRetries(this IHttpStandardResiliencePipelineBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.Configure(options => options.Retry.UseBudgetedRetries());
    }

    /// <summary>
    /// Instructs the underlying builder to select the pipeline instance by redacted authority (scheme + host + port).
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static IHttpStandardResiliencePipelineBuilder SelectPipelineByAuthority(this IHttpStandardResiliencePipelineBuilder builder)
    {
        _ = Throw.IfNull(builder);

        PipelineKeyProviderHelper.SelectPipelineByAuthority(builder.Services, builder.PipelineName);

        return builder;
    }

    /// <summary>
    /// Instructs the underlying builder to select the pipeline instance by custom selector.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="selectorFactory">The factory that returns a key selector.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>The pipeline key is used in metrics and logs, so don't return any sensitive values.</remarks>
    public static IHttpStandardResiliencePipelineBuilder SelectPipelineBy(this IHttpStandardResiliencePipelineBuilder builder, Func<IServiceProvider, Func<HttpRequestMessage, string>> selectorFactory)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(selectorFactory);

        PipelineKeyProviderHelper.SelectPipelineBy(builder.Services, builder.PipelineName, selectorFactory);

        return builder;
    }
}
