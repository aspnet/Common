// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Latency;
using Microsoft.Extensions.Http.Latency.Internal;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions to add http client latency telemetry.
/// </summary>
public static class HttpClientLatencyTelemetryExtensions
{
    /// <summary>
    /// Adds a <see cref="DelegatingHandler" /> to collect latency information and enrich outgoing request log for all http clients.
    /// </summary>
    /// <remarks>
    /// This extension configures latency information collection globally for all http clients.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);

        _ = services.AddLatencyContext();
        _ = services.RegisterCheckpointNames(HttpCheckpoints.Checkpoints);
        _ = services.AddOptions<HttpClientLatencyTelemetryOptions>();
        _ = services.AddSingleton<HttpRequestLatencyListener>();
        _ = services.AddSingleton<HttpClientLatencyContext>();
        _ = services.AddTransient<HttpLatencyTelemetryHandler>();
        _ = services.AddSingleton<HttpLatencyMediator>();
        _ = services.RegisterMeasureNames(HttpMeasures.Measures);
        _ = services.RegisterTagNames(HttpTags.Tags);
        _ = services.AddHttpClientLogEnricher<HttpClientLatencyLogEnricher>();

        return services.ConfigureAll<HttpClientFactoryOptions>(
            httpClientOptions =>
            {
                httpClientOptions
                .HttpMessageHandlerBuilderActions.Add(httpMessageHandlerBuilder =>
                {
                    var handler = httpMessageHandlerBuilder.Services.GetRequiredService<HttpLatencyTelemetryHandler>();
                    httpMessageHandlerBuilder.AdditionalHandlers.Add(handler);
                });
            });
    }

    /// <summary>
    /// Adds a <see cref="DelegatingHandler" /> to collect latency information and enrich outgoing request log for all http clients.
    /// </summary>
    /// <remarks>
    /// This extension configures outgoing request logs auto collection globally for all http clients.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> to use for configuring <see cref="HttpClientLatencyTelemetryOptions"/>.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services, IConfigurationSection section)
    {
        _ = Throw.IfNull(section);

        _ = services
            .Configure<HttpClientLatencyTelemetryOptions>(section);

        return services.AddHttpClientLatencyTelemetry();
    }

    /// <summary>
    /// Adds a <see cref="DelegatingHandler" /> to collect latency information and enrich outgoing request log for all http clients.
    /// </summary>
    /// <remarks>
    /// This extension configures outgoing request logs auto collection globally for all http clients.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">The delegate to configure <see cref="HttpClientLatencyTelemetryOptions"/> with.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services, Action<HttpClientLatencyTelemetryOptions> configure)
    {
        _ = Throw.IfNull(configure);

        _ = services
            .Configure(configure);

        return services.AddHttpClientLatencyTelemetry();
    }
}
