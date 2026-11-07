// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Lets you register log buffering in a dependency injection container.
/// </summary>
public static class GlobalBufferLoggingBuilderExtensions
{
    /// <summary>
    /// Adds global log buffering to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="configuration">The <see cref="IConfiguration" /> to add.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered and can optionally be flushed and emitted.
    /// </remarks>
    public static ILoggingBuilder AddGlobalBuffer(this ILoggingBuilder builder, IConfiguration configuration)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configuration);

        _ = builder
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsCustomValidator>()
            .Services.AddSingleton<IConfigureOptions<GlobalLogBufferingOptions>>(
                new GlobalLogBufferingConfigureOptions(configuration))
            .AddSingleton<IOptionsChangeTokenSource<GlobalLogBufferingOptions>>(
                new ConfigurationChangeTokenSource<GlobalLogBufferingOptions>(configuration));

        return builder.AddGlobalBufferManager();
    }

    /// <summary>
    /// Adds global log buffering to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="configure">Configure buffer options.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered and can optionally be flushed and emitted.
    /// </remarks>
    public static ILoggingBuilder AddGlobalBuffer(this ILoggingBuilder builder, Action<GlobalLogBufferingOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsCustomValidator>()
            .Configure(configure);

        return builder.AddGlobalBufferManager();
    }

    /// <summary>
    /// Adds global log buffering to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder" />.</param>
    /// <param name="logLevel">The log level (and below) to apply the buffer to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be buffered and can optionally be flushed and emitted.
    /// </remarks>
    public static ILoggingBuilder AddGlobalBuffer(this ILoggingBuilder builder, LogLevel? logLevel = null)
    {
        _ = Throw.IfNull(builder);

        _ = builder
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<GlobalLogBufferingOptions, GlobalLogBufferingOptionsCustomValidator>()
            .Configure(options => options.Rules.Add(new LogBufferingFilterRule(logLevel: logLevel)));

        return builder.AddGlobalBufferManager();
    }

    private static ILoggingBuilder AddGlobalBufferManager(this ILoggingBuilder builder)
    {
        _ = builder.Services.AddExtendedLoggerFeactory();

        builder.Services.TryAddSingleton<IExternalScopeProvider>(static sp =>
            new LoggerFactoryScopeProvider(sp.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value.ActivityTrackingOptions));

        builder.Services.TryAddSingleton<GlobalLogBufferManager>();
        builder.Services.TryAddSingleton<GlobalLogBuffer>(static sp => sp.GetRequiredService<GlobalLogBufferManager>());
        builder.Services.TryAddSingleton<LogBuffer>(static sp => sp.GetRequiredService<GlobalLogBufferManager>());

        return builder;
    }
}

#endif
