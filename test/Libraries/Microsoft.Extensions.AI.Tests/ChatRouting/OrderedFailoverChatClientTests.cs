// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.Extensions.AI.FailoverChatClientTests;

namespace Microsoft.Extensions.AI;

public class OrderedFailoverChatClientTests
{
    [Fact]
    public void OrderedFailover_RejectsMissingClients()
    {
        using var inner = new TestChatClient();

        Assert.Throws<ArgumentNullException>(() => new OrderedFailoverChatClient(null!));
        Assert.Throws<ArgumentException>(() => new OrderedFailoverChatClient([]));
        Assert.Throws<ArgumentException>(() => new OrderedFailoverChatClient([inner, null!]));
    }

    [Fact]
    public async Task OrderedFailover_TriesClientsInOrderAndSkipsFailures()
    {
        var calls = new List<string>();
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls.Add("first");
                throw new InvalidOperationException("first failed");
            },
        };
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls.Add("second");
                return Task.FromResult(expected);
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        ChatResponse response = await client.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
        Assert.Equal(["first", "second"], calls);
    }

    [Fact]
    public async Task OrderedFailover_InvokesRepeatedClientOncePerPosition()
    {
        var calls = new List<string>();
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls.Add("first");
                return calls.Count < 3
                    ? throw new InvalidOperationException("first failed")
                    : Task.FromResult(expected);
            },
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls.Add("second");
                throw new InvalidOperationException("second failed");
            },
        };
        using var client = new OrderedFailoverChatClient([first, second, first]);

        ChatResponse response = await client.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
        Assert.Equal(["first", "second", "first"], calls);
    }

    [Fact]
    public async Task OrderedFailover_RepeatedClientExhaustionRethrowsLastFailure()
    {
        int calls = 0;
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls++;
                throw new InvalidOperationException($"failure {calls}");
            },
        };
        using var client = new OrderedFailoverChatClient([inner, inner]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Equal(2, calls);
        Assert.Equal("failure 2", exception.Message);
    }

    [Fact]
    public async Task OrderedFailover_ExhaustionRethrowsLastFailure()
    {
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("first"),
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("second"),
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Equal("second", exception.Message);
    }

    [Fact]
    public async Task OrderedFailover_StateIsScopedToOneRequest()
    {
        int firstCalls = 0;
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                firstCalls++;
                throw new InvalidOperationException("failed");
            },
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(new ChatResponse()),
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        _ = await client.GetResponseAsync([new(ChatRole.User, "one")]);
        _ = await client.GetResponseAsync([new(ChatRole.User, "two")]);

        Assert.Equal(2, firstCalls);
    }

    [Fact]
    public async Task OrderedFailover_ConcurrentRequestsHaveIndependentState()
    {
        const int RequestCount = 10;
        int firstCalls = 0;
        int secondCalls = 0;
        var allStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = async (_, _, _) =>
            {
                if (Interlocked.Increment(ref firstCalls) == RequestCount)
                {
                    allStarted.SetResult(true);
                }

                await allStarted.Task;
                throw new InvalidOperationException("failed");
            },
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                Interlocked.Increment(ref secondCalls);
                return Task.FromResult(new ChatResponse());
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        Task<ChatResponse>[] requests =
        [
            .. Enumerable.Range(0, RequestCount).Select(index =>
                client.GetResponseAsync([new(ChatRole.User, index.ToString())])),
        ];
        _ = await Task.WhenAll(requests);

        Assert.Equal(RequestCount, firstCalls);
        Assert.Equal(RequestCount, secondCalls);
    }

    [Fact]
    public async Task OrderedFailover_ReleasesStateWhenStreamingEnds()
    {
        using var first = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        using var second = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("first", "second"),
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        await using (IAsyncEnumerator<ChatResponseUpdate> enumerator =
            client.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("first", enumerator.Current.Text);
        }

        Assert.Equal(0, GetOrderedFailoverRequestStateCount(client));
    }

    [Fact]
    public async Task OrderedFailover_SnapshotsConfiguredClients()
    {
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(expected),
        };
        var clients = new List<IChatClient> { inner };
        using var failover = new OrderedFailoverChatClient(clients, leaveOpen: true);
        clients.Clear();

        ChatResponse response = await failover.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
    }

    [Fact]
    public async Task OrderedFailover_UsesEachConfiguredClient()
    {
        var seenOptions = new List<(string? modelId, string? instructions)>();
        using var firstInner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                seenOptions.Add((options?.ModelId, options?.Instructions));
                throw new InvalidOperationException("failed");
            },
        };
        using var first = new ConfigureOptionsChatClient(
            firstInner,
            options => options.ModelId = "first");
        using var secondInner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                seenOptions.Add((options?.ModelId, options?.Instructions));
                return Task.FromResult(new ChatResponse());
            },
        };
        using var second = new ConfigureOptionsChatClient(
            secondInner,
            options => options.ModelId = "second");
        using var client = new OrderedFailoverChatClient(
            [first, second],
            leaveOpen: true);

        _ = await client.GetResponseAsync(
            [new(ChatRole.User, "hi")],
            new ChatOptions
            {
                Instructions = "caller",
                ModelId = "request",
            });

        Assert.Equal(
            [("first", "caller"), ("second", "caller")],
            seenOptions);
    }

    [Fact]
    public async Task OrderedFailover_StreamingFallsBackBeforeFirstUpdate()
    {
        using var first = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        using var second = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        ChatResponse response =
            await client.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task OrderedFailover_CancellationDoesNotFallback()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        bool secondCalled = false;
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("failed"),
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                secondCalled = true;
                return Task.FromResult(new ChatResponse());
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token));

        Assert.False(secondCalled);
    }

    [Fact]
    public void OrderedFailover_DisposesEachClientOnce()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var shared = new CountingDisposeClient();
        var other = new CountingDisposeClient();
        var client = new OrderedFailoverChatClient([shared, other]);
#pragma warning restore CA2000

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, shared.DisposeCount);
        Assert.Equal(1, other.DisposeCount);
    }

    [Fact]
    public void OrderedFailover_LeaveOpenDoesNotDisposeInnerClients()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var inner = new CountingDisposeClient();
        var client = new OrderedFailoverChatClient([inner], leaveOpen: true);
#pragma warning restore CA2000

        client.Dispose();

        Assert.Equal(0, inner.DisposeCount);
        inner.Dispose();
    }

    [Fact]
    public async Task NestedRouters_ReturnLeafResponse()
    {
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var leaf = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(expected),
        };
        using var inner = new OrderedFailoverChatClient([leaf]);
        using var outer = new OrderedFailoverChatClient([inner]);

        ChatResponse response = await outer.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
    }

    private static int GetOrderedFailoverRequestStateCount(OrderedFailoverChatClient client)
    {
        object requestStates = typeof(OrderedFailoverChatClient)
            .GetField(
                "_requestStates",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(client)!;
        return (int)requestStates.GetType().GetProperty("Count")!.GetValue(requestStates)!;
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
}
