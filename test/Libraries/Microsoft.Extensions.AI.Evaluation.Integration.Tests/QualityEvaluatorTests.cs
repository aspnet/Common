// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.AI.Evaluation.Tests;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Integration.Tests;

[Experimental("AIEVAL001")]
public class QualityEvaluatorTests
{
    private static readonly ChatOptions? _chatOptions;
    private static readonly ReportingConfiguration? _qualityReportingConfiguration;
    private static readonly ReportingConfiguration? _needsContextReportingConfiguration;

    static QualityEvaluatorTests()
    {
        if (Settings.Current.Configured)
        {
            _chatOptions =
                new ChatOptions
                {
                    Temperature = 0.0f,
                    ResponseFormat = ChatResponseFormat.Text
                };

            ChatConfiguration chatConfiguration = Setup.CreateChatConfiguration();
            ChatClientMetadata? clientMetadata = chatConfiguration.ChatClient.GetService<ChatClientMetadata>();

            string version = $"Product Version: {Constants.Version}";
            string date = $"Date: {DateTime.UtcNow:dddd, dd MMMM yyyy}";
            string projectName = $"Project: Integration Tests";
            string testClass = $"Test Class: {nameof(QualityEvaluatorTests)}";
            string model = $"Model: {clientMetadata?.DefaultModelId ?? "Unknown"}";
            string provider = $"Model Provider: {clientMetadata?.ProviderName ?? "Unknown"}";
            string temperature = $"Temperature: {_chatOptions.Temperature}";
            string usesContext = $"Feature: Context";

            IEvaluator rtcEvaluator = new RelevanceTruthAndCompletenessEvaluator();

            IEvaluator coherenceEvaluator = new CoherenceEvaluator();
            IEvaluator fluencyEvaluator = new FluencyEvaluator();
            IEvaluator relevanceEvaluator = new RelevanceEvaluator();

            _qualityReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [rtcEvaluator, coherenceEvaluator, fluencyEvaluator, relevanceEvaluator],
                    chatConfiguration: chatConfiguration,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, model, provider, temperature]);

            IEvaluator groundednessEvaluator = new GroundednessEvaluator();
            IEvaluator equivalenceEvaluator = new EquivalenceEvaluator();
            IEvaluator completenessEvaluator = new CompletenessEvaluator();
            IEvaluator retrievalEvaluator = new RetrievalEvaluator();

            _needsContextReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [groundednessEvaluator, equivalenceEvaluator, completenessEvaluator, retrievalEvaluator],
                    chatConfiguration: chatConfiguration,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, model, provider, temperature, usesContext]);
        }
    }

    [Fact]
    public async Task SampleSingleResponse()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _qualityReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(QualityEvaluatorTests)}.{nameof(SampleSingleResponse)}");

        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;

        var messages = new List<ChatMessage>();

        string prompt = "How far in miles is the moon from the earth at its closest and furthest points?";
        messages.Add(prompt.ToUserMessage());

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions);

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(6, result.Metrics.Count);
        Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.RelevanceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.TruthMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.CompletenessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(CoherenceEvaluator.CoherenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(FluencyEvaluator.FluencyMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(RelevanceEvaluator.RelevanceMetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task SampleMultipleResponses()
    {
        SkipIfNotConfigured();

#if NET
        await Parallel.ForAsync(1, 6, async (i, cancellationToken) =>
#else
        for (int i = 1; i < 6; i++)
#endif
        {
            await using ScenarioRun scenarioRun =
                await _qualityReportingConfiguration.CreateScenarioRunAsync(
                    scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(QualityEvaluatorTests)}.{nameof(SampleMultipleResponses)}",
                    iterationName: i.ToString(),
                    cancellationToken: cancellationToken);

            IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;

            var messages = new List<ChatMessage>();
            string prompt = @"How far in miles is the planet Venus from the Earth at its closest and furthest points?";
            messages.Add(prompt.ToUserMessage());

            ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions, cancellationToken);

            EvaluationResult result =
                await scenarioRun.EvaluateAsync(messages, response, cancellationToken: cancellationToken);

            Assert.False(
                result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
                string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

            Assert.Equal(6, result.Metrics.Count);
            Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.RelevanceMetricName, out NumericMetric? _));
            Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.TruthMetricName, out NumericMetric? _));
            Assert.True(result.TryGet(RelevanceTruthAndCompletenessEvaluator.CompletenessMetricName, out NumericMetric? _));
            Assert.True(result.TryGet(CoherenceEvaluator.CoherenceMetricName, out NumericMetric? _));
            Assert.True(result.TryGet(FluencyEvaluator.FluencyMetricName, out NumericMetric? _));
            Assert.True(result.TryGet(RelevanceEvaluator.RelevanceMetricName, out NumericMetric? _));
#if NET
        });
#else
        }
