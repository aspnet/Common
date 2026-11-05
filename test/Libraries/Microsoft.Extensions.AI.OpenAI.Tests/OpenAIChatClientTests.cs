// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OpenAI;
using OpenAI.Chat;
using Xunit;

#pragma warning disable S103 // Lines should not be too long
#pragma warning disable OPENAI001 // Experimental OpenAI APIs

namespace Microsoft.Extensions.AI;

public class OpenAIChatClientTests
{
    [Fact]
    public void AsIChatClient_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => ((ChatClient)null!).AsIChatClient());
    }

    [Fact]
    public void AsIChatClient_OpenAIClient_ProducesExpectedMetadata()
    {
        Uri endpoint = new("http://localhost/some/endpoint");
        string model = "amazingModel";

        var client = new OpenAIClient(new ApiKeyCredential("key"), new OpenAIClientOptions { Endpoint = endpoint });

        IChatClient chatClient = client.GetChatClient(model).AsIChatClient();
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.Equal("openai", metadata?.ProviderName);
        Assert.Equal(endpoint, metadata?.ProviderUri);
        Assert.Equal(model, metadata?.DefaultModelId);

        chatClient = client.GetChatClient(model).AsIChatClient();
        metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.Equal("openai", metadata?.ProviderName);
        Assert.Equal(endpoint, metadata?.ProviderUri);
        Assert.Equal(model, metadata?.DefaultModelId);
    }

    [Fact]
    public void GetService_OpenAIClient_SuccessfullyReturnsUnderlyingClient()
    {
        ChatClient openAIClient = new OpenAIClient(new ApiKeyCredential("key")).GetChatClient("model");
        IChatClient chatClient = openAIClient.AsIChatClient();

        Assert.Same(chatClient, chatClient.GetService<IChatClient>());

        Assert.Same(openAIClient, chatClient.GetService<ChatClient>());

        Assert.NotNull(chatClient.GetService<ChatClient>());

        using IChatClient pipeline = chatClient
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .Build();

        Assert.NotNull(pipeline.GetService<FunctionInvokingChatClient>());
        Assert.NotNull(pipeline.GetService<DistributedCachingChatClient>());
        Assert.NotNull(pipeline.GetService<CachingChatClient>());
        Assert.NotNull(pipeline.GetService<OpenTelemetryChatClient>());

        Assert.Same(openAIClient, pipeline.GetService<ChatClient>());
        Assert.IsType<FunctionInvokingChatClient>(pipeline.GetService<IChatClient>());
    }

    [Fact]
    public void GetService_ChatClient_SuccessfullyReturnsUnderlyingClient()
    {
        ChatClient openAIClient = new OpenAIClient(new ApiKeyCredential("key")).GetChatClient("model");
        IChatClient chatClient = openAIClient.AsIChatClient();

        Assert.Same(chatClient, chatClient.GetService<IChatClient>());
        Assert.Same(openAIClient, chatClient.GetService<ChatClient>());

        using IChatClient pipeline = chatClient
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .Build();

        Assert.NotNull(pipeline.GetService<FunctionInvokingChatClient>());
        Assert.NotNull(pipeline.GetService<DistributedCachingChatClient>());
        Assert.NotNull(pipeline.GetService<CachingChatClient>());
        Assert.NotNull(pipeline.GetService<OpenTelemetryChatClient>());

        Assert.Same(openAIClient, pipeline.GetService<ChatClient>());
        Assert.IsType<FunctionInvokingChatClient>(pipeline.GetService<IChatClient>());
    }

    [Fact]
    public async Task BasicRequestResponse_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-4o-mini",
                "max_completion_tokens":10
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI",
              "object": "chat.completion",
              "created": 1727888631,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 8,
                "completion_tokens": 9,
                "total_tokens": 17,
                "prompt_tokens_details": {
                  "cached_tokens": 13
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            AllowMultipleToolCalls = false,
            MaxOutputTokens = 10,
            Temperature = 0.5f,
        });
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI", response.ResponseId);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI", response.Messages.Single().MessageId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_888_631), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(8, response.Usage.InputTokenCount);
        Assert.Equal(9, response.Usage.OutputTokenCount);
        Assert.Equal(17, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public async Task BasicRequestResponse_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-4o-mini",
                "stream":true,
                "stream_options":{"include_usage":true},
                "max_completion_tokens":20
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"role":"assistant","content":"","refusal":null},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" How"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" can"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" I"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" assist"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" you"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":" today"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":"?"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{},"logprobs":null,"finish_reason":"stop"}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[],"usage":{"prompt_tokens":8,"completion_tokens":9,"total_tokens":17,"prompt_tokens_details":{"cached_tokens":5,"audio_tokens":123},"completion_tokens_details":{"reasoning_tokens":90,"audio_tokens":456}}}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello! How can I assist you today?", string.Concat(updates.Select(u => u.Text)));

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_727_889_370);
        Assert.Equal(12, updates.Count);
        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK", updates[i].ResponseId);
            Assert.Equal("chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK", updates[i].MessageId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("gpt-4o-mini-2024-07-18", updates[i].ModelId);
            Assert.Equal(ChatRole.Assistant, updates[i].Role);
            Assert.Equal(i == 10 ? 0 : 1, updates[i].Contents.Count);
            Assert.Equal(i < 10 ? null : ChatFinishReason.Stop, updates[i].FinishReason);
        }

        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(8, usage.Details.InputTokenCount);
        Assert.Equal(9, usage.Details.OutputTokenCount);
        Assert.Equal(17, usage.Details.TotalTokenCount);
        Assert.Equal(5, usage.Details.CachedInputTokenCount);
        Assert.Equal(90, usage.Details.ReasoningTokenCount);

        Assert.Equal(new AdditionalPropertiesDictionary<long>
        {
            { "InputTokenDetails.AudioTokenCount", 123 },
            { "OutputTokenDetails.AudioTokenCount", 456 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, usage.Details.AdditionalCounts);
    }

    [Fact]
    public async Task StreamingResponse_ModelId_IgnoresEmptyChunks()
    {
        const string Output = """
            data: {"id":"","object":"chat.completion.chunk","created":0,"model":"","choices":[],"prompt_filter_results":[]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-mini-2024-07-18","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello!"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1727889370,"model":"","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(new HttpHandlerExpectedInput(), Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello"))
        {
            updates.Add(update);
        }

        Assert.Equal(3, updates.Count);
        Assert.Null(updates[0].ModelId);
        Assert.Equal("gpt-4o-mini-2024-07-18", updates[1].ModelId);
        Assert.Equal("gpt-4o-mini-2024-07-18", updates[2].ModelId);
        Assert.Equal("Hello!", updates[1].Text);
        Assert.Equal(ChatFinishReason.Stop, updates[2].FinishReason);
    }

    [Fact]
    public async Task ChatOptions_StrictRespected()
    {
        const string Input = """
            {
                "tools": [
                    {
                        "function": {
                            "description": "Gets the age of the specified person.",
                            "name": "GetPersonAge",
                            "strict": true,
                            "parameters": {
                                "type": "object",
                                "required": [],
                                "properties": {},
                                "additionalProperties": false
                            }
                        },
                        "type": "function"
                    }
                ],
                "messages": [
                    {
                        "role": "user",
                        "content": "hello"
                    }
                ],
                "model": "gpt-4o-mini",
                "tool_choice": "auto"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI",
              "object": "chat.completion",
              "created": 1727888631,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools = [AIFunctionFactory.Create(() => 42, "GetPersonAge", "Gets the age of the specified person.")],
            AdditionalProperties = new()
            {
                ["strict"] = true,
            },
        });
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ChatOptions_DoNotOverwrite_NotNullPropertiesInRawRepresentation_NonStreaming()
    {
        const string Input = """
            {
              "messages":[{"role":"user","content":"hello"}],
              "model":"gpt-4o-mini",
              "frequency_penalty":0.75,
              "max_completion_tokens":10,
              "top_p":0.5,
              "presence_penalty":0.5,
              "temperature":0.5,
              "seed":42,
              "stop":["hello","world"],
              "response_format":{"type":"text"},
              "tools":[
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"additionalProperties":false,"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}}}}},
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"additionalProperties":false,"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}}}}}
                ],
              "tool_choice":"auto"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-123",
              "object": "chat.completion",
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?"
                  }
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, modelId: "gpt-4o-mini");
        AIFunction tool = AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.");

        ChatOptions chatOptions = new()
        {
            RawRepresentationFactory = (c) =>
            {
                ChatCompletionOptions openAIOptions = new()
                {
                    FrequencyPenalty = 0.75f,
                    MaxOutputTokenCount = 10,
                    TopP = 0.5f,
                    PresencePenalty = 0.5f,
                    Temperature = 0.5f,
                    Seed = 42,
                    ToolChoice = ChatToolChoice.CreateAutoChoice(),
                    ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateTextFormat()
                };
                openAIOptions.StopSequences.Add("hello");
                openAIOptions.Tools.Add(tool.AsOpenAIChatTool());
                return openAIOptions;
            },
            ModelId = null,
            FrequencyPenalty = 0.125f,
            MaxOutputTokens = 1,
            TopP = 0.125f,
            PresencePenalty = 0.125f,
            Temperature = 0.125f,
            Seed = 1,
            StopSequences = ["world"],
            Tools = [tool],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await client.GetResponseAsync("hello", chatOptions);
        Assert.NotNull(response);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
    }

    [Fact]
    public async Task ChatOptions_DoNotOverwrite_NotNullPropertiesInRawRepresentation_Streaming()
    {
        const string Input = """
            {
              "messages":[{"role":"user","content":"hello"}],
              "model":"gpt-4o-mini",
              "frequency_penalty":0.75,
              "max_completion_tokens":10,
              "top_p":0.5,
              "presence_penalty":0.5,
              "temperature":0.5,
              "seed":42,
              "stop":["hello","world"],
              "response_format":{"type":"text"},
              "tools":[
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}},"additionalProperties":false}}},
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}},"additionalProperties":false}}}
                ],
              "tool_choice":"auto",
              "stream":true,
              "stream_options":{"include_usage":true}
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{"role":"assistant","content":"Hello! "}}]}

            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{"content":"How can I assist you today?"}}]}

            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, modelId: "gpt-4o-mini");
        AIFunction tool = AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.");

        ChatOptions chatOptions = new()
        {
            RawRepresentationFactory = (c) =>
            {
                ChatCompletionOptions openAIOptions = new()
                {
                    FrequencyPenalty = 0.75f,
                    MaxOutputTokenCount = 10,
                    TopP = 0.5f,
                    PresencePenalty = 0.5f,
                    Temperature = 0.5f,
                    Seed = 42,
                    ToolChoice = ChatToolChoice.CreateAutoChoice(),
                    ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateTextFormat()
                };
                openAIOptions.StopSequences.Add("hello");
                openAIOptions.Tools.Add(tool.AsOpenAIChatTool());
                return openAIOptions;
            },
            ModelId = null, // has no effect, you cannot change the model of an OpenAI's ChatClient.
            FrequencyPenalty = 0.125f,
            MaxOutputTokens = 1,
            TopP = 0.125f,
            PresencePenalty = 0.125f,
            Temperature = 0.125f,
            Seed = 1,
            StopSequences = ["world"],
            Tools = [tool],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json
        };

        string responseText = string.Empty;
        await foreach (var update in client.GetStreamingResponseAsync("hello", chatOptions))
        {
            responseText += update.Text;
        }

        Assert.Equal("Hello! How can I assist you today?", responseText);
    }

    [Fact]
    public async Task ChatOptions_Overwrite_NullPropertiesInRawRepresentation_NonStreaming()
    {
        const string Input = """
            {
              "messages":[{"role":"user","content":"hello"}],
              "model":"gpt-4o-mini",
              "frequency_penalty":0.125,
              "max_completion_tokens":1,
              "top_p":0.125,
              "presence_penalty":0.125,
              "temperature":0.125,
              "seed":1,
              "stop":["world"],
              "response_format":{"type":"json_object"},
              "tools":[
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"additionalProperties":false,"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}}}}}
                ],
              "tool_choice":"none"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-123",
              "object": "chat.completion",
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?"
                  }
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, modelId: "gpt-4o-mini");
        AIFunction tool = AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.");

        ChatOptions chatOptions = new()
        {
            RawRepresentationFactory = (c) =>
            {
                ChatCompletionOptions openAIOptions = new();
                Assert.Null(openAIOptions.FrequencyPenalty);
                Assert.Null(openAIOptions.MaxOutputTokenCount);
                Assert.Null(openAIOptions.TopP);
                Assert.Null(openAIOptions.PresencePenalty);
                Assert.Null(openAIOptions.Temperature);
                Assert.Null(openAIOptions.Seed);
                Assert.Empty(openAIOptions.StopSequences);
                Assert.Empty(openAIOptions.Tools);
                Assert.Null(openAIOptions.ToolChoice);
                Assert.Null(openAIOptions.ResponseFormat);
                return openAIOptions;
            },
            ModelId = null, // has no effect, you cannot change the model of an OpenAI's ChatClient.
            FrequencyPenalty = 0.125f,
            MaxOutputTokens = 1,
            TopP = 0.125f,
            PresencePenalty = 0.125f,
            Temperature = 0.125f,
            Seed = 1,
            StopSequences = ["world"],
            Tools = [tool],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await client.GetResponseAsync("hello", chatOptions);
        Assert.NotNull(response);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
    }

    [Fact]
    public async Task ChatOptions_Overwrite_NullPropertiesInRawRepresentation_Streaming()
    {
        const string Input = """
            {
              "messages":[{"role":"user","content":"hello"}],
              "model":"gpt-4o-mini",
              "frequency_penalty":0.125,
              "max_completion_tokens":1,
              "top_p":0.125,
              "presence_penalty":0.125,
              "temperature":0.125,
              "seed":1,
              "stop":["world"],
              "response_format":{"type":"json_object"},
              "tools":[
                  {"type":"function","function":{"name":"GetPersonAge","description":"Gets the age of the specified person.","parameters":{"additionalProperties":false,"type":"object","required":["personName"],"properties":{"personName":{"description":"The person whose age is being requested","type":"string"}}}}}
                ],
              "tool_choice":"none",
              "stream":true,
              "stream_options":{"include_usage":true}
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{"role":"assistant","content":"Hello! "}}]}

            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{"content":"How can I assist you today?"}}]}

            data: {"id":"chatcmpl-123","object":"chat.completion.chunk","choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, modelId: "gpt-4o-mini");
        AIFunction tool = AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.");

        ChatOptions chatOptions = new()
        {
            RawRepresentationFactory = (c) =>
            {
                ChatCompletionOptions openAIOptions = new();
                Assert.Null(openAIOptions.FrequencyPenalty);
                Assert.Null(openAIOptions.MaxOutputTokenCount);
                Assert.Null(openAIOptions.TopP);
                Assert.Null(openAIOptions.PresencePenalty);
                Assert.Null(openAIOptions.Temperature);
                Assert.Null(openAIOptions.Seed);
                Assert.Empty(openAIOptions.StopSequences);
                Assert.Empty(openAIOptions.Tools);
                Assert.Null(openAIOptions.ToolChoice);
                Assert.Null(openAIOptions.ResponseFormat);
                return openAIOptions;
            },
            ModelId = null,
            FrequencyPenalty = 0.125f,
            MaxOutputTokens = 1,
            TopP = 0.125f,
            PresencePenalty = 0.125f,
            Temperature = 0.125f,
            Seed = 1,
            StopSequences = ["world"],
            Tools = [tool],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json
        };

        string responseText = string.Empty;
        await foreach (var update in client.GetStreamingResponseAsync("hello", chatOptions))
        {
            responseText += update.Text;
        }

        Assert.Equal("Hello! How can I assist you today?", responseText);
    }

    [Fact]
    public async Task StronglyTypedOptions_AllSent()
    {
        const string Input = """
            {
                "metadata": {
                    "something": "else"
                },
                "user": "12345",
                "messages": [
                    {
                        "role": "user",
                        "content": "hello"
                    }
                ],
                "model": "gpt-4o-mini",
                "top_logprobs": 42,
                "store": true,
                "logit_bias": {
                    "12": 34
                },
                "logprobs": true,
                "tools": [
                    {
                        "type": "function",
                        "function": {
                            "description": "",
                            "name": "GetPersonAge",
                            "parameters": {
                                "type": "object",
                                "required": [
                                    "name"
                                ],
                                "properties": {
                                    "name": {
                                        "type": "string"
                                    }
                                },
                                "additionalProperties": false
                            }
                        }
                    }
                ],
                "tool_choice": "auto",
                "parallel_tool_calls": false
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI",
              "object": "chat.completion",
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        Assert.NotNull(await client.GetResponseAsync("hello", new()
        {
            AllowMultipleToolCalls = false,
            Tools = [AIFunctionFactory.Create((string name) => 42, "GetPersonAge")],
            RawRepresentationFactory = (c) =>
            {
                var openAIOptions = new ChatCompletionOptions
                {
                    StoredOutputEnabled = true,
                    IncludeLogProbabilities = true,
                    TopLogProbabilityCount = 42,
                    EndUserId = "12345",
                };
                openAIOptions.Metadata.Add("something", "else");
                openAIOptions.LogitBiases.Add(12, 34);
                return openAIOptions;
            },
        }));
    }

    [Fact]
    public async Task MultipleMessages_NonStreaming()
    {
        const string Input = """
            {
                "frequency_penalty": 0.75,
                "presence_penalty": 0.5,
                "temperature": 0.25,
                "messages": [
                    {
                        "role": "system",
                        "content": "You are a really nice friend."
                    },
                    {
                        "role": "user",
                        "content": "hello!"
                    },
                    {
                        "role": "assistant",
                        "content": "hi, how are you?"
                    },
                    {
                        "role": "user",
                        "content": "i'm good. how are you?"
                    }
                ],
                "model": "gpt-4o-mini",
                "stop": ["great"],
                "seed": 42
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P",
              "object": "chat.completion",
              "created": 1727894187,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I’m doing well, thank you! What’s on your mind today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 42,
                "completion_tokens": 15,
                "total_tokens": 57,
                "prompt_tokens_details": {
                  "cached_tokens": 13,
                  "audio_tokens": 123
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90,
                  "audio_tokens": 456
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a really nice friend."),
            new(ChatRole.User, "hello!"),
            new(ChatRole.Assistant, "hi, how are you?"),
            new(ChatRole.User, "i'm good. how are you?"),
        ];

        var response = await client.GetResponseAsync(messages, new()
        {
            Temperature = 0.25f,
            FrequencyPenalty = 0.75f,
            PresencePenalty = 0.5f,
            StopSequences = ["great"],
            Seed = 42,
        });
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P", response.ResponseId);
        Assert.Equal("I’m doing well, thank you! What’s on your mind today?", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P", response.Messages.Single().MessageId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_187), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(42, response.Usage.InputTokenCount);
        Assert.Equal(15, response.Usage.OutputTokenCount);
        Assert.Equal(57, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 123 },
            { "OutputTokenDetails.AudioTokenCount", 456 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public async Task MultiPartSystemMessage_NonStreaming()
    {
        const string Input = """
            {
                "messages": [
                    {
                        "role": "system",
                        "content": [
                            {
                                "type": "text",
                                "text": "You are a really nice friend."
                            },
                            {
                                "type": "text",
                                "text": "Really nice."
                            }
                        ]
                    },
                    {
                        "role": "user",
                        "content": "hello!"
                    }
                ],
                "model": "gpt-4o-mini"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P",
              "object": "chat.completion",
              "created": 1727894187,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Hi! It's so good to hear from you!",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 42,
                "completion_tokens": 15,
                "total_tokens": 57,
                "prompt_tokens_details": {
                  "cached_tokens": 13
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, [new TextContent("You are a really nice friend."), new TextContent("Really nice.")]),
            new(ChatRole.User, "hello!"),
        ];

        var response = await client.GetResponseAsync(messages);
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P", response.ResponseId);
        Assert.Equal("Hi! It's so good to hear from you!", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_187), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(42, response.Usage.InputTokenCount);
        Assert.Equal(15, response.Usage.OutputTokenCount);
        Assert.Equal(57, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public async Task EmptyAssistantMessage_NonStreaming()
    {
        const string Input = """
            {
                "messages": [
                    {
                        "role": "system",
                        "content": "You are a really nice friend."
                    },
                    {
                        "role": "user",
                        "content": "hello!"
                    },
                    {
                        "role": "assistant",
                        "content": ""
                    },
                    {
                        "role": "user",
                        "content": "i\u0027m good. how are you?"
                    }
                ],
                "model": "gpt-4o-mini"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P",
              "object": "chat.completion",
              "created": 1727894187,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I’m doing well, thank you! What’s on your mind today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 42,
                "completion_tokens": 15,
                "total_tokens": 57,
                "prompt_tokens_details": {
                  "cached_tokens": 13
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a really nice friend."),
            new(ChatRole.User, "hello!"),
            new(ChatRole.Assistant, (string?)null),
            new(ChatRole.User, "i'm good. how are you?"),
        ];

        var response = await client.GetResponseAsync(messages);
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P", response.ResponseId);
        Assert.Equal("I’m doing well, thank you! What’s on your mind today?", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_187), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(42, response.Usage.InputTokenCount);
        Assert.Equal(15, response.Usage.OutputTokenCount);
        Assert.Equal(57, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public async Task FunctionCallContent_NonStreaming()
    {
        const string Input = """
            {
                "tools": [
                    {
                        "function": {
                            "description": "Gets the age of the specified person.",
                            "name": "GetPersonAge",
                            "parameters": {
                                "type": "object",
                                "required": [
                                    "personName"
                                ],
                                "properties": {
                                    "personName": {
                                        "description": "The person whose age is being requested",
                                        "type": "string"
                                    }
                                },
                                "additionalProperties": false
                            }
                        },
                        "type": "function"
                    }
                ],
                "messages": [
                    {
                        "role": "user",
                        "content": "How old is Alice?"
                    }
                ],
                "model": "gpt-4o-mini",
                "tool_choice": "auto"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADydKhrSKEBWJ8gy0KCIU74rN3Hmk",
              "object": "chat.completion",
              "created": 1727894702,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "id": "call_8qbINM045wlmKZt9bVJgwAym",
                        "type": "function",
                        "function": {
                          "name": "GetPersonAge",
                          "arguments": "{\"personName\":\"Alice\"}"
                        }
                      }
                    ],
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "tool_calls"
                }
              ],
              "usage": {
                "prompt_tokens": 61,
                "completion_tokens": 16,
                "total_tokens": 77,
                "prompt_tokens_details": {
                  "cached_tokens": 13
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("How old is Alice?", new()
        {
            Tools = [AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.")],
        });
        Assert.NotNull(response);

        Assert.Empty(response.Text);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_702), response.CreatedAt);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(61, response.Usage.InputTokenCount);
        Assert.Equal(16, response.Usage.OutputTokenCount);
        Assert.Equal(77, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);

        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);

        Assert.Single(response.Messages.Single().Contents);
        FunctionCallContent fcc = Assert.IsType<FunctionCallContent>(response.Messages.Single().Contents[0]);
        Assert.Equal("GetPersonAge", fcc.Name);
        AssertExtensions.EqualFunctionCallParameters(new Dictionary<string, object?> { ["personName"] = "Alice" }, fcc.Arguments);
    }

    [Fact]
    public async Task HostedWebSearchTool_MapsToWebSearchOptions_NonStreaming()
    {
        const string Input = """
            {
                "messages": [
                    {
                        "role": "user",
                        "content": "What day is it?"
                    }
                ],
                "model": "gpt-4o-mini",
                "web_search_options": {}
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADydKhrSKEBWJ8gy0KCIU74rN3Hmk",
              "object": "chat.completion",
              "created": 1727894702,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "December 31, 2023",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 61,
                "completion_tokens": 16,
                "total_tokens": 77,
                "prompt_tokens_details": {
                  "cached_tokens": 13
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("What day is it?", new()
        {
            Tools = [new HostedWebSearchTool()],
        });
        Assert.NotNull(response);

        Assert.Equal("December 31, 2023", response.Text);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_702), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(61, response.Usage.InputTokenCount);
        Assert.Equal(16, response.Usage.OutputTokenCount);
        Assert.Equal(77, response.Usage.TotalTokenCount);
        Assert.Equal(13, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);

        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);

        Assert.Single(response.Messages.Single().Contents);
        TextContent fcc = Assert.IsType<TextContent>(response.Messages.Single().Contents[0]);
    }

    [Fact]
    public async Task FunctionCallContent_Streaming()
    {
        const string Input = """
            {
                "tools": [
                    {
                        "function": {
                            "description": "Gets the age of the specified person.",
                            "name": "GetPersonAge",
                            "parameters": {
                                "type": "object",
                                "required": [
                                    "personName"
                                ],
                                "properties": {
                                    "personName": {
                                        "description": "The person whose age is being requested",
                                        "type": "string"
                                    }
                                },
                                "additionalProperties": false
                            }
                        },
                        "type": "function"
                    }
                ],
                "messages": [
                    {
                        "role": "user",
                        "content": "How old is Alice?"
                    }
                ],
                "model": "gpt-4o-mini",
                "stream": true,
                "stream_options": {
                    "include_usage": true
                },
                "tool_choice": "auto"
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"role":"assistant","content":null,"tool_calls":[{"index":0,"id":"call_F9ZaqPWo69u0urxAhVt8meDW","type":"function","function":{"name":"GetPersonAge","arguments":""}}],"refusal":null},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\""}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"person"}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"Name"}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\":\""}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"Alice"}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"}"}}]},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{},"logprobs":null,"finish_reason":"tool_calls"}],"usage":null}

            data: {"id":"chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl","object":"chat.completion.chunk","created":1727895263,"model":"gpt-4o-mini-2024-07-18","system_fingerprint":"fp_f85bea6784","choices":[],"usage":{"prompt_tokens":61,"completion_tokens":16,"total_tokens":77,"prompt_tokens_details":{"cached_tokens":0},"completion_tokens_details":{"reasoning_tokens":90}}}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("How old is Alice?", new()
        {
            Tools = [AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.")],
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("", string.Concat(updates.Select(u => u.Text)));

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_727_895_263);
        Assert.Equal(10, updates.Count);
        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl", updates[i].ResponseId);
            Assert.Equal("chatcmpl-ADymNiWWeqCJqHNFXiI1QtRcLuXcl", updates[i].MessageId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("gpt-4o-mini-2024-07-18", updates[i].ModelId);
            Assert.Equal(ChatRole.Assistant, updates[i].Role);
            Assert.Equal(i < 7 ? null : ChatFinishReason.ToolCalls, updates[i].FinishReason);
        }

        FunctionCallContent fcc = Assert.IsType<FunctionCallContent>(Assert.Single(updates[updates.Count - 1].Contents));
        Assert.Equal("call_F9ZaqPWo69u0urxAhVt8meDW", fcc.CallId);
        Assert.Equal("GetPersonAge", fcc.Name);
        AssertExtensions.EqualFunctionCallParameters(new Dictionary<string, object?> { ["personName"] = "Alice" }, fcc.Arguments);

        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(61, usage.Details.InputTokenCount);
        Assert.Equal(16, usage.Details.OutputTokenCount);
        Assert.Equal(77, usage.Details.TotalTokenCount);
        Assert.Equal(0, usage.Details.CachedInputTokenCount);
        Assert.Equal(90, usage.Details.ReasoningTokenCount);

        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, usage.Details.AdditionalCounts);
    }

    [Fact]
    public async Task AssistantMessageWithBothToolsAndContent_NonStreaming()
    {
        const string Input = """
            {
                "messages": [
                    {
                        "role": "system",
                        "content": "You are a really nice friend."
                    },
                    {
                        "role": "user",
                        "content": "hello!"
                    },
                    {
                        "role": "assistant",
                        "content": "hi, how are you?",
                        "tool_calls": [
                            {
                                "id": "12345",
                                "type": "function",
                                "function": {
                                    "name": "SayHello",
                                    "arguments": "null"
                                }
                            },
                            {
                                "id": "12346",
                                "type": "function",
                                "function": {
                                    "name": "SayHi",
                                    "arguments": "null"
                                }
                            }
                        ]
                    },
                    {
                        "role": "tool",
                        "tool_call_id": "12345",
                        "content": "{ \"$type\": \"text\", \"text\": \"Said hello\" }"
                    },
                    {
                        "role":"tool",
                        "tool_call_id":"12346",
                        "content":"Said hi"
                    },
                    {
                        "role":"assistant",
                        "content":"You are great."
                    },
                    {
                        "role":"user",
                        "content":"Thanks!"
                    }
                ],
                "model":"gpt-4o-mini"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P",
              "object": "chat.completion",
              "created": 1727894187,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I’m doing well, thank you! What’s on your mind today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 42,
                "completion_tokens": 15,
                "total_tokens": 57,
                "prompt_tokens_details": {
                  "cached_tokens": 20
                },
                "completion_tokens_details": {
                  "reasoning_tokens": 90
                }
              },
              "system_fingerprint": "fp_f85bea6784"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a really nice friend."),
            new(ChatRole.User, "hello!"),
            new(ChatRole.Assistant,
            [
                new TextContent("hi, how are you?"),
                new FunctionCallContent("12345", "SayHello"),
                new FunctionCallContent("12346", "SayHi"),
            ]),
            new (ChatRole.Tool,
            [
                new FunctionResultContent("12345", new TextContent("Said hello")),
                new FunctionResultContent("12346", "Said hi"),
            ]),
            new(ChatRole.Assistant, "You are great."),
            new(ChatRole.User, "Thanks!"),
        ];

        var response = await client.GetResponseAsync(messages);
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADyV17bXeSm5rzUx3n46O7m3M0o3P", response.ResponseId);
        Assert.Equal("I’m doing well, thank you! What’s on your mind today?", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_894_187), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(42, response.Usage.InputTokenCount);
        Assert.Equal(15, response.Usage.OutputTokenCount);
        Assert.Equal(57, response.Usage.TotalTokenCount);
        Assert.Equal(20, response.Usage.CachedInputTokenCount);
        Assert.Equal(90, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public Task DataContentMessage_Image_AdditionalProperty_ChatImageDetailLevel_NonStreaming()
        => DataContentMessage_Image_AdditionalPropertyDetail_NonStreaming("high");

    [Fact]
    public Task DataContentMessage_Image_AdditionalProperty_StringDetail_NonStreaming()
        => DataContentMessage_Image_AdditionalPropertyDetail_NonStreaming(ChatImageDetailLevel.High);

    [Fact]
    public Task DataContentMessage_Image_AdditionalProperty_InvalidDetail_NonStreaming()
        => DataContentMessage_Image_AdditionalPropertyDetail_NonStreaming(42, expectedDetail: null);

    private static async Task DataContentMessage_Image_AdditionalPropertyDetail_NonStreaming(object detailValue, string? expectedDetail = "high")
    {
        string detailProperty = expectedDetail is not null ? $"\"detail\": \"{expectedDetail}\"," : string.Empty;
        string input = $$"""
            {
              "messages": [
                {
                  "role": "user",
                  "content": [
                    {
                      "type": "text",
                      "text": "What does this logo say?"
                    },
                    {
                      "type": "image_url",
                      "image_url": {
                        {{detailProperty}}
                        "url": "{{ImageDataUri.GetImageDataUri()}}"
                      }
                    }
                  ]
                }
              ],
              "model": "gpt-4o-mini"
            }
            """;

        const string Output = """
            {
              "choices": [
                {
                  "finish_reason": "stop",
                  "index": 0,
                  "logprobs": null,
                  "message": {
                    "content": "The logo says \".NET\", which is a software development framework created by Microsoft. It is used for building and running applications on Windows, macOS, and Linux environments. The logo typically also represents the broader .NET ecosystem, which includes various programming languages, libraries, and tools.",
                    "refusal": null,
                    "role": "assistant"
                  }
                }
              ],
              "created": 1743531271,
              "id": "chatcmpl-BHaQ3nkeSDGhLzLya3mGbB1EXSqve",
              "model": "gpt-4o-mini-2024-07-18",
              "object": "chat.completion",
              "system_fingerprint": "fp_b705f0c291",
              "usage": {
                "completion_tokens": 56,
                "completion_tokens_details": {
                  "accepted_prediction_tokens": 0,
                  "audio_tokens": 0,
                  "reasoning_tokens": 0,
                  "rejected_prediction_tokens": 0
                },
                "prompt_tokens": 8513,
                "prompt_tokens_details": {
                  "audio_tokens": 0,
                  "cached_tokens": 0
                },
                "total_tokens": 8569
              }
            }
            """;

        using VerbatimHttpHandler handler = new(input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync(
            [
            new(ChatRole.User,
                [
                    new TextContent("What does this logo say?"),
                    new DataContent(ImageDataUri.GetImageDataUri(), "image/png")
                    {
                        AdditionalProperties = new()
                        {
                            { "detail", detailValue }
                        }
                    }
                ])
            ]);
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-BHaQ3nkeSDGhLzLya3mGbB1EXSqve", response.ResponseId);
        Assert.Equal("The logo says \".NET\", which is a software development framework created by Microsoft. It is used for building and running applications on Windows, macOS, and Linux environments. The logo typically also represents the broader .NET ecosystem, which includes various programming languages, libraries, and tools.", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("chatcmpl-BHaQ3nkeSDGhLzLya3mGbB1EXSqve", response.Messages.Single().MessageId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_743_531_271), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(8513, response.Usage.InputTokenCount);
        Assert.Equal(56, response.Usage.OutputTokenCount);
        Assert.Equal(8569, response.Usage.TotalTokenCount);
        Assert.Equal(0, response.Usage.CachedInputTokenCount);
        Assert.Equal(0, response.Usage.ReasoningTokenCount);
        Assert.Equal(new Dictionary<string, long>
        {
            { "InputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AudioTokenCount", 0 },
            { "OutputTokenDetails.AcceptedPredictionTokenCount", 0 },
            { "OutputTokenDetails.RejectedPredictionTokenCount", 0 },
        }, response.Usage.AdditionalCounts);
    }

    [Fact]
    public async Task RequestHeaders_UserAgent_ContainsMEAI()
    {
        using var handler = new ThrowUserAgentExceptionHandler();
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        InvalidOperationException e = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("hello"));

        Assert.StartsWith("User-Agent header: OpenAI", e.Message);
        Assert.Contains("MEAI", e.Message);
    }

    [Fact]
    public async Task ChatOptions_ModelId_OverridesClientModel_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-4o",
                "max_completion_tokens":10
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI",
              "object": "chat.completion",
              "created": 1727888631,
              "model": "gpt-4o-2024-08-06",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Hello! How can I assist you today?",
                    "refusal": null
                  },
                  "logprobs": null,
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 8,
                "completion_tokens": 9,
                "total_tokens": 17
              }
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 10,
            Temperature = 0.5f,
            ModelId = "gpt-4o",
        });
        Assert.NotNull(response);

        Assert.Equal("chatcmpl-ADx3PvAnCwJg0woha4pYsBTi3ZpOI", response.ResponseId);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
        Assert.Equal("gpt-4o-2024-08-06", response.ModelId);
    }

    [Fact]
    public async Task ChatOptions_ModelId_OverridesClientModel_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-4o",
                "max_completion_tokens":20,
                "stream":true,
                "stream_options":{"include_usage":true}
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-2024-08-06","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"role":"assistant","content":"","refusal":null},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-2024-08-06","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-2024-08-06","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-2024-08-06","system_fingerprint":"fp_f85bea6784","choices":[{"index":0,"delta":{},"logprobs":null,"finish_reason":"stop"}],"usage":null}

            data: {"id":"chatcmpl-ADxFKtX6xIwdWRN42QvBj2u1RZpCK","object":"chat.completion.chunk","created":1727889370,"model":"gpt-4o-2024-08-06","system_fingerprint":"fp_f85bea6784","choices":[],"usage":{"prompt_tokens":8,"completion_tokens":9,"total_tokens":17}}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ModelId = "gpt-4o",
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));
        Assert.All(updates, u => Assert.Equal("gpt-4o-2024-08-06", u.ModelId));
    }

    private static IChatClient CreateChatClient(HttpClient httpClient, string modelId) =>
        new OpenAIClient(new ApiKeyCredential("apikey"), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) })
        .GetChatClient(modelId)
        .AsIChatClient();

    [Fact]
    public void AsChatMessages_PreservesRole_SystemMessage()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages = [new SystemChatMessage("You are a helpful assistant")];
        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Single(extMessages);
        Assert.Equal(ChatRole.System, extMessages[0].Role);
        Assert.Equal("You are a helpful assistant", extMessages[0].Text);
    }

    [Fact]
    public void AsChatMessages_PreservesRole_UserMessage()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages = [new UserChatMessage("Hello")];
        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Single(extMessages);
        Assert.Equal(ChatRole.User, extMessages[0].Role);
        Assert.Equal("Hello", extMessages[0].Text);
    }

    [Fact]
    public void AsChatMessages_PreservesRole_AssistantMessage()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages = [new AssistantChatMessage("Hi there!")];
        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Single(extMessages);
        Assert.Equal(ChatRole.Assistant, extMessages[0].Role);
        Assert.Equal("Hi there!", extMessages[0].Text);
    }

    [Fact]
    public void AsChatMessages_PreservesRole_DeveloperMessage()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages = [new DeveloperChatMessage("Developer instructions")];
        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Single(extMessages);
        Assert.Equal(ChatRole.System, extMessages[0].Role);
        Assert.Equal("Developer instructions", extMessages[0].Text);
    }

    [Fact]
    public void AsChatMessages_PreservesRole_ToolMessage()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages = [new ToolChatMessage("tool-123", "Result")];
        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Single(extMessages);
        Assert.Equal(ChatRole.Tool, extMessages[0].Role);
        var frc = Assert.IsType<FunctionResultContent>(Assert.Single(extMessages[0].Contents));
        Assert.Equal("tool-123", frc.CallId);
        Assert.Equal("Result", frc.Result);
    }

    [Fact]
    public void AsChatMessages_PreservesRole_MultipleMessages()
    {
        List<OpenAI.Chat.ChatMessage> openAIMessages =
        [
            new SystemChatMessage("System prompt"),
            new UserChatMessage("User message"),
            new AssistantChatMessage("Assistant response"),
            new DeveloperChatMessage("Developer note")
        ];

        var extMessages = openAIMessages.AsChatMessages().ToList();

        Assert.Equal(4, extMessages.Count);
        Assert.Equal(ChatRole.System, extMessages[0].Role);
        Assert.Equal(ChatRole.User, extMessages[1].Role);
        Assert.Equal(ChatRole.Assistant, extMessages[2].Role);
        Assert.Equal(ChatRole.System, extMessages[3].Role);
    }

    [Theory]
    [InlineData(ReasoningEffort.None, "none")]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.ExtraHigh, "xhigh")]
    public async Task ReasoningOptions_Effort_ProducesExpectedJson(ReasoningEffort effort, string expectedEffortString)
    {
        string input = $$"""
            {
                "messages": [
                    {
                        "role": "user",
                        "content": "hello"
                    }
                ],
                "model": "o4-mini",
                "reasoning_effort": "{{expectedEffortString}}"
            }
            """;

        const string Output = """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "model": "o4-mini",
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Hello!"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "o4-mini");

        Assert.NotNull(await client.GetResponseAsync("hello", new()
        {
            Reasoning = new ReasoningOptions { Effort = effort }
        }));
    }

    [Fact]
    public async Task ReasoningContent_NonStreaming_SurfacedAsTextReasoningContent()
    {
        const string Input = """
            {
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-oss-120b"
            }
            """;

        const string Output = """
            {
              "id": "c48a440c7dd64389b7fbe908e006ba3d",
              "object": "chat.completion",
              "created": 1770959477,
              "model": "gpt-oss-120b",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "9.8 is larger.",
                    "reasoning_content": "We just compare decimals: 9.11 vs 9.8. 9.8 > 9.11. Answer briefly."
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 84,
                "completion_tokens": 44,
                "total_tokens": 128
              }
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-oss-120b");

        var response = await client.GetResponseAsync("hello");
        Assert.NotNull(response);

        var message = response.Messages.Single();
        var reasoning = message.Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("We just compare decimals: 9.11 vs 9.8. 9.8 > 9.11. Answer briefly.", reasoning.Text);

        var text = message.Contents.OfType<TextContent>().Single();
        Assert.Equal("9.8 is larger.", text.Text);
    }

    [Fact]
    public async Task ReasoningContent_Streaming_SurfacedAsTextReasoningContent()
    {
        // Streaming format captured from Azure gpt-oss-120b. Reasoning chunks have reasoning_content with content:null,
        // then content chunks have content with reasoning_content:null.
        const string Input = """
            {
                "messages":[{"role":"user","content":"hello"}],
                "model":"gpt-oss-120b",
                "stream":true,
                "stream_options":{"include_usage":true}
            }
            """;

        const string Output = """
            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"role":"assistant","content":"","reasoning_content":null},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":null,"reasoning_content":"User asks"},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":null,"reasoning_content":": which"},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":null,"reasoning_content":" is larger."},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":"9","reasoning_content":null},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":".8 is larger.","reasoning_content":null},"finish_reason":null}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[{"index":0,"delta":{"content":null,"reasoning_content":null},"finish_reason":"stop"}]}

            data: {"id":"381fb75e8a1f451f8a579c9da104b739","object":"chat.completion.chunk","created":1770959485,"model":"gpt-oss-120b","choices":[],"usage":{"completion_tokens":46,"prompt_tokens":84,"total_tokens":130}}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "gpt-oss-120b");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello"))
        {
            updates.Add(update);
        }

        // Verify reasoning content was captured from the reasoning_content deltas
        string reasoningText = string.Concat(updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Select(r => r.Text));
        Assert.Equal("User asks: which is larger.", reasoningText);

        // Verify regular content was also captured from the content deltas
        Assert.Equal("9.8 is larger.", string.Concat(updates.Select(u => u.Text)));
    }

    [Fact]
    public async Task Streaming_NullDeltaContentFilterChunk_ContinuesToUsage()
    {
        const string Input = """
            {
                "messages":[{"role":"user","content":"Hello"}],
                "model":"example-model",
                "stream":true,
                "stream_options":{"include_usage":true}
            }
            """;

        const string Output = """
            data: {"id":"chatcmpl-example","object":"chat.completion.chunk","created":0,"model":"example-model","choices":[{"index":0,"delta":{"role":"assistant","content":"OK"},"finish_reason":null}]}

            data: {"id":"chatcmpl-example","object":"chat.completion.chunk","created":0,"model":"example-model","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: {"choices":[{"content_filter_offsets":{"check_offset":2,"start_offset":0,"end_offset":2},"content_filter_results":{"hate":{"filtered":false,"severity":"safe"}},"finish_reason":null,"index":0}],"created":0,"id":"","model":"","object":""}

            data: {"id":"chatcmpl-example","object":"chat.completion.chunk","created":0,"model":"example-model","choices":[],"usage":{"completion_tokens":1,"prompt_tokens":1,"total_tokens":2}}

            data: [DONE]

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateChatClient(httpClient, "example-model");

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("Hello"))
        {
            updates.Add(update);
        }

        Assert.Equal("OK", string.Concat(updates.Select(u => u.Text)));
        Assert.Contains(updates, u => u.FinishReason == ChatFinishReason.Stop);

        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(1, usage.Details.InputTokenCount);
        Assert.Equal(1, usage.Details.OutputTokenCount);
        Assert.Equal(2, usage.Details.TotalTokenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAIApiTypeTag_SetToChatCompletions(bool streaming)
    {
        const string NonStreamingOutput = """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1727888631,
              "model": "gpt-4o-mini-2024-07-18",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Hello!"
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 8,
                "completion_tokens": 2,
                "total_tokens": 10
              },
              "service_tier": "default",
              "system_fingerprint": "fp_test"
            }
            """;

        const string StreamingOutput = """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1727888631,"model":"gpt-4o-mini-2024-07-18","service_tier":"default","system_fingerprint":"fp_test","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello!"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1727888631,"model":"gpt-4o-mini-2024-07-18","service_tier":"default","system_fingerprint":"fp_test","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":8,"completion_tokens":2,"total_tokens":10}}

            data: [DONE]

            """;

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using VerbatimHttpHandler handler = new(new HttpHandlerExpectedInput(), streaming ? StreamingOutput : NonStreamingOutput);
        using HttpClient httpClient = new(handler);
        using IChatClient client = new OpenAIClient(new ApiKeyCredential("apikey"), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) })
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();

        if (streaming)
        {
            await foreach (var update in client.GetStreamingResponseAsync("hello"))
            {
                // Drain the stream.
            }
        }
        else
        {
            await client.GetResponseAsync("hello");
        }

        var activity = Assert.Single(activities);
        Assert.Equal("chat_completions", activity.GetTagItem("openai.api.type"));
        Assert.Equal("default", activity.GetTagItem("openai.response.service_tier"));
        Assert.Equal("fp_test", activity.GetTagItem("openai.response.system_fingerprint"));
    }
}
