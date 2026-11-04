// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Gen.Logging.Parsing;
using Xunit;

namespace Microsoft.Gen.Logging.Test;

public partial class ParserTests
{
    [Fact]
    public async Task InvalidMethodBody()
    {
        const string Source = @"
                partial class C
                {
                    static partial void M1(ILogger logger);

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    static partial void M1(ILogger logger)
                    /*0+*/{
                    }/*-0*/
                }
            ";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodHasBody);
    }

    [Fact]
    public async Task NullableLogger()
    {
        await RunGenerator(@"
            namespace TestClasses
            {
                internal static partial class NullableTestExtensions
                {
                    [LoggerMessage(6, LogLevel.Debug, ""M6 {p0}"")]
                    internal static partial void M6(ILogger? logger, string p0);
                }
            }");
    }

    [Fact]
    public async Task MissingILogger()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1 {p1}"")]
                    static partial void M1/*0+*/(int p1)/*-0*/;
                }
            ";

        await RunGenerator(Source, DiagDescriptors.MissingLoggerParameter);
    }

    [Fact]
    public async Task NoILoggerField()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void /*0+*/M1/*-0*/();
                }
            ";

        await RunGenerator(Source, DiagDescriptors.MissingLoggerMember);
    }

    [Fact]
    public async Task ValidDerivation()
    {
        const string Source = @"
                partial class C
                {
                    public ILogger? Logger { get; set; }

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }

                partial class D : C
                {
                    private readonly ILogger<D> logger;

                    public D(ILogger<D> logger)
                    {
                        this.logger = logger;
                    }

                    [LoggerMessage(0, LogLevel.Information, ""M2"")]
                    private partial void M2();
                }

                partial class E(ILogger<E> logger)
                {
                    protected readonly ILogger<E> _logger = logger;

                    [LoggerMessage(Level = LogLevel.Information, Message = ""M1"")]
                    private partial void M1();
                }

                partial class F(ILogger<F> logger) : E(logger)
                {
                    [LoggerMessage(Level = LogLevel.Information, Message = ""M2"")]
                    private partial void M2();
                }
        ";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task MultipleILoggerMembers()
    {
        const string Source = @"
                partial class C
                {
                    public ILogger _logger1;
                    public ILogger /*0+*/_logger2/*-0*/;

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }

                partial class D
                {
                    public ILogger? Logger { get; set; } = null!;
                    public ILogger /*1+*/_logger2/*-1*/;

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }

                partial class E
                {
                    public ILogger<int> Logger { get; set; } = null!;
                    public ILogger /*2+*/_logger2/*-2*/;

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }

                partial class F(ILogger logger, ILogger /*3+*/logger2/*-3*/)
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }
            ";

        await RunGenerator(Source, DiagDescriptors.MultipleLoggerMembers);
    }

    [Fact]
    public async Task PrimaryConstructorParameterLoggerHidden()
    {
        const string Source = @"
                partial class C(ILogger /*0+*/logger/*-0*/)
                {
                    public object logger;

                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public partial void M1();
                }
            ";

        await RunGenerator(Source, DiagDescriptors.PrimaryConstructorParameterLoggerHidden,
            ignoreDiag: DiagDescriptors.MissingLoggerMember);
    }

    [Fact]
    public async Task MethodReturnType()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    public static partial /*0+*/int/*-0*/ M1(ILogger logger);

                    public static partial int M1(ILogger logger) { return 0; }
                }
            ";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodMustReturnVoid);
    }

    [Fact]
    public async Task NotStatic()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    partial void /*0+*/M1/*-0*/(ILogger logger);
                }
            ";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodShouldBeStatic);
    }

    [Fact]
    public async Task InstanceEmptyLoggingMethod()
    {
        const string Source = @"
            partial class C
            {
                public ILogger _logger;

                [LoggerMessage]
                public partial void /*0+*/M1/*-0*/(LogLevel level);

                [LoggerMessage(LogLevel.Debug)]
                public partial void /*1+*/M2/*-1*/();
            }";

        await RunGenerator(Source, DiagDescriptors.EmptyLoggingMethod);
    }

    [Fact]
    public async Task StaticEmptyLoggingMethod()
    {
        const string Source = @"
            partial class C
            {
                [LoggerMessage]
                public static partial void /*0+*/M1/*-0*/(ILogger logger, LogLevel level);

                [LoggerMessage(LogLevel.Debug)]
                public static partial void /*1+*/M2/*-1*/(ILogger logger);
            }";

        await RunGenerator(Source, DiagDescriptors.EmptyLoggingMethod);
    }

    [Fact]
    public async Task NonEmptyLoggingMethod()
    {
        await RunGenerator(@"
                partial class C
                {
                    public ILogger _logger;

                    [LoggerMessage]
                    public partial void M1(LogLevel level, Exception ex);

                    [LoggerMessage(LogLevel.Debug)]
                    public partial void M2(Exception ex);

                    [LoggerMessage]
                    public static partial void M3(ILogger logger, LogLevel level, Exception ex);

                    [LoggerMessage(LogLevel.Debug)]
                    public static partial void M4(ILogger logger, Exception ex);
                }");
    }

    [Fact]
    public async Task NotPartial()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    static void /*0+*/M1/*-0*/(ILogger logger);
                }
            ";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodMustBePartial);
    }

    [Fact]
    public async Task MethodGeneric()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1 {value}"")]
                    static partial void M1<T>(ILogger logger, T value);
                }
            ";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task MethodGenericWithConstraints()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1 {code}"")]
                    static partial void M1<TCode>(ILogger logger, TCode code)
                        where TCode : struct, System.Enum;
                }
            ";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task MethodGenericMultipleTypeParams()
    {
        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1 {p1} {p2}"")]
                    static partial void M1<T1, T2>(ILogger logger, T1 p1, T2 p2)
                        where T1 : class
                        where T2 : notnull;
                }
            ";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task MethodGenericWithAllowsRefStructConstraint()
    {
        // The 'allows ref struct' detection requires Roslyn 4.9+ (ITypeParameterSymbol.AllowsRefLikeType).
        // Skip gracefully on older Roslyn versions where the property and syntax are unavailable.
        if (typeof(ITypeParameterSymbol).GetProperty("AllowsRefLikeType") is null)
        {
            return;
        }

        const string Source = @"
                partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M1"")]
                    static partial void /*0+*/M1/*-0*/<T>(ILogger logger) where T : allows ref struct;
                }
            ";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodHasAllowsRefStructConstraint);
    }

    [Theory]
    [CombinatorialData]
    public async Task LogMethodParamsRefKind([CombinatorialValues("ref", "out")] string modifier)
    {
        string source = @$"
            partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""Parameter {{P1}}"")]
                static partial void M(ILogger logger, {modifier} int /*0+*/p1/*-0*/);
            }}";

        await RunGenerator(source, DiagDescriptors.LoggingMethodParameterRefKind);
    }

    [Fact]
    public async Task LogMethodParamsModifier()
    {
        const string Source = @"
            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""Parameter"")]
                static partial void M(ILogger logger, params int[] /*0+*/values/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodParameterParams);
    }

    [Fact]
    public async Task LogMethodRefReadOnlyModifier()
    {
        const string Source = @"
            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""Parameter {p1}"")]
                static partial void M(ILogger logger, ref readonly int p1);
            }";

        await RunGenerator(Source);
    }

    [Fact]
    public async Task LogMethodScopedModifier()
    {
        const string Source = @"
            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""Parameter {p1}"")]
                static partial void M(ILogger logger, scoped ref readonly int /*0+*/p1/*-0*/);
            }";

        await RunGenerator(Source, DiagDescriptors.LoggingMethodParameterParams);
    }

    [Theory]
    [CombinatorialData]
    public async Task LogMethod_DetectsSensitiveMembersInRecord([CombinatorialRange(0, TotalSensitiveCases)] int positionNumber)
    {
        var sb = new System.Text.StringBuilder(@"
            using Microsoft.Extensions.Compliance.Testing;
            using System.Collections;

            {17}
            public record class BaseRecord
            {
                {0}public string PropBase { get; }
                {1}public string FieldBase;
            }

            {18}
            public record struct InlineStruct({2}string InlineProp);

            {19}
            public record class InterimRecord : BaseRecord
            {
                {3}public string PropInterim { get; }
                {4}public string FieldInterim;

                // Hiding on purpose:
                {5}public new string FieldBase;
                public virtual string PropVirt => nameof(PropVirt);
            }

            {20}
            // 'internal' on purpose
            internal record struct EnumerableStructRecord : IEnumerable
            {
                {6}public string EnumProp => nameof(EnumProp);
                public IEnumerator GetEnumerator() => null!;
            }

            {16}
            record MyRecord : InterimRecord
            {
                {7}public string Field;
                {8}public string PropGet { get; }
                {9}public string PropGetSet { get; set; }
                {10}public string PropPrivateGet { private get; set; }
                {11}public string PropInternalGet { internal get; set; }
                {12}public string PropProtectedGet { protected get; set; }
                {13}public override string PropVirt => nameof(MyRecord);
                {14}public string PropGetInit { get; init; }
                public EnumerableStructRecord PropRecord { get; } = new();
                {15}public InlineStruct FieldRecord = new(nameof(FieldRecord));
            }

            partial class C
            {
                [LoggerMessage(LogLevel.Debug, ""Param is {p0}"")]
                public static partial void LogTemplate(ILogger logger, IRedactorProvider provider, MyRecord /*0+*/p0/*-0*/);

                [LoggerMessage(LogLevel.Debug, ""No param here"")]
                public static partial void LogStructured(ILogger logger, MyRecord /*1+*/p0/*-1*/);

                [LoggerMessage(LogLevel.Debug)]
                public static partial void LogFullyStructured(ILogger logger, MyRecord /*2+*/p0/*-2*/);

                [LoggerMessage(LogLevel.Debug, ""Param is {p0}"")]
                public static partial void LogProperties(ILogger logger, IRedactorProvider rp, [LogProperties] MyRecord /*3+*/p0/*-3*/);
            }");

        for (int i = 0; i < TotalSensitiveCases; i++)
        {
            var template = "{" + i.ToString() + "}";
            var replacement = i switch
            {
                _ when positionNumber == i
                        => "[PrivateData] ",
                _ => string.Empty,
            };

            sb.Replace(template, replacement);
        }

        await RunGenerator(
            sb.ToString(),
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate,
            ignoreDiag: DiagDescriptors.ParameterHasNoCorrespondingTemplate);
    }

    [Theory]
    [InlineData("System.Nullable<int>")]
    [InlineData("int?")]
    public async Task LogMethod_DetectsSensitiveNullableMembersInRecord(string type)
    {
        await RunGenerator(@$"
            using Microsoft.Extensions.Compliance.Testing;

            record MyRecord
            {{
                [PrivateData]
                public {type} Field;
            }}

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug, ""Param is {{p0}}"")]
                public static partial void LogTemplate(ILogger logger, MyRecord /*0+*/p0/*-0*/);
            }}",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }

    [Theory]
    [InlineData("[PrivateData]")]
    [InlineData("[property: PrivateData]")]
    public async Task LogMethod_DetectsSensitiveMembersInRecord_WithPrimaryCtor(string annotation)
    {
        await RunGenerator(@$"
            using Microsoft.Extensions.Compliance.Testing;

            record MyRecord({annotation} string userIp);

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug, ""Param is {{p0}}"")]
                public static partial void LogTemplate(ILogger logger, MyRecord /*0+*/p0/*-0*/);
            }}",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }

    [Fact]
    public async Task LogMethod_SkipsNonSensitiveMembersInRecord()
    {
        await RunGenerator(@"
            using Microsoft.Extensions.Compliance.Testing;

            public record class BaseRecord
            {
                [PrivateData] public const int ConstFieldBase = 99;
                [PrivateData] public static int StaticFieldBase;
                [PrivateData] public static int StaticPropBase => 100;
                [PrivateData] private int PrivateFieldBase;
                [PrivateData] internal int InternalFieldBase;
            }

            public record class InterimRecord : BaseRecord
            {
                [PrivateData] public virtual decimal PropVirt => decimal.MinusOne;
            }

            internal record class MyRecord : InterimRecord
            {
                [PrivateData] public const int ConstField = 99;
                [PrivateData] public static int StaticField;
                [PrivateData] public static int StaticProp => 100;
                [PrivateData] private int PrivateField;
                [PrivateData] internal int InternalField;
                [PrivateData] private string PrivatePropGetSet { get; set; }
                [PrivateData] internal string InternalPropGetSet { get; set; }
                [PrivateData] protected string ProtectedPropGetSet { get; set; }

                // This one overrides 'virtual' property declared in 'InterimRecord':
                public override decimal PropVirt => decimal.One;
            }

            partial class C
            {
                [LoggerMessage(LogLevel.Debug, ""Param is {p0}"")]
                public static partial void LogFunc(ILogger logger, IRedactorProvider provider, MyRecord /*0+*/p0/*-0*/);
            }");
    }

    [Theory]
    [InlineData("record Inner(int Value); record MyRecord(Inner First, MyRecord Self);")]
    [InlineData("record Node { public Leaf Next { get; init; } } record Leaf : Node { } record MyRecord(Leaf First);")]
    public async Task LogMethod_SkipsTypeCyclesInRecord(string records)
    {
        await RunGenerator(@$"
            {records}

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug, ""Param is {{p0}}"")]
                public static partial void LogTemplate(ILogger logger, MyRecord p0);
            }}");
    }

    [Fact]
    public async Task LogMethod_DetectsSensitiveMembersInRecordWithTypeCycle()
    {
        await RunGenerator(@"
            using Microsoft.Extensions.Compliance.Testing;

            record Inner(int Value);

            record MyRecord
            {
                public Inner First { get; init; }
                public MyRecord Self { get; init; }
                [PrivateData] public string Secret { get; init; }
            }

            partial class C
            {
                [LoggerMessage(LogLevel.Debug, ""Param is {p0}"")]
                public static partial void LogTemplate(ILogger logger, MyRecord /*0+*/p0/*-0*/);
            }",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }

    [Theory]
    [InlineData("record MyRecord { [PrivateData] public MyRecord Self { get; init; } }")]
    [InlineData("record Child { [PrivateData] public Child Next { get; init; } } record MyRecord(Child First);")]
    public async Task LogMethod_DetectsSensitiveSelfReferencingMemberInRecord(string records)
    {
        await RunGenerator(@$"
            using Microsoft.Extensions.Compliance.Testing;

            {records}

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug, ""Param is {{p0}}"")]
                public static partial void LogTemplate(ILogger logger, MyRecord /*0+*/p0/*-0*/);
            }}",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }

    [Fact]
    public async Task LogMethod_DetectsSensitiveMembersNestedBeyondTraversalDepthInRecord()
    {
        var records = string.Join(" ", Enumerable.Range(1, 39).Select(i => $"record R{i}(R{i + 1} Next);"));

        await RunGenerator(@$"
            using Microsoft.Extensions.Compliance.Testing;

            {records}
            record R40([PrivateData] string Secret);

            partial class C
            {{
                [LoggerMessage(LogLevel.Debug, ""Param is {{p0}}"")]
                public static partial void LogTemplate(ILogger logger, R1 /*0+*/p0/*-0*/);
            }}",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }

    [Fact]
    public async Task LogMethod_ReportsUnboundedGenericRecord()
    {
        await RunGenerator(@"
            record Node<T>(Node<Node<T>> Next);

            partial class C
            {
                [LoggerMessage(LogLevel.Debug, ""Param is {p0}"")]
                public static partial void LogTemplate(ILogger logger, Node<int> /*0+*/p0/*-0*/);
            }",
            DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate);
    }
}
