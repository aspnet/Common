// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Evaluation;
using Evaluation::Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Tests;

public class EvaluationMetricExtensionsTests
{
    [Fact]
    public void MetricWithNoScoreIsFailed()
    {
        var metric = new NumericMetric("test");

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.Equal(EvaluationRating.Inconclusive, interpretation.Rating);
        Assert.True(interpretation.Failed);
    }

    [Theory]
    [InlineData(5.1)]
    [InlineData(7.0)]
    public void ScoreAboveTheMaximumIsFailed(double value)
    {
        var metric = new NumericMetric("test", value);

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.Equal(EvaluationRating.Inconclusive, interpretation.Rating);
        Assert.True(interpretation.Failed);
    }

    [Fact]
    public void ScoreThatIsNotANumberIsFailed()
    {
        var metric = new NumericMetric("test", double.NaN);

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.Equal(EvaluationRating.Inconclusive, interpretation.Rating);
        Assert.True(interpretation.Failed);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(3.9)]
    public void ScoreBelowTheMinimumPassingScoreIsFailed(double value)
    {
        var metric = new NumericMetric("test", value);

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.True(interpretation.Failed);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(4.5)]
    [InlineData(5.0)]
    public void ScoreAtOrAboveTheMinimumPassingScoreIsNotFailed(double value)
    {
        var metric = new NumericMetric("test", value);

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.False(interpretation.Failed);
    }

    [Fact]
    public void BooleanMetricWithNoValueIsFailed()
    {
        var metric = new BooleanMetric("test");

        EvaluationMetricInterpretation interpretation = metric.InterpretScore();

        Assert.Equal(EvaluationRating.Inconclusive, interpretation.Rating);
        Assert.True(interpretation.Failed);
    }
}
