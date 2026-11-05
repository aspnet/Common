// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.Latency;
using Microsoft.Extensions.Diagnostics.Latency.Internal;
using Xunit;

#if NET11_0_OR_GREATER
using System.Threading;
#endif

namespace Microsoft.Extensions.Diagnostics.Latency.Test.Internal;

[Collection("StdoutUsage")]
public class LatencyConsoleExporterTests
{
    private static readonly string _fullResult = NormalizeLineEndings(
@"Latency sample #0: 20ms, 3 checkpoints, 3 tags, 3 measures
  Checkpoint | Value (ms)
  -----------|-----------
  ca         | 1
  cb         | 2
  cc         | 3

  Tag | Value
  ----|------
  ta  | t1
  tb  | t2
  tc  | t3

  Measure | Value
  --------|------
  ma      | 1
  mb      | 2
  mc      | 3
");

    private static readonly string _longResult = NormalizeLineEndings(
@"Latency sample #0: 20ms, 3 checkpoints, 3 tags, 3 measures
  Checkpoint   | Value (ms)
  -------------|-----------
  ccccccccccca | 1
  cccccccccccb | 2
  cccccccccccc | 3

  Tag          | Value
  -------------|------
  ttttttttttta | t1
  tttttttttttb | t2
  tttttttttttc | t3

  Measure      | Value
  -------------|------
  mmmmmmmmmmma | 1
  mmmmmmmmmmmb | 2
  mmmmmmmmmmmc | 3
");

    private static readonly string _truncatedResult = NormalizeLineEndings(
@"Latency sample #0: 20ms, 3 checkpoints, 3 tags, 3 measures
");

    private static readonly string _emptyResult = NormalizeLineEndings(
@"Latency sample #0: 20ms, 0 checkpoints, 0 tags, 0 measures
");

    [Fact]
    public async Task ConsoleExporter_Export_OutputsData()
    {
        using var a = new Accumulator();
        System.Console.SetOut(a);

        var ld = GetLatencyData();

        var options = Options.Options.Create(new LatencyConsoleOptions
        {
            OutputCheckpoints = true,
            OutputTags = true,
            OutputMeasures = true,
        });

        var exporter = new LatencyConsoleExporter(options);
        await exporter.ExportAsync(ld, default);
        a.Flush();
        var result = a.ToString();
        Assert.Equal(_fullResult, result);
    }

    [Fact]
    public async Task ConsoleExporter_Export_OutputsLongData()
    {
        using var a = new Accumulator();
        System.Console.SetOut(a);

        var ld = GetLongLatencyData();

        var options = Options.Options.Create(new LatencyConsoleOptions
        {
            OutputCheckpoints = true,
            OutputTags = true,
            OutputMeasures = true,
        });

        var exporter = new LatencyConsoleExporter(options);
        await exporter.ExportAsync(ld, default);
        a.Flush();
        var result = a.ToString();
        Assert.Equal(_longResult, result);
    }

    [Fact]
    public async Task ConsoleExporter_Export_OutputsTruncatedData()
    {
        using var a = new Accumulator();
        System.Console.SetOut(a);

        var ld = GetLatencyData();

        var options = Options.Options.Create(new LatencyConsoleOptions
        {
            OutputCheckpoints = false,
            OutputTags = false,
            OutputMeasures = false
        });

        var exporter = new LatencyConsoleExporter(options);
        await exporter.ExportAsync(ld, default);
        a.Flush();
        var result = a.ToString();
        Assert.Equal(_truncatedResult, result);
    }

    [Fact]
    public async Task ConsoleExporter_Export_OutputsEmptyData()
    {
        using var a = new Accumulator();
        System.Console.SetOut(a);

        var ld = GetEmptyLatencyData();

        var options = Options.Options.Create(new LatencyConsoleOptions
        {
            OutputCheckpoints = true,
            OutputTags = true,
            OutputMeasures = true,
        });

        var exporter = new LatencyConsoleExporter(options);
        await exporter.ExportAsync(ld, default);
        a.Flush();
        var result = a.ToString();
        Assert.Equal(_emptyResult, result);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task ConsoleExporter_Export_RespectsCancellation()
    {
        using var accumulator = new Accumulator();
        System.Console.SetOut(accumulator);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exporter = new LatencyConsoleExporter(Options.Options.Create(new LatencyConsoleOptions()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(GetLatencyData(), cancellation.Token));
        Assert.Empty(accumulator.ToString());
    }
#endif

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

    private sealed class Accumulator : TextWriter
    {
        private readonly StringBuilder _sb = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => _sb.Append(value);
        public override string ToString() => _sb.ToString();
    }

    private static LatencyData GetLatencyData()
    {
        ArraySegment<Checkpoint> checkpoints = new(new[]
        {
            new Checkpoint("ca", 1, 1000),
            new Checkpoint("cb", 2, 1000),
            new Checkpoint("cc", 3, 1000)
        });

        ArraySegment<Measure> measures = new(new[]
        {
            new Measure("ma", 1),
            new Measure("mb", 2),
            new Measure("mc", 3),
        });

        ArraySegment<Tag> tags = new(new[]
        {
            new Tag("ta", "t1"),
            new Tag("tb", "t2"),
            new Tag("tc", "t3")
        });

        return new LatencyData(tags, checkpoints, measures, 20, 1000);
    }

    private static LatencyData GetLongLatencyData()
    {
        ArraySegment<Checkpoint> checkpoints = new(new[]
        {
            new Checkpoint("ccccccccccca", 1, 1000),
            new Checkpoint("cccccccccccb", 2, 1000),
            new Checkpoint("cccccccccccc", 3, 1000)
        });

        ArraySegment<Measure> measures = new(new[]
        {
            new Measure("mmmmmmmmmmma", 1),
            new Measure("mmmmmmmmmmmb", 2),
            new Measure("mmmmmmmmmmmc", 3),
        });

        ArraySegment<Tag> tags = new(new[]
        {
            new Tag("ttttttttttta", "t1"),
            new Tag("tttttttttttb", "t2"),
            new Tag("tttttttttttc", "t3")
        });

        return new LatencyData(tags, checkpoints, measures, 20, 1000);
    }

    private static LatencyData GetEmptyLatencyData()
    {
        ArraySegment<Checkpoint> checkpoints = new(Array.Empty<Checkpoint>());
        ArraySegment<Measure> measures = new(Array.Empty<Measure>());
        ArraySegment<Tag> tags = new(Array.Empty<Tag>());
        return new LatencyData(tags, checkpoints, measures, 20, 1000);
    }
}
