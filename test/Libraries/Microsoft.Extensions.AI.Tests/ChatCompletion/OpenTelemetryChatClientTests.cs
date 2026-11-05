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
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Extensions.AI;

public class OpenTelemetryChatClientTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExpectedInformationLogged_Async(bool enableSensitiveData, bool streaming)
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "The blue whale, I think."))
                {
                    ResponseId = "id123",
                    FinishReason = ChatFinishReason.Stop,
                    Usage = new UsageDetails
                    {
                        InputTokenCount = 10,
                        OutputTokenCount = 20,
                        TotalTokenCount = 42,
                        CachedInputTokenCount = 5,
                        ReasoningTokenCount = 8,
                    },
                    AdditionalProperties = new()
                    {
                        ["system_fingerprint"] = "abcdefgh",
                        ["AndSomethingElse"] = "value2",
                    },
                };
            },
            GetStreamingResponseAsyncCallback = CallbackAsync,
            GetServiceCallback = (serviceType, serviceKey) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("testservice", new Uri("http://localhost:12345/something"), "amazingmodel") :
                null,
        };

        async static IAsyncEnumerable<ChatResponseUpdate> CallbackAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();

            foreach (string text in new[] { "The ", "blue ", "whale,", " ", "", "I", " think." })
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, text)
                {
                    ResponseId = "id123",
                };
            }

            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop,
            };

            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new()
                {
                    InputTokenCount = 10,
                    OutputTokenCount = 20,
                    TotalTokenCount = 42,
                    CachedInputTokenCount = 5,
                    ReasoningTokenCount = 8,
                })],
                AdditionalProperties = new()
                {
                    ["system_fingerprint"] = "abcdefgh",
                    ["AndSomethingElse"] = "value2",
                },
            };
        }

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName, configure: instance =>
            {
                instance.EnableSensitiveData = enableSensitiveData;
                instance.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
            })
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a close friend."),
            new(ChatRole.User, "Hey!") { AuthorName = "Alice" },
            new(ChatRole.Assistant, [new FunctionCallContent("12345", "GetPersonName")]),
            new(ChatRole.Tool, [new FunctionResultContent("12345", "John")]),
            new(ChatRole.Assistant, "Hey John, what's up?") { AuthorName = "BotAssistant" },
            new(ChatRole.User, "What's the biggest animal?")
        ];

        var options = new ChatOptions
        {
            FrequencyPenalty = 3.0f,
            MaxOutputTokens = 123,
            ModelId = "replacementmodel",
            TopP = 4.0f,
            TopK = 7,
            PresencePenalty = 5.0f,
            ResponseFormat = ChatResponseFormat.Json,
            Temperature = 6.0f,
            Seed = 42,
            StopSequences = ["hello", "world"],
            AdditionalProperties = new()
            {
                ["service_tier"] = "value1",
                ["SomethingElse"] = "value2",
            },
            Instructions = "You are helpful.",
            Tools =
            [
                AIFunctionFactory.Create((string personName) => personName, "GetPersonAge", "Gets the age of a person by name."),
                new HostedWebSearchTool(),
                new HostedFileSearchTool(),
                new HostedCodeInterpreterTool(),
                new HostedMcpServerTool("myAwesomeServer", "http://localhost:1234/somewhere"),
                AIFunctionFactory.Create((string location) => "", "GetCurrentWeather", "Gets the current weather for a location.").AsDeclarationOnly(),
            ],
        };

        if (streaming)
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options))
            {
                await Task.Yield();
            }
        }
        else
        {
            await chatClient.GetResponseAsync(messages, options);
        }

        var activity = Assert.Single(activities);

        Assert.NotNull(activity.Id);
        Assert.NotEmpty(activity.Id);

        Assert.Equal("localhost", activity.GetTagItem("server.address"));
        Assert.Equal(12345, (int)activity.GetTagItem("server.port")!);

        Assert.Equal("chat replacementmodel", activity.DisplayName);
        Assert.Equal("testservice", activity.GetTagItem("gen_ai.provider.name"));

        Assert.Equal("replacementmodel", activity.GetTagItem("gen_ai.request.model"));
        Assert.Equal(streaming ? (object?)true : null, activity.GetTagItem("gen_ai.request.stream"));
        Assert.Equal(3.0f, activity.GetTagItem("gen_ai.request.frequency_penalty"));
        Assert.Equal(4.0f, activity.GetTagItem("gen_ai.request.top_p"));
        Assert.Equal(5.0f, activity.GetTagItem("gen_ai.request.presence_penalty"));
        Assert.Equal(6.0f, activity.GetTagItem("gen_ai.request.temperature"));
        Assert.Equal(7, activity.GetTagItem("gen_ai.request.top_k"));
        Assert.Equal(123, activity.GetTagItem("gen_ai.request.max_tokens"));
        Assert.Equal("""["hello", "world"]""", activity.GetTagItem("gen_ai.request.stop_sequences"));
        Assert.Equal(enableSensitiveData ? "value1" : null, activity.GetTagItem("service_tier"));
        Assert.Equal(enableSensitiveData ? "value2" : null, activity.GetTagItem("SomethingElse"));
        Assert.Equal(42L, activity.GetTagItem("gen_ai.request.seed"));

        Assert.Equal("id123", activity.GetTagItem("gen_ai.response.id"));
        Assert.Equal("""["stop"]""", activity.GetTagItem("gen_ai.response.finish_reasons"));
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(20, activity.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(5, activity.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal(8, activity.GetTagItem("gen_ai.usage.reasoning.output_tokens"));
        Assert.Equal(enableSensitiveData ? "abcdefgh" : null, activity.GetTagItem("system_fingerprint"));
        Assert.Equal(enableSensitiveData ? "value2" : null, activity.GetTagItem("AndSomethingElse"));

        if (streaming)
        {
            var timeToFirstChunk = Assert.IsType<double>(activity.GetTagItem("gen_ai.response.time_to_first_chunk"));
            Assert.True(timeToFirstChunk >= 0);
        }
        else
        {
            Assert.Null(activity.GetTagItem("gen_ai.response.time_to_first_chunk"));
        }

        Assert.True(activity.Duration.TotalMilliseconds > 0);

        var tags = activity.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (enableSensitiveData)
        {
            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "role": "system",
                    "parts": [
                      {
                        "type": "text",
                        "content": "You are a close friend."
                      }
                    ]
                  },
                  {
                    "role": "user",
                    "name": "Alice",
                    "parts": [
                      {
                        "type": "text",
                        "content": "Hey!"
                      }
                    ]
                  },
                  {
                    "role": "assistant",
                    "parts": [
                      {
                        "type": "tool_call",
                        "id": "12345",
                        "name": "GetPersonName"
                      }
                    ]
                  },
                  {
                    "role": "tool",
                    "parts": [
                      {
                        "type": "tool_call_response",
                        "id": "12345",
                        "response": "John"
                      }
                    ]
                  },
                  {
                    "role": "assistant",
                    "name": "BotAssistant",
                    "parts": [
                      {
                        "type": "text",
                        "content": "Hey John, what's up?"
                      }
                    ]
                  },
                  {
                    "role": "user",
                    "parts": [
                      {
                        "type": "text",
                        "content": "What's the biggest animal?"
                      }
                    ]
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.input.messages"]));

            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "role": "assistant",
                    "parts": [
                      {
                        "type": "text",
                        "content": "The blue whale, I think."
                      }
                    ],
                    "finish_reason": "stop"
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.output.messages"]));

            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                      "type": "text",
                      "content": "You are helpful."
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.system_instructions"]));

            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "type": "function",
                    "name": "GetPersonAge",
                    "description": "Gets the age of a person by name.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "personName": {
                          "type": "string"
                        }
                      },
                      "required": [
                        "personName"
                      ]
                    }
                  },
                  {
                    "type": "web_search",
                    "name": "web_search"
                  },
                  {
                    "type": "file_search",
                    "name": "file_search"
                  },
                  {
                    "type": "code_interpreter",
                    "name": "code_interpreter"
                  },
                  {
                    "type": "mcp",
                    "name": "mcp"
                  },
                  {
                    "type": "function",
                    "name": "GetCurrentWeather",
                    "description": "Gets the current weather for a location.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "location": {
                          "type": "string"
                        }
                      },
                      "required": [
                        "location"
                      ]
                    }
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.tool.definitions"]));
        }
        else
        {
            Assert.False(tags.ContainsKey("gen_ai.input.messages"));
            Assert.False(tags.ContainsKey("gen_ai.output.messages"));
            Assert.False(tags.ContainsKey("gen_ai.system_instructions"));
            Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "type": "function",
                    "name": "GetPersonAge"
                  },
                  {
                    "type": "web_search",
                    "name": "web_search"
                  },
                  {
                    "type": "file_search",
                    "name": "file_search"
                  },
                  {
                    "type": "code_interpreter",
                    "name": "code_interpreter"
                  },
                  {
                    "type": "mcp",
                    "name": "mcp"
                  },
                  {
                    "type": "function",
                    "name": "GetCurrentWeather"
                  }
                ]
                """), ReplaceWhitespace(tags["gen_ai.tool.definitions"]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllOfficialOtelContentPartTypes_SerializedCorrectly(bool streaming)
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [
                    new TextContent("Assistant response text"),
                    new TextReasoningContent("This is reasoning"),
                    new FunctionCallContent("call-123", "GetWeather", new Dictionary<string, object?> { ["location"] = "Seattle" }),
                    new FunctionResultContent("call-123", "72°F and sunny"),
                    new DataContent(Convert.FromBase64String("aGVsbG8gd29ybGQ="), "image/png"),
                    new UriContent(new Uri("https://example.com/image.jpg"), "image/jpeg"),
                    new HostedFileContent("file-abc123"),
                ]));
            },
            GetStreamingResponseAsyncCallback = CallbackAsync,
        };

        async static IAsyncEnumerable<ChatResponseUpdate> CallbackAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new(ChatRole.Assistant, "Assistant response text");
            yield return new() { Contents = [new TextReasoningContent("This is reasoning")] };
            yield return new() { Contents = [new FunctionCallContent("call-123", "GetWeather", new Dictionary<string, object?> { ["location"] = "Seattle" })] };
            yield return new() { Contents = [new FunctionResultContent("call-123", "72°F and sunny")] };
            yield return new() { Contents = [new DataContent(Convert.FromBase64String("aGVsbG8gd29ybGQ="), "image/png")] };
            yield return new() { Contents = [new UriContent(new Uri("https://example.com/image.jpg"), "image/jpeg")] };
            yield return new() { Contents = [new HostedFileContent("file-abc123")] };
        }

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName, configure: instance =>
            {
                instance.EnableSensitiveData = true;
                instance.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
            })
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.User,
            [
                new TextContent("User request text"),
                new TextReasoningContent("User reasoning"),
                new DataContent(Convert.FromBase64String("ZGF0YSBjb250ZW50"), "audio/mp3"),
                new UriContent(new Uri("https://example.com/video.mp4"), "video/mp4"),
                new HostedFileContent("file-xyz789"),
            ]),
            new(ChatRole.Assistant, [new FunctionCallContent("call-456", "SearchFiles")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-456", "Found 3 files")]),
        ];

        if (streaming)
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages))
            {
                await Task.Yield();
            }
        }
        else
        {
            await chatClient.GetResponseAsync(messages);
        }

        var activity = Assert.Single(activities);
        Assert.NotNull(activity);

        var inputMessages = activity.Tags.First(kvp => kvp.Key == "gen_ai.input.messages").Value;
        Assert.Equal(ReplaceWhitespace("""
            [
              {
                "role": "user",
                "parts": [
                  {
                    "type": "text",
                    "content": "User request text"
                  },
                  {
                    "type": "reasoning",
                    "content": "User reasoning"
                  },
                  {
                    "type": "blob",
                    "content": "ZGF0YSBjb250ZW50",
                    "mime_type": "audio/mp3",
                    "modality": "audio"
                  },
                  {
                    "type": "uri",
                    "uri": "https://example.com/video.mp4",
                    "mime_type": "video/mp4",
                    "modality": "video"
                  },
                  {
                    "type": "file",
                    "file_id": "file-xyz789"
                  }
                ]
              },
              {
                "role": "assistant",
                "parts": [
                  {
                    "type": "tool_call",
                    "id": "call-456",
                    "name": "SearchFiles"
                  }
                ]
              },
              {
                "role": "tool",
                "parts": [
                  {
                    "type": "tool_call_response",
                    "id": "call-456",
                    "response": "Found 3 files"
                  }
                ]
              }
            ]
            """), ReplaceWhitespace(inputMessages));

        var outputMessages = activity.Tags.First(kvp => kvp.Key == "gen_ai.output.messages").Value;
        Assert.Equal(ReplaceWhitespace("""
            [
              {
                "role": "assistant",
                "parts": [
                  {
                    "type": "text",
                    "content": "Assistant response text"
                  },
                  {
                    "type": "reasoning",
                    "content": "This is reasoning"
                  },
                  {
                    "type": "tool_call",
                    "id": "call-123",
                    "name": "GetWeather",
                    "arguments": {
                      "location": "Seattle"
                    }
                  },
                  {
                    "type": "tool_call_response",
                    "id": "call-123",
                    "response": "72°F and sunny"
                  },
                  {
                    "type": "blob",
                    "content": "aGVsbG8gd29ybGQ=",
                    "mime_type": "image/png",
                    "modality": "image"
                  },
                  {
                    "type": "uri",
                    "uri": "https://example.com/image.jpg",
                    "mime_type": "image/jpeg",
                    "modality": "image"
                  },
                  {
                    "type": "file",
                    "file_id": "file-abc123"
                  }
                ]
              }
            ]
            """), ReplaceWhitespace(outputMessages));
    }

    [Fact]
    public async Task UnknownContentTypes_Ignored()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "The blue whale, I think."));
            },
        };

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName, configure: instance =>
            {
                instance.EnableSensitiveData = true;
                instance.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
            })
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.User,
            [
                new TextContent("Hello!"),
                new NonSerializableAIContent(),
                new TextContent("How are you?"),
            ]),
        ];

        var response = await chatClient.GetResponseAsync(messages);
        Assert.NotNull(response);

        var activity = Assert.Single(activities);
        Assert.NotNull(activity);

        var inputMessages = activity.Tags.First(kvp => kvp.Key == "gen_ai.input.messages").Value;
        Assert.Equal(ReplaceWhitespace("""
                [
                  {
                    "role": "user",
                    "parts": [
                      {
                        "type": "text",
                        "content": "Hello!"
                      },
                      {
                        "type": "Microsoft.Extensions.AI.OpenTelemetryChatClientTests+NonSerializableAIContent",
                        "content": {}
                      },
                      {
                          "type": "text",
                          "content": "How are you?"
                      }
                    ]
                  }
                ]
                """), ReplaceWhitespace(inputMessages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerToolCallContentTypes_SerializedCorrectly(bool streaming)
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [
                    new TextContent("Processing with tools..."),
                    new CodeInterpreterToolCallContent("ci-call-1") { Inputs = [new TextContent("print('hello')")] },
                    new CodeInterpreterToolResultContent("ci-call-1") { Outputs = [new TextContent("hello")] },
                    new ImageGenerationToolCallContent("img-123"),
                    new ImageGenerationToolResultContent("img-123") { Outputs = [new UriContent(new Uri("https://example.com/image.png"), "image/png")] },
                    new McpServerToolCallContent("mcp-call-1", "myTool", "myServer") { Arguments = new Dictionary<string, object?> { ["param1"] = "value1" } },
                    new McpServerToolResultContent("mcp-call-1") { Outputs = [new TextContent("Tool result")] },
                ]));
            },
            GetStreamingResponseAsyncCallback = CallbackAsync,
        };

        async static IAsyncEnumerable<ChatResponseUpdate> CallbackAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new(ChatRole.Assistant, "Processing with tools...");
            yield return new() { Contents = [new CodeInterpreterToolCallContent("ci-call-1") { Inputs = [new TextContent("print('hello')")] }] };
            yield return new() { Contents = [new CodeInterpreterToolResultContent("ci-call-1") { Outputs = [new TextContent("hello")] }] };
            yield return new() { Contents = [new ImageGenerationToolCallContent("img-123")] };
            yield return new()
            {
                Contents =
                [
                    new ImageGenerationToolResultContent("img-123")
                    {
                        Outputs = [new UriContent(new Uri("https://example.com/image.png"), "image/png")]
                    }
                ]
            };
            yield return new() { Contents = [new McpServerToolCallContent("mcp-call-1", "myTool", "myServer") { Arguments = new Dictionary<string, object?> { ["param1"] = "value1" } }] };
            yield return new() { Contents = [new McpServerToolResultContent("mcp-call-1") { Outputs = [new TextContent("Tool result")] }] };
        }

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName, configure: instance =>
            {
                instance.EnableSensitiveData = true;
                instance.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
            })
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "Execute code and generate an image"),
        ];

        if (streaming)
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages))
            {
                await Task.Yield();
            }
        }
        else
        {
            await chatClient.GetResponseAsync(messages);
        }

        var activity = Assert.Single(activities);
        Assert.NotNull(activity);

        var outputMessages = activity.Tags.First(kvp => kvp.Key == "gen_ai.output.messages").Value;
        Assert.Equal(ReplaceWhitespace("""
            [
              {
                "role": "assistant",
                "parts": [
                  {
                    "type": "text",
                    "content": "Processing with tools..."
                  },
                  {
                    "type": "server_tool_call",
                    "id": "ci-call-1",
                    "name": "code_interpreter",
                    "server_tool_call": {
                      "type": "code_interpreter",
                      "code": "print('hello')"
                    }
                  },
                  {
                    "type": "server_tool_call_response",
                    "id": "ci-call-1",
                    "server_tool_call_response": {
                      "type": "code_interpreter",
                      "output": [
                        {
                          "$type": "text",
                          "text": "hello"
                        }
                      ]
                    }
                  },
                  {
                    "type": "server_tool_call",
                    "id": "img-123",
                    "name": "image_generation",
                    "server_tool_call": {
                      "type": "image_generation"
                    }
                  },
                  {
                    "type": "server_tool_call_response",
                    "id": "img-123",
                    "server_tool_call_response": {
                      "type": "image_generation",
                      "output": [
                        {
                          "$type": "uri",
                          "uri": "https://example.com/image.png",
                          "media_type": "image/png"
                        }
                      ]
                    }
                  },
                  {
                    "type": "server_tool_call",
                    "id": "mcp-call-1",
                    "name": "myTool",
                    "server_tool_call": {
                      "type": "mcp",
                      "server_name": "myServer",
                      "arguments": {
                        "param1": "value1"
                      }
                    }
                  },
                  {
                    "type": "server_tool_call_response",
                    "id": "mcp-call-1",
                    "server_tool_call_response": {
                      "type": "mcp",
                      "output": [
                        {
                          "$type": "text",
                          "text": "Tool result"
                        }
                      ]
                    }
                  }
                ]
              }
            ]
            """), ReplaceWhitespace(outputMessages));
    }

    [Fact]
    public async Task McpServerToolApprovalContentTypes_SerializedCorrectly()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"));
            },
        };

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName, configure: instance =>
            {
                instance.EnableSensitiveData = true;
                instance.JsonSerializerOptions = TestJsonSerializerContext.Default.Options;
            })
            .Build();

        var toolCall = new McpServerToolCallContent("mcp-call-2", "dangerousTool", "secureServer")
        {
            Arguments = new Dictionary<string, object?> { ["action"] = "delete" }
        };

        List<ChatMessage> messages =
        [
            new(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("approval-1", toolCall),
            ]),
            new(ChatRole.User,
            [
                new ToolApprovalResponseContent("approval-1", true, toolCall),
            ]),
        ];

        await chatClient.GetResponseAsync(messages);

        var activity = Assert.Single(activities);
        Assert.NotNull(activity);

        var inputMessages = activity.Tags.First(kvp => kvp.Key == "gen_ai.input.messages").Value;
        Assert.Equal(ReplaceWhitespace("""
            [
              {
                "role": "assistant",
                "parts": [
                  {
                    "type": "server_tool_call",
                    "id": "approval-1",
                    "name": "dangerousTool",
                    "server_tool_call": {
                      "type": "mcp_approval_request",
                      "server_name": "secureServer",
                      "arguments": {
                        "action": "delete"
                      }
                    }
                  }
                ]
              },
              {
                "role": "user",
                "parts": [
                  {
                    "type": "server_tool_call_response",
                    "id": "approval-1",
                    "server_tool_call_response": {
                      "type": "mcp_approval_response",
                      "approved": true
                    }
                  }
                ]
              }
            ]
            """), ReplaceWhitespace(inputMessages));
    }

    [Fact]
    public async Task StreamingChunkMetrics_RecordedForStreamingCalls()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = CallbackAsync,
            GetServiceCallback = (serviceType, serviceKey) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("testprovider", new Uri("http://localhost:5000/api"), "testmodel") :
                null,
        };

        async static IAsyncEnumerable<ChatResponseUpdate> CallbackAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "First") { ResponseId = "id1", ModelId = "responsemodel" };
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Second") { ResponseId = "id1" };
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Third") { ResponseId = "id1" };
        }

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName)
            .Build();

        using var timeToFirstChunkCollector = new MetricCollector<double>(null, sourceName, "gen_ai.client.operation.time_to_first_chunk");
        using var timePerOutputChunkCollector = new MetricCollector<double>(null, sourceName, "gen_ai.client.operation.time_per_output_chunk");

        await foreach (var update in chatClient.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], new ChatOptions { ModelId = "mymodel" }))
        {
            // consume all updates
        }

        // time_to_first_chunk: exactly 1 measurement for the first chunk
        var ttfcMeasurements = timeToFirstChunkCollector.GetMeasurementSnapshot();
        Assert.Single(ttfcMeasurements);
        Assert.True(ttfcMeasurements[0].Value > 0);
        Assert.True(ttfcMeasurements[0].ContainsTags(
            new KeyValuePair<string, object?>("gen_ai.operation.name", "chat"),
            new KeyValuePair<string, object?>("gen_ai.request.model", "mymodel"),
            new KeyValuePair<string, object?>("gen_ai.response.model", "responsemodel"),
            new KeyValuePair<string, object?>("gen_ai.provider.name", "testprovider"),
            new KeyValuePair<string, object?>("server.address", "localhost"),
            new KeyValuePair<string, object?>("server.port", 5000)));

        // time_per_output_chunk: one measurement for each chunk after the first (2 chunks)
        var tpocMeasurements = timePerOutputChunkCollector.GetMeasurementSnapshot();
        Assert.Equal(2, tpocMeasurements.Count);
        foreach (var measurement in tpocMeasurements)
        {
            Assert.True(measurement.Value > 0);
            Assert.True(measurement.ContainsTags(
                new KeyValuePair<string, object?>("gen_ai.operation.name", "chat"),
                new KeyValuePair<string, object?>("gen_ai.request.model", "mymodel"),
                new KeyValuePair<string, object?>("gen_ai.response.model", "responsemodel"),
                new KeyValuePair<string, object?>("gen_ai.provider.name", "testprovider"),
                new KeyValuePair<string, object?>("server.address", "localhost"),
                new KeyValuePair<string, object?>("server.port", 5000)));
        }
    }

    [Fact]
    public async Task StreamingChunkMetrics_NotRecordedForNonStreamingCalls()
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (messages, options, cancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response"));
            },
        };

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(null, sourceName)
            .Build();

        using var timeToFirstChunkCollector = new MetricCollector<double>(null, sourceName, "gen_ai.client.operation.time_to_first_chunk");
        using var timePerOutputChunkCollector = new MetricCollector<double>(null, sourceName, "gen_ai.client.operation.time_per_output_chunk");

        await chatClient.GetResponseAsync([new(ChatRole.User, "Hello")]);

        Assert.Empty(timeToFirstChunkCollector.GetMeasurementSnapshot());
        Assert.Empty(timePerOutputChunkCollector.GetMeasurementSnapshot());
    }

    private sealed class NonSerializableAIContent : AIContent;

    private static string ReplaceWhitespace(string? input) => Regex.Replace(input ?? "", @"\s+", " ").Trim();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionLogged_Async(bool streaming)
    {
        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));

        var expectedException = new InvalidOperationException("test exception message");

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => throw expectedException,
            GetStreamingResponseAsyncCallback = (messages, options, cancellationToken) => throw expectedException,
            GetServiceCallback = (serviceType, serviceKey) =>
                serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("testservice", new Uri("http://localhost:12345"), "testmodel") :
                null,
        };

        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry(loggerFactory, sourceName)
            .Build();

        if (streaming)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var update in chatClient.GetStreamingResponseAsync([new(ChatRole.User, "Hello")]))
                {
                    _ = update;
                }
            });
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                chatClient.GetResponseAsync([new(ChatRole.User, "Hello")]));
        }

        var activity = Assert.Single(activities);

        // Existing error behavior is preserved
        Assert.Equal(expectedException.GetType().FullName, activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);

        // Exception is logged via ILogger
        var logEntry = Assert.Single(collector.GetSnapshot());
        Assert.Equal("gen_ai.client.operation.exception", logEntry.Id.Name);
        Assert.Equal(LogLevel.Warning, logEntry.Level);
        Assert.Same(expectedException, logEntry.Exception);
    }

    [Fact]
    public async Task Streaming_NoListeners_PreservesActivityCurrent()
    {
        // When no listener is registered for the ActivitySource, the activity will be null.
        // Activity.Current should NOT be set to null during streaming in that case.
        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = CallbackAsync,
        };

        // Deliberately do NOT register any listener for the source name.
        using var chatClient = innerClient
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();

        // Create an existing activity that should be preserved.
        using var parentSource = new ActivitySource(Guid.NewGuid().ToString());
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source == parentSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var parentActivity = parentSource.StartActivity("parent");
        Assert.NotNull(parentActivity);
        Assert.Same(parentActivity, Activity.Current);

        await foreach (var update in chatClient.GetStreamingResponseAsync([new(ChatRole.User, "Hello")]))
        {
            // Activity.Current should still be the parent activity after each yield return.
            Assert.Same(parentActivity, Activity.Current);
        }

        // Activity.Current should still be preserved after the stream completes.
        Assert.Same(parentActivity, Activity.Current);

        async static IAsyncEnumerable<ChatResponseUpdate> CallbackAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk1");
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk2");
        }
    }
}

