// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Gen.Shared;
using Xunit;

namespace Microsoft.Gen.ComplianceReports.Tests;

public class GeneratorTests
{
    private const string TestTaxonomy = @"
        using Microsoft.Extensions.Compliance.Classification;

        public sealed class C1Attribute : DataClassificationAttribute
        {
            public C1Attribute(new DataClassification(""TAX"", 1)) { }
        }

        public sealed class C2Attribute : DataClassificationAttribute
        {
            public C2Attribute(new DataClassification(""TAX"", 2)) { }
        }

        public sealed class C3Attribute : DataClassificationAttribute
        {
            public C3Attribute(new DataClassification(""TAX"", 4)) { }
        }

        public sealed class C4Attribute : DataClassificationAttribute
        {
            public C4Attribute(new DataClassification(""TAX"", 8)) { }
        }
    ";

    private readonly ITestOutputHelper _output;

    public GeneratorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [CombinatorialData]
    public async Task TestAll(bool useExplicitReportPath)
    {
        Dictionary<string, string>? options = useExplicitReportPath
            ? new() { ["build_property.ComplianceReportOutputPath"] = Directory.GetCurrentDirectory() }
            : null;

        foreach (var inputFile in Directory.GetFiles("TestClasses"))
        {
            var stem = Path.GetFileNameWithoutExtension(inputFile);
            var goldenReportFile = $"GoldenReports/{stem}.json";

            if (File.Exists(goldenReportFile))
            {
                var tmp = Path.GetTempFileName();
                var d = await RunGenerator(File.ReadAllText(inputFile), tmp, options);
                Assert.Empty(d);

                var golden = File.ReadAllText(goldenReportFile);
                var generated = File.ReadAllText(tmp);

                if (golden != generated)
                {
                    _output.WriteLine($"MISMATCH: golden report {goldenReportFile}, generated {tmp}");
                    _output.WriteLine("----");
                    _output.WriteLine("golden:");
                    _output.WriteLine(golden);
                    _output.WriteLine("----");
                    _output.WriteLine("generated:");
                    _output.WriteLine(generated);
                    _output.WriteLine("----");
                }

                File.Delete(tmp);
                Assert.Equal(golden, generated);
            }
            else
            {
                // generate the golden file if it doesn't already exist
                _output.WriteLine($"Generating golden report: {goldenReportFile}");
                _ = await RunGenerator(File.ReadAllText(inputFile), goldenReportFile, options);
            }
        }
    }

    [Fact]
    public async Task MissingDataClassificationSymbol()
    {
        const string Source = "class Nothing {}";

        var (d, _) = await RoslynTestUtils.RunGenerator(
                new ComplianceReportsGenerator("Foo"),
                null,
                new[]
                {
                    Source,
                },
                new OptionsProvider(null))
;

        Assert.Empty(d);
    }

    [Theory]
    [CombinatorialData]
    public async Task Should_EmitWarning_WhenPathUnavailable(bool isReportPathProvided)
    {
        var inputFile = Directory.GetFiles("TestClasses").First();
        var options = new Dictionary<string, string>
        {
            ["build_property.outputpath"] = string.Empty
        };

        if (isReportPathProvided)
        {
            options.Add("build_property.ComplianceReportOutputPath", string.Empty);
        }

        var diags = await RunGenerator(await File.ReadAllTextAsync(inputFile), options: options);
        var diag = Assert.Single(diags);
        Assert.Equal("AUDREPGEN001", diag.Id);
        Assert.Equal(DiagnosticSeverity.Info, diag.Severity);
    }

    [Fact]
    public async Task Should_UseProjectDir_WhenOutputPathIsRelative()
    {
        var projectDir = Path.GetTempPath();
        var outputPath = Guid.NewGuid().ToString();
        var fullReportPath = Path.Combine(projectDir, outputPath);
        Directory.CreateDirectory(fullReportPath);

        try
        {
            var inputFile = Directory.GetFiles("TestClasses").First();
            var options = new Dictionary<string, string>
            {
                ["build_property.projectdir"] = projectDir,
                ["build_property.outputpath"] = outputPath
            };

            var diags = await RunGenerator(await File.ReadAllTextAsync(inputFile), options: options);
            Assert.Empty(diags);
            Assert.True(File.Exists(Path.Combine(fullReportPath, "ComplianceReport.json")));
        }
        finally
        {
            Directory.Delete(fullReportPath, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<Diagnostic>> RunGenerator(string code, string? outputFile = null, Dictionary<string, string>? options = null)
    {
        var (d, _) = await RoslynTestUtils.RunGenerator(
            new ComplianceReportsGenerator(outputFile),
            new[]
            {
                    Assembly.GetAssembly(typeof(ILogger))!,
                    Assembly.GetAssembly(typeof(LoggerMessageAttribute))!,
                    Assembly.GetAssembly(typeof(Extensions.Compliance.Classification.DataClassification))!,
            },
            new[]
            {
                    code,
                    TestTaxonomy,
            },
            new OptionsProvider(analyzerOptions: options)).ConfigureAwait(false);

        return d;
    }

    private sealed class Options : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _options;

        public Options(Dictionary<string, string>? analyzerOptions)
        {
            _options = analyzerOptions ?? [];
            _options.TryAdd("build_property.GenerateComplianceReport", bool.TrueString);
            _options.TryAdd("build_property.outputpath", Directory.GetCurrentDirectory());
        }

        public override bool TryGetValue(string key, out string value)
            => _options.TryGetValue(key, out value!);
    }

    private sealed class OptionsProvider(Dictionary<string, string>? analyzerOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new Options(analyzerOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => throw new System.NotSupportedException();
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => throw new System.NotSupportedException();
    }
}
