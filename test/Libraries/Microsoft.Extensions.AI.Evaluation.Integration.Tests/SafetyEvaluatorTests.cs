// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.AI.Evaluation.Safety;
using Microsoft.Extensions.AI.Evaluation.Tests;
using Microsoft.Extensions.AI.Evaluation.Utilities;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Integration.Tests;

public class SafetyEvaluatorTests
{
    private static readonly ChatOptions? _chatOptions;
    private static readonly ReportingConfiguration? _contentSafetyReportingConfiguration;
    private static readonly ReportingConfiguration? _imageContentSafetyReportingConfiguration;
    private static readonly ReportingConfiguration? _codeVulnerabilityReportingConfiguration;
    private static readonly ReportingConfiguration? _mixedQualityAndSafetyReportingConfiguration;
    private static readonly ReportingConfiguration? _hubBasedContentSafetyReportingConfiguration;

    static SafetyEvaluatorTests()
    {
        if (Settings.Current.Configured)
        {
            _chatOptions =
                new ChatOptions
                {
                    Temperature = 0.0f,
                    ResponseFormat = ChatResponseFormat.Text
                };

            ChatConfiguration llmChatConfiguration = Setup.CreateChatConfiguration();

            string version = $"Product Version: {Constants.Version}";
            string date = $"Date: {DateTime.UtcNow:dddd, dd MMMM yyyy}";
            string projectName = $"Project: Integration Tests";
            string testClass = $"Test Class: {nameof(SafetyEvaluatorTests)}";
            string temperature = $"Temperature: {_chatOptions.Temperature}";
            string usesContext = $"Feature: Context";

            var credential = new ChainedTokenCredential(new AzureCliCredential(), new DefaultAzureCredential());
            var contentSafetyServiceConfiguration =
                new ContentSafetyServiceConfiguration(
                    credential,
                    endpointUrl: Settings.Current.AzureAIProjectEndpoint);

            ChatConfiguration contentSafetyChatConfiguration =
                contentSafetyServiceConfiguration.ToChatConfiguration(llmChatConfiguration);

            ChatClientMetadata? clientMetadata =
                contentSafetyChatConfiguration.ChatClient.GetService<ChatClientMetadata>();

            const string Model = $"Model: {ModelInfo.KnownModels.AzureAIFoundryEvaluation}";
            const string Provider = $"Model Provider: {ModelInfo.KnownModelProviders.AzureAIFoundry}";
            string model2 = $"Model: {clientMetadata?.DefaultModelId ?? "Unknown"}";
            string provider2 = $"Model Provider: {clientMetadata?.ProviderName ?? "Unknown"}";

            IEvaluator hateAndUnfairnessEvaluator = new HateAndUnfairnessEvaluator();
            IEvaluator selfHarmEvaluator = new SelfHarmEvaluator();
            IEvaluator sexualEvaluator = new SexualEvaluator();
            IEvaluator violenceEvaluator = new ViolenceEvaluator();
            IEvaluator protectedMaterialEvaluator = new ProtectedMaterialEvaluator();
            IEvaluator groundednessProEvaluator = new GroundednessProEvaluator();
            IEvaluator ungroundedAttributesEvaluator = new UngroundedAttributesEvaluator();
            IEvaluator indirectAttackEvaluator = new IndirectAttackEvaluator();

            _contentSafetyReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [
                        selfHarmEvaluator,
                        sexualEvaluator,
                        protectedMaterialEvaluator,
                        groundednessProEvaluator,
                        ungroundedAttributesEvaluator,
                        indirectAttackEvaluator],
                    chatConfiguration: contentSafetyChatConfiguration,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, Model, Provider, model2, provider2, temperature, usesContext]);

            ChatConfiguration contentSafetyChatConfigurationWithoutLLM =
                contentSafetyServiceConfiguration.ToChatConfiguration();

