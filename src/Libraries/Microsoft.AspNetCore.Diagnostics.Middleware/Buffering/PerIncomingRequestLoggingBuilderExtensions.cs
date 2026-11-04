// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using Microsoft.AspNetCore.Diagnostics.Buffering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Lets you register per incoming request log buffering in a dependency injection container.
/// </summary>
public static class PerIncomingRequestLoggingBuilderExtensions
{
    /// <summary>
    /// Adds per incoming request log buffering to the logging infrastructure. 
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="configuration">The <see cref="IConfiguration" /> to add.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered in a buffer specific to each incoming request
    /// and can optionally be flushed and emitted during the request lifetime.
    /// </remarks>
    public static ILoggingBuilder AddPerIncomingRequestBuffer(this ILoggingBuilder builder, IConfiguration configuration)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configuration);

        _ = builder.Services
            .AddSingleton<IConfigureOptions<PerRequestLogBufferingOptions>>(
                new PerRequestLogBufferingConfigureOptions(configuration))
            .AddSingleton<IOptionsChangeTokenSource<PerRequestLogBufferingOptions>>(
                new ConfigurationChangeTokenSource<PerRequestLogBufferingOptions>(configuration))
            .AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsCustomValidator>();

        return builder
            .AddPerRequestBufferManager()
            .AddGlobalBuffer(configuration);
    }

    /// <summary>
    /// Adds per incoming request log buffering to the logging infrastructure. 
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="configure">The buffering options configuration delegate.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered in a buffer specific to each incoming request
    /// and can optionally be flushed and emitted during the request lifetime.
    /// </remarks>
    public static ILoggingBuilder AddPerIncomingRequestBuffer(this ILoggingBuilder builder, Action<PerRequestLogBufferingOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder
            .Services.AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsCustomValidator>()
            .Configure(configure);

        PerRequestLogBufferingOptions options = new PerRequestLogBufferingOptions();
        configure(options);

        return builder
            .AddPerRequestBufferManager()
            .AddGlobalBuffer(opts =>
            {
                opts.Rules = options.Rules;
                opts.IncludeScopes = options.IncludeScopes;
            });
    }

    /// <summary>
    /// Adds per incoming request log buffering to the logging infrastructure. 
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="logLevel">The level (and below) of logs to buffer.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered in a buffer specific to each incoming request
    /// and can optionally be flushed and emitted during the request lifetime.
    /// </remarks>
    public static ILoggingBuilder AddPerIncomingRequestBuffer(this ILoggingBuilder builder, LogLevel? logLevel = null)
    {
        _ = Throw.IfNull(builder);

        _ = builder
            .Services.AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<PerRequestLogBufferingOptions, PerRequestLogBufferingOptionsCustomValidator>()
            .Configure(options =>
            {
                options.Rules.Add(new LogBufferingFilterRule(logLevel: logLevel));
            });

        return builder
            .AddPerRequestBufferManager()
            .AddGlobalBuffer(logLevel);
    }

    private static ILoggingBuilder AddPerRequestBufferManager(this ILoggingBuilder builder)
    {
        builder.Services.TryAddScoped<IncomingRequestLogBufferHolder>();
        builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.TryAddSingleton(sp =>
        {
            var globalBufferManager = sp.GetRequiredService<GlobalLogBufferManager>();
            return ActivatorUtilities.CreateInstance<PerRequestLogBufferManager>(sp, globalBufferManager);
        });
        builder.Services.TryAddSingleton<LogBuffer>(sp => sp.GetRequiredService<PerRequestLogBufferManager>());
        builder.Services.TryAddSingleton<PerRequestLogBuffer>(sp => sp.GetRequiredService<PerRequestLogBufferManager>());

        return builder;
    }
}

#endif
