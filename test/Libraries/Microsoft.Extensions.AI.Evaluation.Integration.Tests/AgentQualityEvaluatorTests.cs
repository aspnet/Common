// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
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
public class AgentQualityEvaluatorTests
{
    private static readonly ChatOptions? _chatOptions;
    private static readonly ChatOptions? _chatOptionsWithTools;
    private static readonly ReportingConfiguration? _agentQualityReportingConfiguration;
    private static readonly ReportingConfiguration? _needsContextReportingConfiguration;

    static AgentQualityEvaluatorTests()
    {
        if (Settings.Current.Configured)
        {
            _chatOptions =
                new ChatOptions
                {
                    Temperature = 0.0f,
                    ResponseFormat = ChatResponseFormat.Text
                };

            _chatOptionsWithTools =
                new ChatOptions
                {
                    Temperature = 0.0f,
                    ResponseFormat = ChatResponseFormat.Text,
                    Tools = [AIFunctionFactory.Create(GetOrders), AIFunctionFactory.Create(GetOrderStatus)]
                };

            ChatConfiguration chatConfiguration = Setup.CreateChatConfiguration();
            ChatClientMetadata? clientMetadata = chatConfiguration.ChatClient.GetService<ChatClientMetadata>();

            IChatClient chatClient = chatConfiguration.ChatClient;
            IChatClient chatClientWithToolCalling = chatClient.AsBuilder().UseFunctionInvocation().Build();
            ChatConfiguration chatConfigurationWithToolCalling = new ChatConfiguration(chatClientWithToolCalling);

            string version = $"Product Version: {Constants.Version}";
            string date = $"Date: {DateTime.UtcNow:dddd, dd MMMM yyyy}";
            string projectName = $"Project: Integration Tests";
            string testClass = $"Test Class: {nameof(AgentQualityEvaluatorTests)}";
            string model = $"Model: {clientMetadata?.DefaultModelId ?? "Unknown"}";
            string provider = $"Model Provider: {clientMetadata?.ProviderName ?? "Unknown"}";
            string temperature = $"Temperature: {_chatOptionsWithTools.Temperature}";
            string usesContext = $"Feature: Context";

            IEvaluator toolCallAccuracyEvaluator = new ToolCallAccuracyEvaluator();
            IEvaluator taskAdherenceEvaluator = new TaskAdherenceEvaluator();
            IEvaluator intentResolutionEvaluator = new IntentResolutionEvaluator();

            _agentQualityReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [taskAdherenceEvaluator, intentResolutionEvaluator],
                    chatConfiguration: chatConfigurationWithToolCalling,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, model, provider, temperature]);

