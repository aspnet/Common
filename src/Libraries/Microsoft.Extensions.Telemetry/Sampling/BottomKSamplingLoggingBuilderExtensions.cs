// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Options;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Registers adaptive bottom-K log sampling, which reuses the existing logging pipeline seams: the
/// <see cref="LoggingSampler"/> for the admit/drop decision and the <see cref="LogBuffer"/> for
/// holding admitted records and emitting them &#8212; weighted &#8212; at each period flush.
/// </summary>
[Experimental(DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
public static class BottomKSamplingLoggingBuilderExtensions
{
    /// <summary>
    /// Adds adaptive bottom-K log sampling to the logging infrastructure with default options.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder AddBottomKLogSampling(this ILoggingBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.AddBottomKLogSamplingCore();
    }

    /// <summary>
    /// Adds adaptive bottom-K log sampling to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">The delegate used to configure bottom-K sampling.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder AddBottomKLogSampling(
        this ILoggingBuilder builder,
        Action<BottomKLogSamplingOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = EnsureBottomKCompatible(builder);
        _ = builder.Services.Configure(configure);

        return builder.AddBottomKLogSamplingCore();
    }

    /// <summary>
    /// Adds adaptive bottom-K log sampling to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="section">The configuration section used to configure bottom-K sampling.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    public static ILoggingBuilder AddBottomKLogSampling(
        this ILoggingBuilder builder,
        IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        _ = EnsureBottomKCompatible(builder);
        _ = builder.Services.Configure<BottomKLogSamplingOptions>(section);

        return builder.AddBottomKLogSamplingCore();
    }

    private static ILoggingBuilder AddBottomKLogSamplingCore(this ILoggingBuilder builder)
    {
        if (EnsureBottomKCompatible(builder))
        {
            return builder;
        }

        _ = builder.Services
            .AddOptionsWithValidateOnStart<BottomKLogSamplingOptions, BottomKLogSamplingOptionsValidator>()
            .Services.AddOptionsWithValidateOnStart<BottomKLogSamplingOptions, BottomKLogSamplingOptionsCustomValidator>();

        builder.Services.TryAddSingleton<BottomKLogBuffer>(services => new BottomKLogBuffer(
            services.GetRequiredService<IOptionsMonitor<BottomKLogSamplingOptions>>(),
            TimeProvider.System));
        builder.Services.TryAddSingleton<LogBuffer>(sp => sp.GetRequiredService<BottomKLogBuffer>());

        return builder.AddSampler<BottomKLoggingSampler>();
    }

    private static bool EnsureBottomKCompatible(ILoggingBuilder builder)
    {
        bool bottomKRegistered = builder.Services.Any(descriptor =>
            !descriptor.IsKeyedService && descriptor.ServiceType == typeof(BottomKLogBuffer));
        if (bottomKRegistered)
        {
            return true;
        }

        if (builder.Services.Any(descriptor =>
            !descriptor.IsKeyedService && descriptor.ServiceType == typeof(LogBuffer)))
        {
            Throw.InvalidOperationException(
                "Bottom-K log sampling cannot be combined with another log buffer in the same logging pipeline.");
        }

        if (builder.Services.Any(descriptor =>
            !descriptor.IsKeyedService && descriptor.ServiceType == typeof(LoggingSampler)))
        {
            Throw.InvalidOperationException(
                "Bottom-K log sampling cannot be combined with another logging sampler in the same logging pipeline.");
        }

        return false;
    }
}
#endif
