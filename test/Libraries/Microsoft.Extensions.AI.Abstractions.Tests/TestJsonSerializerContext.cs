// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable SA1402 // Test JSON contexts are co-located for the shared test contracts.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(ChatResponseUpdate))]
[JsonSerializable(typeof(SpeechToTextResponse))]
[JsonSerializable(typeof(SpeechToTextResponseUpdate))]
[JsonSerializable(typeof(SpeechToTextResponseUpdateKind))]
[JsonSerializable(typeof(SpeechToTextOptions))]
[JsonSerializable(typeof(TextToSpeechResponse))]
[JsonSerializable(typeof(TextToSpeechResponseUpdate))]
[JsonSerializable(typeof(TextToSpeechResponseUpdateKind))]
[JsonSerializable(typeof(TextToSpeechOptions))]
[JsonSerializable(typeof(ImageGenerationResponse))]
[JsonSerializable(typeof(ImageGenerationOptions))]
[JsonSerializable(typeof(ChatOptions))]
[JsonSerializable(typeof(EmbeddingGenerationOptions))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(AIFunctionArguments))]
[JsonSerializable(typeof(int[]))] // Used in ChatMessageContentTests
[JsonSerializable(typeof(Embedding))] // Used in EmbeddingTests
[JsonSerializable(typeof(Dictionary<string, JsonDocument>))] // Used in Content tests
[JsonSerializable(typeof(Dictionary<string, JsonNode>))] // Used in Content tests
[JsonSerializable(typeof(Dictionary<string, string>))] // Used in Content tests
[JsonSerializable(typeof(ReadOnlyDictionary<string, string>))] // Used in Content tests
[JsonSerializable(typeof(DayOfWeek[]))] // Used in Content tests
[JsonSerializable(typeof(Guid))] // Used in Content tests
[JsonSerializable(typeof(decimal))] // Used in Content tests
[JsonSerializable(typeof(HostedMcpServerToolApprovalMode))]
[JsonSerializable(typeof(ChatResponseFormatTests.SomeType))]
[JsonSerializable(typeof(ChatResponseFormatTests.TypeWithDisplayName))]
[JsonSerializable(typeof(ResponseContinuationToken))]
[JsonSerializable(typeof(InputRequestContent[]))]
[JsonSerializable(typeof(InputResponseContent[]))]
[JsonSerializable(typeof(FunctionCallContent[]))]
[JsonSerializable(typeof(FunctionResultContent[]))]
[JsonSerializable(typeof(ToolCallContent[]))]
[JsonSerializable(typeof(ToolResultContent[]))]
[JsonSerializable(typeof(ReasoningOptions))]
[JsonSerializable(typeof(ReasoningEffort))]
[JsonSerializable(typeof(ReasoningOutput))]
[JsonSerializable(typeof(HostedFileClientOptions))]
[JsonSerializable(typeof(DecisionOptions))]
[JsonSerializable(typeof(DecisionClientMetadata))]
[JsonSerializable(typeof(DecisionRequest))]
[JsonSerializable(typeof(DecisionQuestion))]
[JsonSerializable(typeof(BinaryDecisionQuestion))]
[JsonSerializable(typeof(ChoiceDecisionQuestion))]
[JsonSerializable(typeof(ScoreDecisionQuestion))]
[JsonSerializable(typeof(DecisionCandidate))]
[JsonSerializable(typeof(DecisionScoreLevel))]
[JsonSerializable(typeof(DecisionResponse))]
[JsonSerializable(typeof(DecisionAnswer))]
[JsonSerializable(typeof(BinaryDecisionAnswer))]
[JsonSerializable(typeof(ChoiceDecisionAnswer))]
[JsonSerializable(typeof(ScoreDecisionAnswer))]
[JsonSerializable(typeof(DecisionProbability))]
[JsonSerializable(typeof(DecisionTests.Classification))]
[JsonSerializable(typeof(DecisionTests.TicketCategory))]
[JsonSerializable(typeof(DecisionTests.FlagCategory))]
[JsonSerializable(typeof(DecisionTests.DecisionState))]
[JsonSerializable(typeof(DecisionTests.DecisionResult))]
[JsonSerializable(typeof(DecisionTests.TypedDecisionState))]
[JsonSerializable(typeof(DecisionTests.TypedDecisionResult))]
[JsonSerializable(typeof(DecisionTests.NestedResult))]
[JsonSerializable(typeof(DecisionTests.PropertyDescriptionResult))]
[JsonSerializable(typeof(DecisionTests.JsonNamedResult))]
[JsonSerializable(typeof(DecisionTests.FlagsResult))]
[JsonSerializable(typeof(DecisionTests.ReadOnlyResult))]
[JsonSerializable(typeof(DecisionTests.RequiredUnmappedResult))]
[JsonSerializable(typeof(DecisionTests.CustomCategory))]
[JsonSerializable(typeof(DecisionTests.CustomEnumResult))]
[JsonSerializable(typeof(DecisionTests.PropertyConverterResult))]
[JsonSerializable(typeof(DecisionTests.JsonElementState))]
internal sealed partial class TestJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    NumberHandling = JsonNumberHandling.WriteAsString)]
[JsonSerializable(typeof(DecisionTests.GlobalConfiguredResult))]
internal sealed partial class GlobalConfiguredJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DecisionTests.PropertyConfiguredResult))]
internal sealed partial class PropertyConfiguredJsonSerializerContext : JsonSerializerContext;
