// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Diagnostics.Latency;

namespace Microsoft.AspNetCore.Diagnostics.Latency.Test.Internal;

internal class MockLatencyData
{
    private readonly ArraySegment<Checkpoint> _checkpoints = new(new[]
    {
        new Checkpoint("ca", 1, 1000),
        new Checkpoint("cb", 2, 1000),
        new Checkpoint("c/c", 3, 1000)
    });

    private readonly ArraySegment<Measure> _measures = new(new[]
    {
        new Measure("m/a", 1),
        new Measure("mb", 2),
        new Measure("mc", 3),
    });

    private readonly ArraySegment<Tag> _tags = new(new[]
    {
        new Tag("t/a", "t1"),
        new Tag("tb", "t/2"),
        new Tag("tc", "t3")
    });

    public MockLatencyData()
    {
        const int MillisecondsPerSecond = 1000;

        LatencyData = new LatencyData(_tags, _checkpoints, _measures, 20, 1000);

        SerializedLatencyData = string.Format(CultureInfo.InvariantCulture, "{0}/,{1}/,{2}/,{3}/,{4}/,{5}/,{6}",
            string.Join("/", _tags.Select(a => a.Name.Replace('/', '_'))),
            string.Join("/", _tags.Select(a => a.Value.Replace('/', '_'))),
            string.Join("/", _checkpoints.Select(a => a.Name.Replace('/', '_'))),
            string.Join("/", _checkpoints.Select(a => (long)Math.Round(((double)a.Elapsed / a.Frequency) * MillisecondsPerSecond))),
            string.Join("/", _measures.Select(a => a.Name.Replace('/', '_'))),
            string.Join("/", _measures.Select(a => a.Value)),
            (long)Math.Round(((double)LatencyData.DurationTimestamp / LatencyData.DurationTimestampFrequency) * MillisecondsPerSecond));
    }

    public string SerializedLatencyData { get; }

    public LatencyData LatencyData { get; }
}
