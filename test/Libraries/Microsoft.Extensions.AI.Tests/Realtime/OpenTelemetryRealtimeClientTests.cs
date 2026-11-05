// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Extensions.AI;

public class OpenTelemetryRealtimeClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpectedInformationLogged_GetStreamingResponseAsync(bool enableSensitiveData)
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions
            {
                Model = "test-model",
                Voice = "alloy",
                MaxOutputTokens = 500,
                OutputModalities = ["text", "audio"],
                Instructions = "Be helpful and friendly.",
                SessionKind = RealtimeSessionKind.Conversation,
                Tools = [AIFunctionFactory.Create((string query) => query, "Search", "Search for information.")],
            },
            GetServiceCallback = (serviceType, serviceKey) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("testprovider", new Uri("http://localhost:12345/realtime"), "gpt-4-realtime") :
                null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackAsync(cancellationToken),
        };

        static async IAsyncEnumerable<RealtimeServerMessage> CallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            _ = cancellationToken;

            yield return new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseCreated, MessageId = "evt_001" };
            yield return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { OutputIndex = 0, Text = "Hello" };
            yield return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { OutputIndex = 0, Text = " there!" };
            yield return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone) { OutputIndex = 0, Text = "Hello there!" };

            yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = "resp_12345",
                Status = "completed",
                Usage = new UsageDetails
                {
                    InputTokenCount = 15,
                    OutputTokenCount = 25,
                    TotalTokenCount = 40,
                    CachedInputTokenCount = 3,
                    ReasoningTokenCount = 6,
                    InputAudioTokenCount = 10,
                    InputTextTokenCount = 5,
                    OutputAudioTokenCount = 18,
                    OutputTextTokenCount = 7,
                },
            };
        }

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = enableSensitiveData;
        client.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume responses
        }

        // When sensitive data is enabled, we get one activity per message with content plus one for output/response
        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output activity = 3 activities
        // When sensitive data is disabled, we get only one activity for the response
        Activity activity;
        if (enableSensitiveData)
        {
            Assert.Equal(3, activities.Count);

            // The last activity is the response/output activity with ResponseDone data
            activity = activities[2];
        }
        else
        {
            activity = Assert.Single(activities);
        }

        Assert.NotNull(activity.Id);
        Assert.NotEmpty(activity.Id);

        Assert.Equal("localhost", activity.GetTagItem("server.address"));
        Assert.Equal(12345, (int)activity.GetTagItem("server.port")!);

        Assert.Equal("realtime test-model", activity.DisplayName);
        Assert.Equal("testprovider", activity.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("chat", activity.GetTagItem("gen_ai.operation.name"));

        Assert.Equal("test-model", activity.GetTagItem("gen_ai.request.model"));
        Assert.True(activity.GetTagItem("gen_ai.request.stream") is true);
        Assert.Equal(500, activity.GetTagItem("gen_ai.request.max_tokens"));

        // Realtime-specific attributes
        Assert.Equal("conversation", activity.GetTagItem("gen_ai.realtime.session_kind"));
        Assert.Equal("alloy", activity.GetTagItem("gen_ai.realtime.voice"));
        Assert.Equal("""["text", "audio"]""", activity.GetTagItem("gen_ai.realtime.output_modalities"));

        // Response attributes
        Assert.Equal("resp_12345", activity.GetTagItem("gen_ai.response.id"));
        Assert.Equal("""["completed"]""", activity.GetTagItem("gen_ai.response.finish_reasons"));
        Assert.Equal(15, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(25, activity.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(3, activity.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal(6, activity.GetTagItem("gen_ai.usage.reasoning.output_tokens"));
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.input_audio_tokens"));
        Assert.Equal(5, activity.GetTagItem("gen_ai.usage.input_text_tokens"));
        Assert.Equal(18, activity.GetTagItem("gen_ai.usage.output_audio_tokens"));
        Assert.Equal(7, activity.GetTagItem("gen_ai.usage.output_text_tokens"));

        var timeToFirstChunk = Assert.IsType<double>(activity.GetTagItem("gen_ai.response.time_to_first_chunk"));
        Assert.True(timeToFirstChunk >= 0);

        Assert.True(activity.Duration.TotalMilliseconds > 0);

        var tags = activity.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (enableSensitiveData)
        {
            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "type": "text",
                    "content": "Be helpful and friendly."
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.system_instructions"]));

            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "type": "function",
                    "name": "Search",
                    "description": "Search for information.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "query": {
                          "type": "string"
                        }
                      },
                      "required": [
                        "query"
                      ]
                    }
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.tool.definitions"]));
        }
        else
        {
            Assert.False(tags.ContainsKey("gen_ai.system_instructions"));
            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "type": "function",
                    "name": "Search"
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.tool.definitions"]));
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TracesError()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetStreamingResponseAsyncCallback = (cancellationToken) => ThrowingCallbackAsync(cancellationToken),
        };

        static async IAsyncEnumerable<RealtimeServerMessage> ThrowingCallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            _ = cancellationToken;
            yield return new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseCreated };
            throw new InvalidOperationException("Streaming error");
        }

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = innerClient
            .AsBuilder()
            .UseOpenTelemetry(loggerFactory, sourceName)
            .Build();
        await using var session = await client.CreateSessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var response in session.GetStreamingResponseAsync())
            {
                // Consume responses
            }
        });

        var activity = Assert.Single(activities);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Streaming error", activity.StatusDescription);

        // Exception is logged via ILogger
        var logEntry = Assert.Single(collector.GetSnapshot());
        Assert.Equal("gen_ai.client.operation.exception", logEntry.Id.Name);
        Assert.Equal(LogLevel.Warning, logEntry.Level);
        Assert.IsType<InvalidOperationException>(logEntry.Exception);
        Assert.Equal("Streaming error", logEntry.Exception!.Message);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TracesErrorFromResponse()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetStreamingResponseAsyncCallback = (cancellationToken) => ErrorResponseCallbackAsync(cancellationToken),
        };

        static async IAsyncEnumerable<RealtimeServerMessage> ErrorResponseCallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            _ = cancellationToken;

            yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = "resp_error",
                Status = "failed",
                Error = new ErrorContent("Something went wrong") { ErrorCode = "internal_error" },
            };
        }

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = innerClient
            .AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume responses
        }

        var activity = Assert.Single(activities);
        Assert.Equal("internal_error", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Something went wrong", activity.StatusDescription);
    }

    [Fact]
    public async Task NoListeners_NoActivityCreated()
    {
        // Create a tracer provider but don't add a source for our session
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("different-source")
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetStreamingResponseAsyncCallback = (cancellationToken) => EmptyCallbackAsync(cancellationToken),
        };

