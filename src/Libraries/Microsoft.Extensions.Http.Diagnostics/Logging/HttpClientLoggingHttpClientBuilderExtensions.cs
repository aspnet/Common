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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Telemetry.Internal;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions to register extended HTTP client logging features.
/// </summary>
public static class HttpClientLoggingHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Argument <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return AddExtendedHttpClientLoggingInternal(builder, configureOptionsBuilder: null, wrapHandlersPipeline: true);
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="wrapHandlersPipeline">
    /// When <see langword="true"/>, the logger is placed at the beginning of the request pipeline, wrapping all other handlers.
    /// When <see langword="false"/>, the logger is placed at the end of the pipeline, right before the primary message handler.
    /// </param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Argument <paramref name="builder"/> is <see langword="null"/>.</exception>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, bool wrapHandlersPipeline)
    {
        _ = Throw.IfNull(builder);

        return AddExtendedHttpClientLoggingInternal(builder, configureOptionsBuilder: null, wrapHandlersPipeline);
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> to use for configuring <see cref="LoggingOptions"/>.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        return AddExtendedHttpClientLoggingInternal(builder, options => options.BindConfigurationSection(section), wrapHandlersPipeline: true);
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> to use for configuring <see cref="LoggingOptions"/>.</param>
    /// <param name="wrapHandlersPipeline">
    /// When <see langword="true"/>, the logger is placed at the beginning of the request pipeline, wrapping all other handlers.
    /// When <see langword="false"/>, the logger is placed at the end of the pipeline, right before the primary message handler.
    /// </param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, IConfigurationSection section, bool wrapHandlersPipeline)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        return AddExtendedHttpClientLoggingInternal(builder, options => options.BindConfigurationSection(section), wrapHandlersPipeline);
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="configure">The delegate to configure <see cref="LoggingOptions"/> with.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, Action<LoggingOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        return AddExtendedHttpClientLoggingInternal(builder, options => options.Configure(configure), wrapHandlersPipeline: true);
    }

    /// <summary>
    /// Adds an <see cref="IHttpClientAsyncLogger" /> to emit logs for outgoing requests for a named <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="configure">The delegate to configure <see cref="LoggingOptions"/> with.</param>
    /// <param name="wrapHandlersPipeline">
    /// When <see langword="true"/>, the logger is placed at the beginning of the request pipeline, wrapping all other handlers.
    /// When <see langword="false"/>, the logger is placed at the end of the pipeline, right before the primary message handler.
    /// </param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <remarks>
    /// All other loggers are removed - including the default one, registered via <see cref="HttpClientBuilderExtensions.AddDefaultLogger(IHttpClientBuilder)"/>.
    /// Information logged by this method is added as key/value pairs to log state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, Action<LoggingOptions> configure, bool wrapHandlersPipeline)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        return AddExtendedHttpClientLoggingInternal(builder, options => options.Configure(configure), wrapHandlersPipeline);
    }

    private static IHttpClientBuilder AddExtendedHttpClientLoggingInternal(
        IHttpClientBuilder builder,
        Action<OptionsBuilder<LoggingOptions>>? configureOptionsBuilder,
        bool wrapHandlersPipeline)
    {
        var optionsBuilder = builder.Services
            .AddOptionsWithValidateOnStart<LoggingOptions, LoggingOptionsValidator>(builder.Name);

        configureOptionsBuilder?.Invoke(optionsBuilder);

        _ = builder.Services
            .AddRedaction()
            .AddHttpRouteProcessor()
            .AddHttpHeadersRedactor()
            .AddOutgoingRequestContext();

        builder.Services.TryAddKeyedSingleton<HttpClientLogger>(builder.Name);
        builder.Services.TryAddKeyedSingleton<IHttpRequestReader, HttpRequestReader>(builder.Name);
        builder.Services.TryAddKeyedSingleton<IHttpHeadersReader, HttpHeadersReader>(builder.Name);

        return builder
            .RemoveAllLoggers()
            .AddLogger(
                serviceProvider => serviceProvider.GetRequiredKeyedService<HttpClientLogger>(builder.Name),
                wrapHandlersPipeline);
    }
}