#endif
    }

    [Fact]
    public async Task AdditionalContextIsNotPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _needsContextReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(QualityEvaluatorTests)}.{nameof(AdditionalContextIsNotPassed)}");

        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;

        var messages = new List<ChatMessage>();
        string prompt = @"How far in miles is the planet Venus from the Earth at its closest and furthest points?";
        messages.Add(prompt.ToUserMessage());

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions);

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.True(
            result.Metrics.Values.All(m => m.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error)),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(4, result.Metrics.Count);
        Assert.True(result.TryGet(GroundednessEvaluator.GroundednessMetricName, out NumericMetric? groundedness));
        Assert.True(result.TryGet(EquivalenceEvaluator.EquivalenceMetricName, out NumericMetric? equivalence));
        Assert.True(result.TryGet(CompletenessEvaluator.CompletenessMetricName, out NumericMetric? completeness));
        Assert.True(result.TryGet(RetrievalEvaluator.RetrievalMetricName, out NumericMetric? retrieval));

        Assert.Null(groundedness.Context);
        Assert.Null(equivalence.Context);
        Assert.Null(completeness.Context);
        Assert.Null(retrieval.Context);
    }

    [Fact]
    public async Task AdditionalContextIsPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _needsContextReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(QualityEvaluatorTests)}.{nameof(AdditionalContextIsPassed)}");

        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;

        var messages = new List<ChatMessage>();
        string prompt = @"How far in miles is the planet Venus from the Earth at its closest and furthest points?";
        messages.Add(prompt.ToUserMessage());

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions);

        var baselineResponseForEquivalenceEvaluator =
            new EquivalenceEvaluatorContext(
                """
                The distance between Earth and Venus varies significantly due to the elliptical orbits of both planets
                around the Sun. At their closest approach, known as inferior conjunction, Venus can be about 24.8
                million miles away from Earth. At their furthest point, when Venus is on the opposite side of the Sun
                from Earth, known as superior conjunction, the distance can be about 162 million miles. These distances
                can vary slightly due to the specific orbital positions of the planets at any given time.
                """);

        var groundingContextForGroundednessEvaluator =
            new GroundednessEvaluatorContext(
                """
                Distance between Venus and Earth at inferior conjunction: About 24.8 million miles.
                Distance between Venus and Earth at superior conjunction: About 162 million miles.
                """);

        var groundTruthForCompletenessEvaluator =
            new CompletenessEvaluatorContext(
                """
                At their closest approach, known as inferior conjunction, Venus can be about 24.8
                million miles away from Earth. At their furthest point, when Venus is on the opposite side of the Sun
                from Earth, known as superior conjunction, the distance can be about 162 million miles. These distances
                can vary slightly due to the specific orbital positions of the planets at any given time.
                """);

        var retrievedContextChunksForRetrievalEvaluator =
            new RetrievalEvaluatorContext(
                "Distance between Venus and Earth at inferior conjunction: About 24.8 million miles.",
                "Distance between Venus and Earth at superior conjunction: About 162 million miles.",
                "Venus and earth are planets in our solar system.",
                "The orbits of most planets in the solar system are elliptical in nature and different planets may have different orbital planes.",
                "Venus and earth both orbit the Sun.",
                "Closest approach between planets is known as inferior conjunction. The planets are farthest apart at what is known as the superior conjunction.");

        EvaluationResult result =
            await scenarioRun.EvaluateAsync(
                messages,
                response,
                additionalContext: [
                    baselineResponseForEquivalenceEvaluator,
                    groundingContextForGroundednessEvaluator,
                    groundTruthForCompletenessEvaluator,
                    retrievedContextChunksForRetrievalEvaluator]);

        Assert.Equal(4, result.Metrics.Count);
        Assert.True(result.TryGet(GroundednessEvaluator.GroundednessMetricName, out NumericMetric? groundedness));
        Assert.True(result.TryGet(EquivalenceEvaluator.EquivalenceMetricName, out NumericMetric? equivalence));
        Assert.True(result.TryGet(CompletenessEvaluator.CompletenessMetricName, out NumericMetric? completeness));
        Assert.True(result.TryGet(RetrievalEvaluator.RetrievalMetricName, out NumericMetric? retrieval));

        Assert.True(
            groundedness.Context?.Count is 1 &&
            groundedness.Context.TryGetValue(GroundednessEvaluatorContext.GroundingContextName, out EvaluationContext? context1) &&
            ReferenceEquals(context1, groundingContextForGroundednessEvaluator));

        Assert.True(
            equivalence.Context?.Count is 1 &&
            equivalence.Context.TryGetValue(EquivalenceEvaluatorContext.GroundTruthContextName, out EvaluationContext? context2) &&
            ReferenceEquals(context2, baselineResponseForEquivalenceEvaluator));

        Assert.True(
            completeness.Context?.Count is 1 &&
            completeness.Context.TryGetValue(CompletenessEvaluatorContext.GroundTruthContextName, out EvaluationContext? context3) &&
            ReferenceEquals(context3, groundTruthForCompletenessEvaluator));

        Assert.True(
            retrieval.Context?.Count is 1 &&
            retrieval.Context.TryGetValue(RetrievalEvaluatorContext.RetrievedContextChunksContextName, out EvaluationContext? context4) &&
            ReferenceEquals(context4, retrievedContextChunksForRetrievalEvaluator));
    }

    [MemberNotNull(nameof(_chatOptions))]
    [MemberNotNull(nameof(_qualityReportingConfiguration))]
    [MemberNotNull(nameof(_needsContextReportingConfiguration))]
    private static void SkipIfNotConfigured()
    {
        Assert.SkipUnless(Settings.Current.Configured, "Test is not configured");

        Assert.NotNull(_chatOptions);
        Assert.NotNull(_qualityReportingConfiguration);
        Assert.NotNull(_needsContextReportingConfiguration);
    }
}