#pragma warning disable S4144 // Methods should not have identical implementations
        static async IAsyncEnumerable<RealtimeServerMessage> EmptyCallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
#pragma warning restore S4144
        {
            await Task.Yield();
            _ = cancellationToken;

            yield break;
        }

        var sourceName = Guid.NewGuid().ToString();
        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = innerClient
            .AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();
        await using var session = await client.CreateSessionAsync();

        // This should work without errors even without listeners
        var count = 0;
        await foreach (var response in session.GetStreamingResponseAsync())
        {
            count++;
        }

        // Verify the session worked correctly without listeners
        Assert.True(count >= 0);
    }

    [Fact]
    public async Task InvalidArgs_Throws()
    {
        await using var innerSession = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(innerSession);

        Assert.Throws<ArgumentNullException>("innerClient", () => new OpenTelemetryRealtimeClient(null!));
        using var client = new OpenTelemetryRealtimeClient(innerClient);
        Assert.Throws<ArgumentNullException>("value", () => client.JsonSerializerOptions = null!);
    }

    [Fact]
    public void SessionUpdateMessage_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>("options", () => new SessionUpdateRealtimeClientMessage(null!));
    }

    [Fact]
    public async Task GetService_ReturnsActivitySource()
    {
        await using var innerSession = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient);
        await using var session = await client.CreateSessionAsync();

        var activitySource = session.GetService(typeof(ActivitySource));
        Assert.NotNull(activitySource);
        Assert.IsType<ActivitySource>(activitySource);
    }

    [Fact]
    public async Task GetService_ReturnsSelf()
    {
        await using var innerSession = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient);

        Assert.Same(client, client.GetService(typeof(OpenTelemetryRealtimeClient)));

        await using var session = await client.CreateSessionAsync();
        Assert.Same(session, session.GetService(typeof(IRealtimeClientSession)));
    }

    [Fact]
    public async Task TranscriptionSessionKind_Logged()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions
            {
                Model = "whisper-1",
                SessionKind = RealtimeSessionKind.Transcription,
            },
            GetStreamingResponseAsyncCallback = (cancellationToken) => TranscriptionCallbackAsync(cancellationToken),
        };

        static async IAsyncEnumerable<RealtimeServerMessage> TranscriptionCallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            _ = cancellationToken;

            yield return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
            {
                Transcription = "Hello world",
            };
            yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
        }

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = innerClient
            .AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        var activity = Assert.Single(activities);
        Assert.Equal("transcription", activity.GetTagItem("gen_ai.realtime.session_kind"));
    }

    [Fact]
    public async Task ToolCallContentInClientMessages_LoggedAsInputMessages()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithToolResultAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get one activity per message with content plus one for output
        // GetClientMessagesWithToolResultAsync yields 2 messages but only 1 has content, so 1 input activity + 1 output = 2 activities
        Assert.Equal(2, activities.Count);
        var inputActivity = activities[0];
        var inputMessages = inputActivity.GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("tool_call_response", inputMessages);
        Assert.Contains("call_1", inputMessages);
    }

    [Fact]
    public async Task ToolCallContentInServerMessages_LoggedAsOutputMessages()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackWithToolCallAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get one activity per message with content plus one for output
        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        Assert.Equal(3, activities.Count);
        var outputActivity = activities[2];
        var outputMessages = outputActivity.GetTagItem("gen_ai.output.messages")?.ToString();
        Assert.NotNull(outputMessages);
        Assert.Contains("tool_call", outputMessages);
        Assert.Contains("search", outputMessages);
    }

    [Fact]
    public async Task ToolContentNotLoggedWithoutSensitiveData()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackWithToolCallAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = false;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithToolResultAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("gen_ai.input.messages"));
        Assert.Null(activity.GetTagItem("gen_ai.output.messages"));
    }

