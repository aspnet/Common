// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Extensions for configuring logging sampling.
/// </summary>
public static class SamplingLoggerBuilderExtensions
{
    /// <summary>
    /// Adds Trace-based logging sampler to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>Sampling decisions for logs match exactly the sampling decisions for the underlying <see cref="System.Diagnostics.Activity"/>.
    /// You may want to configure Tracing Sampling separately as part of OpenTelemetry .NET.</remarks>
    public static ILoggingBuilder AddTraceBasedSampler(this ILoggingBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.AddSampler<TraceBasedSampler>();
    }

    /// <summary>
    /// Adds Random Probabilistic logging sampler to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="configuration">The <see cref="IConfiguration" /> to add.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be randomly sampled according to the configured probability.
    /// Higher the probability value, higher is the probability of a given log record to be sampled in.
    /// </remarks>
    public static ILoggingBuilder AddRandomProbabilisticSampler(this ILoggingBuilder builder, IConfiguration configuration)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configuration);

        _ = builder.Services
            .AddOptionsWithValidateOnStart<RandomProbabilisticSamplerOptions, RandomProbabilisticSamplerOptionsValidator>();
        _ = builder.Services
            .AddSingleton<IConfigureOptions<RandomProbabilisticSamplerOptions>>(
                new RandomProbabilisticSamplerConfigureOptions(configuration));
        _ = builder.Services
            .AddSingleton<IOptionsChangeTokenSource<RandomProbabilisticSamplerOptions>>(
                new ConfigurationChangeTokenSource<RandomProbabilisticSamplerOptions>(configuration));

        _ = builder.Services
            .AddSingleton(typeof(LogSamplingRuleSelector<>));

        return builder.AddSampler<RandomProbabilisticSampler>();
    }

    /// <summary>
    /// Adds Random Probabilistic logging sampler to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="configure">The <see cref="RandomProbabilisticSamplerOptions"/> configuration delegate.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matched logs will be randmoly sampled according to the configured probability.
    /// Higher the probability value, higher is the probability of a given log record to be sampled in.
    /// </remarks>
    public static ILoggingBuilder AddRandomProbabilisticSampler(this ILoggingBuilder builder, Action<RandomProbabilisticSamplerOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder.Services
            .AddOptionsWithValidateOnStart<RandomProbabilisticSamplerOptions, RandomProbabilisticSamplerOptionsValidator>()
            .Configure(configure);

        _ = builder.Services.AddSingleton(typeof(LogSamplingRuleSelector<>));

        return builder.AddSampler<RandomProbabilisticSampler>();
    }

    /// <summary>
    /// Adds Random Probabilistic logging sampler to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="probability">Probability from 0.0 to 1.0.</param>
    /// <param name="level">The log level (and below) to apply the sampler to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> is less than 0 or greater than 1.</exception>
    /// <remarks>
    /// Matched logs will be randomly sampled according to the configured <paramref name="probability"/>.
    /// Higher the probability value, higher is the probability of a given log record to be sampled in.
    /// </remarks>
    public static ILoggingBuilder AddRandomProbabilisticSampler(this ILoggingBuilder builder, double probability, LogLevel? level = null)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfOutOfRange(probability, 0, 1, nameof(probability));

        _ = builder.Services
            .AddOptionsWithValidateOnStart<RandomProbabilisticSamplerOptions, RandomProbabilisticSamplerOptionsValidator>()
            .Configure(options => options.Rules.Add(new RandomProbabilisticSamplerFilterRule(probability, logLevel: level)));

        _ = builder.Services.AddSingleton(typeof(LogSamplingRuleSelector<>));

        return builder.AddSampler<RandomProbabilisticSampler>();
    }

    /// <summary>
    /// Adds a logging sampler type to the logging infrastructure.
    /// </summary>
    /// <typeparam name="T">Logging sampler type.</typeparam>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ILoggingBuilder AddSampler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this ILoggingBuilder builder)
        where T : LoggingSampler
    {
        _ = Throw.IfNull(builder);

        _ = builder.Services.AddExtendedLoggerFactory();
        _ = builder.Services.AddSingleton<LoggingSampler, T>();

        return builder;
    }

    /// <summary>
    /// Adds a logging sampler instance to the logging infrastructure.
    /// </summary>
    /// <param name="builder">The dependency injection container to add logging to.</param>
    /// <param name="sampler">The sampler instance to add.</param>
    /// <returns>The value of <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="sampler"/> is <see langword="null"/>.</exception>
    public static ILoggingBuilder AddSampler(this ILoggingBuilder builder, LoggingSampler sampler)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(sampler);

        _ = builder.Services.AddExtendedLoggerFactory();
        _ = builder.Services.AddSingleton(sampler);

        return builder;
    }
}
