// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public partial class ParserTests
{
    [Fact]
    public async Task StrongTypeCounter_CyclicReference()
    {
        var d = await RunGenerator(@"
            public class TypeA
            {
                public TypeB testB { get; set; }
            }

            public class TypeB
            {
                public TypeA testA { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(TypeA), Name=""CyclicTest"")]
                public static partial CyclicTest CreateCyclicTestCounter(Meter meter);
            }");

        Assert.NotNull(d);
        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorTagTypeCycleDetected.Id, diag.Id);
        Assert.Contains("Test.TypeB ⇆ Test.TypeA", diag.GetMessage());
    }

    [Fact]
    public async Task StrongTypeCounter_CyclicReference_BaseCycle()
    {
        var d = await RunGenerator(@"
            public class TypeA : TypeB
            {
            }

            public class TypeB
            {
                public TypeC testC { get; set; }
            }

            public class TypeC
            {
                public TypeB testB { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(TypeA), Name=""CyclicTest"")]
                public static partial CyclicTest CreateCyclicTestCounter(Meter meter);
            }");

        Assert.NotNull(d);
        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorTagTypeCycleDetected.Id, diag.Id);
        Assert.Contains("Test.TypeC ⇆ Test.TypeB", diag.GetMessage());
    }

    [Fact]
    public async Task StrongTypeCounter_CyclicReference_InterimCycle()
    {
        var d = await RunGenerator(@"
            internal static partial class Metric
            {
                [Counter(typeof(Interim), Name=""CyclicTest"")]
                public static partial CyclicTest CreateCyclicTestCounter(Meter meter);
            }

            class BaseClass
            {
                public Transitive ToTransitive { get; set; }
            }

            class Interim : BaseClass
            {
                public Transitive ToTransitive { get; set; }
            }

            class Transitive
            {
                public Interim ToInterim { get; set; }
            }");

        Assert.NotNull(d);
        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorTagTypeCycleDetected.Id, diag.Id);
        Assert.Contains("Test.Transitive ⇆ Test.Interim", diag.GetMessage());
    }

    [Fact]
    public async Task StructTypeGauge()
    {
        var d = await RunGenerator(@"
            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public struct GaugeStruct
            {
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }

                [Dimension(""Operations_FromAttribute"")]
                public Operations Operations { get; set; }
            }

            public static partial class MetricClass
            {
                [Gauge(typeof(GaugeStruct), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeGauge()
    {
        // This test should return no errors.
        var d = await RunGenerator(@"
            public class DimensionsTest : ParentDimensions
            {
                [Dimension(""test1_FromAttribute"")]
                public string? test1 { get; set; }

                [Dimension(""Operations_FromAttribute"")]
                public Operations operations {get;set;}

                public ChildDimensions? ChildDimensions1 { get; set; }

                public void Method()
                {
                    System.Console.WriteLine(""I am a method."");
                }
            }

            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public class ParentDimensions
            {
                [Dimension(""parentDimension_FromAttribute"")]
                public string? ParentOperationNameWithAttribute { get;set; }

                public string? ParentOperationName { get;set; }

                public DimensionsStruct ChildDimensionsStruct { get; set; }
            }

            public class ChildDimensions
            {
                [Dimension(""test2_FromAttribute"")]
                public string test2_WithAttribute { get; set; }

                public string test2 { get; set; }

                [Dimension(""test1_FromAttribute_In_Child1"")]
                public string? test1 { get; set; }
            }

            public struct DimensionsStruct
            {
                [Dimension(""testStruct_FromAttribute"")]
                public string testStruct_WithAttribute { get; set; }

                public string testStruct { get; set; }
            }

            public static partial class MetricClass
            {
                [Gauge(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Single(d);
    }

    [Fact]
    public async Task DuplicateDimensionStringName()
    {
        var d = await RunGenerator(@"
            public class DimensionsTest
            {
                public string dim1 { get; set; }
                public ChildDimensions childDimensions { get; set; }
            }

            public class ChildDimensions
            {
                public string dim1 {get;set;}
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        _ = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, d[0].Id);
    }

    [Fact]
    public async Task DuplicateDimensionStringNameInAttribute()
    {
        var d = await RunGenerator(@"
            public class DimensionsTest
            {
                [Dimension(""dim1FromAttribute"")]
                public string dim1 { get; set; }
                public ChildDimensions childDimensions { get; set; }
            }

            public class ChildDimensions
            {
                [Dimension(""dim1FromAttribute"")]
                public string dim1 {get;set;}
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        _ = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, d[0].Id);
    }

    [Fact]
    public async Task DuplicateDimensionEnumName()
    {
        var d = await RunGenerator(@"
            public class DimensionsTest
            {
                public Operations operations { get; set; }
                public ChildDimensions childDimensions { get; set; }
            }

            public class ChildDimensions
            {
                public Operations operations { get; set; }
            }

            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        _ = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, d[0].Id);
    }

    [Fact]
    public async Task DuplicateDimensionEnumNameInAttribute()
    {
        var d = await RunGenerator(@"
            public class DimensionsTest
            {
                [Dimension(""operations"")]
                public Operations operations { get; set; }
                public ChildDimensions childDimensions { get; set; }
            }

            public class ChildDimensions
            {
                public Operations operations { get; set; }
            }

            public enum Operations
            {
                Unknown = 0
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        _ = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, d[0].Id);
    }

    [Theory]
    [InlineData("int?")]
    [InlineData("System.Int32?")]
    [InlineData("bool?")]
    [InlineData("System.Boolean?")]
    [InlineData("byte?")]
    [InlineData("char")]
    [InlineData("char?")]
    [InlineData("ulong")]
    [InlineData("System.UInt64")]
    [InlineData("decimal")]
    [InlineData("System.Decimal")]
    [InlineData("System.IntPtr")]
    [InlineData("System.UIntPtr")]
    [InlineData("double?")]
    [InlineData("decimal?")]
    [InlineData("object")]
    [InlineData("object?")]
    [InlineData("System.Object")]
    [InlineData("System.Object?")]
    [InlineData("int[]")]
    [InlineData("int?[]")]
    [InlineData("int[]?")]
    [InlineData("int?[]?")]
    [InlineData("object[]")]
    [InlineData("object[]?")]
    [InlineData("System.Array")]
    [InlineData("System.DateTime")]
    [InlineData("System.DateTime?")]
    [InlineData("System.IDisposable")]
    [InlineData("System.Action")]
    [InlineData("System.Action<int>")]
    [InlineData("System.Func<double>")]
    [InlineData("System.Nullable<int>")]
    [InlineData("System.Nullable<char>")]
    [InlineData("System.Nullable<System.Int32>")]
    [InlineData("System.Nullable<System.Decimal>")]
    [InlineData("System.Nullable<System.DateTime>")]
    public async Task InvalidDimensionType(string type)
    {
        var d = await RunGenerator(@$"
            public class DimensionsTest
            {{
                public {type} dim1 {{ get; set; }}
            }}

            public static partial class MetricClass
            {{
                [Histogram(typeof(DimensionsTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }}");

        _ = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorInvalidTagNameType.Id, d[0].Id);
    }

    [Theory]
    [InlineData("ulong")]
    [InlineData("uint")]
    [InlineData("ushort")]
    [InlineData("char")]
    [InlineData("bool")]
    public async Task GaugeT_InvalidGenericType(string type)
    {
        var d = await RunGenerator(@$"
        partial class C
        {{
            [Gauge<{type}>(""d1"")]
            static partial TestGauge CreateGauge(Meter meter);
        }}");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorInvalidAttributeGenericType.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_StrongType_CyclicReference()
    {
        var d = await RunGenerator(@"
        public class TypeA
        {
            public TypeB B { get; set; }
        }

        public class TypeB
        {
            public TypeA A { get; set; }
        }

        partial class C
        {
            [Gauge(typeof(TypeA), Name=""CyclicTest"")]
            static partial CyclicTest CreateGauge(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorTagTypeCycleDetected.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_ConflictWithCounterName()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Counter(""d1"")]
            static partial SharedMetric CreateCounter(Meter meter);

            [Gauge(""d2"")]
            static partial SharedMetric CreateGauge(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorMetricNameReuse.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_ConflictWithHistogramName()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Histogram(""d1"")]
            static partial SharedMetric CreateHistogram(Meter meter);

            [Gauge(""d2"")]
            static partial SharedMetric CreateGauge(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorMetricNameReuse.Id, diag.Id);
    }

    [Theory]
    [InlineData("int?")]
    [InlineData("double?")]
    [InlineData("System.DateTime?")]
    [InlineData("System.Nullable<int>")]
    public async Task Gauge_StrongType_NullableProperty(string type)
    {
        var d = await RunGenerator(@$"
        public class Tags
        {{
            public {type} InvalidTag {{ get; set; }}
        }}

        partial class C
        {{
            [Gauge(typeof(Tags), Name=""NullableTest"")]
            static partial NullableTest CreateGauge(Meter meter);
        }}");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorInvalidTagNameType.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_NotStaticMethod()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Gauge(""d1"")]
            partial MemoryUsage CreateMemoryUsage(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorNotStaticMethod.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_NotPartialMethod()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Gauge(""d1"")]
            static MemoryUsage CreateMemoryUsage(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorNotPartialMethod.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_MissingMeterParameter()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Gauge(""d1"")]
            static partial MemoryUsage CreateMemoryUsage();
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorMissingMeter.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_InvalidTagNameWithSpecialChars()
    {
        var d = await RunGenerator(@"
        partial class C
        {
            [Gauge(""Invalid*Tag"")]
            static partial TestGauge CreateGauge(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorInvalidTagNames.Id, diag.Id);
    }

    [Fact]
    public async Task Gauge_StrongType_MoreThanThirtyTags()
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 33; i++)
        {
            sb.AppendLine($"public class C{i} : C{i + 1} {{ public string Tag{i} {{ get; set; }} }}");
        }

        sb.AppendLine("public class C33 { public string Tag33 { get; set; } }");

        sb.AppendLine(@"
        partial class C
        {
            [Gauge(typeof(C0), Name=""ManyTags"")]
            static partial ManyTags CreateGauge(Meter meter);
        }");

        var d = await RunGenerator(sb.ToString());

        Assert.Empty(d);
    }

    [Fact]
    public async Task Gauge_StrongType_DuplicateTagName()
    {
        var d = await RunGenerator(@"
        public class Tags
        {
            public string Region { get; set; }
            public ChildTags Child { get; set; }
        }

        public class ChildTags
        {
            public string Region { get; set; }
        }

        partial class C
        {
            [Gauge(typeof(Tags), Name=""DuplicateTest"")]
            static partial DuplicateTest CreateGauge(Meter meter);
        }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, diag.Id);
    }

    [Theory]
    [InlineData("ulong")]
    [InlineData("decimal")]
    [InlineData("System.DateTime")]
    [InlineData("object")]
    public async Task Gauge_StrongType_InvalidPropertyType(string type)
    {
        var d = await RunGenerator(@$"
        public class Tags
        {{
            public {type} InvalidTag {{ get; set; }}
        }}

        partial class C
        {{
            [Gauge(typeof(Tags), Name=""InvalidTest"")]
            static partial InvalidTest CreateGauge(Meter meter);
        }}");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorInvalidTagNameType.Id, diag.Id);
    }
}