            _imageContentSafetyReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [
                        hateAndUnfairnessEvaluator,
                        violenceEvaluator,
                        protectedMaterialEvaluator,
                        indirectAttackEvaluator],
                    chatConfiguration: contentSafetyChatConfigurationWithoutLLM,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, Model, Provider, model2, provider2, temperature]);

            IEvaluator codeVulnerabilityEvaluator = new CodeVulnerabilityEvaluator();

            _codeVulnerabilityReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [codeVulnerabilityEvaluator],
                    chatConfiguration: contentSafetyChatConfigurationWithoutLLM,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, Model, Provider, model2, provider2, temperature]);

            IEvaluator fluencyEvaluator = new FluencyEvaluator();
            IEvaluator contentHarmEvaluator = new ContentHarmEvaluator();

            _mixedQualityAndSafetyReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [fluencyEvaluator, contentHarmEvaluator],
                    chatConfiguration: contentSafetyChatConfiguration,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, Model, Provider, model2, provider2, temperature]);

            var hubBasedContentSafetyServiceConfiguration =
                new ContentSafetyServiceConfiguration(
                    credential,
                    subscriptionId: Settings.Current.AzureSubscriptionId,
                    resourceGroupName: Settings.Current.AzureResourceGroupName,
                    projectName: Settings.Current.AzureAIProjectName);

            ChatConfiguration hubBasedContentSafetyChatConfiguration =
                hubBasedContentSafetyServiceConfiguration.ToChatConfiguration(llmChatConfiguration);

            clientMetadata = hubBasedContentSafetyChatConfiguration.ChatClient.GetService<ChatClientMetadata>();

            model2 = $"Model: {clientMetadata?.DefaultModelId ?? "Unknown"}";
            provider2 = $"Model Provider: {clientMetadata?.ProviderName ?? "Unknown"}";

            _hubBasedContentSafetyReportingConfiguration =
                DiskBasedReportingConfiguration.Create(
                    storageRootPath: Settings.Current.StorageRootPath,
                    evaluators: [
                        selfHarmEvaluator,
                        sexualEvaluator,
                        protectedMaterialEvaluator,
                        groundednessProEvaluator,
                        ungroundedAttributesEvaluator,
                        indirectAttackEvaluator],
                    chatConfiguration: hubBasedContentSafetyChatConfiguration,
                    executionName: Constants.Version,
                    tags: [version, date, projectName, testClass, Model, Provider, model2, provider2, temperature, usesContext]);
        }
    }

    [Fact]
    public async Task EvaluateConversationWithSingleTurn_HubBasedProject()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _hubBasedContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithSingleTurn_HubBasedProject)}");

        await EvaluateConversationWithSingleTurn(scenarioRun);
    }

    [Fact]
    public async Task EvaluateConversationWithSingleTurn_NonHubBasedProject()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _contentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithSingleTurn_NonHubBasedProject)}");

        await EvaluateConversationWithSingleTurn(scenarioRun);
    }

    private static async Task EvaluateConversationWithSingleTurn(ScenarioRun scenarioRun)
    {
        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;
        var messages = new List<ChatMessage>();

        string systemPrompt =
            """
            You are an AI assistant that can answer questions related to astronomy.
            Keep your responses concise staying under 100 words as much as possible.
            Use the imperial measurement system for all measurements in your response.
            """;
        messages.Add(systemPrompt.ToSystemMessage());

        string prompt = "How far is the planet Mars from the Earth at its closest and furthest points?";
        messages.Add(prompt.ToUserMessage());

        ChatResponse response = await chatClient.GetResponseAsync(messages, _chatOptions);

        string groundingContext =
            """
            Mars is approximately 34 million miles from Earth at its closest approach (opposition).
            At its furthest point (conjunction), Mars is about 250 million miles from Earth.
            The distance varies due to the elliptical orbits of both planets.
            """;

        var groundednessProContext = new GroundednessProEvaluatorContext(groundingContext);
        var ungroundedAttributesContext = new UngroundedAttributesEvaluatorContext(groundingContext);
        IEnumerable<EvaluationContext> additionalContext = [groundednessProContext, ungroundedAttributesContext];

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response, additionalContext);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(6, result.Metrics.Count);
        Assert.True(result.TryGet(SelfHarmEvaluator.SelfHarmMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(SexualEvaluator.SexualMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(GroundednessProEvaluator.GroundednessProMetricName, out NumericMetric? groundednessPro));
        Assert.True(result.TryGet(UngroundedAttributesEvaluator.UngroundedAttributesMetricName, out BooleanMetric? ungroundedAttributes));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));

        Assert.True(
            groundednessPro.Context?.Count is 1 &&
            groundednessPro.Context.TryGetValue(GroundednessProEvaluatorContext.GroundingContextName, out EvaluationContext? context1) &&
            ReferenceEquals(context1, groundednessProContext));

        Assert.True(
            ungroundedAttributes.Context?.Count is 1 &&
            ungroundedAttributes.Context.TryGetValue(UngroundedAttributesEvaluatorContext.GroundingContextName, out EvaluationContext? context2) &&
            ReferenceEquals(context2, ungroundedAttributesContext));
    }

    [Fact]
    public async Task EvaluateConversationWithMultipleTurns_HubBasedProject()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _hubBasedContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithMultipleTurns_HubBasedProject)}");

        await EvaluateConversationWithMultipleTurns(scenarioRun);
    }

    [Fact]
    public async Task EvaluateConversationWithMultipleTurns_NonHubBasedProject()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _contentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithMultipleTurns_NonHubBasedProject)}");

        await EvaluateConversationWithMultipleTurns(scenarioRun);
    }

    private static async Task EvaluateConversationWithMultipleTurns(ScenarioRun scenarioRun)
    {
        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;
        var messages = new List<ChatMessage>();

        string systemPrompt =
            """
            You are an AI assistant that can answer questions related to astronomy.
            Keep your responses concise staying under 100 words as much as possible.
            Use the imperial measurement system for all measurements in your response.
            """;
        messages.Add(systemPrompt.ToSystemMessage());

        string prompt1 = "How far is the planet Mercury from the Earth at its closest and furthest points?";
        messages.Add(prompt1.ToUserMessage());

        ChatResponse response1 = await chatClient.GetResponseAsync(messages, _chatOptions);
        messages.AddRange(response1.Messages);

        string prompt2 = "How far is the planet Jupiter from the Earth at its closest and furthest points?";
        messages.Add(prompt2.ToUserMessage());

        ChatResponse response2 = await chatClient.GetResponseAsync(messages, _chatOptions);

        // At the moment, the GroundednessProEvaluator only supports evaluating the last turn of the conversation. We
        // include context that is relevant to both turns as part of the string above. However, only the included
        // context relevant to the last (second) turn matters for the evaluation.
        string groundingContext =
            """
            Mercury's distance from Earth varies due to their elliptical orbits.
            At its closest (during inferior conjunction), Mercury is about 48 million miles away.
            At its furthest (during superior conjunction), it can be approximately 138 million miles away.

            Jupiter's distance from Earth varies due to their elliptical orbits.
            At its closest (opposition), Jupiter is about 365 million miles away.
            At its furthest (conjunction), it can be approximately 601 million miles away.
            """;

        var groundednessProContext = new GroundednessProEvaluatorContext(groundingContext);
        var ungroundedAttributesContext = new UngroundedAttributesEvaluatorContext(groundingContext);
        IEnumerable<EvaluationContext> additionalContext = [groundednessProContext, ungroundedAttributesContext];

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response2, additionalContext);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(6, result.Metrics.Count);
        Assert.True(result.TryGet(SelfHarmEvaluator.SelfHarmMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(SexualEvaluator.SexualMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(GroundednessProEvaluator.GroundednessProMetricName, out NumericMetric? groundednessPro));
        Assert.True(result.TryGet(UngroundedAttributesEvaluator.UngroundedAttributesMetricName, out BooleanMetric? ungroundedAttributes));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));

        Assert.True(
            groundednessPro.Context?.Count is 1 &&
            groundednessPro.Context.TryGetValue(GroundednessProEvaluatorContext.GroundingContextName, out EvaluationContext? context1) &&
            ReferenceEquals(context1, groundednessProContext));

        Assert.True(
            ungroundedAttributes.Context?.Count is 1 &&
            ungroundedAttributes.Context.TryGetValue(UngroundedAttributesEvaluatorContext.GroundingContextName, out EvaluationContext? context2) &&
            ReferenceEquals(context2, ungroundedAttributesContext));
    }

    [Fact]
    public async Task EvaluateConversationWithImageInQuestion()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _imageContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithImageInQuestion)}");

        var question =
            new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [
                    new TextContent("What does this image depict?"),
                    new UriContent("https://uhf.microsoft.com/images/microsoft/RE1Mu3b.png", "image/png")],
            };

        ChatMessage answer = "The image depicts a logo for Microsoft Corporation.".ToAssistantMessage();

        EvaluationResult result = await scenarioRun.EvaluateAsync(question, answer);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(7, result.Metrics.Count);
        Assert.True(result.TryGet(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ViolenceEvaluator.ViolenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedArtworkMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedFictionalCharactersMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedLogosAndBrandsMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateConversationWithImageInAnswer()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _imageContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithImageInAnswer)}");

        ChatMessage question = "Can you show me an image pertaining to DotNet?".ToUserMessage();

        var answer =
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents = [
                    new TextContent("Here's an image pertaining to DotNet:"),
                    new DataContent(ImageDataUri.GetImageDataUri())],
            };

        EvaluationResult result = await scenarioRun.EvaluateAsync(question, answer);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(7, result.Metrics.Count);
        Assert.True(result.TryGet(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ViolenceEvaluator.ViolenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedArtworkMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedFictionalCharactersMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedLogosAndBrandsMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateConversationWithImagesInMultipleTurns()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _imageContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithImagesInMultipleTurns)}");

        var question1 =
            new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [
                    new TextContent("What does this image depict?"),
                    new DataContent(ImageDataUri.GetImageDataUri())],
            };

        ChatMessage answer1 = "The image depicts a logo for DotNet.".ToAssistantMessage();

        ChatMessage question2 = "Can you show me an image pertaining to Microsoft Copilot?".ToUserMessage();

        var answer2 =
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents = [
                    new TextContent("Here's an image pertaining to Microsoft Copilot:"),
                    new UriContent("https://uhf.microsoft.com/images/banners/RW1iGSh.png", "image/png")],
            };

        ChatMessage[] messages = [question1, answer1, question2];
        var response = new ChatResponse(answer2);
        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(7, result.Metrics.Count);
        Assert.True(result.TryGet(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ViolenceEvaluator.ViolenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedArtworkMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedFictionalCharactersMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedLogosAndBrandsMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateConversationWithImagesAndTextInMultipleTurns()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _imageContentSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateConversationWithImagesAndTextInMultipleTurns)}");

        var question1 =
            new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [
                    new TextContent("What does this image depict?"),
                    new UriContent("https://uhf.microsoft.com/images/microsoft/RE1Mu3b.png", "image/png")],
            };

        ChatMessage answer1 = "The image depicts a logo for Microsoft Corporation.".ToAssistantMessage();

        ChatMessage question2 = "Can you show me an image pertaining to Microsoft Copilot?".ToUserMessage();

        var answer2 =
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents = [
                    new TextContent("Here's an image pertaining to Microsoft Copilot:"),
                    new UriContent("https://uhf.microsoft.com/images/banners/RW1iGSh.png", "image/png")],
            };

        ChatMessage question3 =
            """
            How far in miles is the planet Venus from the Earth at its closest and furthest points?
            """.ToUserMessage();

        ChatMessage answer3 =
            """
            The distance between Venus and Earth varies because both planets have elliptical orbits around the Sun.
            At its closest approach (known as inferior conjunction), Venus can be approximately 23.6 million miles (38 million kilometers) away from Earth.
            At its furthest point (when Venus is on the opposite side of the Sun, known as superior conjunction), the distance can be about 162 million miles (261 million kilometers).
            These distances are approximate and can vary slightly depending on the specific orbital positions of the planets at any given time.
            """.ToAssistantMessage();

        ChatMessage[] messages = [question1, answer1, question2, answer2, question3];
        var response = new ChatResponse(answer3);
        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(7, result.Metrics.Count);
        Assert.True(result.TryGet(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ViolenceEvaluator.ViolenceMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedMaterialMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedArtworkMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedFictionalCharactersMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(ProtectedMaterialEvaluator.ProtectedLogosAndBrandsMetricName, out BooleanMetric? _));
        Assert.True(result.TryGet(IndirectAttackEvaluator.IndirectAttackMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateCodeCompletionWithSingleTurn()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _codeVulnerabilityReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateCodeCompletionWithSingleTurn)}");

        string context =
            """
            Console.WriteLine(
            """;

        string completion =
            """
            "Hello, World!");
            """;

        EvaluationResult result = await scenarioRun.EvaluateAsync(context, completion);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Single(result.Metrics);
        Assert.True(result.TryGet(CodeVulnerabilityEvaluator.CodeVulnerabilityMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateCodeCompletionWithMultipleTurns()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _codeVulnerabilityReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateCodeCompletionWithMultipleTurns)}");

        ChatMessage context1 =
            """
            Console.WriteLine(
            """.ToUserMessage();

        ChatMessage completion1 =
            """
            "Hello, World!");
            """.ToAssistantMessage();

        ChatMessage context2 =
            """
            for(int i = 0; i
            """.ToUserMessage();

        ChatMessage completion2 =
            """
            < 10; i++)
            """.ToAssistantMessage();

        ChatMessage[] messages = [context1, completion1, context2];
        var response = new ChatResponse(completion2);
        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Single(result.Metrics);
        Assert.True(result.TryGet(CodeVulnerabilityEvaluator.CodeVulnerabilityMetricName, out BooleanMetric? _));
    }

    [Fact]
    public async Task EvaluateSafetyAndQualityMetricsForSameConversation()
    {
        SkipIfNotConfigured();

        await using ScenarioRun scenarioRun =
            await _mixedQualityAndSafetyReportingConfiguration.CreateScenarioRunAsync(
                scenarioName: $"Microsoft.Extensions.AI.Evaluation.Integration.Tests.{nameof(SafetyEvaluatorTests)}.{nameof(EvaluateSafetyAndQualityMetricsForSameConversation)}");

        IChatClient chatClient = scenarioRun.ChatConfiguration!.ChatClient;

        var messages = new List<ChatMessage>();

        string prompt1 = "How far is the planet Saturn from the Earth at its closest and furthest points? Keep your responses concise staying under 100 words as much as possible.";
        messages.Add(prompt1.ToUserMessage());

        ChatResponse response1 = await chatClient.GetResponseAsync(messages, _chatOptions);
        messages.AddRange(response1.Messages);

        string prompt2 = "How far is the planet Neptune from the Earth at its closest and furthest points? Keep your responses concise staying under 100 words as much as possible.";
        messages.Add(prompt2.ToUserMessage());

        ChatResponse response2 = await chatClient.GetResponseAsync(messages, _chatOptions);

        EvaluationResult result = await scenarioRun.EvaluateAsync(messages, response2);

        Assert.False(
            result.ContainsDiagnostics(d => d.Severity is EvaluationDiagnosticSeverity.Error),
            string.Join("\r\n\r\n", result.Metrics.Values.SelectMany(m => m.Diagnostics ?? []).Select(d => d.ToString())));

        Assert.Equal(5, result.Metrics.Count);
        Assert.True(result.TryGet(FluencyEvaluator.FluencyMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(SelfHarmEvaluator.SelfHarmMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(SexualEvaluator.SexualMetricName, out NumericMetric? _));
        Assert.True(result.TryGet(ViolenceEvaluator.ViolenceMetricName, out NumericMetric? _));
    }

    [MemberNotNull(nameof(_chatOptions))]
    [MemberNotNull(nameof(_contentSafetyReportingConfiguration))]
    [MemberNotNull(nameof(_imageContentSafetyReportingConfiguration))]
    [MemberNotNull(nameof(_codeVulnerabilityReportingConfiguration))]
    [MemberNotNull(nameof(_mixedQualityAndSafetyReportingConfiguration))]
    [MemberNotNull(nameof(_hubBasedContentSafetyReportingConfiguration))]
    private static void SkipIfNotConfigured()
    {
        Assert.SkipUnless(Settings.Current.Configured, "Test is not configured");

        Assert.NotNull(_chatOptions);
        Assert.NotNull(_contentSafetyReportingConfiguration);
        Assert.NotNull(_codeVulnerabilityReportingConfiguration);
        Assert.NotNull(_imageContentSafetyReportingConfiguration);
        Assert.NotNull(_mixedQualityAndSafetyReportingConfiguration);
        Assert.NotNull(_hubBasedContentSafetyReportingConfiguration);
    }
}
