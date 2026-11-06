// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable SA1402 // Test models and their source-generated context are co-located.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public sealed class DecisionRoutingProofTests
{
    [Theory]
    [InlineData(RouteKind.Fast, "fast-model")]
    [InlineData(RouteKind.Reasoning, "reasoning-model")]
    public async Task ConsumerOwnedTypedDecisionSelectorSelectsConfiguredClientAndPreservesRequest(
        RouteKind expectedRoute,
        string expectedModel)
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, "Compare the retry behavior of these two designs."),
            new(ChatRole.Assistant, "The first design retries immediately."),
        ];
        ChatOptions options = new() { ModelId = "caller-model", Temperature = 0.2f };
        using CancellationTokenSource cancellation = new();
        using RecordingDecisionClient decisionClient = new(expectedRoute);
        using ConfiguredChatClient fastClient = new("fast-model", "fast response");
        using ConfiguredChatClient reasoningClient = new("reasoning-model", "reasoning response");
        IReadOnlyDictionary<RouteKind, IChatClient> clients = new Dictionary<RouteKind, IChatClient>
        {
            [RouteKind.Fast] = fastClient,
            [RouteKind.Reasoning] = reasoningClient,
        };

        using RoutingChatClient router = CreateRouter(decisionClient, clients);
        ChatResponse response = await router.GetResponseAsync(messages, options, cancellation.Token);

        ConfiguredChatClient selected = expectedRoute == RouteKind.Fast ? fastClient : reasoningClient;
        ConfiguredChatClient unused = expectedRoute == RouteKind.Fast ? reasoningClient : fastClient;
        Assert.Equal($"{expectedRoute switch { RouteKind.Fast => "fast", _ => "reasoning" }} response", response.Text);
        Assert.Same(messages, selected.ForwardedMessages);
        Assert.NotSame(options, selected.ForwardedOptions);
        Assert.Equal(expectedModel, selected.ForwardedOptions!.ModelId);
        Assert.Equal(0.2f, selected.ForwardedOptions.Temperature);
        Assert.Equal("caller-model", options.ModelId);
        Assert.Equal(1, decisionClient.CallCount);
        Assert.Equal(1, selected.CallCount);
        Assert.Equal(0, unused.CallCount);
        Assert.Equal(cancellation.Token, decisionClient.LastCancellationToken);
        Assert.Equal(cancellation.Token, selected.LastCancellationToken);

        router.Dispose();
        Assert.Equal(0, selected.DisposeCount);
    }

    [Fact]
    public async Task ConsumerOwnedTypedDecisionSelectorForwardsStreamingResponse()
    {
        ChatMessage[] messages = [new(ChatRole.User, "Stream the reasoning route.")];
        ChatOptions options = new() { ModelId = "caller-model" };
        using RecordingDecisionClient decisionClient = new(RouteKind.Reasoning);
        using ConfiguredChatClient fastClient = new("fast-model", "fast response");
        using ConfiguredChatClient reasoningClient = new("reasoning-model", "reasoning response");
        using RoutingChatClient router = CreateRouter(
            decisionClient,
            new Dictionary<RouteKind, IChatClient>
            {
                [RouteKind.Fast] = fastClient,
                [RouteKind.Reasoning] = reasoningClient,
            });

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in router.GetStreamingResponseAsync(messages, options))
        {
            updates.Add(update);
        }

        Assert.Equal("reasoning response", Assert.Single(updates).Text);
        Assert.Same(messages, reasoningClient.ForwardedMessages);
        Assert.Equal("reasoning-model", reasoningClient.ForwardedOptions!.ModelId);
        Assert.Equal(1, decisionClient.CallCount);
        Assert.Equal(1, reasoningClient.StreamingCallCount);
        Assert.Equal(0, fastClient.StreamingCallCount);
    }

    [Fact]
    public async Task ConsumerOwnedTypedDecisionSelectorRequiresConfiguredCandidate()
    {
        using RecordingDecisionClient decisionClient = new(RouteKind.Fast) { ReturnUnconfigured = true };
        using ConfiguredChatClient fastClient = new("fast-model", "fast response");
        using ConfiguredChatClient reasoningClient = new("reasoning-model", "reasoning response");
        using RoutingChatClient router = CreateRouter(
            decisionClient,
            new Dictionary<RouteKind, IChatClient>
            {
                [RouteKind.Fast] = fastClient,
                [RouteKind.Reasoning] = reasoningClient,
            });

        await Assert.ThrowsAsync<DecisionProtocolException>(
            () => router.GetResponseAsync([new(ChatRole.User, "select a route")]));

        Assert.Equal(1, decisionClient.CallCount);
        Assert.Equal(0, fastClient.CallCount);
        Assert.Equal(0, reasoningClient.CallCount);
    }

    [Theory]
    [InlineData(RouteResponseFailure.Provider)]
    [InlineData(RouteResponseFailure.WrongQuestion)]
    public async Task ConsumerOwnedTypedDecisionSelectorPropagatesDecisionFailureBeforeChat(
        RouteResponseFailure failure)
    {
        using RecordingDecisionClient decisionClient = new(RouteKind.Fast) { Failure = failure };
        using ConfiguredChatClient fastClient = new("fast-model", "fast response");
        using ConfiguredChatClient reasoningClient = new("reasoning-model", "reasoning response");
        using RoutingChatClient router = CreateRouter(
            decisionClient,
            new Dictionary<RouteKind, IChatClient>
            {
                [RouteKind.Fast] = fastClient,
                [RouteKind.Reasoning] = reasoningClient,
            });

        Type exceptionType = failure == RouteResponseFailure.Provider
            ? typeof(DecisionClientException)
            : typeof(DecisionProtocolException);
        Exception exception = await Assert.ThrowsAsync(
            exceptionType,
            () => router.GetResponseAsync([new(ChatRole.User, "select a route")]));

        Assert.NotNull(exception);
        Assert.Equal(0, fastClient.CallCount);
        Assert.Equal(0, reasoningClient.CallCount);
    }

    [Fact]
    public async Task ConsumerOwnedTypedDecisionSelectorPropagatesCancellationBeforeChat()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using RecordingDecisionClient decisionClient = new(RouteKind.Fast);
        using ConfiguredChatClient fastClient = new("fast-model", "fast response");
        using RoutingChatClient router = CreateRouter(
            decisionClient,
            new Dictionary<RouteKind, IChatClient>
            {
                [RouteKind.Fast] = fastClient,
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.GetResponseAsync(
                [new(ChatRole.User, "select a route")],
                cancellationToken: cancellation.Token));

        Assert.Equal(1, decisionClient.CallCount);
        Assert.Equal(0, fastClient.CallCount);
    }

    private static RoutingChatClient CreateRouter(
        IDecisionClient decisionClient,
        IReadOnlyDictionary<RouteKind, IChatClient> clients) =>
        RoutingChatClient.Create(async (context, cancellationToken) =>
        {
            string requestText = string.Join(
                "\n",
                context.Messages.Select(static message => message.Text));
            RoutingState state = new(requestText, context.ChatOptions?.Tools is not null);
            DecisionResponse<RoutingDecision> response = await decisionClient.GetResponseAsync(
                state,
                DecisionRoutingJsonContext.Default.RoutingState,
                CreateDefinition(),
                cancellationToken: cancellationToken);

            RouteKind selectedRoute = response.Result.Route;
            if (!clients.TryGetValue(selectedRoute, out IChatClient? selected))
            {
                throw new InvalidOperationException($"Decision selected unconfigured route '{selectedRoute}'.");
            }

            return selected;
        });

    private static DecisionDefinition<RoutingDecision> CreateDefinition() =>
        DecisionDefinition<RoutingDecision>.Create(
            DecisionRoutingJsonContext.Default.RoutingDecision,
            definition => definition.Choice(result => result.Route));

    private sealed class RecordingDecisionClient : IDecisionClient
    {
        private readonly RouteKind _selectedRoute;

        public RecordingDecisionClient(RouteKind selectedRoute)
        {
            _selectedRoute = selectedRoute;
        }

        public int CallCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public RouteResponseFailure Failure { get; init; }

        public bool ReturnUnconfigured { get; init; }

        public Task<DecisionResponse> GetResponseAsync(
            DecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;

            if (Failure == RouteResponseFailure.Provider)
            {
                return Task.FromException<DecisionResponse>(
                    new DecisionClientException("route provider failed", isTransient: true));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<DecisionResponse>(cancellationToken);
            }

            DecisionQuestion question = request.Questions[0];
            if (Failure == RouteResponseFailure.WrongQuestion)
            {
                question = new ChoiceDecisionQuestion(
                    "wrong-route-question",
                    question.Instructions,
                    ((ChoiceDecisionQuestion)question).Candidates);
            }

            ChoiceDecisionQuestion choice = Assert.IsType<ChoiceDecisionQuestion>(question);
            string selectedId = ReturnUnconfigured
                ? "unconfigured"
                : _selectedRoute switch
                {
                    RouteKind.Fast => choice.Candidates[0].Id,
                    RouteKind.Reasoning => choice.Candidates[1].Id,
                    _ => throw new InvalidOperationException(),
                };
            DecisionRequest responseRequest = new(request.State, [question]);
            return Task.FromResult(
                new DecisionResponse(
                    responseRequest,
                    [
                        new ChoiceDecisionAnswer(
                            question.Id,
                            selectedId,
                            [
                                new(choice.Candidates[0].Id, selectedId == choice.Candidates[0].Id ? 0.9 : 0.1),
                                new(choice.Candidates[1].Id, selectedId == choice.Candidates[1].Id ? 0.9 : 0.1),
                            ]),
                    ]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConfiguredChatClient : IChatClient
    {
        private readonly string _configuredModel;
        private readonly string _responseText;

        public ConfiguredChatClient(string configuredModel, string responseText)
        {
            _configuredModel = configuredModel;
            _responseText = responseText;
        }

        public int CallCount { get; private set; }

        public int StreamingCallCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IEnumerable<ChatMessage>? ForwardedMessages { get; private set; }

        public ChatOptions? ForwardedOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ForwardedMessages = messages;
            ChatOptions forwardedOptions = options ?? new ChatOptions();
            ForwardedOptions = forwardedOptions;
            LastCancellationToken = cancellationToken;
            forwardedOptions.ModelId = _configuredModel;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCallCount++;
            ForwardedMessages = messages;
            ChatOptions forwardedOptions = options ?? new ChatOptions();
            ForwardedOptions = forwardedOptions;
            LastCancellationToken = cancellationToken;
            forwardedOptions.ModelId = _configuredModel;

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _responseText);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => DisposeCount++;
    }

    public enum RouteResponseFailure
    {
        None,
        Provider,
        WrongQuestion,
    }

    public enum RouteKind
    {
        Fast,
        Reasoning,
    }

    internal sealed record RoutingDecision(RouteKind Route);

    internal sealed record RoutingState(string RequestText, bool HasTools);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DecisionRoutingProofTests.RoutingDecision))]
[JsonSerializable(typeof(DecisionRoutingProofTests.RoutingState))]
internal sealed partial class DecisionRoutingJsonContext : JsonSerializerContext;
