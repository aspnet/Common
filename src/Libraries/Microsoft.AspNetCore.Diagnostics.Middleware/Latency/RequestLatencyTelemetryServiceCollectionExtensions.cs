// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Diagnostics.Latency;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions to register the request latency telemetry middleware.
/// </summary>
public static class RequestLatencyTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds all request checkpointing services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRequestCheckpoint(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);

        _ = services.AddLatencyContext();
        services.TryAddScoped(p => p.GetRequiredService<ILatencyContextProvider>().CreateContext());

        return services
            .AddPipelineEntryCheckpoint()
            .RegisterCheckpointNames(RequestCheckpointConstants.RequestCheckpointNames);
    }

    /// <summary>
    /// Adds request latency telemetry middleware to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);

        _ = services.AddLatencyContext();
        services.TryAddScoped(p => p.GetRequiredService<ILatencyContextProvider>().CreateContext());

        _ = services.AddOptionsWithValidateOnStart<RequestLatencyTelemetryOptions, RequestLatencyTelemetryOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Adds request latency telemetry middleware to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Configuration of <see cref="RequestLatencyTelemetryOptions"/>.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="services"/> or <paramref name="configure"/> is <see langword="null" />.</exception>
    public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services, Action<RequestLatencyTelemetryOptions> configure)
        => Throw.IfNull(services)
        .Configure(Throw.IfNull(configure))
        .AddRequestLatencyTelemetry();

    /// <summary>
    /// Adds request latency telemetry middleware to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="section">Configuration of <see cref="RequestLatencyTelemetryOptions"/>.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="services"/> or <paramref name="section"/> is <see langword="null" />.</exception>
    public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services, IConfigurationSection section)
        => Throw.IfNull(services)
        .Configure<RequestLatencyTelemetryOptions>(Throw.IfNull(section))
        .AddRequestLatencyTelemetry();

    internal static IServiceCollection AddPipelineEntryCheckpoint(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, CapturePipelineEntryStartupFilter>());
        return services;
    }
}