            _needsContextReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [toolCallAccuracyEvaluator, taskAdherenceEvaluator, intentResolutionEvaluator],
                    chatConfiguration: chatConfigurationWithToolCalling,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, model, provider, temperature, usesContext]);
        }
    }

    [Fact]
    public async Task ToolDefinitionsAreNotNeededAndNotPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _agentQualityReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(AgentQualityEvaluatorTests)}.{nameof(ToolDefinitionsAreNotNeededAndNotPassed)}");

        (IEnumerable<ChatMessage> messages, ChatResponse response) =
            await GetConversationWithoutToolsAsync(scenarioRun.ChatConfiguration!.ChatClient);

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(2, result.Metrics.Count);
        Assert.True(result.TryGet(TaskAdherenceEvaluator.TaskAdherenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(IntentResolutionEvaluator.IntentResolutionMetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task ToolDefinitionsAreNotNeededButPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _agentQualityReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(AgentQualityEvaluatorTests)}.{nameof(ToolDefinitionsAreNotNeededButPassed)}");

        (IEnumerable<ChatMessage> messages, ChatResponse response) =
            await GetConversationWithoutToolsAsync(scenarioRun.ChatConfiguration!.ChatClient);

        var toolDefinitionsForTaskAdherenceEvaluator =
            new TaskAdherenceEvaluatorContext(toolDefinitions: _chatOptionsWithTools.Tools!);

        var toolDefinitionsForIntentResolution =
            new IntentResolutionEvaluatorContext(toolDefinitions: _chatOptionsWithTools.Tools!);

        EvaluationResult result =
            await scenarioRun.EvaluateAsync(
                messages,
                response,
                additionalContext: [toolDefinitionsForTaskAdherenceEvaluator, toolDefinitionsForIntentResolution]);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(2, result.Metrics.Count);
        Assert.True(result.TryGet(TaskAdherenceEvaluator.TaskAdherenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(IntentResolutionEvaluator.IntentResolutionMetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task ToolDefinitionsAreNeededButNotPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _needsContextReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(AgentQualityEvaluatorTests)}.{nameof(ToolDefinitionsAreNeededButNotPassed)}");

        (IEnumerable<ChatMessage> messages, ChatResponse response) =
            await GetConversationWithToolsAsync(scenarioRun.ChatConfiguration!.ChatClient);

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.True(
            result.Metrics.Values.All(m => m.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error)),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(ToolCallAccuracyEvaluator.ToolCallAccuracyMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(TaskAdherenceEvaluator.TaskAdherenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(IntentResolutionEvaluator.IntentResolutionMetricName, out NumericMetric? _));
    }

    [Fact]
    public async Task ToolDefinitionsAreNeededAndPassed()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _needsContextReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(AgentQualityEvaluatorTests)}.{nameof(ToolDefinitionsAreNeededAndPassed)}");

        (IEnumerable<ChatMessage> messages, ChatResponse response) =
            await GetConversationWithToolsAsync(scenarioRun.ChatConfiguration!.ChatClient);

        var toolDefinitionsForToolCallAccuracyEvaluator =
            new ToolCallAccuracyEvaluatorContext(toolDefinitions: _chatOptionsWithTools.Tools!);

        var toolDefinitionsForTaskAdherenceEvaluator =
            new TaskAdherenceEvaluatorContext(toolDefinitions: _chatOptionsWithTools.Tools!);

        var toolDefinitionsForIntentResolutionEvaluator =
            new IntentResolutionEvaluatorContext(toolDefinitions: _chatOptionsWithTools.Tools!);

        EvaluationResult result =
            await scenarioRun.EvaluateAsync(
                messages,
                response,
                additionalContext: [
                    toolDefinitionsForToolCallAccuracyEvaluator,
                    toolDefinitionsForTaskAdherenceEvaluator,
                    toolDefinitionsForIntentResolutionEvaluator]);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity >= EvaluationDiagnosticSeverity.Warning),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(3, result.Metrics.Count);
        Assert.True(result.TryGet(ToolCallAccuracyEvaluator.ToolCallAccuracyMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(TaskAdherenceEvaluator.TaskAdherenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(IntentResolutionEvaluator.IntentResolutionMetricName, out NumericMetric? _));
    }

    private static async Task<(IEnumerable<ChatMessage> messages, ChatResponse response)>
        GetConversationWithoutToolsAsync(IChatClient chatClient)
    {
        List<ChatMessage> messages =
            [
                "You are a friendly and helpful assistant that can answer questions.".ToSystemMessage(),
                "Hi, could you help me figure out the correct pronunciation for the word rendezvous?".ToUserMessage()
            ];

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions);
        return (messages, response);
    }

    private static async Task<(IEnumerable<ChatMessage> messages, ChatResponse response)>
        GetConversationWithToolsAsync(IChatClient chatClient)
    {
        List<ChatMessage> messages =
            [
                "You are a friendly and helpful customer service agent.".ToSystemMessage(),
                "Hi, I need help with the last 2 orders on my account #888. Could you please update me on their status?".ToUserMessage()
            ];

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptionsWithTools);
        return (messages, response);
    }

    [Description("Gets the orders for a customer")]
    private static IReadOnlyList<Order> GetOrders(int accountNumber)
    {
        if (accountNumber != 888)
        {
            throw new InvalidOperationException($"Account number {accountNumber} is not valid.");
        }

        return [new Order(123), new Order(124)];
    }

    [Description("Gets the delivery status of an order")]
    private static OrderStatus GetOrderStatus(int orderId)
    {
        if (orderId == 123)
        {
            return new OrderStatus(orderId, "shipped", DateTime.Now.AddDays(1));
        }
        else if (orderId == 124)
        {
            return new OrderStatus(orderId, "delayed", DateTime.Now.AddDays(10));
        }
        else
        {
            throw new InvalidOperationException($"Order with ID {orderId} not found.");
        }
    }

    private record Order(int OrderId)
    {
    }

    private record OrderStatus(int OrderId, string Status, DateTime ExpectedDelivery)
    {
    }

    [MemberNotNull(nameof(_chatOptionsWithTools))]
    [MemberNotNull(nameof(_agentQualityReportingConfiguration))]
    [MemberNotNull(nameof(_needsContextReportingConfiguration))]
    private static void SkipIfNotConfigured()
    {
        Assert.SkipUnless(Settings.Current.Configured, "Test is not configured");

        Assert.NotNull(_chatOptionsWithTools);
        Assert.NotNull(_agentQualityReportingConfiguration);
        Assert.NotNull(_needsContextReportingConfiguration);
    }
}
