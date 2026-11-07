// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.AI.Evaluation.Quality;

internal static class EvaluationMetricExtensions
{
    internal static EvaluationMetricInterpretation InterpretScore(this NumericMetric metric)
    {
        EvaluationRating rating = metric.Value switch
        {
            null => EvaluationRating.Inconclusive,
            > 5.0 => EvaluationRating.Inconclusive,
            > 4.0 and <= 5.0 => EvaluationRating.Exceptional,
            > 3.0 and <= 4.0 => EvaluationRating.Good,
            > 2.0 and <= 3.0 => EvaluationRating.Average,
            > 1.0 and <= 2.0 => EvaluationRating.Poor,
            > 0.0 and <= 1.0 => EvaluationRating.Unacceptable,
            <= 0.0 => EvaluationRating.Inconclusive,
            _ => EvaluationRating.Inconclusive,
        };

        const double MinimumPassingScore = 4.0;
        return metric.Value switch
        {
            null =>
                new EvaluationMetricInterpretation(
                    rating,
                    failed: true,
                    reason: $"{metric.Name} has no score."),
            double value when value < MinimumPassingScore =>
                new EvaluationMetricInterpretation(
                    rating,
                    failed: true,
                    reason: $"{metric.Name} is less than {MinimumPassingScore}."),
            _ when rating is EvaluationRating.Inconclusive =>
                new EvaluationMetricInterpretation(
                    rating,
                    failed: true,
                    reason: $"{metric.Name} is outside the valid range."),
            _ => new EvaluationMetricInterpretation(rating),
        };
    }

    internal static EvaluationMetricInterpretation InterpretScore(
        this BooleanMetric metric,
        bool passValue = true)
    {
        EvaluationRating rating = metric.Value switch
        {
            null => EvaluationRating.Inconclusive,
            true => passValue ? EvaluationRating.Exceptional : EvaluationRating.Unacceptable,
            false => passValue ? EvaluationRating.Unacceptable : EvaluationRating.Exceptional,
        };

        return metric.Value is bool value && value == passValue
            ? new EvaluationMetricInterpretation(rating)
            : new EvaluationMetricInterpretation(
                rating,
                failed: true,
                reason: $"{metric.Name} is not {passValue}.");
    }

    internal static bool TryParseEvaluationResponseWithValue<T>(
        this EvaluationMetric<T> metric,
        ChatResponse evaluationResponse,
        TimeSpan evaluationDuration)
    {
        metric.AddOrUpdateChatMetadata(evaluationResponse, evaluationDuration);

        string evaluationResponseText = evaluationResponse.Text.Trim();
        return metric.TryParseValue(valueText: evaluationResponseText);
    }

    internal static bool TryParseEvaluationResponseWithTags<T>(
        this EvaluationMetric<T> metric,
        ChatResponse evaluationResponse,
        TimeSpan evaluationDuration)
    {
        metric.AddOrUpdateChatMetadata(evaluationResponse, evaluationDuration);

        // Format: <S0>chain of thought</S0>, <S1>reason</S1>, <S2>value</S2>
        string evaluationResponseText = evaluationResponse.Text.Trim();

        if (TryParseTag(evaluationResponseText, tagName: "S0", out string? chainOfThought))
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Informational(
                $"Model's evaluation chain of thought: {chainOfThought}"));
        }

        if (TryParseTag(evaluationResponseText, tagName: "S1", out string? reason))
        {
            metric.Reason = reason;
        }

        if (!TryParseTag(evaluationResponseText, tagName: "S2", out string? valueText))
        {
            metric.AddDiagnostics(
                EvaluationDiagnostic.Error(
                    $"""
                    Failed to parse score for '{metric.Name}' from the following evaluation response:
                    {evaluationResponseText}
                    """));

            return false;
        }

        return metric.TryParseValue(valueText);

        static bool TryParseTag(string text, string tagName, [NotNullWhen(true)] out string? tagValue)
        {
            const RegexOptions Options = RegexOptions.Singleline;
            Match match = Regex.Match(text, $@"<{tagName}>(?<value>.*?)</{tagName}>", Options);

            if (!match.Success || match.Groups["value"] is not Group valueGroup || !valueGroup.Success)
            {
                tagValue = null;
                return false;
            }

            if (valueGroup.Value is not string matchText ||
                matchText.Trim() is not string trimmedMatchText ||
                string.IsNullOrEmpty(trimmedMatchText))
            {
                tagValue = null;
                return false;
            }

            tagValue = trimmedMatchText;
            return true;
        }
    }

    private static bool TryParseValue<T>(this EvaluationMetric<T> metric, string valueText)
    {
        switch (metric)
        {
            case NumericMetric numericMetric:
                if (double.TryParse(valueText, out double doubleValue))
                {
                    numericMetric.Value = doubleValue;
                    return true;
                }
                else
                {
                    metric.AddDiagnostics(
                        EvaluationDiagnostic.Error(
                            $"""
                            Failed to parse numeric score for '{metric.Name}' from the following text:
                            {valueText}
                            """));

                    return false;
                }

            case BooleanMetric booleanMetric:
                if (bool.TryParse(valueText, out bool booleanValue))
                {
                    booleanMetric.Value = booleanValue;
                    return true;
                }
                else if (int.TryParse(valueText, out int intValue) && (intValue is 0 or 1))
                {
                    booleanMetric.Value = intValue is 1;
                    return true;
                }
                else
                {
                    metric.AddDiagnostics(
                        EvaluationDiagnostic.Error(
                            $"""
                            Failed to parse boolean score for '{metric.Name}' from the following text:
                            {valueText}
                            """));

                    return false;
                }

            default:
                throw new NotSupportedException($"{metric.GetType().Name} is not supported.");
        }
    }
}
