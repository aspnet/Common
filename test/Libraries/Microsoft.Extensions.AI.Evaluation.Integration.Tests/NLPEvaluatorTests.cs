// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI.Evaluation.NLP;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Integration.Tests;

[Experimental("AIEVAL001")]
public class NLPEvaluatorTests
{
    private static readonly ReportingConfiguration? _nlpReportingConfiguration;

    static NLPEvaluatorTests()
    {
        if (Settings.Current.Configured)
        {
            string version = $"Product Version: {Constants.Version}";
            string date = $"Date: {DateTime.UtcNow:dddd, dd MMMM yyyy}";
            string projectName = $"Project: Integration Tests";
            string testClass = $"Test Class: {nameof(NLPEvaluatorTests)}";
            string usesContext = $"Feature: Context";

            IEvaluator bleuEvaluator = new BLEUEvaluator();
            IEvaluator gleuEvaluator = new GLEUEvaluator();
            IEvaluator f1Evaluator = new F1Evaluator();

            _nlpReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [bleuEvaluator, gleuEvaluator, f1Evaluator],
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, usesContext]);
        }
    }

    [Fact]
    public async Task ExactMatch()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _nlpReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(NLPEvaluatorTests)}.{nameof(ExactMatch)}");

        var referenceText = "The quick brown fox jumps over the lazy dog.";
        var bleuContext = new BLEUEvaluatorContext(referenceText);
        var gleuContext = new GLEUEvaluatorContext(referenceText);
        var f1Context = new F1EvaluatorContext(referenceText);

        EvaluationResult result = await scenarioRun.EvaluateAsync(referenceText, [bleuContext, gleuContext, f1Context]);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(BLEUEvaluator.BLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(GLEUEvaluator.GLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(F1Evaluator.F1MetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task PartialMatch()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _nlpReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(NLPEvaluatorTests)}.{nameof(PartialMatch)}");

        var referenceText = "The quick brown fox jumps over the lazy dog.";
        var bleuContext = new BLEUEvaluatorContext(referenceText);
        var gleuContext = new GLEUEvaluatorContext(referenceText);
        var f1Context = new F1EvaluatorContext(referenceText);

        var similarText = "The brown fox quickly jumps over a lazy dog.";
        EvaluationResult result = await scenarioRun.EvaluateAsync(similarText, [bleuContext, gleuContext, f1Context]);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(BLEUEvaluator.BLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(GLEUEvaluator.GLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(F1Evaluator.F1MetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task Unmatched()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _nlpReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(NLPEvaluatorTests)}.{nameof(Unmatched)}");

        var referenceText = "The quick brown fox jumps over the lazy dog.";
        var bleuContext = new BLEUEvaluatorContext(referenceText);
        var gleuContext = new GLEUEvaluatorContext(referenceText);
        var f1Context = new F1EvaluatorContext(referenceText);

        EvaluationResult result = await scenarioRun.EvaluateAsync("What is life's meaning?", [bleuContext, gleuContext, f1Context]);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(BLEUEvaluator.BLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(GLEUEvaluator.GLEUMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(F1Evaluator.F1MetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task AdditionalContextIsNotPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _nlpReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(NLPEvaluatorTests)}.{nameof(AdditionalContextIsNotPassed)}");

        EvaluationResult result = await scenarioRun.EvaluateAsync("What is the meaning of life?");

        Assert.True(
            result.Metrics.Values.All(m => m.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error)),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(BLEUEvaluator.BLEUMetricName, out NumericMetric? bleu));
        Assert.True(result.TryGet(GLEUEvaluator.GLEUMetricName, out NumericMetric? gleu));
        Assert.True(result.TryGet(F1Evaluator.F1MetricName, out NumericMetric? f1));

        Assert.Null(bleu.Context);
        Assert.Null(gleu.Context);
        Assert.Null(f1.Context);

    }

    [MemberNotNull(nameof(_nlpReportingConfiguration))]
    private static void SkipIfNotConfigured()
    {
        Assert.SkipUnless(Settings.Current.Configured, "Test is not configured");

        Assert.NotNull(_nlpReportingConfiguration);
    }
}