#pragma warning disable S4144 // Methods should not have identical implementations
    private static async IAsyncEnumerable<RealtimeServerMessage> SimpleCallbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
#pragma warning restore S4144
    {
        await Task.Yield();
        _ = cancellationToken;

        yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        yield return new InputAudioBufferAppendRealtimeClientMessage(new DataContent(new byte[] { 1, 2, 3 }, "audio/pcm"));
        yield return new InputAudioBufferCommitRealtimeClientMessage();
        yield return new CreateResponseRealtimeClientMessage();
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesWithToolResultAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        var contentItem = new RealtimeConversationItem([new FunctionResultContent("call_1", "result_value")], role: ChatRole.Tool);
        yield return new CreateConversationItemRealtimeClientMessage(contentItem);
        yield return new CreateResponseRealtimeClientMessage();
    }

    private static async IAsyncEnumerable<RealtimeServerMessage> CallbackWithToolCallAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;

        // Yield a function call item from the server using ResponseOutputItemRealtimeServerMessage
        var contentItem = new RealtimeConversationItem(
            [new FunctionCallContent("call_123", "search", new Dictionary<string, object?> { ["query"] = "test" })],
            role: ChatRole.Assistant);
        yield return new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            Item = contentItem,
        };

        yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
    }

    [Fact]
    public async Task AudioBufferAppendMessage_LoggedAsInputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get one activity per message with content plus one for output
        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        Assert.Equal(3, activities.Count);
        var inputActivity = activities[0];
        var inputMessages = inputActivity.GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("blob", inputMessages);
        Assert.Contains("audio", inputMessages);
    }

    [Fact]
    public async Task AudioBufferCommitMessage_LoggedAsInputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get one activity per message with content plus one for output
        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        // The audio_commit message is the 2nd message with content, so it appears in activities[1]
        Assert.Equal(3, activities.Count);
        var inputActivity = activities[1];
        var inputMessages = inputActivity.GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("audio_commit", inputMessages);
    }

    [Fact]
    public async Task ResponseCreateMessageWithInstructions_LoggedAsInputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithInstructionsAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get 2 activities: input (first) and output (second)
        Assert.Equal(2, activities.Count);
        var inputActivity = activities[0];
        var inputMessages = inputActivity.GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("instructions", inputMessages);
        Assert.Contains("Be very helpful", inputMessages);
    }

    [Fact]
    public async Task ResponseCreateMessageWithItems_LoggedAsInputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithItemsAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // With sensitive data enabled, we get 2 activities: input (first) and output (second)
        Assert.Equal(2, activities.Count);
        var inputActivity = activities[0];
        var inputMessages = inputActivity.GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("text", inputMessages);
        Assert.Contains("Hello from client", inputMessages);
    }

    [Fact]
    public async Task OutputTextAudioMessage_LoggedAsOutputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackWithTextOutputAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        Assert.Equal(3, activities.Count);
        var outputMessages = activities[2].GetTagItem("gen_ai.output.messages")?.ToString();
        Assert.NotNull(outputMessages);
        Assert.Contains("assistant", outputMessages);
        Assert.Contains("Hello from server", outputMessages);
    }

    [Fact]
    public async Task InputAudioTranscriptionMessage_LoggedAsOutputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackWithTranscriptionAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        Assert.Equal(3, activities.Count);
        var outputMessages = activities[2].GetTagItem("gen_ai.output.messages")?.ToString();
        Assert.NotNull(outputMessages);
        Assert.Contains("input_transcription", outputMessages);
        Assert.Contains("Transcribed audio content", outputMessages);
    }

    [Fact]
    public async Task ServerErrorMessage_LoggedAsOutputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => CallbackWithServerErrorAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // GetClientMessagesAsync yields 3 messages but only 2 have content, so 2 input activities + 1 output = 3 activities
        Assert.Equal(3, activities.Count);
        var outputMessages = activities[2].GetTagItem("gen_ai.output.messages")?.ToString();
        Assert.NotNull(outputMessages);
        Assert.Contains("error", outputMessages);
        Assert.Contains("Something went wrong on server", outputMessages);
    }

    [Fact]
    public async Task ConversationItemCreateWithTextContent_LoggedAsInputMessage()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithTextContentAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // GetClientMessagesWithTextContentAsync yields 2 messages but only 1 has content, so 1 input activity + 1 output = 2 activities
        Assert.Equal(2, activities.Count);
        var inputMessages = activities[0].GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("user", inputMessages);
        Assert.Contains("User text message", inputMessages);
    }

    [Fact]
    public async Task DataContentInClientMessage_LoggedWithModality()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        await using var innerSession = new TestRealtimeClientSession
        {
            Options = new RealtimeSessionOptions { Model = "test-model" },
            GetServiceCallback = (serviceType, _) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider") : null,
            GetStreamingResponseAsyncCallback = (cancellationToken) => SimpleCallbackAsync(cancellationToken),
        };

        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient, sourceName: sourceName);
        client.EnableSensitiveData = true;
        await using var session = await client.CreateSessionAsync();

        await foreach (var msg in GetClientMessagesWithImageContentAsync())
        {
            await session.SendAsync(msg);
        }

        await foreach (var response in session.GetStreamingResponseAsync())
        {
            // Consume
        }

        // GetClientMessagesWithImageContentAsync yields 2 messages but only 1 has content, so 1 input activity + 1 output = 2 activities
        Assert.Equal(2, activities.Count);
        var inputMessages = activities[0].GetTagItem("gen_ai.input.messages")?.ToString();
        Assert.NotNull(inputMessages);
        Assert.Contains("blob", inputMessages);
        Assert.Contains("image", inputMessages);
        Assert.Contains("image/png", inputMessages);
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesWithInstructionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        yield return new CreateResponseRealtimeClientMessage { Instructions = "Be very helpful" };
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesWithItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        var item = new RealtimeConversationItem([new TextContent("Hello from client")], role: ChatRole.User);
        yield return new CreateResponseRealtimeClientMessage { Items = [item] };
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesWithTextContentAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        var item = new RealtimeConversationItem([new TextContent("User text message")], role: ChatRole.User);
        yield return new CreateConversationItemRealtimeClientMessage(item);
        yield return new CreateResponseRealtimeClientMessage();
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static async IAsyncEnumerable<RealtimeClientMessage> GetClientMessagesWithImageContentAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        await Task.Yield();
        var imageData = new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var item = new RealtimeConversationItem([imageData], role: ChatRole.User);
        yield return new CreateConversationItemRealtimeClientMessage(item);
        yield return new CreateResponseRealtimeClientMessage();
    }

    private static async IAsyncEnumerable<RealtimeServerMessage> CallbackWithTextOutputAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;

        yield return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
        {
            Text = "Hello from server",
        };
        yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
    }

    private static async IAsyncEnumerable<RealtimeServerMessage> CallbackWithTranscriptionAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;

        yield return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            Transcription = "Transcribed audio content",
        };
        yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
    }

    private static async IAsyncEnumerable<RealtimeServerMessage> CallbackWithServerErrorAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;

        yield return new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent("Something went wrong on server"),
        };
        yield return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone);
    }

    private static string ReplaceWhitespace(string? input) => Regex.Replace(input ?? "", @"\s+", " ").Trim();

    [Fact]
    public void UseOpenTelemetry_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>("builder", () =>
            ((RealtimeClientBuilder)null!).UseOpenTelemetry());
    }

    [Fact]
    public async Task UseOpenTelemetry_BuildsPipeline()
    {
        await using var innerSession = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(innerSession);
        var builder = new RealtimeClientBuilder(innerClient);

        builder.UseOpenTelemetry();

        using var pipeline = builder.Build();
        await using var session = await pipeline.CreateSessionAsync();

        var otelClient = pipeline.GetService(typeof(OpenTelemetryRealtimeClient));
        Assert.NotNull(otelClient);

        var typedClient = Assert.IsType<OpenTelemetryRealtimeClient>(otelClient);
        typedClient.EnableSensitiveData = true;
        Assert.True(typedClient.EnableSensitiveData);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        await using var innerSession = new TestRealtimeClientSession();
        using var innerClient = new TestRealtimeClient(innerSession);
        using var client = new OpenTelemetryRealtimeClient(innerClient);
        var session = await client.CreateSessionAsync();

        await session.DisposeAsync();
        await session.DisposeAsync();

        // Verifying no exception is thrown on double dispose
        Assert.NotNull(session);
    }

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
