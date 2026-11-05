// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats;
using Microsoft.Extensions.AI.Evaluation.Reporting.JsonSerialization;
using Xunit;
using static Microsoft.Extensions.AI.Evaluation.Reporting.Storage.DiskBasedResponseCache;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class ScenarioRunResultTests
{
    private sealed class TestContext(string name, params AIContent[] contents)
        : EvaluationContext(name, contents);

    [Fact]
    public void SerializeScenarioRunResult()
    {
        var content1 = new TextContent("content1");
        var content2 = new TextContent("content2");
        var content3 = new TextContent("content3");

        var context1 = new TestContext("context1", content1, content2);
        var context2 = new TestContext("context2", content1);
        var context3 = new TestContext("context3", content2, content3);

        var interpretation1 = new EvaluationMetricInterpretation(EvaluationRating.Poor, failed: true, "int-reason1");
        var interpretation2 = new EvaluationMetricInterpretation(EvaluationRating.Exceptional, failed: false, "int-reason2");

        var booleanMetric = new BooleanMetric("boolean", value: true, reason: "reason1")
        {
            Interpretation = interpretation1
        };

        booleanMetric.AddOrUpdateContext(context1);
        booleanMetric.AddDiagnostics(EvaluationDiagnostic.Error("error"));
        booleanMetric.AddDiagnostics(EvaluationDiagnostic.Warning("warning"));
        booleanMetric.AddOrUpdateMetadata("metadata1", "value1");
        booleanMetric.AddOrUpdateMetadata("metadata2", "value2");

        var numericMetric = new NumericMetric("numeric", value: 3)
        {
            Interpretation = interpretation2
        };

        numericMetric.AddOrUpdateContext(context2);
        numericMetric.AddDiagnostics(EvaluationDiagnostic.Informational("info"));
        numericMetric.AddOrUpdateMetadata("metadata3", "value3");
        numericMetric.AddOrUpdateMetadata("metadata4", "value4");

        var stringMetric = new StringMetric("string", value: "A", reason: string.Empty);
        stringMetric.AddOrUpdateContext(context3);

        var metricWithNoValue = new EvaluationMetric("none", reason: "reason2");
        metricWithNoValue.AddDiagnostics(EvaluationDiagnostic.Error("error"));
        metricWithNoValue.AddDiagnostics(EvaluationDiagnostic.Informational("info"));
        metricWithNoValue.AddOrUpdateMetadata("metadata5", "value5");

        var turn1 =
            new ChatTurnDetails(
                latency: TimeSpan.FromSeconds(1),
                model: "gpt-4o",
                modelProvider: "openai",
                usage: new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20, TotalTokenCount = 30 },
                cacheKey: Guid.NewGuid().ToString(),
                cacheHit: true);

        var turn2 =
            new ChatTurnDetails(
                latency: TimeSpan.FromSeconds(2),
                model: "gpt-4o",
                usage: new UsageDetails { InputTokenCount = 20, OutputTokenCount = 30, TotalTokenCount = 50 },
                cacheKey: Guid.NewGuid().ToString(),
                cacheHit: false);

        var chatDetails = new ChatDetails(turn1, turn2);

        var entry = new ScenarioRunResult(
            scenarioName: "Test Scenario",
            iterationName: "2",
            executionName: "Test Execution",
            creationTime: DateTime.UtcNow,
            messages: [new ChatMessage(ChatRole.User, "prompt")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")),
            evaluationResult: new EvaluationResult(booleanMetric, numericMetric, stringMetric, metricWithNoValue),
            chatDetails: chatDetails,
            tags: ["first", "second"]);

        Assert.Equal(Defaults.ReportingFormatVersion, entry.FormatVersion);

        string json = JsonSerializer.Serialize(entry, JsonUtilities.Default.ScenarioRunResultTypeInfo);
        ScenarioRunResult? deserialized = JsonSerializer.Deserialize(json, JsonUtilities.Default.ScenarioRunResultTypeInfo);

        Assert.NotNull(deserialized);
        Assert.Equal(entry.ScenarioName, deserialized!.ScenarioName);
        Assert.Equal(entry.IterationName, deserialized.IterationName);
        Assert.Equal(entry.ExecutionName, deserialized.ExecutionName);
        Assert.Equal(entry.CreationTime, deserialized.CreationTime);
        Assert.True(entry.Messages.SequenceEqual(deserialized.Messages, ChatMessageComparer.Instance));
        Assert.Equal(entry.ModelResponse, deserialized.ModelResponse, ChatResponseComparer.Instance);
        Assert.True(entry.ChatDetails!.TurnDetails.SequenceEqual(deserialized.ChatDetails!.TurnDetails!, ChatTurnDetailsComparer.Instance));
        Assert.True(entry.Tags!.SequenceEqual(deserialized.Tags!));
        Assert.Equal(entry.FormatVersion, deserialized.FormatVersion);

        ValidateEquivalence(entry.EvaluationResult, deserialized.EvaluationResult);
    }

    [Fact]
    public void SerializeDatasetCompact()
    {
        var content1 = new TextContent("content1");
        var content2 = new TextContent("content2");
        var content3 = new TextContent("content3");

        var context1 = new TestContext("context1", content1, content2);
        var context2 = new TestContext("context2", content1);
        var context3 = new TestContext("context3", content2, content3);

        var interpretation1 = new EvaluationMetricInterpretation(EvaluationRating.Poor, failed: true, "int-reason1");
        var interpretation2 = new EvaluationMetricInterpretation(EvaluationRating.Exceptional, failed: false, "int-reason2");

        var booleanMetric = new BooleanMetric("boolean", value: true, reason: "reason1")
        {
            Interpretation = interpretation1
        };

        booleanMetric.AddOrUpdateContext(context1);
        booleanMetric.AddDiagnostics(EvaluationDiagnostic.Error("error"));
        booleanMetric.AddDiagnostics(EvaluationDiagnostic.Warning("warning"));
        booleanMetric.AddOrUpdateMetadata("metadata1", "value1");
        booleanMetric.AddOrUpdateMetadata("metadata2", "value2");

        var numericMetric = new NumericMetric("numeric", value: 3)
        {
            Interpretation = interpretation2
        };

        numericMetric.AddOrUpdateContext(context2);
        numericMetric.AddDiagnostics(EvaluationDiagnostic.Informational("info"));
        numericMetric.AddOrUpdateMetadata("metadata3", "value3");
        numericMetric.AddOrUpdateMetadata("metadata4", "value4");

        var stringMetric = new StringMetric("string", value: "A", reason: string.Empty);
        stringMetric.AddOrUpdateContext(context3);

        var metricWithNoValue = new EvaluationMetric("none", reason: "reason2");
        metricWithNoValue.AddDiagnostics(EvaluationDiagnostic.Error("error"));
        metricWithNoValue.AddDiagnostics(EvaluationDiagnostic.Informational("info"));
        metricWithNoValue.AddOrUpdateMetadata("metadata5", "value5");

        var turn1 =
            new ChatTurnDetails(
                latency: TimeSpan.FromSeconds(1),
                model: "gpt-4o",
                modelProvider: "openai",
                usage: new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20, TotalTokenCount = 30 },
                cacheKey: Guid.NewGuid().ToString(),
                cacheHit: true);

        var turn2 =
            new ChatTurnDetails(
                latency: TimeSpan.FromSeconds(2),
                model: "gpt-4o",
                usage: new UsageDetails { InputTokenCount = 20, OutputTokenCount = 30, TotalTokenCount = 50 },
                cacheKey: Guid.NewGuid().ToString(),
                cacheHit: false);

        var chatDetails = new ChatDetails(turn1, turn2);

        var entry = new ScenarioRunResult(
            scenarioName: "Test Scenario",
            iterationName: "2",
            executionName: "Test Execution",
            creationTime: DateTime.UtcNow,
            messages: [new ChatMessage(ChatRole.User, "prompt")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")),
            evaluationResult: new EvaluationResult(booleanMetric, numericMetric, stringMetric, metricWithNoValue),
            chatDetails,
            tags: ["first", "second"]);

        Assert.Equal(Defaults.ReportingFormatVersion, entry.FormatVersion);

        var dataset = new Dataset([entry], createdAt: DateTime.UtcNow, generatorVersion: "1.2.3.4");

        string json = JsonSerializer.Serialize(dataset, JsonUtilities.Compact.DatasetTypeInfo);
        Dataset? deserialized = JsonSerializer.Deserialize(json, JsonUtilities.Default.DatasetTypeInfo);

        Assert.NotNull(deserialized);
        Assert.Equal(entry.ScenarioName, deserialized!.ScenarioRunResults[0].ScenarioName);
        Assert.Equal(entry.IterationName, deserialized.ScenarioRunResults[0].IterationName);
        Assert.Equal(entry.ExecutionName, deserialized.ScenarioRunResults[0].ExecutionName);
        Assert.Equal(entry.CreationTime, deserialized.ScenarioRunResults[0].CreationTime);
        Assert.True(entry.Messages.SequenceEqual(deserialized.ScenarioRunResults[0].Messages, ChatMessageComparer.Instance));
        Assert.Equal(entry.ModelResponse, deserialized.ScenarioRunResults[0].ModelResponse, ChatResponseComparer.Instance);
        Assert.True(entry.ChatDetails!.TurnDetails.SequenceEqual(deserialized.ScenarioRunResults[0].ChatDetails!.TurnDetails!, ChatTurnDetailsComparer.Instance));
        Assert.True(entry.Tags!.SequenceEqual(deserialized.ScenarioRunResults[0].Tags!));
        Assert.Equal(entry.FormatVersion, deserialized.ScenarioRunResults[0].FormatVersion);

        Assert.Single(deserialized.ScenarioRunResults);
        Assert.Equal(dataset.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(dataset.GeneratorVersion, deserialized.GeneratorVersion);

        ValidateEquivalence(entry.EvaluationResult, deserialized.ScenarioRunResults[0].EvaluationResult);
    }

    [Fact]
    public void SerializeDatasetForTypeScriptContract()
    {
        var metricWithNoValue = new EvaluationMetric("none", reason: "No score was produced.");
        var nullableNumericMetric = new NumericMetric("nullableNumeric");
        var nullableBooleanMetric = new BooleanMetric("nullableBoolean");
        var nullableStringMetric = new StringMetric("nullableString");

        var functionCall = new FunctionCallContent(
            callId: "call-contract-001",
            name: "lookup",
            arguments: new Dictionary<string, object?> { ["query"] = "fixed" })
        {
            InformationalOnly = true
        };

        var entry = new ScenarioRunResult(
            scenarioName: "Contract.Witness",
            iterationName: "iteration-fixed",
            executionName: "execution-fixed",
            creationTime: new DateTime(2026, 7, 21, 12, 34, 56, DateTimeKind.Utc),
            messages:
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new AIContent(),
                        new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
                        functionCall
                    ])
            ],
            modelResponse: new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionResultContent("call-contract-001", result: "fixed-result")])),
            evaluationResult: new EvaluationResult(
                metricWithNoValue,
                nullableNumericMetric,
                nullableBooleanMetric,
                nullableStringMetric))
        {
            FormatVersion = null
        };

        var dataset = new Dataset(
            [entry],
            createdAt: new DateTime(2026, 7, 21, 12, 35, 0, DateTimeKind.Utc),
            generatorVersion: null);

        string json = JsonSerializer.Serialize(dataset, JsonUtilities.Compact.DatasetTypeInfo);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement scenario = document.RootElement.GetProperty("scenarioRunResults")[0];
        JsonElement metrics = scenario.GetProperty("evaluationResult").GetProperty("metrics");
        Assert.False(metrics.GetProperty("none").TryGetProperty("value", out _));
        Assert.False(metrics.GetProperty("nullableNumeric").TryGetProperty("value", out _));
        Assert.False(metrics.GetProperty("nullableBoolean").TryGetProperty("value", out _));
        Assert.False(metrics.GetProperty("nullableString").TryGetProperty("value", out _));
        Assert.False(scenario.TryGetProperty("formatVersion", out _));

        JsonElement contents = scenario.GetProperty("messages")[0].GetProperty("contents");
        Assert.False(contents[0].TryGetProperty("$type", out _));
        Assert.False(contents[1].TryGetProperty("mediaType", out _));
        Assert.True(contents[2].GetProperty("informationalOnly").GetBoolean());

        string? witnessPath = Environment.GetEnvironmentVariable("AI_EVALUATION_DATASET_WITNESS_PATH");
        if (!string.IsNullOrEmpty(witnessPath))
        {
            string? witnessDirectory = Path.GetDirectoryName(witnessPath);
            if (!string.IsNullOrEmpty(witnessDirectory))
            {
                Directory.CreateDirectory(witnessDirectory);
            }

            string testModule = $$"""
                // Licensed to the .NET Foundation under one or more agreements.
                // The .NET Foundation licenses this file to you under the MIT license.

                import { describe, expect, it } from 'vitest';
                import { createScoreSummary } from '../../components';

                const dataset = {{json}} satisfies Dataset;

                describe('production Dataset contract witness', () => {
                    it('matches the TypeScript contract and shared summary boundary', () => {
                        const scenario = dataset.scenarioRunResults[0];
                        const metrics = scenario.evaluationResult.metrics;
                        const contents = scenario.messages[0].contents;

                        expect(metrics.none).not.toHaveProperty('value');
                        expect(metrics.nullableNumeric).not.toHaveProperty('value');
                        expect(metrics.nullableBoolean).not.toHaveProperty('value');
                        expect(metrics.nullableString).not.toHaveProperty('value');
                        expect(scenario).not.toHaveProperty('formatVersion');
                        expect(contents[0]).not.toHaveProperty('$type');
                        expect(contents[1]).toEqual({ $type: 'data', uri: 'data:image/png;base64,iVBORw==' });
                        expect(contents[1]).not.toHaveProperty('mediaType');
                        expect(contents[2]).toMatchObject({
                            $type: 'functionCall',
                            callId: 'call-contract-001',
                            name: 'lookup',
                            informationalOnly: true,
                        });

                        const summary = createScoreSummary(dataset);
                        expect(summary.primaryResult.flattenedNodes.some(node =>
                            node.scenario?.scenarioName === 'Contract.Witness')).toBe(true);
                    });
                });
                """;

            File.WriteAllText(witnessPath, testModule);
        }
    }

    [Fact]
    public void VerifyCompactSerialization()
    {
        var entry = new CacheEntry(
            scenarioName: "Scenario1",
            iterationName: "Iteration2",
            creation: DateTime.UtcNow,
            expiration: DateTime.UtcNow.Add(TimeSpan.FromMinutes(5)));

        string defaultJson = JsonSerializer.Serialize(entry, JsonUtilities.Default.CacheEntryTypeInfo);
        string compactJson = JsonSerializer.Serialize(entry, JsonUtilities.Compact.CacheEntryTypeInfo);

        Assert.NotEqual(defaultJson, compactJson);
        Assert.True(defaultJson.Length > compactJson.Length);
        Assert.Contains("\n", defaultJson);
        Assert.DoesNotContain("\n", compactJson);
    }

    private static void ValidateEquivalence(EvaluationResult? first, EvaluationResult? second)
    {
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.Equal(first!.Metrics.Count, second!.Metrics.Count);

        BooleanMetric booleanMetric = first.Get<BooleanMetric>("boolean");
        BooleanMetric deserializedBooleanMetric = second.Get<BooleanMetric>("boolean");
        Assert.Equal(booleanMetric.Value, deserializedBooleanMetric.Value);
        ValidateEquivalence(booleanMetric, deserializedBooleanMetric);

        NumericMetric numericMetric = first.Get<NumericMetric>("numeric");
        NumericMetric deserializedNumericMetric = second.Get<NumericMetric>("numeric");
        Assert.Equal(numericMetric.Value, deserializedNumericMetric.Value);
        ValidateEquivalence(numericMetric, deserializedNumericMetric);

        StringMetric stringMetric = first.Get<StringMetric>("string");
        StringMetric deserializedStringMetric = second.Get<StringMetric>("string");
        Assert.Equal(stringMetric.Value, deserializedStringMetric.Value);
        ValidateEquivalence(stringMetric, deserializedStringMetric);

        EvaluationMetric metricWithNoValue = first.Get<EvaluationMetric>("none");
        EvaluationMetric deserializedMetricWithNoValue = second.Get<EvaluationMetric>("none");
        ValidateEquivalence(metricWithNoValue, deserializedMetricWithNoValue);
    }

    private static void ValidateEquivalence(EvaluationMetric metric, EvaluationMetric deserializedMetric)
    {
        Assert.Equal(metric.Name, deserializedMetric.Name);
        Assert.Equal(metric.Reason, deserializedMetric.Reason);

        Assert.Equal(metric.Interpretation is null, deserializedMetric.Interpretation is null);
        if (metric.Interpretation is not null && deserializedMetric.Interpretation is not null)
        {
            Assert.Equal(metric.Interpretation, deserializedMetric.Interpretation, InterpretationComparer.Instance);
        }

        Assert.Equal(metric.Context is null, deserializedMetric.Context is null);
        if (metric.Context is not null && deserializedMetric.Context is not null)
        {
            Assert.Equal(metric.Context.Count, deserializedMetric.Context.Count);
            foreach (var key in metric.Context.Keys)
            {
                Assert.Equal(metric.Context[key], deserializedMetric.Context[key], ContextComparer.Instance);
            }
        }

        Assert.Equal(metric.Diagnostics is null, deserializedMetric.Diagnostics is null);
        if (metric.Diagnostics is not null && deserializedMetric.Diagnostics is not null)
        {
            Assert.True(metric.Diagnostics.SequenceEqual(deserializedMetric.Diagnostics, DiagnosticComparer.Instance));
        }

        Assert.Equal(metric.Metadata is null, deserializedMetric.Metadata is null);
        if (metric.Metadata is not null && deserializedMetric.Metadata is not null)
        {
            Assert.Equal(metric.Metadata.Count, deserializedMetric.Metadata.Count);
            foreach (var key in metric.Metadata.Keys)
            {
                Assert.Equal(metric.Metadata[key], deserializedMetric.Metadata[key]);
            }
        }
    }

    private class ChatMessageComparer : IEqualityComparer<ChatMessage>
    {
        public static ChatMessageComparer Instance { get; } = new ChatMessageComparer();

        public bool Equals(ChatMessage? x, ChatMessage? y)
            => x?.AuthorName == y?.AuthorName && x?.Role == y?.Role && x?.Text == y?.Text;

        public int GetHashCode(ChatMessage obj)
            => obj.Text.GetHashCode();
    }

    private class ChatResponseComparer : IEqualityComparer<ChatResponse>
    {
        public static ChatResponseComparer Instance { get; } = new ChatResponseComparer();

        public bool Equals(ChatResponse? x, ChatResponse? y)
            =>
            x is null ? y is null :
            y is not null && x.Messages.SequenceEqual(y.Messages, ChatMessageComparer.Instance);

        public int GetHashCode(ChatResponse obj)
            => obj.Text.GetHashCode();
    }

    private class InterpretationComparer : IEqualityComparer<EvaluationMetricInterpretation>
    {
        public static InterpretationComparer Instance { get; } = new InterpretationComparer();

        public bool Equals(EvaluationMetricInterpretation? x, EvaluationMetricInterpretation? y)
            => x?.Rating == y?.Rating && x?.Failed == y?.Failed && x?.Reason == y?.Reason;

        public int GetHashCode(EvaluationMetricInterpretation obj)
            => obj.GetHashCode();
    }

    private class ContextComparer : IEqualityComparer<EvaluationContext>
    {
        public static ContextComparer Instance { get; } = new ContextComparer();

        public bool Equals(EvaluationContext? x, EvaluationContext? y)
        {
            if (x?.Name != y?.Name)
            {
                return false;
            }

            if (x?.Contents.Count != y?.Contents.Count)
            {
                return false;
            }

            if (x?.Contents is IList<AIContent> xContents && y?.Contents is IList<AIContent> yContents)
            {
                return xContents.SequenceEqual(yContents, AIContentComparer.Instance);
            }

            return true;
        }

        public int GetHashCode(EvaluationContext obj)
            => obj.GetHashCode();
    }

    private class AIContentComparer : IEqualityComparer<AIContent>
    {
        public static AIContentComparer Instance { get; } = new AIContentComparer();

        public bool Equals(AIContent? x, AIContent? y)
        {
            if (x?.GetType().Name != y?.GetType().Name)
            {
                return false;
            }

            if (x is TextContent xText && y is TextContent yText)
            {
                return xText.Text == yText.Text;
            }

            return true;
        }

        public int GetHashCode(AIContent obj)
            => obj.GetHashCode();
    }

    private class DiagnosticComparer : IEqualityComparer<EvaluationDiagnostic>
    {
        public static DiagnosticComparer Instance { get; } = new DiagnosticComparer();

        public bool Equals(EvaluationDiagnostic? x, EvaluationDiagnostic? y)
            => x?.Severity == y?.Severity && x?.Message == y?.Message;

        public int GetHashCode(EvaluationDiagnostic obj)
            => obj.GetHashCode();
    }

    private class ChatTurnDetailsComparer : IEqualityComparer<ChatTurnDetails>
    {
        public static ChatTurnDetailsComparer Instance { get; } = new ChatTurnDetailsComparer();

#pragma warning disable S1067 // Expressions should not be too complex.
        public bool Equals(ChatTurnDetails? x, ChatTurnDetails? y) =>
            x?.Latency == y?.Latency &&
            x?.Model == y?.Model &&
            x?.ModelProvider == y?.ModelProvider &&
            x?.Usage?.InputTokenCount == y?.Usage?.InputTokenCount &&
            x?.Usage?.OutputTokenCount == y?.Usage?.OutputTokenCount &&
            x?.Usage?.TotalTokenCount == y?.Usage?.TotalTokenCount &&
            x?.CacheKey == y?.CacheKey &&
            x?.CacheHit == y?.CacheHit;
#pragma warning restore S1067

        public int GetHashCode(ChatTurnDetails obj)
            => obj.GetHashCode();
    }
}
