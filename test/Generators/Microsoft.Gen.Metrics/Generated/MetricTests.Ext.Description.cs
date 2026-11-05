// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using TestClasses;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public partial class MetricTests
{
    [Fact]
    public void CounterWithDescription_WorkCorrectly()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(CounterWithDescription));

        CounterWithDescription counter = MetricsWithDescription.CreateCounterWithDescription(_meter);
        counter.Add(100L);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(100L, measurement.Value);
        Assert.Empty(measurement.Tags);

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Number of requests processed.", collector.Instrument.Description);
        Assert.Null(collector.Instrument.Unit);
    }

    [Fact]
    public void CounterWithUnitAndDescription_WorkCorrectly()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(CounterWithUnitAndDescription));

        CounterWithUnitAndDescription counter = MetricsWithDescription.CreateCounterWithUnitAndDescription(_meter);
        counter.Add(12345L, "dim1Value", "dim2Value");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(12345L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "dim1Value"), ("s2", "dim2Value") },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Number of requests processed, by dimension.", collector.Instrument.Description);
        Assert.Equal("{request}", collector.Instrument.Unit);
    }

    [Fact]
    public void CounterStrongTypeWithDescription_WorkCorrectly()
    {
        var dimensions = new Dimensions { Dim1 = "s1", Dim2 = "s2" };

        using var collector = new MetricCollector<long>(_meter, nameof(CounterStrongTypeWithDescription));

        CounterStrongTypeWithDescription counter = MetricsWithDescription.CreateCounterStrongTypeWithDescription(_meter);
        counter.Add(50L, dimensions);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(50L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("Dim1", "s1"), ("Dim2", "s2") },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Strongly-typed counter.", collector.Instrument.Description);
        Assert.Null(collector.Instrument.Unit);
    }

    [Fact]
    public void GenericCounterWithDescription_WorkCorrectly()
    {
        using var collector = new MetricCollector<double>(_meter, nameof(GenericDoubleCounterWithDescription));

        GenericDoubleCounterWithDescription counter = MetricsWithDescription.CreateGenericDoubleCounterWithDescription(_meter);
        counter.Add(3.14);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(3.14, measurement.Value);

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Generic counter.", collector.Instrument.Description);
    }

    [Fact]
    public void HistogramWithUnitAndDescription_WorkCorrectly()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(HistogramWithUnitAndDescription));

        HistogramWithUnitAndDescription histogram = MetricsWithDescription.CreateHistogramWithUnitAndDescription(_meter);
        histogram.Record(50L);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(50L, measurement.Value);

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Duration of the operation.", collector.Instrument.Description);
        Assert.Equal("ms", collector.Instrument.Unit);
    }

    [Fact]
    public void HistogramStrongTypeWithDescription_WorkCorrectly()
    {
        var dimensions = new Dimensions { Dim1 = "s1", Dim2 = "s2" };

        using var collector = new MetricCollector<long>(_meter, nameof(HistogramStrongTypeWithDescription));

        HistogramStrongTypeWithDescription histogram = MetricsWithDescription.CreateHistogramStrongTypeWithDescription(_meter);
        histogram.Record(50L, dimensions);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(50L, measurement.Value);

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Strongly-typed histogram.", collector.Instrument.Description);
    }

    [Fact]
    public void GenericHistogramWithEscapedDescription_PreservesDescription()
    {
        using var collector = new MetricCollector<int>(_meter, nameof(GenericIntHistogramWithEscapedDescription));

        GenericIntHistogramWithEscapedDescription histogram = MetricsWithDescription.CreateGenericIntHistogramWithEscapedDescription(_meter);
        histogram.Record(3, "val1");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(3, measurement.Value);
        Assert.Equal(new KeyValuePair<string, object?>("s1", "val1"), Assert.Single(measurement.Tags));

        Assert.NotNull(collector.Instrument);
        Assert.Equal("Contains \"quotes\", a backslash \\, {braces}\tand a\nnew line.", collector.Instrument.Description);
    }

    [Fact]
    public void CounterWithEmptyDescription_DescriptionInInstrumentIsNull()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(CounterWithEmptyDescription));

        CounterWithEmptyDescription counter = MetricsWithDescription.CreateCounterWithEmptyDescription(_meter);
        counter.Add(1L);

        Assert.Single(collector.GetMeasurementSnapshot());
        Assert.NotNull(collector.Instrument);
        Assert.Null(collector.Instrument.Description);
    }

    [Fact]
    public void CounterWithNoDescription_DescriptionInInstrumentIsNull()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(Counter0D));

        Counter0D counter = CounterTestExtensions.CreateCounter0D(_meter);
        counter.Add(10L);

        Assert.Single(collector.GetMeasurementSnapshot());
        Assert.NotNull(collector.Instrument);
        Assert.Null(collector.Instrument.Description);
    }
}
