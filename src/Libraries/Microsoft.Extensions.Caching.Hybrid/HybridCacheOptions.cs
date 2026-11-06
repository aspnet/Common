// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Caching.Hybrid;

/// <summary>
/// Options for configuring the default <see cref="HybridCache"/> implementation.
/// </summary>
public class HybridCacheOptions
{
    private const int ShiftBytesToMebiBytes = 20;

    /// <summary>
    /// Gets or sets the default global options to be applied to <see cref="HybridCache"/> operations.
    /// </summary>
    /// <remarks>
    /// If options are specified at the individual call level, the non-null values are merged
    /// (with the per-call options being used in preference to the global options). If no value is
    /// specified for a given option (globally or per-call), the implementation can choose a reasonable default.
    /// </remarks>
    public HybridCacheEntryOptions? DefaultEntryOptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether compression for this <see cref="HybridCache"/> instance is disabled.
    /// </summary>
    /// <remarks>
    /// The default <see cref="HybridCache"/> implementation does not provide built-in compression, so this option has no effect.
    /// To compress cached data, configure a serializer that performs compression.
    /// </remarks>
#pragma warning disable CS0436 // Type conflicts with imported type
    [Obsolete(
        DiagnosticIds.Obsoletions.HybridCacheDisableCompressionMessage,
        DiagnosticId = DiagnosticIds.Obsoletions.HybridCacheDisableCompressionDiagId,
        UrlFormat = DiagnosticIds.UrlFormat)]
#pragma warning restore CS0436 // Type conflicts with imported type
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool DisableCompression { get; set; }

    /// <summary>
    /// Gets or sets the maximum size of cache items.
    /// </summary>
    /// <value>
    /// The maximum size of cache items. The default value is 1 MiB.
    /// </value>
    /// <remarks>
    /// Attempts to store values over this size are logged,
    /// and the value isn't stored in the cache.
    /// </remarks>
    public long MaximumPayloadBytes { get; set; } = 1 << ShiftBytesToMebiBytes; // 1MiB

    /// <summary>
    /// Gets or sets the maximum permitted length (in characters) of keys.
    /// </summary>
    /// <value>
    /// The maximum permitted length of keys, in characters. The default value is 1024 characters.
    /// </value>
    /// <remarks>Attempts to use keys over this size are logged.</remarks>
    public int MaximumKeyLength { get; set; } = 1024; // characters

    /// <summary>
    /// Gets or sets a value indicating whether to use "tags" data as dimensions on metric reporting.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to use "tags" data as dimensions on metric reporting; otherwise, <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// When enabled, cache operations will emit System.Diagnostics.Metrics with tag values as dimensions,
    /// providing richer telemetry for cache performance analysis by tag categories.
    /// </para>
    /// <para>
    /// <strong>Important PII and Security Considerations:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Ensure that tag values do not contain personally identifiable information (PII), 
    /// sensitive data, or high-cardinality values that could overwhelm metrics systems.
    /// </description></item>
    /// <item><description>
    /// Tag values will be visible in metrics collection systems, dashboards, and telemetry pipelines.
    /// Only use tags that are safe for observability purposes.
    /// </description></item>
    /// <item><description>
    /// Consider using categorical values like "region", "service", "environment" rather than 
    /// user-specific identifiers or sensitive business data.
    /// </description></item>
    /// <item><description>
    /// High-cardinality tags (e.g., user IDs, session IDs) can cause performance issues 
    /// in metrics systems and should be avoided.
    /// </description></item>
    /// </list>
    /// <para>
    /// Example of appropriate tags: ["region:us-west", "service:api", "environment:prod"].
    /// </para>
    /// <para>
    /// Example of inappropriate tags: ["user:john.doe@company.com", "session:abc123", "customer-data:sensitive"].
    /// </para>
    /// </remarks>
    public bool ReportTagMetrics { get; set; }

    /// <summary>
    /// Gets or sets the key used to resolve the distributed cache service from the <see cref="System.IServiceProvider"/>.
    /// </summary>
    public object? DistributedCacheServiceKey { get; set; }
}
