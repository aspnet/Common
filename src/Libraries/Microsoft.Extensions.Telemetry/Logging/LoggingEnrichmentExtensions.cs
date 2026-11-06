// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Extensions for configuring logging enrichment features.
/// </summary>
public static class LoggingEnrichmentExtensions
{
    /// <summary>
    /// Enables enrichment functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableEnrichment(this ILoggingBuilder builder)
        => EnableEnrichment(builder, _ => { });

    /// <summary>
    /// Enables enrichment functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="configure">Delegate the fine-tune the options.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableEnrichment(this ILoggingBuilder builder, Action<LoggerEnrichmentOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder.Services
            .AddExtendedLoggerFactory()
            .Configure(configure)
            .AddOptionsWithValidateOnStart<LoggerEnrichmentOptions, LoggerEnrichmentOptionsValidator>();

        return builder;
    }

    /// <summary>
    /// Enables enrichment functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="section">Configuration section that contains <see cref="LoggerEnrichmentOptions"/>.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableEnrichment(this ILoggingBuilder builder, IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        _ = builder.Services
            .AddExtendedLoggerFactory()
            .AddOptionsWithValidateOnStart<LoggerEnrichmentOptions, LoggerEnrichmentOptionsValidator>().Bind(section);

        return builder;
    }

    /// <summary>
    /// Adds a default implementation of the <see cref="ILoggerFactory"/> to the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    internal static IServiceCollection AddExtendedLoggerFactory(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerFactory, ExtendedLoggerFactory>());

        return services;
    }
}
