// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Extensions for configuring logging redaction features.
/// </summary>
public static class LoggingRedactionExtensions
{
    /// <summary>
    /// Enables redaction functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableRedaction(this ILoggingBuilder builder)
        => EnableRedaction(builder, _ => { });

    /// <summary>
    /// Enables redaction functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="configure">Delegate the fine-tune the options.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableRedaction(this ILoggingBuilder builder, Action<LoggerRedactionOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder.Services
                .AddExtendedLoggerFactory()
                .Configure(configure);

        return builder;
    }

    /// <summary>
    /// Enables redaction functionality within the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="section">Configuration section that contains <see cref="LoggerRedactionOptions"/>.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder EnableRedaction(this ILoggingBuilder builder, IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        _ = builder.Services
                .AddExtendedLoggerFactory()
                .AddOptions<LoggerRedactionOptions>().Bind(section);

        return builder;
    }
}
