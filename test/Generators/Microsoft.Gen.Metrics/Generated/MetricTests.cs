// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using TestClasses;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public partial class MetricTests : IDisposable
{
    private const string BaseMeterName = "Microsoft.Gen.Metrics.Test." + nameof(MetricTests) + ".";
    private readonly string _meterName;
    private readonly Meter _meter;
    private bool _disposedValue;

    public MetricTests()
    {
        _meterName = BaseMeterName + Guid.NewGuid().ToString("d", CultureInfo.InvariantCulture);
        _meter = new Meter(_meterName);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _meter.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NonGenericCounter0DInstrumentTests()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(Counter0D));
        Counter0D counter0D = CounterTestExtensions.CreateCounter0D(_meter);
        counter0D.Add(10L);
        counter0D.Add(5L);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Collection(measurements, x => Assert.Equal(10L, x.Value), x => Assert.Equal(5L, x.Value));
        Assert.All(measurements, x => Assert.Empty(x.Tags));
    }

    [Fact]
    public void NonGenericCounter2DInstrumentTests()
    {
        const long Value = int.MaxValue + 4L;

        using var collector = new MetricCollector<long>(_meter, nameof(Counter2D));
        Counter2D counter2D = CounterTestExtensions.CreateCounter2D(_meter);
        counter2D.Add(Value, "val1", "val2");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(Value, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void NonGenericHistogram0DInstrumentTests()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(Histogram0D));
        Histogram0D histogram0D = HistogramTestExtensions.CreateHistogram0D(_meter);
        histogram0D.Record(12L);
        histogram0D.Record(6L);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Collection(measurements, x => Assert.Equal(12L, x.Value), x => Assert.Equal(6L, x.Value));
        Assert.All(measurements, x => Assert.Empty(x.Tags));
    }

    [Fact]
    public void NonGenericHistogram1DInstrumentTests()
    {
        const long Value = int.MaxValue + 3L;

        using var collector = new MetricCollector<long>(_meter, nameof(Histogram1D));
        Histogram1D histogram1D = HistogramTestExtensions.CreateHistogram1D(_meter);
        histogram1D.Record(Value, "val_1");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(Value, measurement.Value);
        var tag = Assert.Single(measurement.Tags);
        Assert.Equal(new KeyValuePair<string, object?>("s1", "val_1"), tag);
    }

    [Fact]
    public void GenericCounter0DInstrumentTests()
    {
        using var collector = new MetricCollector<int>(_meter, nameof(GenericIntCounter0D));
        GenericIntCounter0D counter0D = CounterTestExtensions.CreateGenericIntCounter0D(_meter);
        counter0D.Add(10);
        counter0D.Add(5);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Collection(measurements, x => Assert.Equal(10, x.Value), x => Assert.Equal(5, x.Value));
        Assert.All(measurements, x => Assert.Empty(x.Tags));
    }

    [Fact]
    public void GenericCounter2DInstrumentTests()
    {
        const int Value = int.MaxValue - 1;

        using var collector = new MetricCollector<int>(_meter, nameof(GenericIntCounter1D));
        GenericIntCounter1D counter2D = CounterTestExtensions.CreateGenericIntCounter1D(_meter);
        counter2D.Add(Value, "val1");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(Value, measurement.Value);
        var tag = Assert.Single(measurement.Tags);
        Assert.Equal(new KeyValuePair<string, object?>("s1", "val1"), tag);
    }

    [Fact]
    public void GenericHistogram0DInstrumentTests()
    {
        using var collector = new MetricCollector<int>(_meter, nameof(GenericIntHistogram0D));
        GenericIntHistogram0D histogram0D = HistogramTestExtensions.CreateGenericIntHistogram0D(_meter);
        histogram0D.Record(12);
        histogram0D.Record(6);

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Collection(measurements, x => Assert.Equal(12, x.Value), x => Assert.Equal(6, x.Value));
        Assert.All(measurements, x => Assert.Empty(x.Tags));
    }

    [Fact]
    public void GenericHistogram2DInstrumentTests()
    {
        const int Value = short.MaxValue + 2;

        using var collector = new MetricCollector<int>(_meter, nameof(GenericIntHistogram2D));
        GenericIntHistogram2D histogram1D = HistogramTestExtensions.CreateGenericIntHistogram2D(_meter);
        histogram1D.Record(Value, "val_1", "val_2");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(Value, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val_1"), ("s2", "val_2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void CreateOnExistingCounter()
    {
        const long Value = int.MaxValue + 2L;

        using var collector = new MetricCollector<long>(_meter, nameof(Counter4D));
        Counter4D counter4D = CounterTestExtensions.CreateCounter4D(_meter);
        Counter4D newCounter4D = CounterTestExtensions.CreateCounter4D(_meter);
        Assert.Same(counter4D, newCounter4D);

        counter4D.Add(Value, "val1", "val2", "val3", "val4");
        newCounter4D.Add(Value, "val3", "val4", "val5", "val6");

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Equal(2, measurements.Count);
        Assert.All(measurements, x => Assert.Equal(Value, x.Value));

        var tags = measurements[0].Tags.Select(x => (x.Key, x.Value));
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2"), ("s3", "val3"), ("s4", "val4") }, tags);

        tags = measurements[1].Tags.Select(x => (x.Key, x.Value));
        Assert.Equal(new (string, object?)[] { ("s1", "val3"), ("s2", "val4"), ("s3", "val5"), ("s4", "val6") }, tags);
    }

    [Fact]
    public void CreateOnExistingHistogram()
    {
        const long Value = int.MaxValue + 1L;

        using var collector = new MetricCollector<long>(_meter, nameof(Histogram4D));
        Histogram4D histogram4D = HistogramTestExtensions.CreateHistogram4D(_meter);
        Histogram4D newHistogram4D = HistogramTestExtensions.CreateHistogram4D(_meter);
        Assert.Same(histogram4D, newHistogram4D);

        histogram4D.Record(Value, "val1", "val2", "val3", "val4");
        newHistogram4D.Record(Value, "val3", "val4", "val5", "val6");

        var measurements = collector.GetMeasurementSnapshot();
        Assert.Equal(2, measurements.Count);
        Assert.All(measurements, x => Assert.Equal(Value, x.Value));

        var tags = measurements[0].Tags.Select(x => (x.Key, x.Value));
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2"), ("s3", "val3"), ("s4", "val4") }, tags);

        tags = measurements[1].Tags.Select(x => (x.Key, x.Value));
        Assert.Equal(new (string, object?)[] { ("s1", "val3"), ("s2", "val4"), ("s3", "val5"), ("s4", "val6") }, tags);
    }

    [Fact]
    public void CreateOnExistingCounter_WithDifferentMeterName_ShouldReturnNewMetric()
    {
        using var meter2 = new Meter(_meterName + "2");
        Counter3D counter = CounterTestExtensions.CreateCounter3D(_meter);

        // "Create()" with another meter name should return a different counter object
        Counter3D counterWithDifferentMeterName = CounterTestExtensions.CreateCounter3D(meter2);
        Assert.NotNull(counterWithDifferentMeterName);
        Assert.NotSame(counter, counterWithDifferentMeterName);

        Histogram3D histogram = HistogramTestExtensions.CreateHistogram3D(_meter);

        // "Create()" with another meter name should return a different histogram object
        Histogram3D histogramWithDifferentMeterName = HistogramTestExtensions.CreateHistogram3D(meter2);
        Assert.NotNull(histogramWithDifferentMeterName);
        Assert.NotSame(histogram, histogramWithDifferentMeterName);
    }

    [Fact]
    public void CreateOnExistingCounter_WithSameMeterName_ShouldReturnDifferentMetric()
    {
        using var meter2 = new Meter(_meterName);
        Counter3D counter = CounterTestExtensions.CreateCounter3D(_meter);

        // "Create()" with the same meter name should return a different counter object
        Counter3D counterWithSameMeterName = CounterTestExtensions.CreateCounter3D(meter2);
        Assert.NotNull(counterWithSameMeterName);
        Assert.NotSame(counter, counterWithSameMeterName);

        Histogram3D histogram3D = HistogramTestExtensions.CreateHistogram3D(_meter);

        // "Create()" with the same meter name should return a different histogram object
        Histogram3D histogramWithSameMeterName = HistogramTestExtensions.CreateHistogram3D(meter2);
        Assert.NotNull(histogramWithSameMeterName);
        Assert.NotSame(histogram3D, histogramWithSameMeterName);
    }

    [Fact]
    public void ValidateCounterWithDifferentDimensions()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(Counter2D));
        Counter2D counter2D = CounterTestExtensions.CreateCounter2D(_meter);

        counter2D.Add(17L, "val1", "val2");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(17L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
        collector.Clear();

        counter2D.Add(5L, "val1", "val2");
        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(5L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
        collector.Clear();

        // Different Dimensions
        counter2D.Add(5L, "val1", "val4");
        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(5L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val4") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateHistogramWithDifferentDimensions()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(Histogram2D));
        Histogram2D histogram = HistogramTestExtensions.CreateHistogram2D(_meter);
        histogram.Record(10L, "val1", "val2");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(10L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
        collector.Clear();

        histogram.Record(5L, "val1", "val2");
        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(5L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val2") }, measurement.Tags.Select(x => (x.Key, x.Value)));
        collector.Clear();

        // Different Dimensions
        histogram.Record(5L, "val1", "val4");
        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(5L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("s1", "val1"), ("s2", "val4") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateCounterWithFileScopedNamespaceNoDims()
    {
        using var collector = new MetricCollector<long>(_meter, nameof(FileScopedNamespaceCounter));
        FileScopedNamespaceCounter longCounter = FileScopedExtensions.CreateCounter(_meter);
        longCounter.Add(12L);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(12L, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public void ValidateCounterWithFileScopedNamespace()
    {
        using var collector = new MetricCollector<double>(_meter, nameof(FileScopedNamespaceGenericDoubleCounter));

        var genericDoubleCounter = FileScopedExtensions.CreateGenericDoubleCounter(_meter);
        genericDoubleCounter.Add(1.05D);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1.05D, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public void ValidateCounterWithVariableParamsDimensions()
    {
        using var collector = new MetricCollector<long>(_meter, "MyCounterMetric");
        CounterWithVariableParams counter = CounterTestExtensions.CreateCounterWithVariableParams(_meter);

        counter.Add(100_500L, Dim1: "val1", Dim_2: "val2", Dim_3: "val3");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(100_500L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("Dim1", "val1"), ("Dim_2", "val2"), ("Dim_3", "val3") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateHistogramWithVariableParamsDimensions()
    {
        using var collector = new MetricCollector<long>(_meter, "MyHistogramMetric");
        HistogramWithVariableParams histogram = HistogramTestExtensions.CreateHistogramWithVariableParams(_meter);

        histogram.Record(100L, Dim1: "val1", Dim_2: "val2", Dim_3: "val3");

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(100L, measurement.Value);
        Assert.Equal(new (string, object?)[] { ("Dim1", "val1"), ("Dim_2", "val2"), ("Dim_3", "val3") }, measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateHistogramStructType()
    {
        var histogramStruct = new HistogramStruct
        {
            Dim1 = "Dim1",
            Dim2 = "Dim2",
            DimInField = "Dim in field",
            Operations = HistogramOperations.Operation1,
            Operations2 = HistogramOperations.Operation1
        };

        using var collector = new MetricCollector<long>(_meter, "MyHistogramStructTypeMetric");
        StructTypeHistogram recorder = HistogramTestExtensions.CreateHistogramStructType(_meter);
        recorder.Record(10L, histogramStruct);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(10L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1", histogramStruct.Dim1),
                ("DimInField", histogramStruct.DimInField),
                ("Dim2_FromAttribute", histogramStruct.Dim2),
                ("Operations", histogramStruct.Operations.ToString()),
                ("Operations_FromAttribute", histogramStruct.Operations2.ToString())
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateHistogramStrongType()
    {
        var histogramDimensionsTest = new HistogramDimensionsTest
        {
            Dim1 = "Dim1",
            OperationsEnum = HistogramOperations.Operation1,
            OperationsEnum2 = HistogramOperations.Operation1,
            ParentOperationName = "ParentOperationName",
            ChildDimensionsObject = new HistogramChildDimensions
            {
                Dim2 = "Dim2",
                SomeDim = "SomeDime"
            },
            ChildDimensionsStruct = new HistogramDimensionsStruct
            {
                Dim4Struct = "Dim4",
                Dim5Struct = "Dim5"
            },
            GrandChildrenDimensionsObject = new HistogramGrandChildrenDimensions
            {
                Dim3 = "Dim3",
                SomeDim = "SomeDim"
            }
        };

        using var collector = new MetricCollector<long>(_meter, "MyHistogramStrongTypeMetric");
        StrongTypeHistogram recorder = HistogramTestExtensions.CreateHistogramStrongType(_meter);
        recorder.Record(1L, histogramDimensionsTest);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                histogramDimensionsTest.Dim1),
                ("OperationsEnum",      histogramDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               histogramDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                histogramDimensionsTest.ChildDimensionsObject.Dim2),
                ("dim2FromAttribute",   histogramDimensionsTest.ChildDimensionsObject.SomeDim),
                ("Dim3",                histogramDimensionsTest.GrandChildrenDimensionsObject.Dim3),
                ("Dim3FromAttribute",   histogramDimensionsTest.GrandChildrenDimensionsObject.SomeDim),
                ("ParentOperationName", histogramDimensionsTest.ParentOperationName),
                ("Dim4Struct",          histogramDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   histogramDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        histogramDimensionsTest.ChildDimensionsObject = null!;
        collector.Clear();

        recorder.Record(2L, histogramDimensionsTest);

        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(2L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                histogramDimensionsTest.Dim1),
                ("OperationsEnum",      histogramDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               histogramDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                null),
                ("dim2FromAttribute",   null),
                ("Dim3",                histogramDimensionsTest.GrandChildrenDimensionsObject.Dim3),
                ("Dim3FromAttribute",   histogramDimensionsTest.GrandChildrenDimensionsObject.SomeDim),
                ("ParentOperationName", histogramDimensionsTest.ParentOperationName),
                ("Dim4Struct",          histogramDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   histogramDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        histogramDimensionsTest.GrandChildrenDimensionsObject = null!;
        collector.Clear();

        recorder.Record(3L, histogramDimensionsTest);

        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(3L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                histogramDimensionsTest.Dim1),
                ("OperationsEnum",      histogramDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               histogramDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                null),
                ("dim2FromAttribute",   null),
                ("Dim3",                null),
                ("Dim3FromAttribute",   null),
                ("ParentOperationName", histogramDimensionsTest.ParentOperationName),
                ("Dim4Struct",          histogramDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   histogramDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ThrowsOnNullStrongTypeObject()
    {
        using (var collector = new MetricCollector<long>(_meter, "MyHistogramStrongTypeMetric"))
        {
            StrongTypeHistogram recorder = HistogramTestExtensions.CreateHistogramStrongType(_meter);
            var ex = Assert.Throws<ArgumentNullException>(() => recorder.Record(4L, null!));
            Assert.NotNull(ex);
        }

        using (var collector = new MetricCollector<decimal>(_meter, "MyCounterStrongTypeMetric"))
        {
            StrongTypeDecimalCounter counter = CounterTestExtensions.CreateStrongTypeDecimalCounter(_meter);
            var ex = Assert.Throws<ArgumentNullException>(() => counter.Add(4M, null!));
            Assert.NotNull(ex);
        }
    }

    [Fact]
    public void ValidateCounterStructType()
    {
        var counterStruct = new CounterStructDimensions
        {
            Dim1 = "Dim1",
            Dim2 = "Dim2",
            DimInField = "Dim in field",
            Operations = CounterOperations.Operation1,
            Operations2 = CounterOperations.Operation1
        };

        using var collector = new MetricCollector<long>(_meter, "MyCounterStructTypeMetric");
        StructTypeCounter recorder = CounterTestExtensions.CreateCounterStructType(_meter);
        recorder.Add(11L, counterStruct);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(11L, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1", counterStruct.Dim1),
                ("DimInField", counterStruct.DimInField),
                ("Dim2_FromAttribute", counterStruct.Dim2),
                ("Operations", counterStruct.Operations.ToString()),
                ("Operations_FromAttribute", counterStruct.Operations2.ToString())
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateCounterStrongType()
    {
        var counterDimensionsTest = new CounterDimensions
        {
            OperationsEnum = CounterOperations.Operation1,
            OperationsEnum2 = CounterOperations.Operation1,
            ParentOperationName = "ParentOperationName",
            ChildDimensionsObject = new CounterChildDimensions
            {
                Dim2 = "Dim2",
                SomeDim = "SomeDime"
            },
            ChildDimensionsStruct = new CounterDimensionsStruct
            {
                Dim4Struct = "Dim4",
                Dim5Struct = "Dim5"
            },
            GrandChildDimensionsObject = new CounterGrandChildCounterDimensions
            {
                Dim3 = "Dim3",
                SomeDim = "SomeDim"
            },
            Dim1 = "Dim1",
        };

        using var collector = new MetricCollector<decimal>(_meter, "MyCounterStrongTypeMetric");
        StrongTypeDecimalCounter counter = CounterTestExtensions.CreateStrongTypeDecimalCounter(_meter);
        counter.Add(1M, counterDimensionsTest);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1M, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                counterDimensionsTest.Dim1),
                ("OperationsEnum",      counterDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               counterDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                counterDimensionsTest.ChildDimensionsObject.Dim2),
                ("dim2FromAttribute",   counterDimensionsTest.ChildDimensionsObject.SomeDim),
                ("Dim3",                counterDimensionsTest.GrandChildDimensionsObject.Dim3),
                ("Dim3FromAttribute",   counterDimensionsTest.GrandChildDimensionsObject.SomeDim),
                ("ParentOperationName", counterDimensionsTest.ParentOperationName),
                ("Dim4Struct",          counterDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   counterDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        counterDimensionsTest.ChildDimensionsObject = null!;
        collector.Clear();

        counter.Add(2M, counterDimensionsTest);

        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(2M, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                counterDimensionsTest.Dim1),
                ("OperationsEnum",      counterDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               counterDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                null),
                ("dim2FromAttribute",   null),
                ("Dim3",                counterDimensionsTest.GrandChildDimensionsObject.Dim3),
                ("Dim3FromAttribute",   counterDimensionsTest.GrandChildDimensionsObject.SomeDim),
                ("ParentOperationName", counterDimensionsTest.ParentOperationName),
                ("Dim4Struct",          counterDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   counterDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));

        counterDimensionsTest.GrandChildDimensionsObject = null!;
        collector.Clear();
        counter.Add(3M, counterDimensionsTest);

        measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(3M, measurement.Value);
        Assert.Equal(
            new (string, object?)[]
            {
                ("Dim1",                counterDimensionsTest.Dim1),
                ("OperationsEnum",      counterDimensionsTest.OperationsEnum.ToString()),
                ("Enum2",               counterDimensionsTest.OperationsEnum2.ToString()),
                ("Dim2",                null),
                ("dim2FromAttribute",   null),
                ("Dim3",                null),
                ("Dim3FromAttribute",   null),
                ("ParentOperationName", counterDimensionsTest.ParentOperationName),
                ("Dim4Struct",          counterDimensionsTest.ChildDimensionsStruct.Dim4Struct),
                ("Dim5FromAttribute",   counterDimensionsTest.ChildDimensionsStruct.Dim5Struct)
            },
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void ValidateStrongTypeWithMoreThanThirtyTags()
    {
        var tags = new ManyTagsDimensions();
        for (int i = 1; i <= 33; i++)
        {
            typeof(ManyTagsDimensions).GetProperty($"D{i}")!.SetValue(tags, $"v{i}");
        }

        using var collector = new MetricCollector<long>(_meter, nameof(ManyTagsCounter));
        ManyTagsCounter counter = ManyTagsTestExtensions.CreateManyTagsCounter(_meter);
        counter.Add(1L, tags);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            Enumerable.Range(1, 33).Select(i => ($"D{i}", (object?)$"v{i}")),
            measurement.Tags.Select(x => (x.Key, x.Value)));
    }
}
