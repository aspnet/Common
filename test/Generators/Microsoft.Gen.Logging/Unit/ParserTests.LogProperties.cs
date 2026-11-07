// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Gen.Logging.Parsing;
using Microsoft.Gen.Shared;
using VerifyXunit;
using Xunit;

namespace Microsoft.Gen.Logging.Test;

public partial class ParserTests
{
    [Fact]
    public static async Task InvalidLogPropertiesUsage()
    {
        await RunGenerator(@"
            class MyClass2
            {
                public int A { get; set; }
            }   

            class MyClass
            {
                [/*0+*/LogProperties/*-0*/]
                internal MyClass2 P0 { get; set; }

                [/*1+*/LogProperties/*-1*/]
                internal static MyClass2 P1 { get; set; }

                [/*2+*/LogProperties/*-2*/]
                internal MyClass2 P2 { set; }

                [/*3+*/LogProperties/*-3*/]
                public MyClass2 P3 { internal get; set; }

                public int A { get; set; }
            }

            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""Parameter"")]
                static partial void M0(ILogger logger, [LogProperties] MyClass p1);
            }", DiagDescriptors.InvalidAttributeUsage);
    }

    [Fact]
    public static async Task LogPropertiesOmitParamName_DetectsNameCollision()
    {
        const string Source = @"
            class MyType
            {
                public int p0 { get; }
            }

            partial class C
            {
                [LoggerMessage(LogLevel.Debug)]
                static partial void M0(ILogger logger, int p0, [LogProperties(OmitReferenceName = true)] MyType /*0+*/p1/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.TagNameCollision);
    }

    [Fact]
    public static async Task LogProperties_AllowsDefaultLogMethodCtor()
    {
        await RunGenerator(@"
            class MyType
            {
                public int p0 { get; }
            }

            partial class C
            {
                [LoggerMessage]
                static partial void M0(ILogger logger, LogLevel level, [LogProperties] MyType p0);
            }");
    }

    [Theory]
    [InlineData("record class")]
    [InlineData("record struct")]
    [InlineData("readonly record struct")]
    public async Task LogProperties_AllowsRecordTypes(string type)
    {
        await RunGenerator(@$"
            internal {type} MyRecord(int Value)
            {{
                public int GetOnlyValue => Value + 1;
            }}

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug)]
                public static partial void LogFunc(ILogger logger, [LogProperties] MyRecord p0);
            }}");
    }

    [Theory]
    [InlineData("LogLevel")]
    [InlineData("System.Exception")]
    public async Task LogPropertiesInvalidUsage(string annotation)
    {
        // We don't check [LogProperties] on ILogger here since it produces a lot of errors
        string source = @$"
            partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""Parameterless..."")]
                static partial void M(ILogger logger, [LogProperties] {annotation} /*0+*/param/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesInvalidUsage);
    }

    [Theory]
    [InlineData("MyClass")]
    [InlineData("MyStruct")]
    [InlineData("MyInterface")]
    public async Task LogPropertiesValidUsage(string parameterType)
    {
        await RunGenerator(@$"
            class MyClass
            {{
                public string Property {{ get; set; }}
            }}

            struct MyStruct
            {{
                public string Property {{ get; set; }}
            }}

            struct MyInterface
            {{
                public string Property {{ get; set; }}
            }}

            partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""Parameter {{P1}}"")]
                static partial void M(ILogger logger, [LogProperties] {parameterType} p1);
            }}");
    }

    [Theory]
    [InlineData("MyClass")]
    [InlineData("MyStruct")]
    [InlineData("MyInterface")]
    public async Task LogPropertiesParameterSkipped(string parameterType)
    {
        string source = @$"
            class MyClass {{ }}

            struct MyStruct {{ }}

            struct MyInterface {{ }}

            partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""Empty template"")]
                static partial void M(ILogger logger, [LogProperties] {parameterType} /*0+*/param/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesParameterSkipped);
    }

    [Fact]
    public async Task LogPropertiesParameterNotSkipped()
    {
        await RunGenerator(@"
            class MyClass
            {
                public int A { get; set; }
            }

            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""{Param} is here"")]
                static partial void M(ILogger logger, [LogProperties] MyClass param);
            }");
    }

    [Fact]
    public async Task LogPropertiesPointlessUsage()
    {
        const string Source = @"
            class MyClass
            {
                private int A { get; set; }
                protected double B { get; set; }
            }

            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""{param}"")]
                static partial void M(ILogger logger, [LogProperties] MyClass /*0+*/param/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.LogPropertiesParameterSkipped);
    }

    [Fact]
    public async Task SimpleNameCollision()
    {
        const string Source = @"
            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""{Param} {Param}"")]
                static partial void M(ILogger logger, string param, string /*0+*/Param/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.TagNameCollision);
    }

    [Fact]
    public async Task LogPropertiesNameCollision()
    {
        const string Source = @"
            class MyClass
            {
                public int A { get; set; }

                [LogPropertyIgnore]
                public int B { get; set; }
            }

            partial class C
            {
                [LoggerMessage(LogLevel.Debug)]
                static partial void M0(ILogger logger, string param_A, [LogProperties] MyClass /*0+*/param/*-0*/);

                [LoggerMessage(LogLevel.Debug)]
                static partial void M1(ILogger logger, string param_B, [LogProperties] MyClass param);
            }";

        await RunGenerator(Source, DiagDescriptors.TagNameCollision);
    }

    [Fact]
    public async Task LogPropertiesTransitiveNameCollision()
    {
        const string Source = @"
            public class MyClass
            {
                public int Transitive_Prop { get; set; }

                [LogProperties]
                public MyTransitiveClass Transitive { get; set; }
            }

            public class MyTransitiveClass
            {
                public int Prop { get; set; }
            }

            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [LogProperties] MyClass /*0+*/param/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.TagNameCollision);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("System.Int32")]
    [InlineData("System.Int32?")]
    [InlineData("bool")]
    [InlineData("bool?")]
    [InlineData("System.Boolean")]
    [InlineData("System.Boolean?")]
    [InlineData("byte")]
    [InlineData("byte?")]
    [InlineData("char?")]
    [InlineData("string")]
    [InlineData("string?")]
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
    [InlineData("System.DateTimeOffset")]
    [InlineData("System.TimeSpan")]
    [InlineData("System.Guid")]
    [InlineData("System.Type")]
    [InlineData("System.DateTime?")]
    [InlineData("System.DateTimeOffset?")]
    [InlineData("System.TimeSpan?")]
    [InlineData("System.Guid?")]
    [InlineData("System.IDisposable")]
    [InlineData("System.Action")]
    [InlineData("System.Action<int>")]
    [InlineData("System.Func<double>")]
    [InlineData("System.Nullable<int>")]
    [InlineData("System.Nullable<char>")]
    [InlineData("System.Nullable<System.Int32>")]
    [InlineData("System.Nullable<System.Decimal>")]
    [InlineData("System.Nullable<System.DateTime>")]
    [InlineData("System.Nullable<System.DateTimeOffset>")]
    [InlineData("System.Nullable<System.TimeSpan>")]
    [InlineData("System.Nullable<System.Guid>")]
    public async Task IneligibleTypeForPropertiesLogging(string type)
    {
        string source = @$"
            class MyClass
            {{
                [LogProperties]
                public {type} /*0+*/Prop/*-0*/ {{ get; set; }}
            }}

            partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M0(ILogger logger, [LogProperties] {type} /*1+*/test/*-1*/);

                [LoggerMessage(1, LogLevel.Debug, ""No params..."")]
                static partial void M1(ILogger logger, [LogProperties] MyClass test);
            }}";

        await RunGenerator(source, DiagDescriptors.InvalidTypeToLogProperties);
    }

    [Theory]
    [InlineData("ClassA")] // Type self-reference
    [InlineData("ClassC")] // One-level cycle
    [InlineData("ClassB")] // Two-level cycle
    [InlineData("StructA")] // Custom struct
    [InlineData("StructA?")] // Nullable struct
    [InlineData("System.Nullable<StructA>")] // Explicit nullable struct
    public async Task PropertyToLogWithTypeCycle(string propertyType)
    {
        string source = @$"
            public class ClassA
            {{
                [LogProperties]
                public {propertyType} Prop {{ get; set; }}
            }}

            public class ClassB
            {{
                [LogProperties]
                public ClassC Prop {{ get; set; }}
            }}

            public class ClassC
            {{
                [LogProperties]
                public ClassA Prop {{ get; set; }}
            }}

            public struct StructA
            {{
                [LogProperties]
                public ClassA Prop {{ get; set; }}
            }}

            partial class LoggerClass
            {{
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [LogProperties] ClassA /*0+*/test/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesCycleDetected);
    }

    [Theory]
    [InlineData("[LogProperties(Transitive = true)]", "")]
    [InlineData("[LogProperties]", "[LogProperties]")]
    public async Task PropertyToLogWithTypeCycleThroughBaseType(string parameterAttribute, string propertyAttribute)
    {
        string source = @$"
            public class Node
            {{
                {propertyAttribute}
                public Leaf Next {{ get; set; }}
            }}

            public sealed class Leaf : Node
            {{
            }}

            public class Holder
            {{
                {propertyAttribute}
                public Leaf Child {{ get; set; }}
            }}

            partial class LoggerClass
            {{
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, {parameterAttribute} Holder /*0+*/test/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesCycleDetected);
    }

    [Theory]
    [InlineData("[LogProperties(Transitive = true)]", "")]
    [InlineData("[LogProperties]", "[LogProperties]")]
    public async Task PropertyToLogWithTypeCycleAfterAnotherProperty(string parameterAttribute, string propertyAttribute)
    {
        string source = @$"
            public class Inner
            {{
                public int Value {{ get; set; }}
            }}

            public class Holder
            {{
                {propertyAttribute}
                public Inner First {{ get; set; }}

                {propertyAttribute}
                public Holder Self {{ get; set; }}
            }}

            partial class LoggerClass
            {{
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, {parameterAttribute} Holder /*0+*/test/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesCycleDetected);
    }

    [Theory]
    [InlineData("[LogProperties(Transitive = true)]", "")]
    [InlineData("[LogProperties]", "[LogProperties]")]
    public async Task PropertyToLogWithUnboundedGenericType(string parameterAttribute, string propertyAttribute)
    {
        string source = @$"
            public class Node<T>
            {{
                {propertyAttribute}
                public Node<Node<T>> Next {{ get; set; }}
            }}

            partial class LoggerClass
            {{
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, {parameterAttribute} Node<int> /*0+*/test/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LogPropertiesCycleDetected);
    }

    [Fact]
    public async Task PropertyToLogWithSeveralTypeCyclesReportsOneDiagnostic()
    {
        const string Source = @"
            public class A
            {
                public B First { get; set; }
                public C Second { get; set; }
            }

            public class B
            {
                public A Back { get; set; }
            }

            public class C
            {
                public A Back { get; set; }
            }

            partial class LoggerClass
            {
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [LogProperties(Transitive = true)] A /*0+*/test/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.LogPropertiesCycleDetected);
    }

    [Fact]
    public async Task TransitivePropertyOfSystemTypeIsNotExpanded()
    {
        const string Source = @"
            public class MyClass
            {
                public System.Type Kind { get; set; }
            }

            partial class LoggerClass
            {
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [LogProperties(Transitive = true)] MyClass test);
            }";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task PropertyHiddenInDerivedClass()
    {
        const string Source = @"
            public class BaseType
            {
                public int Prop { get; set; }
            }

            public class DerivedType : BaseType
            {
                public new string Prop { get; set; }
            }

            partial class LoggerClass
            {
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [LogProperties] DerivedType /*0+*/test/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.LogPropertiesHiddenPropertyDetected);
    }

    [Fact]
    public async Task DefaultToString()
    {
        await RunGenerator(@"
            record class MyRecordClass(int x);
            record struct MyRecordStruct(int x);

            class MyClass2
            {
            }

            class MyClass3
            {
                public override string ToString() => ""FIND ME!"";
            }

            class MyClass<T>
            {
                public object /*0+*/P0/*-0*/ { get; set; }
                public MyClass2 /*1+*/P1/*-1*/ { get; set; }
                public MyClass3 P2 { get; set; }
                public int P3 { get; set; }
                public System.Numerics.BigInteger P4 { get; set; }
                public T P5 { get; set; }
            }

            partial class C<T>
            {
                [LoggerMessage(LogLevel.Debug)]
                static partial void M0(this ILogger logger,
                    object /*2+*/p0/*-2*/,
                    MyClass2 /*3+*/p1/*-3*/,
                    MyClass3 p2,
                    [LogProperties] MyClass<int> p3,
                    T p4,
                    MyRecordClass p5,
                    MyRecordStruct p6);
            }", DiagDescriptors.DefaultToString);
    }

    [Fact]
    public async Task ClassWithNullableProperty()
    {
        string source = @"
                namespace Test
                {
                    using System;

                    using Microsoft.Extensions.Logging;

                    internal static class LoggerUtils
                    {
                        public class MyClassWithNullableProperty
                        {
                            public DateTime? NullableDateTime { get; set; }
                            public DateTime NonNullableDateTime { get; set; }
                        }

                        partial class MyLogger
                        {
                            [LoggerMessage(0, LogLevel.Information, ""Testing nullable property within class here..."")]
                            public static partial void LogMethodNullablePropertyInClassMatchesNonNullable(ILogger logger, [LogProperties] MyClassWithNullableProperty classWithNullablePropertyParam);
                        }
                    }
                }";

#if NET6_0_OR_GREATER
        var symbols = new[] { "NET7_0_OR_GREATER", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER" };
#else
        var symbols = new[] { "NET5_0_OR_GREATER" };
#endif

        var (d, r) = await RoslynTestUtils.RunGenerator(
            new LoggingGenerator(),
            new[]
            {
                Assembly.GetAssembly(typeof(ILogger))!,
                Assembly.GetAssembly(typeof(LogPropertiesAttribute))!,
                Assembly.GetAssembly(typeof(LoggerMessageAttribute))!,
                Assembly.GetAssembly(typeof(DateTime))!,
            },
            [source],
            symbols);

        Assert.Empty(d);
        await Verifier.Verify(r[0].SourceText.ToString())
            .AddScrubber(_ => _.Replace(GeneratorUtilities.CurrentVersion, "VERSION"))
            .UseDirectory(Path.Combine("..", "Verified"));
    }
}
