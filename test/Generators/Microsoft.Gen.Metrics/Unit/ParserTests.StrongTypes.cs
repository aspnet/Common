// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Gen.Metrics.Test;

public partial class ParserTests
{
    [Fact]
    public async Task NullDimensionNamesInAttributes()
    {
        var d = await RunGenerator(@"
            public struct HistogramStruct
            {
                [Dimension(null)]
                public string? Dim1 { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(HistogramStruct), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeHistogram()
    {
        var d = await RunGenerator(@"
            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public struct HistogramStruct
            {
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }

                [Dimension(""Operations_FromAttribute"")]
                public Operations Operations { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(HistogramStruct), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeHistogram()
    {
        // This test should return no errors.
        var d = await RunGenerator(@"
            public class TagNamesTest : ParentTagNames
            {
                public string? test1 { get; set; }

                [TagName(""test1_FromAttribute"")]
                public string? test1_WithAttribute { get; set; }

                [TagName(""operations_FromAttribute"")]
                public Operations operations {get;set;}

                public ChildTagNames? ChildTagNames1 { get; set; }

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

            public class ParentTagNames
            {
                [TagName(""parentTagName_FromAttribute"")]
                public string? ParentOperationNameWithAttribute { get;set; }

                public string? ParentOperationName { get;set; }

                public TagNamesStruct ChildTagNamesStruct { get; set; }
            }

            public class ChildTagNames
            {
                [TagName(""test2_FromAttribute"")]
                public string test2_WithAttribute { get; set; }

                public string test2 { get; set; }

                [TagName(""test1_FromAttribute_In_Child1"")]
                public string? test1 { get; set; }

                public ChildTagNames2? ChildTagNames2 { get; set;}
            }

            public class ChildTagNames2
            {
                [TagName(""test3_FromAttribute"")]
                public string test3_WithAttribute { get; set; }

                public string test3 { get; set; }

                [TagName(""test1_FromAttribute_In_Child2"")]
                public string? test1 { get; set; }
            }

            public struct TagNamesStruct
            {
                [TagName(""testStruct_FromAttribute"")]
                public string testStruct_WithAttribute { get; set; }

                public string testStruct { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(TagNamesTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task SimpleStrongTypeHistogram()
    {
        // This test should return no errors.
        var d = await RunGenerator(@"
            public struct TagNamesTest
            {
                [TagName(""test1_FromAttribute"")]
                public string? test1_WithAttribute { get; set; }
                public string? test1 { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(TagNamesTest))]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task TestNoStrongTypeDefined()
    {
        var d = await RunGenerator(@"
            public static partial class MetricClass
            {
                [Histogram(typeof(DimensionsTest))]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeCounter()
    {
        var d = await RunGenerator(@"
            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public struct CounterStruct
            {
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }

                [Dimension(""Dim2_FromAttribute"")]
                public string? Dim2;

                [Dimension(""Operations_FromAttribute"")]
                public Operations Operations { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(CounterStruct), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }
        ");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeCounter()
    {
        // This test should return no errors.
        var d = await RunGenerator(@"
            public class TagNamesTest : ParentTagNames
            {
                [TagName(""test1_FromAttribute"")]
                public string? test1 { get; set; }

                [TagName(""Operations_FromAttribute"")]
                public Operations operations {get;set;}

                public ChildTagNames? ChildTagNames1 { get; set; }

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

            public class ParentTagNames
            {
                [TagName(""parentTagName_FromAttribute"")]
                public string? ParentOperationNameWithAttribute { get;set; }

                public string? ParentOperationName { get;set; }

                public TagNamesStruct ChildTagNamesStruct { get; set; }
            }

            public class ChildTagNames
            {
                [TagName(""test2_FromAttribute"")]
                public string test2_WithAttribute { get; set; }

                public string test2 { get; set; }

                [TagName(""test1_FromAttribute_In_Child1"")]
                public string? test1 { get; set; }
            }

            public struct TagNamesStruct
            {
                [TagName(""testStruct_FromAttribute"")]
                public string testStruct_WithAttribute { get; set; }

                public string testStruct { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(TagNamesTest), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeCounterWithDescription()
    {
        var d = await RunGenerator(@"
        internal static partial class Metric
        {
            /// <summary>
            /// TagName1 description.
            /// </summary>
            public const string Dim1 = ""Dim1"";

            /// <summary>
            /// DescribedTagNameCounter description.
            /// </summary>
            /// <param name=""meter""></param>
            /// <returns></returns>
            [Counter(DescripedTagNames.TagName1, Dim1)]
            public static partial DescribedTagNameCounter CreatePublicCounter(Meter meter);

            /// <summary>
            /// DimenisonDefinedInMetricClass description.
            /// </summary>
            public const string DimenisonDefinedInMetricClass = ""DimenisonDefinedInMetricClass"";

            /// <summary>
            /// DescribedTagNameHistogram description.
            /// </summary>
            /// <param name=""meter""></param>
            /// <returns></returns>
            [Histogram(DescripedTagNames.TagName2, DimenisonDefinedInMetricClass)]
            public static partial DescribedTagNameHistogram CreatePublicHistogram(Meter meter);

            /// <summary>
            /// StrongTypeCounterWithDescripedTagName description.
            /// </summary>
            /// <param name=""meter""></param>
            /// <returns></returns>
            [Counter(typeof(TagNameForStrongTypes), Name = ""MyStrongTypeMetricWithDescription"")]
            public static partial StrongTypeCounterWithDescripedTagName CreateStrongTypeCounterWithDescibedTagNames(Meter meter);
        }

        /// <summary>
        /// DescripedTagNames class description.
        /// </summary>
        internal static class DescripedTagNames
        {
            /// <summary>
            /// TagName1 in class description.
            /// </summary>
            public const string TagName1 = ""TagName1"";

            /// <summary>
            /// TagName2 description.
            /// </summary>
            public const string TagName2 = ""TagName2"";

            /// <summary>
            /// TagName3 description.
            /// </summary>
            public const string TagName3 = ""TagName3"";
        }

        public class TagNameForStrongTypes
        {
            /// <summary>
            /// Gets or sets anotherTagName.
            /// </summary>
            public string? AnotherTagName { get; set; }

            /// <summary>
            /// Gets or sets MetricEnum.
            /// </summary>
            public MetricOperations MetricEnum { get; set; }

            /// <summary>
            /// Gets or sets MetricEnum2.
            /// </summary>
            [TagName(""Enum2"")]
            public MetricOperations MetricEnum2 { get; set; }

            /// <summary>
            /// Gets or sets ChildTagNamesClass.
            /// </summary>
            public ChildClassTagNameForStrongTypes? ChildTagNamesClass { get; set; }

            /// <summary>
            ///  Gets or sets ChildTagNamesStruct.
            /// </summary>
            public TagNameForStrongTypesTagNamesStruct ChildTagNamesStruct { get; set; }
        }

        public enum MetricOperations
        {
            Unknown = 0,
            Operation1 = 1,
        }

        public class ChildClassTagNameForStrongTypes
        {
            /// <summary>
            /// Gets or sets Dim2.
            /// </summary>
            public string? Dim2 { get; set; }

            /// <summary>
            /// Gets or sets SomeDim.
            /// </summary>
            [TagName(""dim2FromAttribute"")]
            public string? SomeDim;
        }

        public struct TagNameForStrongTypesTagNamesStruct
        {
            /// <summary>
            /// Gets or sets Dim4Struct.
            /// </summary>
            public string Dim4Struct { get; set; }

            /// <summary>
            /// Gets or sets Dim5Struct.
            /// </summary>
            [TagName(""Dim5FromAttribute"")]
            public string Dim5Struct { get; set; }
        }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeHistogramWithDescription()
    {
        // This test should return no errors.
        var d = await RunGenerator(@"
            public class TagNamesTest : ParentTagNames
            {
                /// <summary>
                /// test1 description.
                /// </summary>
                public string? test1 { get; set; }

                /// <summary>
                /// test1_FromAttribute description.
                /// </summary>
                [TagName(""test1_FromAttribute"")]
                public string? test1_WithAttribute { get; set; }

                /// <summary>
                /// operations_FromAttribute description.
                /// </summary>
                [TagName(""operations_FromAttribute"")]
                public Operations operations {get;set;}

                /// <summary>
                /// ChildTagNames1 description.
                /// </summary>
                public ChildTagNames? ChildTagNames1 { get; set; }

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

            public class ParentTagNames
            {
                /// <summary>
                /// parentTagName_FromAttribute description.
                /// </summary>
                [TagName(""parentTagName_FromAttribute"")]
                public string? ParentOperationNameWithAttribute { get;set; }

                /// <summary>
                /// ParentOperationName description.
                /// </summary>
                public string? ParentOperationName { get;set; }

                public TagNamesStruct ChildTagNamesStruct { get; set; }
            }

            public class ChildTagNames
            {
                /// <summary>
                /// test2_WithAttribute description.
                /// </summary>
                [TagName(""test2_FromAttribute"")]
                public string test2_WithAttribute { get; set; }

                /// <summary>
                /// test2 description.
                /// </summary>
                public string test2 { get; set; }

                /// <summary>
                /// test1_FromAttribute_In_Child1 description.
                /// </summary>
                [TagName(""test1_FromAttribute_In_Child1"")]
                public string? test1 { get; set; }

                public ChildTagNames2? ChildTagNames2 { get; set;}
            }

            public class ChildTagNames2
            {
                /// <summary>
                /// test3_FromAttribute description.
                /// </summary>
                [TagName(""test3_FromAttribute"")]
                public string test3_WithAttribute { get; set; }

                /// <summary>
                /// test3 description.
                /// </summary>
                public string test3 { get; set; }

                /// <summary>
                /// test1_FromAttribute_In_Child2 description.
                /// </summary>
                [TagName(""test1_FromAttribute_In_Child2"")]
                public string? test1 { get; set; }
            }

            public struct TagNamesStruct
            {
                /// <summary>
                /// testStruct_WithAttribute description.
                /// </summary>
                [TagName(""testStruct_FromAttribute"")]
                public string testStruct_WithAttribute { get; set; }

                /// <summary>
                /// testStruct description.
                /// </summary>
                public string testStruct { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(TagNamesTest), Name=""TotalCountTest"")]
                public static partial TestHistogram CreateTestHistogram(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeCounterWithDescription()
    {
        var d = await RunGenerator(@"
            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public struct CounterStruct
            {
                /// <summary>
                /// Dim1_FromAttribute description.
                /// </summary>
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }

                /// <summary>
                /// Dim2_FromAttribute description.
                /// </summary>
                [Dimension(""Dim2_FromAttribute"")]
                public string? Dim2;

                /// <summary>
                /// Operations_FromAttribute description.
                /// </summary>
                [Dimension(""Operations_FromAttribute"")]
                public Operations Operations { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(CounterStruct), Name=""TotalCountTest"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }
        ");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeHistogramWithDescription()
    {
        var d = await RunGenerator(@"
            public enum Operations
            {
                Unknown = 0,
                Operation1 = 1,
            }

            public struct HistogramStruct
            {
                /// <summary>
                /// Dim1_FromAttribute description.
                /// </summary>
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }

                /// <summary>
                /// Operations_FromAttribute description.
                /// </summary>
                [Dimension(""Operations_FromAttribute"")]
                public Operations Operations { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(HistogramStruct), Name=""TotalCountTest"")]
                public static partial TotalHistogram CreateTotalHistogram(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task MoreThanThirtyDimensions()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("public class ManyTags");
        sb.AppendLine("{");
        for (int i = 1; i <= 33; i++)
        {
            sb.AppendLine($"    public string D{i} {{ get; set; }} = string.Empty;");
        }

        sb.AppendLine("}");
        sb.AppendLine(@"        public static partial class MetricClass
        {
            [Histogram(typeof(ManyTags), Name=""TotalCountTest"")]
            public static partial TotalCount CreateTotalCountCounter(Meter meter);
        }");
        var d = await RunGenerator(sb.ToString());
        Assert.Empty(d);
    }

    [Fact]
    public async Task TransitiveDimensions()
    {
        var d = await RunGenerator(@"
            class MyClassA
            {
                public string Dim1 { get; set; }
                public string Dim2 { get; set; }
                public string Dim3 { get; set; }
                public string Dim4 { get; set; }
                public string Dim5 { get; set; }
                public string Dim6 { get; set; }
                public string Dim7 { get; set; }
                public string Dim8 { get; set; }
                public string Dim9 { get; set; }
                public string Dim10 { get; set; }
                public string Dim11 { get; set; }
                public string Dim12 { get; set; }
                public string Dim13 { get; set; }
                public string Dim14 { get; set; }
                public string Dim15 { get; set; }
                public string Dim16 { get; set; }
                public string Dim17 { get; set; }
                public string Dim18 { get; set; }
                public string Dim19 { get; set; }
                public string Dim20 { get; set; }
                public MyClassB MyTransitiveProperty { get; set; }
            }
            class MyClassB
            {
                public string Dim21 { get; set; }
                public string Dim22 { get; set; }
                public string Dim23 { get; set; }
                public string Dim24 { get; set; }
                public string Dim25 { get; set; }
                public string Dim26 { get; set; }
                public string Dim27 { get; set; }
                public string Dim28 { get; set; }
                public string Dim29 { get; set; }
                public string Dim30 { get; set; }
            }
            static partial class MetricClass
            {
                [Histogram(typeof(MyClassA))]
                static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");
        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeWithUnit()
    {
        var d = await RunGenerator(@"
            public struct HistogramStruct
            {
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(HistogramStruct), Name=""TotalCountTest"", Unit = ""s"")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StructTypeWithDescription()
    {
        var d = await RunGenerator(@"
            public struct HistogramStruct
            {
                [Dimension(""Dim1_FromAttribute"")]
                public string? Dim1 { get; set; }
            }

            public static partial class MetricClass
            {
                [Histogram(typeof(HistogramStruct), Name=""TotalCountTest"", Unit = ""s"", Description = ""Total count."")]
                public static partial TotalCount CreateTotalCountCounter(Meter meter);
            }");

        Assert.Empty(d);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("System.Boolean")]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("int")]
    [InlineData("System.Int32")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("System.Int64")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("System.Double")]
    public async Task StrongTypeWithPrimitiveTags(string type)
    {
        var d = await RunGenerator(@$"
            public class PrimitiveTags
            {{
                public {type} Tag1 {{ get; set; }}

                [TagName(""tag2"")]
                public {type} Tag2;

                public string? StringTag {{ get; set; }}

                public ChildPrimitiveTags? Child {{ get; set; }}

                public PrimitiveTagsStruct ChildStruct {{ get; set; }}
            }}

            public class ChildPrimitiveTags
            {{
                public {type} Tag3 {{ get; set; }}
            }}

            public struct PrimitiveTagsStruct
            {{
                [TagName(""tag4"")]
                public {type} Tag4 {{ get; set; }}
            }}

            public static partial class MetricClass
            {{
                [Counter(typeof(PrimitiveTags), Name=""CounterTest"")]
                public static partial TestCounter CreateCounter(Meter meter);

                [Histogram(typeof(PrimitiveTagsStruct), Name=""HistogramTest"")]
                public static partial TestHistogram CreateHistogram(Meter meter);

                [Gauge(typeof(PrimitiveTags), Name=""GaugeTest"")]
                public static partial TestGauge CreateGauge(Meter meter);
            }}");

        Assert.Empty(d);
    }

    [Fact]
    public async Task StrongTypeWithPrimitiveTags_DuplicateTagName()
    {
        var d = await RunGenerator(@"
            public class PrimitiveTags
            {
                [TagName(""server.port"")]
                public int Port { get; set; }

                public ChildPrimitiveTags? Child { get; set; }
            }

            public class ChildPrimitiveTags
            {
                [TagName(""server.port"")]
                public long OtherPort { get; set; }
            }

            public static partial class MetricClass
            {
                [Counter(typeof(PrimitiveTags), Name=""CounterTest"")]
                public static partial TestCounter CreateCounter(Meter meter);
            }");

        var diag = Assert.Single(d);
        Assert.Equal(DiagDescriptors.ErrorDuplicateTagName.Id, diag.Id);
    }
}
