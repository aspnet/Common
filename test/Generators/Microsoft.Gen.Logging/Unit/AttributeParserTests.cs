// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Logging;
using Microsoft.Gen.Logging.Parsing;
using Microsoft.Gen.Shared;
using VerifyXunit;
using Xunit;

namespace Microsoft.Gen.Logging.Test;

public class AttributeParserTests
{
    [Fact]
    public async Task RandomAttribute()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [Test] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LegacyAttribute()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
#pragma warning disable CS0618

                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [Test] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DataClassificationAttributeFullName()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [PrivateDataAttribute] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DataClassificationAttributeShortName()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [PrivateData] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public Task DataClassificationAttributeNamespaceIsGloballyQualified()
    {
        const string Source = @"
                using Microsoft.Extensions.Compliance.Classification;
                using Microsoft.Extensions.Logging;

                namespace Bar.Classifications
                {
                    public sealed class SecretAttribute : DataClassificationAttribute
                    {
                        public SecretAttribute()
                            : base(new DataClassification(""Taxonomy"", ""Secret""))
                        {
                        }
                    }
                }

                namespace Foo.Bar
                {
                    using global::Bar.Classifications;

                    internal static partial class C
                    {
                        [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                        static partial void M(ILogger logger, [Secret] string p0);
                    }
                }
            ";

        var generatedSource = RunGeneratorAndAssertNoErrors(Source);

        return Verifier.Verify(generatedSource)
            .AddScrubber(_ => _.Replace(GeneratorUtilities.CurrentVersion, "VERSION"))
            .UseDirectory(Path.Combine("..", "Verified"));
    }

    [Fact]
    public void DataClassificationAttributeNamespaceIsGloballyQualifiedForLogProperties()
    {
        const string Source = @"
                using Microsoft.Extensions.Compliance.Classification;
                using Microsoft.Extensions.Logging;

                namespace Bar.Classifications
                {
                    public sealed class SecretAttribute : DataClassificationAttribute
                    {
                        public SecretAttribute()
                            : base(new DataClassification(""Taxonomy"", ""Secret""))
                        {
                        }
                    }
                }

                namespace Foo.Bar
                {
                    using global::Bar.Classifications;

                    public class Payload
                    {
                        [Secret]
                        public string? Value { get; set; }
                    }

                    public record class Record([property: Secret] string Value);

                    internal static partial class C
                    {
                        [LoggerMessage(0, LogLevel.Debug, ""M"")]
                        static partial void M(ILogger logger, [LogProperties] Payload p0, [LogProperties] Record p1);
                    }
                }
            ";

        var generatedSource = RunGeneratorAndAssertNoErrors(Source);

        Assert.Contains("new global::Bar.Classifications.SecretAttribute().Classification", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleAttributesOnDifferentTopics()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [Test][PrivateData] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("[PrivateData] string")]
    public async Task RedactorProviderIsInTheInstance(string type)
    {
        var diagnostics = await RunGenerator(@$"
            internal partial class TestInstance
            {{
                private readonly ILogger _logger;
                private readonly IRedactorProvider _redactorProvider;

                public TestInstance(ILogger logger, IRedactorProvider redactorProvider)
                {{
                    _logger = logger;
                    _redactorProvider = redactorProvider;
                }}

                [LoggerMessage(0, LogLevel.Debug, ""M0 {{p0}}"")]
                public partial void M0({type} p0);
            }}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DataClassOnAllTypes()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [PrivateData] int p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MissingLogger()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M([PrivateData] string p0);
                }
            ");

        _ = Assert.Single(diagnostics);
        Assert.Equal(DiagDescriptors.MissingLoggerParameter.Id, diagnostics[0].Id);
    }

    [Fact]
    public async Task NotMissingDataClassificationAttributeInStatic()
    {
        var diagnostics = await RunGenerator(@"
                internal static partial class C
                {
                    [LoggerMessage(0, LogLevel.Debug, ""M {p0}"")]
                    static partial void M(ILogger logger, [PublicData] string p0);
                }
            ");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MethodNotStatic()
    {
        var diagnostics = await RunGenerator(@$"
            internal partial class C
            {{
                [LoggerMessage(0, LogLevel.Debug, ""M {{p0}}"")]
                partial void M(ILogger logger, [PrivateData] string p0);
            }}");

        _ = Assert.Single(diagnostics);
        Assert.Equal(DiagDescriptors.LoggingMethodShouldBeStatic.Id, diagnostics[0].Id);
    }

    [Fact]
    public async Task CantUseDataClassificationWithLogProperties()
    {
        const string Source = @"
            class MyClass
            {
                public string Property { get; set; }
            }

            partial class LoggerClass
            {
                [LoggerMessage(0, LogLevel.Debug, ""No params..."")]
                static partial void M(ILogger logger, [PrivateData][LogProperties] MyClass p1);
            }";

        var diagnostics = await RunGenerator(Source);

        _ = Assert.Single(diagnostics);

        Assert.Equal(DiagDescriptors.CantUseDataClassificationWithLogPropertiesOrTagProvider.Id, diagnostics[0].Id);
    }

    [Fact]
    public async Task CantUseDataClassificationWithTagProvider()
    {
        const string Source = @"
            class MyClass
            {
                public string Property { get; set; }
            }

            static class Provider
            {
                public static void Provide(ITagCollector props, MyClass? value)
                {
                    return;
                }
            }

            partial class C
            {
                [LoggerMessage(0, LogLevel.Debug, ""Parameter"")]
                static partial void M(ILogger logger, [PrivateData][TagProvider(typeof(Provider), nameof(Provider.Provide))] MyClass p1);
            }";

        var diagnostics = await RunGenerator(Source);

        _ = Assert.Single(diagnostics);

        Assert.Equal(DiagDescriptors.CantUseDataClassificationWithLogPropertiesOrTagProvider.Id, diagnostics[0].Id);
    }

    // Runs the generator over a full compilation so that the emitted code is verified to actually compile.
    private static string RunGeneratorAndAssertNoErrors(string source)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new LoggingGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            CompilationHelper.CreateCompilation(source),
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = Assert.Single(driver.GetRunResult().Results[0].GeneratedSources);
        return generatedSource.SourceText.ToString();
    }

    private static async Task<IReadOnlyList<Diagnostic>> RunGenerator(string code)
    {
        var text = $@"
                namespace Test {{
                using Microsoft.Extensions.Compliance.Classification;
                using Microsoft.Extensions.Compliance.Testing;
                using Microsoft.Extensions.Compliance.Redaction;
                using Microsoft.Extensions.Logging;
                {code}
                }}
            ";

        var loggerAssembly = Assembly.GetAssembly(typeof(ILogger));
        var loggerMessageAssembly = Assembly.GetAssembly(typeof(LoggerMessageAttribute));
        var enrichmentAssembly = Assembly.GetAssembly(typeof(IEnrichmentTagCollector));
        var dataClassificationAssembly = Assembly.GetAssembly(typeof(DataClassification));
        var simpleDataClassificationAssembly = Assembly.GetAssembly(typeof(PrivateDataAttribute));
        var redactorProviderAssembly = Assembly.GetAssembly(typeof(IRedactorProvider));
        var refs = new[] { loggerAssembly!, loggerMessageAssembly!, enrichmentAssembly!, dataClassificationAssembly!, simpleDataClassificationAssembly!, redactorProviderAssembly! };

        var (d, _) = await RoslynTestUtils.RunGenerator(
            new LoggingGenerator(),
            refs,
            new[] { text }).ConfigureAwait(false);

        return d;
    }
}
