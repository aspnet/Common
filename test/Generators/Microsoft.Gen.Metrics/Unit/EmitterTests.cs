// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Gen.Metrics.Model;
using Microsoft.Gen.Shared;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public class EmitterTests
{
    [Fact]
    public async Task TestEmitter()
    {
        var sources = new List<string>();
        foreach (var file in Directory.GetFiles("TestClasses"))
        {
            sources.Add(File.ReadAllText(file));
        }

        var (d, r) = await RoslynTestUtils.RunGenerator(
            new MetricsGenerator(),
            new[]
            {
                Assembly.GetAssembly(typeof(Meter))!,
                Assembly.GetAssembly(typeof(CounterAttribute))!,
                Assembly.GetAssembly(typeof(HistogramAttribute))!,
                Assembly.GetAssembly(typeof(CounterAttribute<>))!,
                Assembly.GetAssembly(typeof(HistogramAttribute<>))!,
                Assembly.GetAssembly(typeof(GaugeAttribute))!,
                Assembly.GetAssembly(typeof(GaugeAttribute<>))!,
            },
            sources)
;

        Assert.Empty(d);
        Assert.Equal(2, r.Length);

        string generatedContentPath = "GoldenFiles/Microsoft.Gen.Metrics/Microsoft.Gen.Metrics.MetricsGenerator";
        var goldenCache = File.ReadAllText($"{generatedContentPath}/Factory.g.cs");
        var goldenMetrics = File.ReadAllText($"{generatedContentPath}/Metrics.g.cs");

        var result = r.First(x => x.HintName == "Factory.g.cs").SourceText.ToString();
        Assert.Equal(goldenCache, result);

        result = r.First(x => x.HintName == "Metrics.g.cs").SourceText.ToString();
        Assert.Equal(goldenMetrics, result);
    }

    [Theory]
    [InlineData(10)]
    [InlineData((int)InstrumentKind.None)]
    public void EmitMeter_GivenMetricTypeIsUnknown_ThrowsNotSupportedException(int instrumentKind)
    {
        var metricClass = new MetricType
        {
            Name = "Logger",
            Namespace = "Samples",
            Methods =
            {
                new MetricMethod
                {
                    Name = "CreateUnknownMetric",
                    MetricName = "UnknownMetric",
                    MetricTypeName = "UnknownMetric",
                    InstrumentKind = (InstrumentKind)instrumentKind,
                    TagKeys = { "Dim1" },
                    IsExtensionMethod = false,
                    Modifiers = "static partial",
                    AllParameters =
                    {
                        new MetricParameter
                        {
                            Name = "meter",
                            Type = "global::Microsoft.Extensions.Diagnostics.Metrics.IMeter",
                            IsMeter = true
                        }
                    }
                }
            }
        };

        var emitter = new Emitter();

        Assert.Throws<NotSupportedException>(() =>
            emitter.EmitMetrics(new List<MetricType> { metricClass }, cancellationToken: default));
    }

    [Theory]
    [InlineData(10)]
    [InlineData((int)InstrumentKind.None)]
    public void EmitFactory_GivenMetricTypeIsUnknown_ThrowsNotSupportedException(int instrumentKind)
    {
        var metricClass = new MetricType
        {
            Name = "MetricClass",
            Namespace = "Samples",
            Methods =
            {
                new MetricMethod
                {
                    Name = "CreateUnknownMetric",
                    MetricName = "UnknownMetric",
                    MetricTypeName = "UnknownMetric",
                    InstrumentKind = (InstrumentKind)instrumentKind,
                    GenericType = "long",
                    IsExtensionMethod = false,
                    Modifiers = "static partial",
                    MetricTypeModifiers = "public static partial",
                    AllParameters =
                    {
                        new MetricParameter
                        {
                            Name = "meter",
                            Type = "global::System.Diagnostics.Metrics.Meter",
                            IsMeter = true
                        }
                    }
                }
            }
        };

        var emitter = new MetricFactoryEmitter();

        Assert.Throws<NotSupportedException>(() =>
            emitter.Emit(new List<MetricType> { metricClass }, cancellationToken: default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void EmitFactory_MetricTypeNameWithLowercaseOrEmptyFirstChar_DoesNotThrow(string metricTypeName)
    {
        var metricClass = new MetricType
        {
            Name = "MetricClass",
            Namespace = "Samples",
            Methods =
            {
                new MetricMethod
                {
                    Name = "CreateMetric",
                    MetricName = "TestMetric",
                    MetricTypeName = metricTypeName,
                    InstrumentKind = InstrumentKind.Counter,
                    GenericType = "long",
                    IsExtensionMethod = false,
                    Modifiers = "static partial",
                    MetricTypeModifiers = "public static partial",
                    AllParameters =
                    {
                        new MetricParameter
                        {
                            Name = "meter",
                            Type = "global::System.Diagnostics.Metrics.Meter",
                            IsMeter = true
                        }
                    }
                }
            }
        };

        var emitter = new MetricFactoryEmitter();
        var result = emitter.Emit(new List<MetricType> { metricClass }, cancellationToken: default);

        Assert.NotNull(result);
    }

    [Fact]
    public void EmitFactory_MetricTypeNameWithSingleUppercaseChar_DoesNotThrow()
    {
        var metricClass = new MetricType
        {
            Name = "MetricClass",
            Namespace = "Samples",
            Methods =
            {
                new MetricMethod
                {
                    Name = "CreateMetric",
                    MetricName = "TestMetric",
                    MetricTypeName = "M",
                    InstrumentKind = InstrumentKind.Counter,
                    GenericType = "long",
                    IsExtensionMethod = false,
                    Modifiers = "static partial",
                    MetricTypeModifiers = "public static partial",
                    AllParameters =
                    {
                        new MetricParameter
                        {
                            Name = "meter",
                            Type = "global::System.Diagnostics.Metrics.Meter",
                            IsMeter = true
                        }
                    }
                }
            }
        };

        var emitter = new MetricFactoryEmitter();
        var result = emitter.Emit(new List<MetricType> { metricClass }, cancellationToken: default);

        Assert.NotNull(result);
        Assert.Contains("_mInstruments", result);
    }

    [Theory]
    [InlineData(null, null, "@\"TestMetric\"")]
    [InlineData("", "", "@\"TestMetric\"")]
    [InlineData("ms", null, "@\"TestMetric\", @\"ms\"")]
    [InlineData(null, "Some description.", "@\"TestMetric\", null, \"Some description.\"")]
    [InlineData("ms", "Some description.", "@\"TestMetric\", @\"ms\", \"Some description.\"")]
    [InlineData(null, "Has \"quotes\", \\ and\nnew line.", "@\"TestMetric\", null, \"Has \\\"quotes\\\", \\\\ and\\nnew line.\"")]
    public void EmitFactory_EmitsExpectedInstrumentArguments(string? unit, string? description, string expectedArguments)
    {
        var result = EmitFactory(InstrumentKind.Counter, unit, description);

        Assert.Contains($"var instrument = _meter.CreateCounter<long>({expectedArguments});", result);
    }

    [Theory]
    [InlineData((int)InstrumentKind.Counter, "CreateCounter")]
    [InlineData((int)InstrumentKind.CounterT, "CreateCounter")]
    [InlineData((int)InstrumentKind.Histogram, "CreateHistogram")]
    [InlineData((int)InstrumentKind.HistogramT, "CreateHistogram")]
    [InlineData((int)InstrumentKind.Gauge, "CreateGauge")]
    [InlineData((int)InstrumentKind.GaugeT, "CreateGauge")]
    public void EmitFactory_WithDescription_PassesDescriptionForAllInstrumentKinds(int instrumentKind, string createMethodName)
    {
        var result = EmitFactory((InstrumentKind)instrumentKind, unit: null, description: "Some description.");

        Assert.Contains($"var instrument = _meter.{createMethodName}<long>(@\"TestMetric\", null, \"Some description.\");", result);
    }

    [Fact]
    public async Task Generator_GaugeWithDescription_PassesDescriptionToInstrument()
    {
        var source = @"
            namespace Test
            {
                using System.Diagnostics.Metrics;
                using Microsoft.Extensions.Diagnostics.Metrics;

                public class GaugeTags
                {
                    public string? Region { get; set; }
                }

                public static partial class GaugeMetrics
                {
                    [Gauge(""region"", Description = ""Gauge description."")]
                    public static partial MyGauge CreateGauge(Meter meter);

                    [Gauge<double>(Unit = ""By"", Description = ""Generic gauge description."")]
                    public static partial MyGenericGauge CreateGenericGauge(Meter meter);

                    [Gauge(typeof(GaugeTags), Description = ""Strongly-typed gauge description."")]
                    public static partial MyStrongTypeGauge CreateStrongTypeGauge(Meter meter);
                }
            }";

        var (d, r) = await RoslynTestUtils.RunGenerator(
            new MetricsGenerator(),
            new[]
            {
                Assembly.GetAssembly(typeof(Meter))!,
                Assembly.GetAssembly(typeof(GaugeAttribute))!,
            },
            new[] { source });

        Assert.Empty(d);

        var factory = r.First(x => x.HintName == "Factory.g.cs").SourceText.ToString();
        Assert.Contains("_meter.CreateGauge<long>(@\"MyGauge\", null, \"Gauge description.\");", factory);
        Assert.Contains("_meter.CreateGauge<double>(@\"MyGenericGauge\", @\"By\", \"Generic gauge description.\");", factory);
        Assert.Contains("_meter.CreateGauge<long>(@\"MyStrongTypeGauge\", null, \"Strongly-typed gauge description.\");", factory);
    }

    private static string EmitFactory(InstrumentKind instrumentKind, string? unit, string? description)
    {
        var metricClass = new MetricType
        {
            Name = "MetricClass",
            Namespace = "Samples",
            Methods =
            {
                new MetricMethod
                {
                    Name = "CreateMetric",
                    MetricName = "TestMetric",
                    MetricTypeName = "TestMetric",
                    MetricUnit = unit,
                    MetricDescription = description,
                    InstrumentKind = instrumentKind,
                    GenericType = "long",
                    IsExtensionMethod = false,
                    Modifiers = "static partial",
                    MetricTypeModifiers = "public static partial",
                    AllParameters =
                    {
                        new MetricParameter
                        {
                            Name = "meter",
                            Type = "global::System.Diagnostics.Metrics.Meter",
                            IsMeter = true
                        }
                    }
                }
            }
        };

        return new MetricFactoryEmitter().Emit(new List<MetricType> { metricClass }, cancellationToken: default);
    }
}
