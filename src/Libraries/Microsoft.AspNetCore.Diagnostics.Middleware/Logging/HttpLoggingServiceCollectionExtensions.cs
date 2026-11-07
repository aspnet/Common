// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.AspNetCore.Diagnostics.Logging.Internal;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register the HTTP logging feature within the service.
/// </summary>
public static partial class HttpLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Enables enrichment and redaction of HTTP request logging output.
    /// </summary>
    /// <remarks>
    /// This will enable <see cref="HttpLoggingOptions.CombineLogs"/> and <see cref="HttpLoggingFields.Duration"/> by default.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the redaction options.</param>
    /// <returns>The value of <paramref name="services" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null" />.</exception>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.HttpLogging, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IServiceCollection AddHttpLoggingRedaction(this IServiceCollection services, Action<LoggingRedactionOptions>? configure = null)
    {
        _ = Throw.IfNull(services);

        _ = services.AddRedaction();
        _ = services.AddOptionsWithValidateOnStart<LoggingRedactionOptions, LoggingRedactionOptionsValidator>()
            .Configure(configure ?? (static _ => { }));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpLoggingInterceptor, HttpLoggingRedactionInterceptor>());

        return services.AddHttpLogging(o =>
        {
            o.CombineLogs = true;
            o.LoggingFields |= HttpLoggingFields.Duration;
        })

        // Internal stuff for route processing:
        .AddHttpRouteProcessor()
        .AddHttpRouteUtilities();
    }

    /// <summary>
    /// Enables enrichment and redaction of HTTP request logging output.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="section">The configuration section with the redaction settings.</param>
    /// <returns>The value of <paramref name="services" />.</returns>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.HttpLogging, UrlFormat = DiagnosticIds.UrlFormat)]
    public static IServiceCollection AddHttpLoggingRedaction(this IServiceCollection services, IConfigurationSection section)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(section);

        _ = services.AddSingleton<IConfigureOptions<LoggingRedactionOptions>>(
            new LoggingRedactionOptionsConfigureOptions(section));

        return services.AddHttpLoggingRedaction();
    }

    /// <summary>
    /// Adds an enricher instance of <typeparamref name="T"/> to the <see cref="IServiceCollection"/> to enrich incoming HTTP requests logs.
    /// </summary>
    /// <typeparam name="T">Type of enricher.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the instance of <typeparamref name="T"/> to.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null" />.</exception>
    public static IServiceCollection AddHttpLogEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IHttpLogEnricher
    {
        _ = Throw.IfNull(services);
        return services.AddHttpLoggingRedaction()
            .AddActivatedSingleton<IHttpLogEnricher, T>();
    }
}
#endif
