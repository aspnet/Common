// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable AIEVAL001
// AIEVAL001: Some of the below APIs are experimental and subject to change or removal in future updates.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.NLP.Tests;

public class GLEUEvaluatorTests
{
    [Fact]
    public async Task ReturnsPerfectScoreForIdenticalText()
    {
        var evaluator = new GLEUEvaluator();
        var context = new GLEUEvaluatorContext("The quick brown fox jumps over the lazy dog.");
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "The quick brown fox jumps over the lazy dog."));
        var result = await evaluator.EvaluateAsync(response, null, [context]);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.Equal(1.0, (double)metric!.Value!, 4);
        Assert.NotNull(metric.Interpretation);
        Assert.Equal(EvaluationRating.Exceptional, metric.Interpretation.Rating);
        Assert.False(metric.Interpretation.Failed);
    }

    [Fact]
    public async Task ReturnsLowScoreForCompletelyDifferentText()
    {
        var evaluator = new GLEUEvaluator();
        var context = new GLEUEvaluatorContext("The quick brown fox jumps over the lazy dog.");
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Completely unrelated sentence."));
        var result = await evaluator.EvaluateAsync(response, null, [context]);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.Equal(0.02939, (double)metric!.Value!, 4);
        Assert.NotNull(metric.Interpretation);
        Assert.Equal(EvaluationRating.Unacceptable, metric.Interpretation.Rating);
        Assert.True(metric.Interpretation.Failed);
    }

    [Fact]
    public async Task ReturnsErrorDiagnosticIfNoContext()
    {
        var evaluator = new GLEUEvaluator();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Some text."));
        var result = await evaluator.EvaluateAsync(response, null, null);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.NotNull(metric.Diagnostics);
        Assert.Contains(metric.Diagnostics, d => d.Severity == EvaluationDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("the cat is on the mat",
        "the the the the the the the", 0.0909)]
    [InlineData("It is a guide to action that ensures that the military will forever heed Party commands",
        "It is a guide to action which ensures that the military always obeys the commands of the party", 0.4545)]
    [InlineData("It is the practical guide for the army always to heed the directions of the party",
        "It is to insure the troops forever hearing the activity guidebook that party direct", 0.12069)]
    public async Task SampleCases(string reference, string hypothesis, double score)
    {
        var evaluator = new GLEUEvaluator();
        var context = new GLEUEvaluatorContext(reference);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, hypothesis));
        var result = await evaluator.EvaluateAsync(response, null, [context]);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.Equal(score, (double)metric!.Value!, 4);
    }

    [Fact]
    public async Task MultipleReferences()
    {
        string[] references = [
            "It is a guide to action that ensures that the military will forever heed Party commands",
            "It is the guiding principle which guarantees the military forces always being under the command of the Party",
            "It is the practical guide for the army always to heed the directions of the party",
        ];
        string hypothesis = "It is a guide to action which ensures that the military always obeys the commands of the party";

        var evaluator = new GLEUEvaluator();
        var context = new GLEUEvaluatorContext(references);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, hypothesis));
        var result = await evaluator.EvaluateAsync(response, null, [context]);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.Equal(0.45454, (double)metric!.Value!, 4);
    }

    [Fact]
    public async Task ReturnsErrorDiagnosticIfEmptyResponse()
    {
        var evaluator = new GLEUEvaluator();
        var context = new GLEUEvaluatorContext("Reference text.");
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        var result = await evaluator.EvaluateAsync(response, null, [context]);
        var metric = Assert.Single(result.Metrics.Values) as NumericMetric;
        Assert.NotNull(metric);
        Assert.Equal(GLEUEvaluator.GLEUMetricName, metric.Name);
        Assert.NotNull(metric.Diagnostics);
        Assert.Contains(metric.Diagnostics, d => d.Severity == EvaluationDiagnosticSeverity.Error);
    }
}
