// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Shared.Pools;

namespace Microsoft.AspNetCore.Diagnostics.Latency;

/// <summary>
/// Enriches incoming HTTP request logs with latency information captured in the request's <see cref="ILatencyContext"/>.
/// </summary>
internal sealed class HttpLatencyLogEnricher : IHttpLogEnricher
{
    internal const string DataVersion = "v1.0";

    private static readonly ObjectPool<StringBuilder> _builderPool = PoolFactory.SharedStringBuilderPool;

    public void Enrich(IEnrichmentTagCollector collector, HttpContext httpContext)
    {
        var latencyContext = httpContext.RequestServices.GetService<ILatencyContext>();

        if (latencyContext != null)
        {
            StringBuilder stringBuilder = _builderPool.Get();
            try
            {
                _ = stringBuilder.Append(DataVersion);
                _ = stringBuilder.Append(',');
                AppendClientName(httpContext.Request, stringBuilder);
                _ = stringBuilder.Append(',');
                FormatLatencyData(stringBuilder, latencyContext.LatencyData);
                collector.Add("LatencyInfo", stringBuilder.ToString());
            }
            finally
            {
                _builderPool.Return(stringBuilder);
            }
        }
    }

    private static void AppendClientName(HttpRequest request, StringBuilder stringBuilder)
    {
        if (request.Headers.TryGetValue(TelemetryConstants.ClientApplicationNameHeader, out var values))
        {
            AppendEscaped(stringBuilder, values[0].AsSpan());
        }
    }

    private static void FormatLatencyData(StringBuilder sb, LatencyData latencyData)
    {
        const int MillisecondsPerSecond = 1000;

        // Append tags
        AppendSpanEscapingDelimiters(sb, latencyData.Tags, a => a.Name);
        _ = sb.Append(',');
        AppendSpanEscapingDelimiters(sb, latencyData.Tags, a => a.Value);
        _ = sb.Append(',');

        // Append checkpoints
        AppendSpanEscapingDelimiters(sb, latencyData.Checkpoints, a => a.Name);
        _ = sb.Append(',');
        AppendSpan(sb, latencyData.Checkpoints, a => (long)Math.Round(((double)a.Elapsed / a.Frequency) * MillisecondsPerSecond));
        _ = sb.Append(',');

        // Append measures
        AppendSpanEscapingDelimiters(sb, latencyData.Measures, a => a.Name);
        _ = sb.Append(',');
        AppendSpan(sb, latencyData.Measures, a => a.Value);
        _ = sb.Append(',');

        // Append duration
        _ = sb.Append((long)Math.Round(((double)latencyData.DurationTimestamp / latencyData.DurationTimestampFrequency) * MillisecondsPerSecond));
    }

    private static void AppendSpanEscapingDelimiters<TX>(StringBuilder sb, ReadOnlySpan<TX> span, Func<TX, string> select)
    {
        for (int i = 0; i < span.Length; i++)
        {
            AppendEscaped(sb, select(span[i]).AsSpan());
            _ = sb.Append('/');
        }
    }

    private static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            _ = sb.Append(c is '/' or ',' ? '_' : c);
        }
    }

    private static void AppendSpan<TX, TY>(StringBuilder sb, ReadOnlySpan<TX> span, Func<TX, TY> apply)
    {
        for (int i = 0; i < span.Length; i++)
        {
            _ = sb.Append(apply(span[i]));
            _ = sb.Append('/');
        }
    }
}
