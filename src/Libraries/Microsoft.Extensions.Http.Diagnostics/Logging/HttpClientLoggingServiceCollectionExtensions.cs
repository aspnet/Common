// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Telemetry.Internal;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions to register extended HTTP client logging features.
/// </summary>
public static class HttpClientLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for all HTTP clients created with <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Argument <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);

        _ = services
            .AddRedaction()
            .AddHttpRouteProcessor()
            .AddHttpHeadersRedactor()
            .AddOutgoingRequestContext();

        services.TryAddSingleton<HttpClientLogger>();
        services.TryAddActivatedSingleton<IHttpRequestReader, HttpRequestReader>();
        services.TryAddActivatedSingleton<IHttpHeadersReader, HttpHeadersReader>();

        return services.ConfigureHttpClientDefaults(
            static httpClientBuilder =>
                httpClientBuilder
                    .RemoveAllLoggers()
                    .AddLogger<HttpClientLogger>(wrapHandlersPipeline: true));
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for all HTTP clients created with <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> to use for configuring <see cref="LoggingOptions"/>.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services, IConfigurationSection section)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(section);

        _ = services
            .AddOptionsWithValidateOnStart<LoggingOptions, LoggingOptionsValidator>()
            .BindConfigurationSection(section);

        return services.AddExtendedHttpClientLogging();
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for all HTTP clients created with <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">The delegate to configure <see cref="LoggingOptions"/> with.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services, Action<LoggingOptions> configure)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(configure);

        _ = services
            .AddOptionsWithValidateOnStart<LoggingOptions, LoggingOptionsValidator>()
            .Configure(configure);

        return services.AddExtendedHttpClientLogging();
    }

    /// <summary>
    /// Adds an enricher instance of <typeparamref name="T"/> to the <see cref="IServiceCollection"/> to enrich <see cref="HttpClient"/> logs.
    /// </summary>
    /// <typeparam name="T">Type of enricher.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the instance of <typeparamref name="T"/> to.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    public static IServiceCollection AddHttpClientLogEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IHttpClientLogEnricher
        => Throw.IfNull(services).AddActivatedSingleton<IHttpClientLogEnricher, T>();
}
