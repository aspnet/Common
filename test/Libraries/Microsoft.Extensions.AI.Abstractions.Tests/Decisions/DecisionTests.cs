// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class DecisionTests
{
    [Fact]
    public void EnumDefinition_PreservesExactIdsAndRejectsInvalidMappings()
    {
        DecisionEnumDefinition<Classification> definition = new(
        [
            new(Classification.Approved, "approve|v1", "Approve the request"),
            new(Classification.Rejected, "reject|v1", "Reject the request"),
        ]);

        Assert.Equal(["approve|v1", "reject|v1"], definition.Candidates.Select(static c => c.Id));
        Assert.Equal(Classification.Approved, definition.Parse("approve|v1"));
        Assert.Throws<DecisionProtocolException>(() => definition.Parse("approve"));
        Assert.Throws<ArgumentException>(() => new DecisionEnumDefinition<Classification>(
        [
            new(Classification.Approved, "one", "One"),
            new(Classification.Approved, "two", "Two"),
        ]));
        Assert.Throws<ArgumentException>(() => new DecisionEnumDefinition<Classification>(
        [
            new(Classification.Approved, "duplicate", "One"),
            new(Classification.Rejected, "duplicate", "Two"),
        ]));

        DecisionRequest request = CreateRequest(
            definition.CreateQuestion("classification", "Classify the request."));
        DecisionResponse response = new(
            request,
            [
                new ChoiceDecisionAnswer(
                    "classification",
                    "reject|v1",
                    [
                        new("approve|v1", 0.25),
                        new("reject|v1", 0.75),
                    ]),
            ]);

        Assert.Equal(
            Classification.Rejected,
            response.Bind(
                new DecisionResultBinding<Classification>(
                    TestJsonSerializerContext.Default.Classification,
                    answers => answers.GetChoice("classification", definition))));
        Assert.Equal(["approve|v1", "reject|v1"], ((ChoiceDecisionAnswer)response.Answers[0]).Probabilities.Select(static p => p.Id));
    }

    [Fact]
    public void MixedAnswers_BindToOwnedResultAndRetainRoundedScoreEvidence()
    {
        DecisionResponse response = CreateMixedResponse();

        DecisionResult result = response.Bind(
            new DecisionResultBinding<DecisionResult>(
                TestJsonSerializerContext.Default.DecisionResult,
                answers =>
                {
                    ScoreDecisionAnswer score = answers.GetScore("score");
                    return new DecisionResult
                    {
                        Approved = answers.GetBinary("binary").TrueProbability > 0.5,
                        Selected = answers.GetChoice("choice").SelectedCandidateId,
                        Score = score.Score!.Value,
                        ExpectedScore = score.ExpectedScore,
                    };
                }));

        Assert.True(result.Approved);
        Assert.Equal("blue|candidate", result.Selected);
        Assert.Equal(1.6, result.Score);
        Assert.Equal(1.6, result.ExpectedScore, precision: 12);
        Assert.Equal(3, ((ScoreDecisionAnswer)response.Answers[2]).Probabilities.Count);
    }

    [Fact]
    public void ProbabilityValidation_RejectsNullEntriesAndAllowsDeclaredRounding()
    {
        Assert.Throws<DecisionProtocolException>(() => new ChoiceDecisionAnswer(
            "choice",
            "a",
            [new DecisionProbability("a", 1), null!]));

        ChoiceDecisionAnswer twoDecimal = new(
            "choice",
            "a",
            [
                new("a", 0.33),
                new("b", 0.33),
                new("c", 0.33),
            ],
            DecisionPrecision.TwoDecimalPlaces);
        ChoiceDecisionAnswer fourDecimal = new(
            "choice",
            "a",
            [
                new("a", 0.3333),
                new("b", 0.3333),
                new("c", 0.3333),
            ],
            DecisionPrecision.FourDecimalPlaces);

        Assert.Equal(3, twoDecimal.Probabilities.Count);
        Assert.Equal(3, fourDecimal.Probabilities.Count);
        Assert.Throws<DecisionProtocolException>(() => new ChoiceDecisionAnswer(
            "choice",
            "a",
            [
                new("a", 1),
                new("b", 0),
                new("c", 0),
                new("d", 0.02),
            ],
            DecisionPrecision.TwoDecimalPlaces));
        Assert.Throws<DecisionProtocolException>(() => new ChoiceDecisionAnswer(
            "choice",
            "a",
            [
                new("a", 0.33),
                new("b", 0.33),
                new("c", 0.33),
            ]));
    }

    [Fact]
    public void RoundedScoreValidation_UsesProbabilityAndScoreBounds()
    {
        ScoreDecisionAnswer valid = new(
            "score",
            1.6,
            [
                new("low", 0.1),
                new("medium", 0.2),
                new("high", 0.7),
            ],
            DecisionPrecision.TwoDecimalPlaces);

        Assert.Equal(1.6, valid.Score!.Value);
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            0.02,
            [
                new("low", 1),
                new("medium", 0),
                new("high", 0),
            ],
            DecisionPrecision.TwoDecimalPlaces));
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            2,
            [
                new("low", 1),
                new("medium", 0),
                new("high", 0),
            ],
            DecisionPrecision.TwoDecimalPlaces));
    }

    [Fact]
    public void RoundedScoreValidation_AllowsBothFeasibleExpectationExtremes()
    {
        ScoreDecisionAnswer minimum = new(
            "score",
            1.59,
            [
                new("low", 0.1),
                new("medium", 0.2),
                new("high", 0.7),
            ],
            DecisionPrecision.TwoDecimalPlaces);
        ScoreDecisionAnswer maximum = new(
            "score",
            1.61,
            [
                new("low", 0.1),
                new("medium", 0.2),
                new("high", 0.7),
            ],
            DecisionPrecision.TwoDecimalPlaces);

        Assert.Equal(1.59, minimum.Score!.Value);
        Assert.Equal(1.61, maximum.Score!.Value);
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            1.58,
            [
                new("low", 0.1),
                new("medium", 0.2),
                new("high", 0.7),
            ],
            DecisionPrecision.TwoDecimalPlaces));
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            1.62,
            [
                new("low", 0.1),
                new("medium", 0.2),
                new("high", 0.7),
            ],
            DecisionPrecision.TwoDecimalPlaces));
    }

    [Fact]
    public void RoundedScoreValidation_FourDecimalPrecisionRequiresNormalizedLatentMass()
    {
        ScoreDecisionAnswer valid = new(
            "score",
            1,
            [
                new("low", 0.3333),
                new("medium", 0.3333),
                new("high", 0.3333),
            ],
            DecisionPrecision.FourDecimalPlaces);

        Assert.Equal(1, valid.Score!.Value);
        Assert.Equal(0.9999, valid.ExpectedScore, precision: 12);
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            0.9998,
            [
                new("low", 0.3333),
                new("medium", 0.3333),
                new("high", 0.3333),
            ],
            DecisionPrecision.FourDecimalPlaces));
    }

    [Fact]
    public void RoundedScoreValidation_UsesClippedTwoDecimalFeasibleExtremes()
    {
        ScoreDecisionAnswer valid = new(
            "score",
            0.02,
            [
                new("low", 0.99),
                new("medium", 0.01),
                new("high", 0),
            ],
            DecisionPrecision.TwoDecimalPlaces);

        Assert.Equal(0.02, valid.Score!.Value);
        Assert.Equal(0.01, valid.ExpectedScore, precision: 12);
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            0.03,
            [
                new("low", 0.99),
                new("medium", 0.01),
                new("high", 0),
            ],
            DecisionPrecision.TwoDecimalPlaces));
    }

    [Fact]
    public void RoundedScoreValidation_PreservesHighPrecisionSlackWithoutRepair()
    {
        ScoreDecisionAnswer above = new(
            "score",
            0.5,
            [new("low", 0.5000004), new("high", 0.5)]);
        ScoreDecisionAnswer below = new(
            "score",
            0.5,
            [new("low", 0.4999996), new("high", 0.5)]);

        Assert.Equal(0.5, above.ExpectedScore, precision: 12);
        Assert.Equal(0.5, below.ExpectedScore, precision: 12);
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer(
            "score",
            0.500002,
            [new("low", 0.5000004), new("high", 0.5)]));
    }

    [Fact]
    public void ScoreAnswer_PreservesAbsentNativeScoreAndRoundedExpectedScore()
    {
        ScoreDecisionAnswer answer = new(
            "score",
            null,
            [
                new("low", 0),
                new("medium", 0),
                new("high", 0.01),
                new("highest", 1),
            ],
            DecisionPrecision.TwoDecimalPlaces);

        Assert.Null(answer.Score);
        Assert.Equal(3.02, answer.ExpectedScore, precision: 12);

        string json = JsonSerializer.Serialize(answer, TestJsonSerializerContext.Default.ScoreDecisionAnswer);
        ScoreDecisionAnswer roundtrip = JsonSerializer.Deserialize(
            json,
            TestJsonSerializerContext.Default.ScoreDecisionAnswer)!;
        Assert.Null(roundtrip.Score);
        Assert.Equal(3.02, roundtrip.ExpectedScore, precision: 12);

        ScoreDecisionAnswer zero = new("score", 0, [new("low", 1), new("high", 0)]);
        Assert.Equal(0, zero.Score!.Value);
    }

    [Fact]
    public void DynamicIds_AreCorrelatedPerRequestAndRejectStaleCandidates()
    {
        string activeId = "route|" + Guid.NewGuid().ToString("N");
        ChoiceDecisionQuestion question = new(
            "route",
            "Select the active route.",
            [new DecisionCandidate(activeId, "Current route")]);
        DecisionRequest request = CreateRequest(question);

        DecisionResponse response = new(
            request,
            [new ChoiceDecisionAnswer("route", activeId, [new DecisionProbability(activeId, 1)])]);

        Assert.Equal(activeId, ((ChoiceDecisionAnswer)response.Answers[0]).SelectedCandidateId);
        Assert.Throws<DecisionProtocolException>(() => new DecisionResponse(
            request,
            [new ChoiceDecisionAnswer("route", "stale", [new DecisionProbability("stale", 1)])]));
    }

    [Fact]
    public void FeatureProjection_UsesNamedNativeDoubleCoordinatesAndProvenance()
    {
        DecisionResponse response = CreateMixedResponse();
        DecisionFeatureSchema schema = new(
            "routing-decision-v1",
            2,
            [
                new("binary.true", "binary", DecisionFeatureValueKind.BinaryTrueProbability),
                new("choice.blue", "choice", DecisionFeatureValueKind.ChoiceProbability, "blue|candidate"),
                new("score.high", "score", DecisionFeatureValueKind.ScoreProbability, "high"),
                new("score.value", "score", DecisionFeatureValueKind.Score),
                new("score.expected", "score", DecisionFeatureValueKind.ExpectedScore),
            ]);

        DecisionFeatureVector vector = DecisionFeatureProjection.Project(response, schema);

        Assert.Equal("routing-decision-v1", vector.Schema.Id);
        Assert.Equal(2, vector.Schema.Version);
        Assert.Equal(["binary.true", "choice.blue", "score.high", "score.value", "score.expected"], vector.Values.Select(static value => value.Name));
        Assert.Equal(0.75, vector.Values[0].Value);
        Assert.Equal(0.5, vector.Values[1].Value);
        Assert.Equal(0.7, vector.Values[2].Value);
        Assert.Equal(1.6, vector.Values[3].Value);
        Assert.Equal(1.6, vector.Values[4].Value, precision: 12);
        Assert.Equal("test-provider", vector.Provenance!.ProviderName);
    }

    [Fact]
    public void FeatureProjection_RejectsMissingReportedScoreButProjectsExpectedScore()
    {
        DecisionRequest request = CreateRequest(
            new ScoreDecisionQuestion(
                "score",
                "Score the request.",
                [new("low", "Low"), new("medium", "Medium"), new("high", "High"), new("highest", "Highest")]));
        DecisionResponse response = new(
            request,
            [
                new ScoreDecisionAnswer(
                    "score",
                    null,
                    [new("low", 0), new("medium", 0), new("high", 0.01), new("highest", 1)],
                    DecisionPrecision.TwoDecimalPlaces),
            ]);

        DecisionFeatureSchema expectedSchema = new(
            "score-expected-v1",
            1,
            [new("score.expected", "score", DecisionFeatureValueKind.ExpectedScore)]);
        Assert.Equal(3.02, DecisionFeatureProjection.Project(response, expectedSchema).Values[0].Value, precision: 12);

        DecisionFeatureSchema reportedSchema = new(
            "score-reported-v1",
            1,
            [new("score.reported", "score", DecisionFeatureValueKind.Score)]);
        Assert.Throws<DecisionProtocolException>(() => DecisionFeatureProjection.Project(response, reportedSchema));
    }

    [Fact]
    public void ReflectionDisabledJson_UsesExplicitSourceGeneratedContracts()
    {
        DecisionQuestion question = new BinaryDecisionQuestion("q", "Is this valid?");
        DecisionRequest request = DecisionRequest.Create(
            new DecisionState { Value = "payload" },
            TestJsonSerializerContext.Default.DecisionState,
            [question]);

        string json = JsonSerializer.Serialize(request, TestJsonSerializerContext.Default.DecisionRequest);
        DecisionRequest? roundtrip = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.DecisionRequest);

        Assert.NotNull(roundtrip);
        Assert.Equal("payload", roundtrip.State.GetProperty("value").GetString());
        Assert.Equal("q", roundtrip.Questions[0].Id);
    }

    [Fact]
    public void ReflectionDisabledJson_RoundTripsAnswerAndResponseSnapshots()
    {
        DecisionResponse original = CreateMixedResponse();

        string json = JsonSerializer.Serialize(original, TestJsonSerializerContext.Default.DecisionResponse);
        DecisionResponse? roundtrip = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.DecisionResponse);

        Assert.NotNull(roundtrip);
        Assert.Equal(3, roundtrip.Answers.Count);
        Assert.Equal("binary", ((BinaryDecisionAnswer)roundtrip.Answers[0]).AdditionalProperties!["kind"]!.ToString());
        Assert.Equal("choice", ((ChoiceDecisionAnswer)roundtrip.Answers[1]).AdditionalProperties!["kind"]!.ToString());
        Assert.Equal("score", ((ScoreDecisionAnswer)roundtrip.Answers[2]).AdditionalProperties!["kind"]!.ToString());
        Assert.Equal("response", roundtrip.AdditionalProperties!["kind"]!.ToString());
        Assert.Equal(1.6, ((ScoreDecisionAnswer)roundtrip.Answers[2]).Score!.Value);
    }

    [Fact]
    public async Task ClientCancellation_IsNotConvertedToFabricatedResponse()
    {
        using CancellationClient client = new();
        DecisionRequest request = CreateRequest(new BinaryDecisionQuestion("q", "Is this valid?"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync(request, cancellationToken: cancellation.Token));
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Projection_IsSafeToRunConcurrentlyOnAnOwnedResponse()
    {
        DecisionResponse response = CreateMixedResponse();
        DecisionFeatureSchema schema = new(
            "concurrent-v1",
            1,
            [new("choice.blue", "choice", DecisionFeatureValueKind.ChoiceProbability, "blue|candidate")]);

        DecisionFeatureVector[] vectors = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(() => DecisionFeatureProjection.Project(response, schema))));

        Assert.All(vectors, vector => Assert.Equal(0.5, vector.Values[0].Value));
    }

    [Fact]
    public void BindingMapperFailure_IsPropagatedWithoutPartialSuccess()
    {
        DecisionResponse response = CreateMixedResponse();
        DecisionResultBinding<DecisionResult> binding = new(
            TestJsonSerializerContext.Default.DecisionResult,
            _ => throw new InvalidOperationException("mapper failed"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => response.Bind(binding));
        Assert.Equal("mapper failed", exception.Message);
    }

    [Fact]
    public void RequestsAndResponses_OwnCollectionSnapshots()
    {
        List<DecisionQuestion> questions = [new BinaryDecisionQuestion("q", "Is this valid?")];
        DecisionRequest request = CreateRequest(questions.ToArray());
        questions.Clear();

        List<DecisionAnswer> answers = [new BinaryDecisionAnswer("q", 0.75)];
        DecisionResponse response = new(request, answers);
        answers.Clear();

        Assert.Single(request.Questions);
        Assert.Single(response.Answers);
    }

    [Fact]
    public void OptionsClone_OwnsProviderExtensionSnapshot()
    {
        DecisionOptions options = new()
        {
            ModelId = "model",
            AdditionalProperties = new AdditionalPropertiesDictionary { ["mode"] = "original" },
        };

        DecisionOptions clone = options.Clone();
        options.AdditionalProperties["mode"] = "changed";

        Assert.Equal("model", clone.ModelId);
        Assert.Equal("original", clone.AdditionalProperties!["mode"]);
    }

    [Fact]
    public void DecisionDefinition_UsesExplicitDeclarationsAndMetadataDescriptions()
    {
        DecisionDefinition<TypedDecisionResult> definition = DecisionDefinition<TypedDecisionResult>.Create(
            TestJsonSerializerContext.Default.TypedDecisionResult,
            builder =>
            {
                builder.Choice(result => result.Category);
                builder.BinaryProbability(result => result.RefundRequestProbability, "Explicit refund instruction.");
                builder.Score(
                    result => result.Satisfaction,
                    [
                        new("low", "Low"),
                        new("medium", "Medium"),
                        new("high", "High"),
                    ]);
            });

        Assert.Equal(["category", "refundRequestProbability", "satisfaction"], definition.Questions.Select(static q => q.Id));
        Assert.Equal("Classify the customer's main concern.", definition.Questions[0].Instructions);
        Assert.Equal("Explicit refund instruction.", definition.Questions[1].Instructions);
        Assert.Equal("Rate the customer's satisfaction.", definition.Questions[2].Instructions);

        ChoiceDecisionQuestion category = Assert.IsType<ChoiceDecisionQuestion>(definition.Questions[0]);
        Assert.Equal(["Billing", "Technical", "Account"], category.Candidates.Select(static candidate => candidate.Id));
        Assert.Equal(
            ["Payment, invoice, or charge concerns.", "Product defects, errors, outages, or troubleshooting.", "Sign-in, profile, or account-access concerns."],
            category.Candidates.Select(static candidate => candidate.Description));
    }

    [Fact]
    public async Task DecisionDefinition_BindsTypedResultOnceAndRetainsEvidence()
    {
        DecisionDefinition<TypedDecisionResult> definition = CreateTypedDefinition();
        using RecordingDecisionClient client = new(request => CreateTypedDefinitionResponse(request));
        DecisionOptions options = new() { ModelId = "model" };

        DecisionResponse<TypedDecisionResult> response = await client.GetResponseAsync(
            new TypedDecisionState("I need a refund for a defective product."),
            TestJsonSerializerContext.Default.TypedDecisionState,
            definition,
            options);

        Assert.Equal(1, client.CallCount);
        Assert.Same(options, client.Options);
        Assert.Equal(TicketCategory.Technical, response.Result.Category);
        Assert.Equal(0.25, response.Result.RefundRequestProbability);
        Assert.Equal(1.6, response.Result.Satisfaction);
        Assert.Equal("test-provider", response.Evidence.Provenance!.ProviderName);
        Assert.Equal(
            new Dictionary<TicketCategory, double>
            {
                [TicketCategory.Billing] = 0.2,
                [TicketCategory.Technical] = 0.5,
                [TicketCategory.Account] = 0.3,
            },
            response.GetDistribution(result => result.Category));
    }

    [Fact]
    public async Task DecisionDefinition_InferredStateOverloadUsesOneProviderInvocation()
    {
        DecisionDefinition<TypedDecisionResult> definition = CreateTypedDefinition();
        using RecordingDecisionClient client = new(request => CreateTypedDefinitionResponse(request));

        DecisionResponse<TypedDecisionResult> response = await client.GetResponseAsync(
            new TypedDecisionState("Please reverse this payment."),
            definition,
            cancellationToken: CancellationToken.None);

        Assert.Equal(TicketCategory.Technical, response.Result.Category);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task RawMetadataOverloadRejectsSelfValidResponseWithChangedQuestionDomain()
    {
        ChoiceDecisionQuestion question = new(
            "route",
            "Select the active route.",
            [new("alpha", "Alpha"), new("beta", "Beta")]);
        using RecordingDecisionClient client = new(request =>
        {
            DecisionRequest staleRequest = new(
                request.State.Clone(),
                [
                    new ChoiceDecisionQuestion(
                        "route",
                        "Select the active route.",
                        [new("beta", "Changed beta"), new("alpha", "Alpha")]),
                ]);
            return new DecisionResponse(
                staleRequest,
                [
                    new ChoiceDecisionAnswer(
                        "route",
                        "beta",
                        [new("beta", 0.75), new("alpha", 0.25)]),
                ]);
        });

        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(
            new DecisionState { Value = "request" },
            TestJsonSerializerContext.Default.DecisionState,
            [question]));
    }

    [Fact]
    public async Task RawMetadataOverloadRejectsSelfValidResponseWithStaleState()
    {
        BinaryDecisionQuestion question = new("valid", "Is this valid?");
        using RecordingDecisionClient client = new(request =>
        {
            using JsonDocument staleState = JsonDocument.Parse("""{"value":"stale"}""");
            DecisionRequest staleRequest = new(staleState.RootElement.Clone(), [question]);
            return new DecisionResponse(staleRequest, [new BinaryDecisionAnswer("valid", 0.5)]);
        });

        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(
            new DecisionState { Value = "request" },
            TestJsonSerializerContext.Default.DecisionState,
            [question]));
    }

    [Fact]
    public async Task DecisionDefinition_ReflectionMetadataPathRejectsSelfValidStaleResponse()
    {
        DefaultJsonTypeInfoResolver resolver = new();
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        DecisionDefinition<ReflectionResult> definition = DecisionDefinition<ReflectionResult>.Create(
            builder => builder.BinaryProbability(result => result.Probability),
            options);
        using RecordingDecisionClient client = new(request =>
        {
            DecisionRequest staleRequest = new(
                request.State.Clone(),
                [new BinaryDecisionQuestion(request.Questions[0].Id, "A stale instruction.")]);
            return new DecisionResponse(staleRequest, [new BinaryDecisionAnswer(request.Questions[0].Id, 0.5)]);
        });

        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(
            new ReflectionState { Value = "request" },
            definition));
        Assert.Same(resolver, options.TypeInfoResolver);
        Assert.False(options.IsReadOnly);
    }

    [Fact]
    public void DecisionDefinition_RejectsNestedDuplicateAndFlagsSelectors()
    {
        Assert.Throws<ArgumentException>(() => DecisionDefinition<TypedDecisionResult>.Create(
            TestJsonSerializerContext.Default.TypedDecisionResult,
            builder => builder.Choice(result => result.Nested.Category)));

        Assert.Throws<ArgumentException>(() => DecisionDefinition<TypedDecisionResult>.Create(
            TestJsonSerializerContext.Default.TypedDecisionResult,
            builder =>
            {
                builder.BinaryProbability(result => result.RefundRequestProbability);
                builder.Score(result => result.RefundRequestProbability, [new("low", "Low"), new("high", "High")]);
            }));

        Assert.Throws<ArgumentException>(() => DecisionDefinition<FlagsResult>.Create(
            TestJsonSerializerContext.Default.FlagsResult,
            builder => builder.Choice(result => result.Value)));
    }

    [Fact]
    public void DecisionDefinition_UsesPropertyDescriptionAndFluentPrecedence()
    {
        DecisionDefinition<PropertyDescriptionResult> propertyDefinition = DecisionDefinition<PropertyDescriptionResult>.Create(
            TestJsonSerializerContext.Default.PropertyDescriptionResult,
            builder => builder.BinaryProbability(result => result.Probability));
        Assert.Equal("Description on the property.", propertyDefinition.Questions[0].Instructions);

        DecisionDefinition<PropertyDescriptionResult> overrideDefinition = DecisionDefinition<PropertyDescriptionResult>.Create(
            TestJsonSerializerContext.Default.PropertyDescriptionResult,
            builder => builder.BinaryProbability(result => result.Probability, "Fluent instruction."));
        Assert.Equal("Fluent instruction.", overrideDefinition.Questions[0].Instructions);
    }

    [Fact]
    public void DecisionDefinition_UsesExactConfiguredJsonPropertyName()
    {
        DecisionDefinition<JsonNamedResult> definition = DecisionDefinition<JsonNamedResult>.Create(
            TestJsonSerializerContext.Default.JsonNamedResult,
            builder => builder.BinaryProbability(result => result.Probability));

        Assert.Equal("refund_probability", definition.Questions[0].Id);
    }

    [Fact]
    public async Task DecisionDefinition_PropagatesCancellationWithoutFabricatedTypedResult()
    {
        DecisionDefinition<TypedDecisionResult> definition = CreateTypedDefinition();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using RecordingDecisionClient client = new(request => CreateTypedDefinitionResponse(request), cancel: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResponseAsync(
                new TypedDecisionState("cancelled"),
                TestJsonSerializerContext.Default.TypedDecisionState,
                definition,
                cancellationToken: cancellation.Token));
        Assert.Equal(1, client.CallCount);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("stale")]
    [InlineData("missing")]
    [InlineData("reordered")]
    [InlineData("extra")]
    [InlineData("wrong-kind")]
    public async Task DecisionDefinition_RejectsResponseNotMatchingSentRequest(string mutation)
    {
        DecisionDefinition<TypedDecisionResult> definition = CreateTypedDefinition();
        bool providerReturnedResponse = false;
        using RecordingDecisionClient client = new(request =>
        {
            DecisionResponse response = CreateMismatchedResponse(request, mutation);
            _ = new DecisionResponse(
                response.Request,
                response.Answers,
                response.Provenance,
                response.Usage,
                response.RawRepresentation,
                response.AdditionalProperties);
            providerReturnedResponse = true;
            return response;
        });

        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(
            new TypedDecisionState("request"),
            TestJsonSerializerContext.Default.TypedDecisionState,
            definition));
        Assert.True(providerReturnedResponse);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task DecisionDefinition_AllowsEquivalentOwnedResponseSnapshots()
    {
        DecisionDefinition<TypedDecisionResult> definition = CreateTypedDefinition();
        using RecordingDecisionClient client = new(request => CreateTypedDefinitionResponse(CloneRequest(request)));

        DecisionResponse<TypedDecisionResult> response = await client.GetResponseAsync(
            new TypedDecisionState("request"),
            TestJsonSerializerContext.Default.TypedDecisionState,
            definition);

        Assert.Equal(TicketCategory.Technical, response.Result.Category);
    }

    [Fact]
    public void DecisionDefinition_RejectsReadOnlyUnboundProperties()
    {
        Assert.Throws<ArgumentException>(() => DecisionDefinition<ReadOnlyResult>.Create(
            TestJsonSerializerContext.Default.ReadOnlyResult,
            builder => builder.BinaryProbability(result => result.Probability)));
    }

    [Fact]
    public async Task DecisionDefinition_RejectsRequiredUnmappedProperties()
    {
        DecisionDefinition<RequiredUnmappedResult> definition = DecisionDefinition<RequiredUnmappedResult>.Create(
            TestJsonSerializerContext.Default.RequiredUnmappedResult,
            builder => builder.BinaryProbability(result => result.Probability));
        using RecordingDecisionClient client = new(request => new DecisionResponse(
            request,
            [new BinaryDecisionAnswer(request.Questions[0].Id, 0.25)]));

        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(
            new TypedDecisionState("request"),
            definition));
    }

    [Fact]
    public void DecisionDefinition_RejectsPropertySpecificEnumConverters()
    {
        Assert.Throws<NotSupportedException>(() => DecisionDefinition<PropertyConverterResult>.Create(
            TestJsonSerializerContext.Default.PropertyConverterResult,
            builder => builder.Choice(result => result.Category)));
    }

    [Fact]
    public async Task DecisionDefinition_UsesConfiguredEnumConverterForExactCandidateIds()
    {
        DecisionDefinition<CustomEnumResult> definition = DecisionDefinition<CustomEnumResult>.Create(
            TestJsonSerializerContext.Default.CustomEnumResult,
            builder => builder.Choice(result => result.Category));
        ChoiceDecisionQuestion question = Assert.IsType<ChoiceDecisionQuestion>(definition.Questions[0]);
        Assert.Equal(["billing-id", "technical-id", "account-id"], question.Candidates.Select(static candidate => candidate.Id));

        using RecordingDecisionClient client = new(request => new DecisionResponse(
            request,
            [new ChoiceDecisionAnswer(
                request.Questions[0].Id,
                "technical-id",
                [new("billing-id", 0.2), new("technical-id", 0.5), new("account-id", 0.3)])]));

        DecisionResponse<CustomEnumResult> response = await client.GetResponseAsync(
            new JsonElementState(),
            definition);
        Assert.Equal(CustomCategory.Technical, response.Result.Category);
    }

    [Fact]
    public void DecisionDefinition_UsesConfiguredEnumNamingPolicy()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        DecisionDefinition<NamingPolicyResult> definition = DecisionDefinition<NamingPolicyResult>.Create(
            builder => builder.Choice(result => result.Category),
            options);

        ChoiceDecisionQuestion question = Assert.IsType<ChoiceDecisionQuestion>(definition.Questions[0]);
        Assert.Equal(["billing", "technical", "account"], question.Candidates.Select(static candidate => candidate.Id));
    }

    [Fact]
    public void DecisionDefinition_ReflectionDisabledOptionsRequireExplicitResolver()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        Assert.Throws<NotSupportedException>(() => DecisionDefinition<ReflectionResult>.Create(
            builder => builder.BinaryProbability(result => result.Probability),
            options));
        Assert.Null(options.TypeInfoResolver);
        Assert.False(options.IsReadOnly);
    }

    [Fact]
    public async Task DecisionDefinition_RejectsMaterializationWhenSerializationGetterMasksActualValue()
    {
        DefaultJsonTypeInfoResolver resolver = new();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(MaskedGetterResult))
            {
                JsonPropertyInfo property = typeInfo.Properties.Single(static property => property.Name == nameof(MaskedGetterResult.Probability));
                property.Set = static (obj, value) => ((MaskedGetterResult)obj).Probability = 0;
                property.Get = static _ => 0.75;
            }
        });
        JsonSerializerOptions options = new() { TypeInfoResolver = resolver };
        DecisionDefinition<MaskedGetterResult> definition = DecisionDefinition<MaskedGetterResult>.Create(
            builder => builder.BinaryProbability(result => result.Probability),
            options);
        using RecordingDecisionClient client = new(request => new DecisionResponse(
            request,
            [new BinaryDecisionAnswer(request.Questions[0].Id, 0.75)]));

        using JsonDocument state = JsonDocument.Parse("{}");
        await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(state.RootElement.Clone(), definition));
    }

    [Fact]
    public async Task DecisionDefinition_PreservesGlobalWriteAsStringAndDefaultIgnoreSettings()
    {
        string serialized = JsonSerializer.Serialize(
            new GlobalConfiguredResult { Probability = 0.25, NativeScore = 1.6, ExpectedScore = 1.6 },
            GlobalConfiguredJsonSerializerContext.Default.GlobalConfiguredResult);
        Assert.Contains("\"probability\":\"0.25\"", serialized);

        DecisionResponse<GlobalConfiguredResult> response = await BindConfiguredResult(
            GlobalConfiguredJsonSerializerContext.Default.GlobalConfiguredResult,
            builder =>
            {
                builder.BinaryProbability(result => result.Probability);
                builder.Score(result => result.NativeScore, CreateLevels());
                builder.ExpectedScore(result => result.ExpectedScore, CreateLevels());
            },
            zero: false);

        Assert.Equal(0.25, response.Result.Probability);
        Assert.Equal(1.6, response.Result.NativeScore, precision: 12);
        Assert.Equal(1.6, response.Result.ExpectedScore, precision: 12);
        Assert.Equal(3, response.Evidence.Answers.Count);
        Assert.Equal(3, response.Evidence.Request.Questions.Count);
    }

    [Fact]
    public async Task DecisionDefinition_PreservesGlobalDefaultIgnoredNativeZeroAndExpectedScore()
    {
        string serialized = JsonSerializer.Serialize(
            new GlobalConfiguredResult(),
            GlobalConfiguredJsonSerializerContext.Default.GlobalConfiguredResult);
        Assert.DoesNotContain("probability", serialized);
        Assert.DoesNotContain("nativeScore", serialized);
        Assert.DoesNotContain("expectedScore", serialized);

        DecisionResponse<GlobalConfiguredResult> response = await BindConfiguredResult(
            GlobalConfiguredJsonSerializerContext.Default.GlobalConfiguredResult,
            builder =>
            {
                builder.BinaryProbability(result => result.Probability);
                builder.Score(result => result.NativeScore, CreateLevels());
                builder.ExpectedScore(result => result.ExpectedScore, CreateLevels());
            },
            zero: true);

        Assert.Equal(0, response.Result.Probability);
        Assert.Equal(0, response.Result.NativeScore);
        Assert.Equal(0, response.Result.ExpectedScore);
        Assert.Equal(0, ((ScoreDecisionAnswer)response.Evidence.Answers[1]).Score!.Value);
        Assert.Null(((ScoreDecisionAnswer)response.Evidence.Answers[2]).Score);
        Assert.Equal(3, response.Evidence.Answers.Count);
    }

    [Fact]
    public async Task DecisionDefinition_PreservesPropertyWriteAsStringAndDefaultIgnoreSettings()
    {
        string serialized = JsonSerializer.Serialize(
            new PropertyConfiguredResult { Probability = 0.25, NativeScore = 1.6, ExpectedScore = 1.6 },
            PropertyConfiguredJsonSerializerContext.Default.PropertyConfiguredResult);
        Assert.Contains("\"probability\":\"0.25\"", serialized);

        DecisionResponse<PropertyConfiguredResult> response = await BindConfiguredResult(
            PropertyConfiguredJsonSerializerContext.Default.PropertyConfiguredResult,
            builder =>
            {
                builder.BinaryProbability(result => result.Probability);
                builder.Score(result => result.NativeScore, CreateLevels());
                builder.ExpectedScore(result => result.ExpectedScore, CreateLevels());
            },
            zero: false);

        Assert.Equal(0.25, response.Result.Probability);
        Assert.Equal(1.6, response.Result.NativeScore, precision: 12);
        Assert.Equal(1.6, response.Result.ExpectedScore, precision: 12);
        Assert.Equal(3, response.Evidence.Answers.Count);
    }

    [Fact]
    public async Task DecisionDefinition_PreservesPropertyDefaultIgnoredNativeZeroAndExpectedScore()
    {
        string serialized = JsonSerializer.Serialize(
            new PropertyConfiguredResult(),
            PropertyConfiguredJsonSerializerContext.Default.PropertyConfiguredResult);
        Assert.DoesNotContain("probability", serialized);
        Assert.DoesNotContain("nativeScore", serialized);
        Assert.DoesNotContain("expectedScore", serialized);

        DecisionResponse<PropertyConfiguredResult> response = await BindConfiguredResult(
            PropertyConfiguredJsonSerializerContext.Default.PropertyConfiguredResult,
            builder =>
            {
                builder.BinaryProbability(result => result.Probability);
                builder.Score(result => result.NativeScore, CreateLevels());
                builder.ExpectedScore(result => result.ExpectedScore, CreateLevels());
            },
            zero: true);

        Assert.Equal(0, response.Result.Probability);
        Assert.Equal(0, response.Result.NativeScore);
        Assert.Equal(0, response.Result.ExpectedScore);
        Assert.Equal(0, ((ScoreDecisionAnswer)response.Evidence.Answers[1]).Score!.Value);
        Assert.Null(((ScoreDecisionAnswer)response.Evidence.Answers[2]).Score);
    }

    [Fact]
    public async Task DecisionDefinition_ExpectedScoreUsesObservedDistributionAndReportedScoreRequiresNativeValue()
    {
        DecisionDefinition<TypedDecisionResult> expectedDefinition = CreateExpectedScoreDefinition();
        using RecordingDecisionClient expectedClient = new(request => CreateTypedDefinitionResponse(request, reportedScore: null));

        DecisionResponse<TypedDecisionResult> expectedResponse = await expectedClient.GetResponseAsync(
            new TypedDecisionState("request"),
            TestJsonSerializerContext.Default.TypedDecisionState,
            expectedDefinition);

        Assert.Equal(1.6, expectedResponse.Result.Satisfaction, precision: 12);
        Assert.Null(((ScoreDecisionAnswer)expectedResponse.Evidence.Answers[2]).Score);

        DecisionDefinition<TypedDecisionResult> reportedDefinition = CreateTypedDefinition();
        using RecordingDecisionClient reportedClient = new(request => CreateTypedDefinitionResponse(request, reportedScore: null));
        await Assert.ThrowsAsync<DecisionProtocolException>(() => reportedClient.GetResponseAsync(
            new TypedDecisionState("request"),
            TestJsonSerializerContext.Default.TypedDecisionState,
            reportedDefinition));
    }

    private static DecisionScoreLevel[] CreateLevels() =>
    [
        new("low", "Low"),
        new("medium", "Medium"),
        new("high", "High"),
    ];

    private static async Task<DecisionResponse<TResult>> BindConfiguredResult<TResult>(
        JsonTypeInfo<TResult> resultTypeInfo,
        Action<DecisionDefinition<TResult>.Builder> configure,
        bool zero)
    {
        DecisionDefinition<TResult> definition = DecisionDefinition<TResult>.Create(resultTypeInfo, configure);
        using RecordingDecisionClient client = new(request => CreateConfiguredSerializationResponse(request, zero));
        DecisionResponse<TResult> response = await client.GetResponseAsync(
            new TypedDecisionState("request"),
            TestJsonSerializerContext.Default.TypedDecisionState,
            definition);
        Assert.Equal(1, client.CallCount);
        return response;
    }

    private static DecisionResponse CreateConfiguredSerializationResponse(DecisionRequest request, bool zero)
    {
        double binary = zero ? 0 : 0.25;
        double nativeScore = zero ? 0 : 1.6;
        double[] distribution = zero ? [1, 0, 0] : [0.1, 0.2, 0.7];

        return new DecisionResponse(
            request,
            [
                new BinaryDecisionAnswer(request.Questions[0].Id, binary),
                new ScoreDecisionAnswer(
                    request.Questions[1].Id,
                    nativeScore,
                    distribution.Select((value, index) => new DecisionProbability(
                        ((ScoreDecisionQuestion)request.Questions[1]).Levels[index].Id,
                        value)).ToArray(),
                    DecisionPrecision.TwoDecimalPlaces),
                new ScoreDecisionAnswer(
                    request.Questions[2].Id,
                    null,
                    distribution.Select((value, index) => new DecisionProbability(
                        ((ScoreDecisionQuestion)request.Questions[2]).Levels[index].Id,
                        value)).ToArray(),
                    DecisionPrecision.TwoDecimalPlaces),
            ]);
    }

    private static DecisionDefinition<TypedDecisionResult> CreateTypedDefinition() =>
        DecisionDefinition<TypedDecisionResult>.Create(
            TestJsonSerializerContext.Default.TypedDecisionResult,
            builder =>
            {
                builder.Choice(result => result.Category);
                builder.BinaryProbability(result => result.RefundRequestProbability);
                builder.Score(
                    result => result.Satisfaction,
                    [
                        new("low", "Low"),
                        new("medium", "Medium"),
                        new("high", "High"),
                    ]);
            });

    private static DecisionDefinition<TypedDecisionResult> CreateExpectedScoreDefinition() =>
        DecisionDefinition<TypedDecisionResult>.Create(
            TestJsonSerializerContext.Default.TypedDecisionResult,
            builder =>
            {
                builder.Choice(result => result.Category);
                builder.BinaryProbability(result => result.RefundRequestProbability);
                builder.ExpectedScore(
                    result => result.Satisfaction,
                    [
                        new("low", "Low"),
                        new("medium", "Medium"),
                        new("high", "High"),
                    ]);
            });

    private static DecisionResponse CreateTypedDefinitionResponse(DecisionRequest request, double? reportedScore = 1.6)
    {
        return new DecisionResponse(
            request,
            [
                new ChoiceDecisionAnswer(
                    request.Questions[0].Id,
                    ((ChoiceDecisionQuestion)request.Questions[0]).Candidates[1].Id,
                    [
                        new(((ChoiceDecisionQuestion)request.Questions[0]).Candidates[0].Id, 0.2),
                        new(((ChoiceDecisionQuestion)request.Questions[0]).Candidates[1].Id, 0.5),
                        new(((ChoiceDecisionQuestion)request.Questions[0]).Candidates[2].Id, 0.3),
                    ]),
                new BinaryDecisionAnswer(request.Questions[1].Id, 0.25),
                new ScoreDecisionAnswer(
                    request.Questions[2].Id,
                    reportedScore,
                    [
                        new(((ScoreDecisionQuestion)request.Questions[2]).Levels[0].Id, 0.1),
                        new(((ScoreDecisionQuestion)request.Questions[2]).Levels[1].Id, 0.2),
                        new(((ScoreDecisionQuestion)request.Questions[2]).Levels[2].Id, 0.7),
                    ],
                    DecisionPrecision.TwoDecimalPlaces),
            ],
            new DecisionProvenance(providerName: "test-provider"));
    }

    private static DecisionResponse CreateMismatchedResponse(DecisionRequest request, string mutation)
    {
        if (mutation == "wrong-kind")
        {
            DecisionRequest wrongKindRequest = new(
                request.State,
                [
                    new BinaryDecisionQuestion(request.Questions[0].Id, request.Questions[0].Instructions),
                    request.Questions[1],
                    request.Questions[2],
                ]);
            return new DecisionResponse(
                wrongKindRequest,
                [
                    new BinaryDecisionAnswer(wrongKindRequest.Questions[0].Id, 0.5),
                    new BinaryDecisionAnswer(wrongKindRequest.Questions[1].Id, 0.25),
                    new ScoreDecisionAnswer(
                        wrongKindRequest.Questions[2].Id,
                        1.6,
                        [new("low", 0.1), new("medium", 0.2), new("high", 0.7)],
                        DecisionPrecision.TwoDecimalPlaces),
                ]);
        }

        ChoiceDecisionQuestion original = (ChoiceDecisionQuestion)request.Questions[0];
        IReadOnlyList<DecisionCandidate> candidates = mutation switch
        {
            "case" =>
            [
                new("Billing", "Payment, invoice, or charge concerns."),
                new("technical", "Product defects, errors, outages, or troubleshooting."),
                new("Account", "Sign-in, profile, or account-access concerns."),
            ],
            "stale" =>
            [
                new("Billing", "Old billing instruction."),
                new("Technical", "Old technical instruction."),
                new("Account", "Old account instruction."),
            ],
            "missing" =>
            [
                original.Candidates[0],
                original.Candidates[1],
            ],
            "reordered" =>
            [
                original.Candidates[1],
                original.Candidates[0],
                original.Candidates[2],
            ],
            "extra" =>
            [
                original.Candidates[0],
                original.Candidates[1],
                original.Candidates[2],
                new("Other", "Other"),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        DecisionQuestion changedChoice = mutation == "stale"
            ? new ChoiceDecisionQuestion(original.Id, "A stale instruction.", candidates)
            : new ChoiceDecisionQuestion(original.Id, original.Instructions, candidates);
        DecisionRequest changedRequest = new(
            request.State,
            [changedChoice, request.Questions[1], request.Questions[2]]);
        return CreateResponseForRequest(changedRequest);
    }

    private static DecisionResponse CreateResponseForRequest(DecisionRequest request)
    {
        ChoiceDecisionQuestion choice = (ChoiceDecisionQuestion)request.Questions[0];
        ScoreDecisionQuestion score = (ScoreDecisionQuestion)request.Questions[2];
        return new DecisionResponse(
            request,
            [
                new ChoiceDecisionAnswer(
                    choice.Id,
                    choice.Candidates[0].Id,
                    choice.Candidates.Select(static (candidate, index) => new DecisionProbability(candidate.Id, index == 0 ? 1 : 0)).ToArray()),
                new BinaryDecisionAnswer(request.Questions[1].Id, 0.25),
                new ScoreDecisionAnswer(
                    score.Id,
                    1,
                    score.Levels.Select(static (level, index) => new DecisionProbability(level.Id, index == 1 ? 1 : 0)).ToArray(),
                    DecisionPrecision.TwoDecimalPlaces),
            ]);
    }

    private static DecisionRequest CloneRequest(DecisionRequest request) =>
        new(
            request.State.Clone(),
            request.Questions.Select(CloneQuestion).ToArray());

    private static DecisionQuestion CloneQuestion(DecisionQuestion question) =>
        question switch
        {
            BinaryDecisionQuestion binary => new BinaryDecisionQuestion(binary.Id, binary.Instructions, binary.TrueDescription, binary.FalseDescription),
            ChoiceDecisionQuestion choice => new ChoiceDecisionQuestion(
                choice.Id,
                choice.Instructions,
                choice.Candidates.Select(static candidate => new DecisionCandidate(candidate.Id, candidate.Description)).ToArray()),
            ScoreDecisionQuestion score => new ScoreDecisionQuestion(
                score.Id,
                score.Instructions,
                score.Levels.Select(static level => new DecisionScoreLevel(level.Id, level.Description)).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(question)),
        };

    private static DecisionRequest CreateRequest(params DecisionQuestion[] questions)
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return new(document.RootElement.Clone(), questions);
    }

    private static DecisionResponse CreateMixedResponse()
    {
        DecisionRequest request = CreateRequest(
            new BinaryDecisionQuestion("binary", "Is the request approved?"),
            new ChoiceDecisionQuestion(
                "choice",
                "Select a color.",
                [
                    new("red|candidate", "Red"),
                    new("blue|candidate", "Blue"),
                    new("green|candidate", "Green"),
                ]),
            new ScoreDecisionQuestion(
                "score",
                "Score the request.",
                [
                    new("low", "Low"),
                    new("medium", "Medium"),
                    new("high", "High"),
                ]));

        return new DecisionResponse(
            request,
            [
                new BinaryDecisionAnswer(
                    "binary",
                    0.75,
                    additionalProperties: new Dictionary<string, object?> { ["kind"] = "binary" }),
                new ChoiceDecisionAnswer(
                    "choice",
                    "blue|candidate",
                    [
                        new("red|candidate", 0.2),
                        new("blue|candidate", 0.5),
                        new("green|candidate", 0.3),
                    ],
                    additionalProperties: new Dictionary<string, object?> { ["kind"] = "choice" }),
                new ScoreDecisionAnswer(
                    "score",
                    1.6,
                    [
                        new("low", 0.1),
                        new("medium", 0.2),
                        new("high", 0.7),
                    ],
                    DecisionPrecision.TwoDecimalPlaces,
                    additionalProperties: new Dictionary<string, object?> { ["kind"] = "score" }),
            ],
            new DecisionProvenance(providerName: "test-provider", modelId: "test-model"),
            additionalProperties: new Dictionary<string, object?> { ["kind"] = "response" });
    }

    internal enum Classification
    {
        Approved,
        Rejected,
    }

    internal enum TicketCategory
    {
        [Description("Payment, invoice, or charge concerns.")]
        Billing,

        [Description("Product defects, errors, outages, or troubleshooting.")]
        Technical,

        [Description("Sign-in, profile, or account-access concerns.")]
        Account,
    }

    [Flags]
    internal enum FlagCategory
    {
        None = 0,
        First = 1,
        Second = 2,
    }

    internal sealed record TypedDecisionResult(
        [Description("Classify the customer's main concern.")] TicketCategory Category,
        [Description("Is the customer requesting a refund or payment reversal?")] double RefundRequestProbability,
        [Description("Rate the customer's satisfaction.")] double Satisfaction)
    {
        public NestedResult Nested { get; } = new();
    }

    internal sealed record NestedResult
    {
        public TicketCategory Category { get; init; }
    }

    internal sealed record TypedDecisionState(string Message);

    internal sealed record PropertyDescriptionResult
    {
        [Description("Description on the property.")]
        public double Probability { get; init; }
    }

    internal sealed record JsonNamedResult
    {
        [JsonPropertyName("refund_probability")]
        public double Probability { get; init; }
    }

    internal sealed class ReadOnlyResult
    {
        public double Probability { get; }
    }

    internal sealed class RequiredUnmappedResult
    {
        public double Probability { get; set; }

        [JsonRequired]
        public string Required { get; set; } = null!;
    }

    [JsonConverter(typeof(CustomCategoryConverter))]
    internal enum CustomCategory
    {
        [Description("Billing category.")]
        Billing,

        [Description("Technical category.")]
        Technical,

        [Description("Account category.")]
        Account,
    }

    internal sealed record CustomEnumResult(CustomCategory Category);

    internal sealed record NamingPolicyResult(TicketCategory Category);

    internal sealed record PropertyConverterResult(
        [property: JsonConverter(typeof(CustomCategoryConverter))] CustomCategory Category);

    internal sealed record JsonElementState;

    internal sealed class ReflectionState
    {
        public string? Value { get; set; }
    }

    internal sealed class ReflectionResult
    {
        public double Probability { get; set; }
    }

    internal sealed class GlobalConfiguredResult
    {
        public double Probability { get; set; }
        public double NativeScore { get; set; }
        public double ExpectedScore { get; set; }
    }

    internal sealed class PropertyConfiguredResult
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Probability { get; set; }

        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double NativeScore { get; set; }

        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double ExpectedScore { get; set; }
    }

    internal sealed class MaskedGetterResult
    {
        public double Probability { get; set; }
    }

    internal sealed class CustomCategoryConverter : JsonConverter<CustomCategory>
    {
        public override CustomCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() switch
            {
                "billing-id" => CustomCategory.Billing,
                "technical-id" => CustomCategory.Technical,
                "account-id" => CustomCategory.Account,
                _ => throw new JsonException(),
            };

        public override void Write(Utf8JsonWriter writer, CustomCategory value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value switch
            {
                CustomCategory.Billing => "billing-id",
                CustomCategory.Technical => "technical-id",
                CustomCategory.Account => "account-id",
                _ => throw new JsonException(),
            });
    }

    internal sealed record FlagsResult(FlagCategory Value);

    internal sealed class DecisionState
    {
        public string? Value { get; set; }
    }

    internal sealed class DecisionResult
    {
        public bool Approved { get; set; }
        public string? Selected { get; set; }
        public double Score { get; set; }
        public double ExpectedScore { get; set; }
    }

    internal sealed class RecordingDecisionClient : IDecisionClient
    {
        private readonly Func<DecisionRequest, DecisionResponse> _responseFactory;
        private readonly bool _cancel;

        public RecordingDecisionClient(DecisionResponse response, bool cancel = false)
            : this(_ => response, cancel)
        {
        }

        public RecordingDecisionClient(Func<DecisionRequest, DecisionResponse> responseFactory, bool cancel = false)
        {
            _responseFactory = responseFactory;
            _cancel = cancel;
        }

        public int CallCount { get; private set; }

        public DecisionOptions? Options { get; private set; }

        public Task<DecisionResponse> GetResponseAsync(
            DecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Options = options;
            if (_cancel)
            {
                return Task.FromCanceled<DecisionResponse>(cancellationToken);
            }

            return Task.FromResult(_responseFactory(request));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CancellationClient : IDecisionClient
    {
        public int CallCount { get; private set; }

        public Task<DecisionResponse> GetResponseAsync(
            DecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromCanceled<DecisionResponse>(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
