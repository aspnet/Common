// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class RoutingChatClientTests
{
    [Fact]
    public void Create_RejectsNullSelector()
    {
        Assert.Throws<ArgumentNullException>(() => RoutingChatClient.Create(null!));
    }

    [Fact]
    public async Task Create_SelectsClientForRequest()
    {
        var messages = new ChatMessage[] { new(ChatRole.User, "hi") };
        var options = new ChatOptions { ModelId = "request" };
        using var cancellationSource = new CancellationTokenSource();
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        ChatOptions? forwardedOptions = null;
        using var selected = new TestChatClient
        {
            GetResponseAsyncCallback = (_, selectedOptions, _) =>
            {
                forwardedOptions = selectedOptions;
                return Task.FromResult(expected);
            },
        };
        RoutingContext? observedContext = null;
        CancellationToken observedToken = default;
        int selectionCount = 0;
        using RoutingChatClient router = RoutingChatClient.Create((context, cancellationToken) =>
        {
            observedContext = context;
            observedToken = cancellationToken;
            selectionCount++;
            context.ChatOptions!.ModelId = "selected";
            return new(selected);
        });

        ChatResponse response = await router.GetResponseAsync(messages, options, cancellationSource.Token);

        Assert.Same(expected, response);
        Assert.Same(messages, observedContext!.Messages);
        Assert.NotSame(options, observedContext.ChatOptions);
        Assert.Same(observedContext.ChatOptions, forwardedOptions);
        Assert.Equal("selected", forwardedOptions!.ModelId);
        Assert.Equal("request", options.ModelId);
        Assert.Equal(cancellationSource.Token, observedToken);
        Assert.Equal(1, selectionCount);
    }

    [Fact]
    public async Task Create_DoesNotReselectAfterFailure()
    {
        var expected = new InvalidOperationException("failed");
        using var selected = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw expected,
        };
        int selectionCount = 0;
        using RoutingChatClient router = RoutingChatClient.Create((_, _) =>
        {
            selectionCount++;
            return new(selected);
        });

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Same(expected, actual);
        Assert.Equal(1, selectionCount);
    }

    [Fact]
    public async Task Create_NullSelectionThrowsForNonStreamingAndStreaming()
    {
        using RoutingChatClient router = RoutingChatClient.Create((_, _) => new((IChatClient)null!));

        InvalidOperationException nonStreaming = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));
        InvalidOperationException streaming = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")])));

        Assert.Contains("SelectClientAsync", nonStreaming.Message, StringComparison.Ordinal);
        Assert.Contains("SelectClientAsync", streaming.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RejectsNullMessagesForNonStreamingAndStreaming()
    {
        using var selected = new TestChatClient();
        using RoutingChatClient router = RoutingChatClient.Create((_, _) => new(selected));

        await Assert.ThrowsAsync<ArgumentNullException>(() => router.GetResponseAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CollectAsync(router.GetStreamingResponseAsync(null!)));
    }

    [Fact]
    public async Task Create_DoesNotDisposeSelectedClient()
    {
        using var selected = new CountingDisposeClient();
        using (RoutingChatClient router = RoutingChatClient.Create((_, _) => new(selected)))
        {
            _ = await router.GetResponseAsync([new(ChatRole.User, "hi")]);
        }

        Assert.Equal(0, selected.DisposeCount);
    }

    [Fact]
    public void GetService_ReturnsSelfAndNullForUnknownOrKeyed()
    {
        using var client = new DelegatingTestRouter(_ => throw new NotSupportedException());

        Assert.Same(client, client.GetService(typeof(DelegatingTestRouter)));
        Assert.Same(client, client.GetService(typeof(RoutingChatClient)));
        Assert.Same(client, client.GetService(typeof(IChatClient)));
        Assert.Null(client.GetService(typeof(DelegatingTestRouter), serviceKey: "key"));
        Assert.Null(client.GetService(typeof(string)));
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(
        IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in updates)
        {
            result.Add(update);
        }

        return result;
    }

    private sealed class DelegatingTestRouter : RoutingChatClient
    {
        private readonly Func<RoutingContext, IChatClient> _select;

        public DelegatingTestRouter(Func<RoutingContext, IChatClient> select)
        {
            _select = select;
        }

        protected override ValueTask<IChatClient> SelectClientAsync(
            RoutingContext context,
            CancellationToken cancellationToken) =>
            new(_select(context));
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
