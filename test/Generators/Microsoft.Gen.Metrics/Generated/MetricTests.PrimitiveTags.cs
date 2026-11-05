// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using TestClasses;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public partial class MetricTests
{
    [Fact]
    public void ValidateCounterWithPrimitiveTags()
    {
        var tags = new PrimitiveTags
        {
            Port = 443,
            StatusCode = 200,
            BoolTag = true,
            SByteTag = -1,
            ByteTag = 2,
            ShortTag = -3,
            UShortTag = 4,
            UIntTag = 5U,
            LongTag = long.MaxValue,
            FloatTag = 6.5F,
            DoubleTag = 7.25,
            StringTag = "str",
            Child = new PrimitiveTagsChild { ChildPort = 8080 },
        };

        using var collector = new MetricCollector<long>(_meter, "CounterWithPrimitiveTags");
        CounterWithPrimitiveTags counter = MetricsWithPrimitiveTags.CreateCounterWithPrimitiveTags(_meter);
        counter.Add(1L, tags);

        // Tag values must keep their original types rather than being converted to strings.
        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("server.port",               443),
                ("http.response.status_code", 200),
                ("BoolTag",                   true),
                ("SByteTag",                  (sbyte)-1),
                ("ByteTag",                   (byte)2),
                ("ShortTag",                  (short)-3),
                ("UShortTag",                 (ushort)4),
                ("UIntTag",                   5U),
                ("LongTag",                   long.MaxValue),
                ("FloatTag",                  6.5F),
                ("DoubleTag",                 7.25),
                ("StringTag",                 "str"),
                ("child.port",                8080),
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        tags.Child = null;
        collector.Clear();

        counter.Add(2L, tags);

        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(2L, measurement.Value);
        Assert.True(measurement.Tags.ContainsKey("child.port"));
        Assert.Null(measurement.Tags["child.port"]);
    }

    [Fact]
    public void ValidateHistogramWithPrimitiveTagsStruct()
    {
        var tags = new PrimitiveTagsStruct
        {
            Port = 8443,
            Flag = false,
            Ratio = 0.5,
            Nested = new PrimitiveTagsNestedStruct { NestedLong = -42L },
        };

        using var collector = new MetricCollector<double>(_meter, "HistogramWithPrimitiveTags");
        HistogramWithPrimitiveTags histogram = _meter.CreateHistogramWithPrimitiveTags();
        histogram.Record(12.5, tags);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(12.5, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("server.port", 8443),
                ("Flag",        false),
                ("Ratio",       0.5),
                ("NestedLong",  -42L),
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateCounterWithPrimitiveTagsRecordStruct()
    {
        using var collector = new MetricCollector<long>(_meter, "CounterWithPrimitiveTagsRecordStruct");
        CounterWithPrimitiveTagsRecordStruct counter = MetricsWithPrimitiveTags.CreateCounterWithPrimitiveTagsRecordStruct(_meter);
        counter.Add(3L, new PrimitiveTagsRecordStruct("GET", 200, 443));

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(3L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("http.request.method",       "GET"),
                ("http.response.status_code", 200),
                ("server.port",               443),
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }
}
