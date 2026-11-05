// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Memoization;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Diagnostics.Latency.Internal;

internal sealed class LatencyConsoleExporter : ILatencyDataExporter
{
    private const int MillisPerSecond = 1000;

#if NET8_0_OR_GREATER
    private static readonly CompositeFormat _title = CompositeFormat.Parse("Latency sample #{0}: {1}ms, {2} checkpoints, {3} tags, {4} measures" + Environment.NewLine);
    private static readonly Func<int, CompositeFormat> _rows = Memoize.Function((int nameColumnWidth) => CompositeFormat.Parse($"  {{0,-{nameColumnWidth}}} | {{1}}" + Environment.NewLine));
#else
    private static readonly string _title = "Latency sample #{0}: {1}ms, {2} checkpoints, {3} tags, {4} measures" + Environment.NewLine;
    private static readonly Func<int, string> _rows = Memoize.Function((int nameColumnWidth) => $"  {{0,-{nameColumnWidth}}} | {{1}}" + Environment.NewLine);
#endif

    private static readonly Func<int, string> _dashes = Memoize.Function<int, string>(num => new('-', num));

    private readonly bool _outputCheckpoints;
    private readonly bool _outputTags;
    private readonly bool _outputMeasures;
    private long _sampleCount = -1;

    public LatencyConsoleExporter(IOptions<LatencyConsoleOptions> options)
    {
        var o = options.Value;
        _outputCheckpoints = o.OutputCheckpoints;
        _outputTags = o.OutputTags;
        _outputMeasures = o.OutputMeasures;
    }

    public Task ExportAsync(LatencyData latencyData, CancellationToken cancellationToken)
    {
        var sb = PoolFactory.SharedStringBuilderPool.Get();
        try
        {
            var cnt = Interlocked.Increment(ref _sampleCount);

            _ = sb.AppendFormat(
                CultureInfo.InvariantCulture,
                _title,
                cnt,
                (double)latencyData.DurationTimestamp / latencyData.DurationTimestampFrequency * MillisPerSecond,
                latencyData.Checkpoints.Length,
                latencyData.Tags.Length,
                latencyData.Measures.Length);

            bool needBlankLine = false;
            if (_outputCheckpoints && latencyData.Checkpoints.Length > 0)
            {
                int nameColumnWidth = 0;
                for (int i = 0; i < latencyData.Checkpoints.Length; i++)
                {
                    nameColumnWidth = Math.Max(nameColumnWidth, latencyData.Checkpoints[i].Name.Length);
                }

                var fmt = StartTable(sb, "Checkpoint", "Value (ms)", nameColumnWidth, ref needBlankLine);
                for (int i = 0; i < latencyData.Checkpoints.Length; i++)
                {
                    var c = latencyData.Checkpoints[i];
                    _ = sb.AppendFormat(CultureInfo.InvariantCulture, fmt, c.Name, (double)c.Elapsed / c.Frequency * MillisPerSecond);
                }
            }

            if (_outputTags && latencyData.Tags.Length > 0)
            {
                int nameColumnWidth = 0;
                for (int i = 0; i < latencyData.Tags.Length; i++)
                {
                    nameColumnWidth = Math.Max(nameColumnWidth, latencyData.Tags[i].Name.Length);
                }

                var fmt = StartTable(sb, "Tag", "Value", nameColumnWidth, ref needBlankLine);
                for (int i = 0; i < latencyData.Tags.Length; i++)
                {
                    var t = latencyData.Tags[i];
                    _ = sb.AppendFormat(CultureInfo.InvariantCulture, fmt, t.Name, t.Value);
                }
            }

            if (_outputMeasures && latencyData.Measures.Length > 0)
            {
                int nameColumnWidth = 0;
                for (int i = 0; i < latencyData.Measures.Length; i++)
                {
                    nameColumnWidth = Math.Max(nameColumnWidth, latencyData.Measures[i].Name.Length);
                }

                var fmt = StartTable(sb, "Measure", "Value", nameColumnWidth, ref needBlankLine);
                for (int i = 0; i < latencyData.Measures.Length; i++)
                {
                    var m = latencyData.Measures[i];
                    _ = sb.AppendFormat(CultureInfo.InvariantCulture, fmt, m.Name, m.Value);
                }
            }

            // the whole sample is output in a single shot so it won't be interrupted with conflicting output
#if NET11_0_OR_GREATER
            return Console.Out.WriteAsync(sb.ToString(), cancellationToken);
#else
            return Console.Out.WriteAsync(sb.ToString());
#endif
        }
        finally
        {
            PoolFactory.SharedStringBuilderPool.Return(sb);
        }
    }

#if NET8_0_OR_GREATER
    private static CompositeFormat StartTable(StringBuilder sb, string nameHeader, string valueHeader, int nameColumnWidth, ref bool needBlankLine)
#else
    private static string StartTable(StringBuilder sb, string nameHeader, string valueHeader, int nameColumnWidth, ref bool needBlankLine)
#endif
    {
        if (needBlankLine)
        {
            _ = sb.AppendLine();
        }
        else
        {
            needBlankLine = true;
        }

        nameColumnWidth = Math.Max(nameColumnWidth, nameHeader.Length);
        var fmt = _rows(nameColumnWidth);
        _ = sb.AppendFormat(CultureInfo.InvariantCulture, fmt, nameHeader, valueHeader);

        _ = sb.Append("  ");
        _ = sb.Append(_dashes(nameColumnWidth + 1));
        _ = sb.Append('|');
        _ = sb.AppendLine(_dashes(valueHeader.Length + 1));

        return fmt;
    }
}
