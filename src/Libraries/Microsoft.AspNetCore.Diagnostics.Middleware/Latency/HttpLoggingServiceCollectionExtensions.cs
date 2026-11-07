// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using Microsoft.AspNetCore.Diagnostics.Latency;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class HttpLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds an enricher that appends latency information from the request's latency context to incoming HTTP request logs.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The value of <paramref name="services"/>.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The latency data this enricher reads is populated by the request latency telemetry services. Call
    /// <c>AddRequestLatencyTelemetry</c> and <c>AddRequestCheckpoint</c>, and add the
    /// middleware to the request pipeline with <c>UseRequestCheckpoint</c> and <c>UseRequestLatencyTelemetry</c>, so
    /// that an <see cref="Microsoft.Extensions.Diagnostics.Latency.ILatencyContext"/> is available for each request.
    /// </remarks>
    public static IServiceCollection AddHttpServerLatencyTelemetry(this IServiceCollection services)
    {
        _ = Throw.IfNull(services);
        return services.AddHttpLogEnricher<HttpLatencyLogEnricher>();
    }
}
#endif
