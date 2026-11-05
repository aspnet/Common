// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class SemanticRoutingChatClientTests
{
    [Fact]
    public void SemanticRouting_RejectsInvalidConfiguration()
    {
        using var client = new TestChatClient();
        using var generator = new TestEmbeddingGenerator();
        var profiles = new Dictionary<IChatClient, IReadOnlyList<string>>(ChatClientReferenceComparer.Instance)
        {
            [client] = ["profile"],
        };

        Assert.Throws<ArgumentNullException>(() =>
            new SemanticRoutingChatClient(null!, profiles, client));
        Assert.Throws<ArgumentNullException>(() =>
            new SemanticRoutingChatClient(generator, null!, client));
        Assert.Throws<ArgumentNullException>(() =>
            new SemanticRoutingChatClient(generator, profiles, null!));
        Assert.Throws<ArgumentException>(() =>
            new SemanticRoutingChatClient(
                generator,
                new Dictionary<IChatClient, IReadOnlyList<string>>(),
                client));
        Assert.Throws<ArgumentException>(() =>
            new SemanticRoutingChatClient(
                generator,
                new Dictionary<IChatClient, IReadOnlyList<string>>
                {
                    [client] = [],
                },
                client));
        Assert.Throws<ArgumentException>(() =>
            new SemanticRoutingChatClient(
                generator,
                new Dictionary<IChatClient, IReadOnlyList<string>>
                {
                    [client] = [" "],
                },
                client));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticRoutingChatClient(generator, profiles, client, scoreThreshold: 1.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticRoutingChatClient(generator, profiles, client, topK: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticRoutingChatClient(
                generator,
                profiles,
                client,
                scoreAggregation: (SemanticRoutingChatClient.ScoreAggregation)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticRoutingChatClient(
                generator,
                profiles,
                client,
                scoreThreshold: 2.1f,
                topK: 2,
                scoreAggregation: SemanticRoutingChatClient.ScoreAggregation.Sum));
    }

    [Fact]
    public async Task SemanticRouting_SelectsBestProfileAndCachesIndex()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["code profile"] = [1, 0],
            ["writing profile"] = [0, 1],
            ["debug this code"] = [1, 0],
        };
        int profileBatches = 0;
        using var generator = new TestEmbeddingGenerator
        {
            GenerateAsyncCallback = (values, _, _) =>
            {
                string[] inputs = [.. values];
                if (inputs.Length > 1)
                {
                    profileBatches++;
                }

                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    [.. inputs.Select(input => new Embedding<float>(vectors[input]))]));
            },
        };
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "code"));
        using var code = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(expected),
        };
        using var writing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "writing"))),
        };
        var profiles = new Dictionary<IChatClient, IReadOnlyList<string>>(ChatClientReferenceComparer.Instance)
        {
            [code] = ["code profile"],
            [writing] = ["writing profile"],
        };
        using var router = new SemanticRoutingChatClient(
            generator,
            profiles,
            defaultClient: writing,
            leaveOpen: true);

        ChatResponse first = await router.GetResponseAsync([new(ChatRole.User, "debug this code")]);
        ChatResponse second = await router.GetResponseAsync([new(ChatRole.User, "debug this code")]);

        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, profileBatches);
    }

    [Theory]
    [InlineData(SemanticRoutingChatClient.ScoreAggregation.Mean, "code")]
    [InlineData(SemanticRoutingChatClient.ScoreAggregation.Sum, "writing")]
    public async Task SemanticRouting_AggregatesGlobalTopKByClient(
        SemanticRoutingChatClient.ScoreAggregation scoreAggregation,
        string expectedResponse)
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["code"] = [1, 0],
            ["writing one"] = [0.8f, 0.6f],
            ["writing two"] = [0.8f, -0.6f],
            ["query"] = [1, 0],
        };
        using var generator = new TestEmbeddingGenerator
        {
            GenerateAsyncCallback = (values, _, _) =>
                Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    [.. values.Select(input => new Embedding<float>(vectors[input]))])),
        };
        using var code = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "code"))),
        };
        using var writing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "writing"))),
        };
        var profiles = new Dictionary<IChatClient, IReadOnlyList<string>>(ChatClientReferenceComparer.Instance)
        {
            [code] = ["code"],
            [writing] = ["writing one", "writing two"],
        };
        using var router = new SemanticRoutingChatClient(
            generator,
            profiles,
            defaultClient: code,
            scoreThreshold: scoreAggregation == SemanticRoutingChatClient.ScoreAggregation.Sum ? 1.5f : 0.3f,
            topK: 3,
            scoreAggregation: scoreAggregation,
            leaveOpen: true);

        ChatResponse response = await router.GetResponseAsync([new(ChatRole.User, "query")]);

        Assert.Equal(expectedResponse, response.Text);
    }

    [Fact]
    public async Task SemanticRouting_UsesDefaultBelowThreshold()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["code profile"] = [1, 0],
            ["unrelated query"] = [0, 1],
        };
        using var generator = new TestEmbeddingGenerator
        {
            GenerateAsyncCallback = (values, _, _) =>
                Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    [.. values.Select(input => new Embedding<float>(vectors[input]))])),
        };
        using var profiled = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "profiled"))),
        };
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "default"));
        using var defaultClient = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(expected),
        };
        var profiles = new Dictionary<IChatClient, IReadOnlyList<string>>(ChatClientReferenceComparer.Instance)
        {
            [profiled] = ["code profile"],
        };
        using var router = new SemanticRoutingChatClient(
            generator,
            profiles,
            defaultClient,
            scoreThreshold: 0.5f,
            leaveOpen: true);

        ChatResponse response =
            await router.GetResponseAsync([new(ChatRole.User, "unrelated query")]);

        Assert.Same(expected, response);
    }

    [Fact]
    public void SemanticRouting_DisposesOwnedResourcesOnce()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var generator = new CountingEmbeddingGenerator();
        var profiled = new CountingDisposeClient();
        var defaultClient = new CountingDisposeClient();
        var profiles = new Dictionary<IChatClient, IReadOnlyList<string>>(ChatClientReferenceComparer.Instance)
        {
            [profiled] = ["profile"],
        };
        var router = new SemanticRoutingChatClient(generator, profiles, defaultClient);
#pragma warning restore CA2000

        router.Dispose();
        router.Dispose();

        Assert.Equal(1, generator.DisposeCount);
        Assert.Equal(1, profiled.DisposeCount);
        Assert.Equal(1, defaultClient.DisposeCount);
    }

    private sealed class CountingDisposeClient : IChatClient
    {
        public int DisposeCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => DisposeCount++;

        public override bool Equals(object? obj) => obj is CountingDisposeClient;

        public override int GetHashCode() => 0;
    }

    private sealed class CountingEmbeddingGenerator :
        IEmbeddingGenerator<string, Embedding<float>>
    {
        public int DisposeCount { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => DisposeCount++;
    }

    private sealed class ChatClientReferenceComparer : IEqualityComparer<IChatClient>
    {
        public static ChatClientReferenceComparer Instance { get; } = new();

        public bool Equals(IChatClient? x, IChatClient? y) => ReferenceEquals(x, y);

        public int GetHashCode(IChatClient obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
