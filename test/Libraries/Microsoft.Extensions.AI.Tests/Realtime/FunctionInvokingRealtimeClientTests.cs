// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace Microsoft.Extensions.AI;

public class FunctionInvokingRealtimeClientTests
{
    [Fact]
    public void Ctor_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerClient", () => new FunctionInvokingRealtimeClient(null!));
    }

    [Fact]
    public void Properties_DefaultValues()
    {
        using var client = CreateClient();

        Assert.False(client.IncludeDetailedErrors);
        Assert.False(client.AllowConcurrentInvocation);
        Assert.Equal(40, client.MaximumIterationsPerRequest);
        Assert.Equal(3, client.MaximumConsecutiveErrorsPerRequest);
        Assert.Null(client.AdditionalTools);
        Assert.False(client.TerminateOnUnknownCalls);
        Assert.Null(client.FunctionInvoker);
    }

    [Fact]
    public void MaximumIterationsPerRequest_InvalidValue_Throws()
    {
        using var client = CreateClient();

        Assert.Throws<ArgumentOutOfRangeException>("value", () => client.MaximumIterationsPerRequest = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => client.MaximumIterationsPerRequest = -1);
    }

    [Fact]
    public void MaximumConsecutiveErrorsPerRequest_InvalidValue_Throws()
    {
        using var client = CreateClient();

        Assert.Throws<ArgumentOutOfRangeException>("value", () => client.MaximumConsecutiveErrorsPerRequest = -1);

        // 0 is valid (means immediately rethrow on any error)
        client.MaximumConsecutiveErrorsPerRequest = 0;
        Assert.Equal(0, client.MaximumConsecutiveErrorsPerRequest);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoFunctionCalls_PassesThrough()
    {
        var serverMessages = new RealtimeServerMessage[]
        {
            new() { Type = RealtimeServerMessageType.ResponseCreated, MessageId = "evt_001" },
            new() { Type = RealtimeServerMessageType.ResponseDone, MessageId = "evt_002" },
        };

        await using var inner = new TestRealtimeClientSession
        {
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(serverMessages, ct),
        };
        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        Assert.Equal(2, received.Count);
        Assert.Equal("evt_001", received[0].MessageId);
        Assert.Equal("evt_002", received[1].MessageId);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionCall_InvokesAndInjectsResult()
    {
        AIFunction getWeather = AIFunctionFactory.Create(
            (string city) => $"Sunny in {city}",
            "get_weather",
            "Gets the weather");

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [getWeather] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_001", "get_weather", new Dictionary<string, object?> { ["city"] = "Seattle" }),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        // The function call message should be yielded to the consumer
        Assert.Single(received);

        // Function result + response.create should be injected
        Assert.Equal(2, injectedMessages.Count);

        // First injected: conversation.item.create with function result
        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        Assert.NotNull(resultMsg.Item);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.Equal("call_001", functionResult.CallId);
        Assert.Contains("Sunny in Seattle", functionResult.Result?.ToString());

        // Second injected: response.create (no hardcoded modalities)
        var responseCreate = Assert.IsType<CreateResponseRealtimeClientMessage>(injectedMessages[1]);
        Assert.Null(responseCreate.OutputModalities);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionCall_FromAdditionalTools()
    {
        AIFunction getWeather = AIFunctionFactory.Create(
            (string city) => $"Rainy in {city}",
            "get_weather",
            "Gets weather");

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_002", "get_weather", new Dictionary<string, object?> { ["city"] = "London" }),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        client.AdditionalTools = [getWeather];
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        Assert.Equal(2, injectedMessages.Count);
        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.Contains("Rainy in London", functionResult.Result?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_MaxIterations_StopsInvoking()
    {
        int invocationCount = 0;
        AIFunction countFunc = AIFunctionFactory.Create(
            () =>
            {
                invocationCount++;
                return "result";
            },
            "counter",
            "Counts");

        var messages = Enumerable.Range(0, 5).Select<int, RealtimeServerMessage>(i =>
            CreateFunctionCallOutputItemMessage($"call_{i}", "counter", null)).ToList();

        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [countFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(messages, ct),
            SendAsyncCallback = (_, _) => Task.CompletedTask,
        };

        using var client = CreateClient(inner);
        client.MaximumIterationsPerRequest = 2;
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        // All 5 messages should be yielded
        Assert.Equal(5, received.Count);

        // But only 2 should have been invoked
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionInvoker_CustomDelegate()
    {
        var customInvoked = false;
        AIFunction myFunc = AIFunctionFactory.Create(
            () => "default",
            "my_func",
            "Test");

        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [myFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_custom", "my_func", null),
            ], ct),
            SendAsyncCallback = (_, _) => Task.CompletedTask,
        };

        using var client = CreateClient(inner);
        client.FunctionInvoker = (context, ct) =>
        {
            customInvoked = true;
            return new ValueTask<object?>("custom_result");
        };
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        Assert.True(customInvoked);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UnknownFunction_SendsErrorByDefault()
    {
        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_unknown", "nonexistent_func", null),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        // Should inject error result + response.create
        Assert.Equal(2, injectedMessages.Count);
        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.Contains("not found", functionResult.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionErrorWithTerminate_StopsLoop()
    {
        AIFunction failFunc = AIFunctionFactory.Create(
            new Func<string>(() =>
            {
                FunctionInvokingRealtimeClient.CurrentContext!.Terminate = true;
                throw new InvalidOperationException("Something broke");
            }),
            "fail_func",
            "Fails");

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [failFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_fail", "fail_func", null),
                new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone, MessageId = "should_not_reach" },
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        // The function call message should be yielded, then the loop terminates
        Assert.Single(received);

        // No function results should be injected since we're terminating
        Assert.Empty(injectedMessages);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionError_IncludesDetailedErrors()
    {
        AIFunction failFunc = AIFunctionFactory.Create(
            new Func<string>(() => throw new InvalidOperationException("Something broke")),
            "fail_func",
            "Fails");

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [failFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_fail", "fail_func", null),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        client.IncludeDetailedErrors = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        Assert.Equal(2, injectedMessages.Count);
        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.Contains("Something broke", functionResult.Result?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionError_HidesDetailsWhenNotEnabled()
    {
        AIFunction failFunc = AIFunctionFactory.Create(
            new Func<string>(() => throw new InvalidOperationException("Secret error info")),
            "fail_func",
            "Fails");

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [failFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_fail2", "fail_func", null),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        client.IncludeDetailedErrors = false;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.DoesNotContain("Secret error info", functionResult.Result?.ToString());
        Assert.Contains("failed", functionResult.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetService_ReturnsSelf()
    {
        await using var inner = new TestRealtimeClientSession();
        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        Assert.Same(client, client.GetService(typeof(FunctionInvokingRealtimeClient)));
        Assert.Same(session, session.GetService(typeof(IRealtimeClientSession)));
        Assert.Same(inner, session.GetService(typeof(TestRealtimeClientSession)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TerminateOnUnknownCalls_StopsLoop()
    {
        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_unknown", "nonexistent_func", null),
                new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone, MessageId = "should_not_reach" },
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        client.TerminateOnUnknownCalls = true;
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        // The function call message should be yielded, then the loop terminates
        Assert.Single(received);

        // No function results should be injected since we're terminating
        Assert.Empty(injectedMessages);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TerminateOnUnknownCalls_False_SendsError()
    {
        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_unknown", "nonexistent_func", null),
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        client.TerminateOnUnknownCalls = false;
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        Assert.Single(received);

        // Error result + response.create should be injected (default behavior)
        Assert.Equal(2, injectedMessages.Count);
        var resultMsg = Assert.IsType<CreateConversationItemRealtimeClientMessage>(injectedMessages[0]);
        var functionResult = Assert.IsType<FunctionResultContent>(resultMsg.Item.Contents[0]);
        Assert.Contains("not found", functionResult.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ConcurrentInvocation_InvokesInParallel()
    {
        int concurrentCount = 0;
        int maxConcurrency = 0;
        object lockObj = new();

        AIFunction slowFunc = AIFunctionFactory.Create(
            async () =>
            {
                int current;
                lock (lockObj)
                {
                    concurrentCount++;
                    current = concurrentCount;
                    if (current > maxConcurrency)
                    {
                        maxConcurrency = current;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return "done";
            },
            "slow_func",
            "Slow");

        // Create two function call messages in the same response
        var msg1 = CreateFunctionCallOutputItemMessage("call_a", "slow_func", null);
        var msg2 = CreateFunctionCallOutputItemMessage("call_b", "slow_func", null);

        // Combine both into a single ResponseOutputItem with multiple function calls
        var combinedItem = new RealtimeConversationItem(
        [
            new FunctionCallContent("call_a", "slow_func"),
            new FunctionCallContent("call_b", "slow_func"),
        ], "item_combined");

        var combinedMessage = new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = "resp_combined",
            OutputIndex = 0,
            Item = combinedItem,
        };

        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [slowFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages([combinedMessage], ct),
            SendAsyncCallback = (_, _) => Task.CompletedTask,
        };

        using var client = CreateClient(inner);
        client.AllowConcurrentInvocation = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            // consume
        }

        Assert.True(maxConcurrency >= 1, "At least one invocation should have occurred");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ConsecutiveErrors_ExceedsLimit_Throws()
    {
        int callCount = 0;
        AIFunction failFunc = AIFunctionFactory.Create(
            new Func<string>(() =>
            {
                callCount++;
                throw new InvalidOperationException($"Error #{callCount}");
            }),
            "fail_func",
            "Fails");

        // Create messages that will trigger multiple error iterations
        // Each time the function is called, it fails, and the error count increases
        var messages = new List<RealtimeServerMessage>
        {
            CreateFunctionCallOutputItemMessage("call_1", "fail_func", null),
            CreateFunctionCallOutputItemMessage("call_2", "fail_func", null),
            CreateFunctionCallOutputItemMessage("call_3", "fail_func", null),
            CreateFunctionCallOutputItemMessage("call_4", "fail_func", null),
        };

        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [failFunc] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(messages, ct),
            SendAsyncCallback = (_, _) => Task.CompletedTask,
        };

        using var client = CreateClient(inner);
        client.MaximumConsecutiveErrorsPerRequest = 1;
        await using var session = await client.CreateSessionAsync();

        // Should eventually throw after exceeding the consecutive error limit
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var msg in session.GetStreamingResponseAsync())
            {
                // consume
            }
        });
    }

    [Fact]
    public void UseFunctionInvocation_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>("builder", () =>
            ((RealtimeClientBuilder)null!).UseFunctionInvocation());
    }

    [Fact]
    public async Task UseFunctionInvocation_ConfigureCallback_IsInvoked()
    {
        await using var inner = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(inner);
        var builder = new RealtimeClientBuilder(innerClient);

        bool configured = false;
        builder.UseFunctionInvocation(configure: client =>
        {
            configured = true;
            client.IncludeDetailedErrors = true;
            client.MaximumIterationsPerRequest = 10;
        });

        using var pipeline = builder.Build();
        Assert.True(configured);

        await using var session = await pipeline.CreateSessionAsync();

        var funcClient = pipeline.GetService(typeof(FunctionInvokingRealtimeClient));
        Assert.NotNull(funcClient);
        var typedClient = Assert.IsType<FunctionInvokingRealtimeClient>(funcClient);
        Assert.True(typedClient.IncludeDetailedErrors);
        Assert.Equal(10, typedClient.MaximumIterationsPerRequest);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NonInvocableTool_TerminatesLoop()
    {
        // Create a non-invocable AIFunctionDeclaration (not an AIFunction)
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"x":{"type":"string"}}}""").RootElement;
        var declaration = AIFunctionFactory.CreateDeclaration("my_declaration", "A non-invocable tool", schema);

        var injectedMessages = new List<RealtimeClientMessage>();
        await using var inner = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Tools = [declaration] },
            GetStreamingResponseAsyncCallback = (ct) => YieldMessages(
            [
                CreateFunctionCallOutputItemMessage("call_decl", "my_declaration", null),
                new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone, MessageId = "should_not_reach" },
            ], ct),
            SendAsyncCallback = (msg, _) =>
            {
                injectedMessages.Add(msg);
                return Task.CompletedTask;
            },
        };

        using var client = CreateClient(inner);
        await using var session = await client.CreateSessionAsync();

        var received = new List<RealtimeServerMessage>();
        await foreach (var msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        // The function call message should be yielded, then loop terminates
        // because the tool is a declaration-only (non-invocable)
        Assert.Single(received);

        // No results should be injected since we terminated
        Assert.Empty(injectedMessages);
    }

    #region Helpers

#pragma warning disable CA2000 // Dispose objects before losing scope - ownership transferred to FunctionInvokingRealtimeClient
    private static FunctionInvokingRealtimeClient CreateClient(IRealtimeClientSession? session = null)
    {
        return new FunctionInvokingRealtimeClient(new TestRealtimeClient(session ?? new TestRealtimeClientSession()));
    }
#pragma warning restore CA2000

    private static ResponseOutputItemRealtimeServerMessage CreateFunctionCallOutputItemMessage(
        string callId, string functionName, IDictionary<string, object?>? arguments)
    {
        var functionCallContent = new FunctionCallContent(callId, functionName, arguments);
        var item = new RealtimeConversationItem([functionCallContent], $"item_{callId}");

        return new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = $"resp_{callId}",
            OutputIndex = 0,
            Item = item,
        };
    }

    private static async IAsyncEnumerable<RealtimeServerMessage> YieldMessages(
        IList<RealtimeServerMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        foreach (var msg in messages)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return msg;
        }
    }

    #endregion

    private sealed class TestRealtimeClient : IRealtimeClient
    {
        private readonly IRealtimeClientSession _session;

        public TestRealtimeClient(IRealtimeClientSession session)
        {
            _session = session;
        }

        public Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_session);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : _session.GetService(serviceType, serviceKey);

        public void Dispose()
        {
        }
    }
}
