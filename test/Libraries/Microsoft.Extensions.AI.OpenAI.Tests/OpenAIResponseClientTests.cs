// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OpenAI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable S103 // Lines should not be too long
#pragma warning disable OPENAI001 // Experimental OpenAI APIs

namespace Microsoft.Extensions.AI;

public class OpenAIResponseClientTests
{
    [Fact]
    public void AsIChatClient_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("responseClient", () => ((ResponsesClient)null!).AsIChatClient());
    }

    [Fact]
    public void AsIChatClient_ProducesExpectedMetadata()
    {
        Uri endpoint = new("http://localhost/some/endpoint");
        string model = "amazingModel";

        var client = new OpenAIClient(new ApiKeyCredential("key"), new OpenAIClientOptions { Endpoint = endpoint });

        IChatClient chatClient = client.GetResponsesClient().AsIChatClient(model);
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.Equal("openai", metadata?.ProviderName);
        Assert.Equal(endpoint, metadata?.ProviderUri);
        Assert.Equal(model, metadata?.DefaultModelId);
    }

    [Fact]
    public void GetService_SuccessfullyReturnsUnderlyingClient()
    {
        ResponsesClient openAIClient = new OpenAIClient(new ApiKeyCredential("key")).GetResponsesClient();
        IChatClient chatClient = openAIClient.AsIChatClient("model");

        Assert.Same(chatClient, chatClient.GetService<IChatClient>());
        Assert.Same(openAIClient, chatClient.GetService<ResponsesClient>());

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

        Assert.Same(openAIClient, pipeline.GetService<ResponsesClient>());
        Assert.IsType<FunctionInvokingChatClient>(pipeline.GetService<IChatClient>());
    }

    [Fact]
    public async Task BasicRequestResponse_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": 20,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "generate_summary": null
              },
              "store": true,
              "temperature": 0.5,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 26,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 10,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 36
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
        });
        Assert.NotNull(response);

        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ResponseId);
        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ConversationId);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
        Assert.Single(response.Messages.Single().Contents);
        Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_741_891_428), response.CreatedAt);
        Assert.Null(response.FinishReason);

        Assert.NotNull(response.Usage);
        Assert.Equal(26, response.Usage.InputTokenCount);
        Assert.Equal(10, response.Usage.OutputTokenCount);
        Assert.Equal(36, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task BasicReasoningResponse_Streaming()
    {
        const string Input = """
            {
              "input":[{
                "type":"message",
                "role":"user",
                "content":[{
                  "type":"input_text",
                  "text":"Calculate the sum of the first 5 positive integers."
                }]
              }],
              "reasoning": {
                "summary": "detailed",
                "effort": "low"
              },
              "model": "o4-mini",
              "stream": true
            }
            """;

        // Compressed down for testing purposes; real-world output would be larger.
        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_68b5ebab461881969ed94149372c2a530698ecbf1b9f2704","object":"response","created_at":1756752811,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"o4-mini-2025-04-16","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"low","summary":"detailed"},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_68b5ebab461881969ed94149372c2a530698ecbf1b9f2704","object":"response","created_at":1756752811,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"o4-mini-2025-04-16","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"low","summary":"detailed"},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","type":"reasoning","summary":[]}}

            event: response.reasoning_summary_part.added
            data: {"type":"response.reasoning_summary_part.added","sequence_number":3,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"part":{"type":"summary_text","text":""}}

            event: response.reasoning_summary_text.delta
            data: {"type":"response.reasoning_summary_text.delta","sequence_number":4,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"delta":"**Calcul","obfuscation":"sLkbFySM"}

            event: response.reasoning_summary_text.delta
            data: {"type":"response.reasoning_summary_text.delta","sequence_number":5,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"delta":"ating","obfuscation":"dkm1f6DKqUj"}

            event: response.reasoning_summary_text.delta
            data: {"type":"response.reasoning_summary_text.delta","sequence_number":6,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"delta":" a","obfuscation":"X8ahc2lfCf9eA1"}

            event: response.reasoning_summary_text.delta
            data: {"type":"response.reasoning_summary_text.delta","sequence_number":7,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"delta":" simple","obfuscation":"1rLVyIaNl"}

            event: response.reasoning_summary_text.delta
            data: {"type":"response.reasoning_summary_text.delta","sequence_number":8,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"delta":" sum","obfuscation":"jCK7mgNR80Re"}

            event: response.reasoning_summary_text.done
            data: {"type":"response.reasoning_summary_text.done","sequence_number":9,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"text":"**Calculating a simple sum**"}

            event: response.reasoning_summary_part.done
            data: {"type":"response.reasoning_summary_part.done","sequence_number":10,"item_id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","output_index":0,"summary_index":0,"part":{"type":"summary_text","text":"**Calculating a simple sum**"}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":11,"output_index":0,"item":{"id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","type":"reasoning","summary":[{"type":"summary_text","text":"**Calculating a simple sum**"}]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":12,"output_index":1,"item":{"id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":13,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":14,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":"The","logprobs":[],"obfuscation":"japg2KaCkjNsp"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":15,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" sum","logprobs":[],"obfuscation":"1BEqjKQ0KU41"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":16,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" of","logprobs":[],"obfuscation":"GUqom1rsdZsnT"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":17,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" the","logprobs":[],"obfuscation":"UmCms91yrTlg"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":18,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" first","logprobs":[],"obfuscation":"AyNbZpfTXo"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":19,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" ","logprobs":[],"obfuscation":"tuyz4HkKODFQRtk"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":20,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":"5","logprobs":[],"obfuscation":"QAwyISolmjXfTlc"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":21,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" positive","logprobs":[],"obfuscation":"2Euge1H"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":22,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" integers","logprobs":[],"obfuscation":"ih0Znt8"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":23,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" is","logprobs":[],"obfuscation":"oQihR5Pw8jRz5"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":24,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":" 15","logprobs":[],"obfuscation":"7TdJ1FWlZF8lTd"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":25,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"delta":".","logprobs":[],"obfuscation":"x2VAJKlWI8qjgYq"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":26,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"text":"The sum of the first 5 positive integers is 15.","logprobs":[]}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":27,"item_id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of the first 5 positive integers is 15."}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":28,"output_index":1,"item":{"id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of the first 5 positive integers is 15."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":29,"response":{"id":"resp_68b5ebab461881969ed94149372c2a530698ecbf1b9f2704","object":"response","created_at":1756752811,"status":"completed","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"o4-mini-2025-04-16","output":[{"id":"rs_68b5ebabc0088196afb9fa86b487732d0698ecbf1b9f2704","type":"reasoning","summary":[{"type":"summary_text","text":"**Calculating a simple sum**"}]},{"id":"msg_68b5ebae5a708196b74b94f22ca8995e0698ecbf1b9f2704","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of the first 5 positive integers is 15."}],"role":"assistant"}],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"low","summary":"detailed"},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":{"input_tokens":17,"input_tokens_details":{"cached_tokens":0},"output_tokens":122,"output_tokens_details":{"reasoning_tokens":64},"total_tokens":139},"user":null,"metadata":{}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Calculate the sum of the first 5 positive integers.", new()
        {
            Reasoning = new()
            {
                Effort = ReasoningEffort.Low,
                Output = ReasoningOutput.Full,
            },
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("The sum of the first 5 positive integers is 15.", string.Concat(updates.Select(u => u.Text)));

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_756_752_811);
        Assert.Equal(30, updates.Count);

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_68b5ebab461881969ed94149372c2a530698ecbf1b9f2704", updates[i].ResponseId);
            Assert.Equal("resp_68b5ebab461881969ed94149372c2a530698ecbf1b9f2704", updates[i].ConversationId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("o4-mini-2025-04-16", updates[i].ModelId);
            Assert.Null(updates[i].AdditionalProperties);

            if (i is (>= 4 and <= 8))
            {
                // Reasoning updates
                Assert.Single(updates[i].Contents);
                Assert.Null(updates[i].Role);

                var reasoning = Assert.IsType<TextReasoningContent>(updates[i].Contents.Single());
                Assert.NotNull(reasoning);
                Assert.NotNull(reasoning.Text);
            }
            else if (i is (>= 14 and <= 25) or 29)
            {
                // Response Complete and Assistant message updates
                Assert.Single(updates[i].Contents);
            }
            else
            {
                // Other updates
                Assert.Empty(updates[i].Contents);
            }

            Assert.Equal(i < updates.Count - 1 ? null : ChatFinishReason.Stop, updates[i].FinishReason);
        }

        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(17, usage.Details.InputTokenCount);
        Assert.Equal(122, usage.Details.OutputTokenCount);
        Assert.Equal(139, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task ReasoningTextDelta_Streaming()
    {
        const string Input = """
            {
              "input":[{
                "type":"message",
                "role":"user",
                "content":[{
                  "type":"input_text",
                  "text":"Solve this problem step by step."
                }]
              }],
              "reasoning": {
                "effort": "medium"
              },
              "model": "o4-mini",
              "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[],"reasoning":{"effort":"medium"}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":""}}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":3,"item_id":"rs_reasoning123","output_index":0,"delta":"First, "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":4,"item_id":"rs_reasoning123","output_index":0,"delta":"let's analyze "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":5,"item_id":"rs_reasoning123","output_index":0,"delta":"the problem."}

            event: response.reasoning_text.done
            data: {"type":"response.reasoning_text.done","sequence_number":6,"item_id":"rs_reasoning123","output_index":0,"text":"First, let's analyze the problem."}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":7,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem."}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":8,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":9,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":10,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"delta":"The solution is 42."}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":11,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"text":"The solution is 42."}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":12,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":"The solution is 42."}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":13,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":14,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"completed","model":"o4-mini-2025-04-16","output":[{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem."},{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}],"usage":{"input_tokens":10,"output_tokens":25,"total_tokens":35}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Solve this problem step by step.", new()
        {
            RawRepresentationFactory = options => new CreateResponseOptions
            {
                ReasoningOptions = new()
                {
                    ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium
                }
            }
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("The solution is 42.", string.Concat(updates.Where(u => u.Role == ChatRole.Assistant).Select(u => u.Text)));

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_756_752_900);
        Assert.Equal(15, updates.Count);

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_reasoning123", updates[i].ResponseId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("o4-mini-2025-04-16", updates[i].ModelId);
        }

        // Verify reasoning text delta updates (sequence 3-5)
        var reasoningUpdates = updates.Where((u, idx) => idx >= 3 && idx <= 5).ToList();
        Assert.Equal(3, reasoningUpdates.Count);
        Assert.All(reasoningUpdates, u =>
        {
            Assert.Single(u.Contents);
            Assert.Null(u.Role);
            var reasoning = Assert.IsType<TextReasoningContent>(u.Contents.Single());
            Assert.NotNull(reasoning.Text);
        });

        // Verify the reasoning text content
        var allReasoningText = string.Concat(reasoningUpdates.Select(u => u.Contents.OfType<TextReasoningContent>().First().Text));
        Assert.Equal("First, let's analyze the problem.", allReasoningText);

        // Verify assistant response
        var assistantUpdate = updates.First(u => u.Role == ChatRole.Assistant && !string.IsNullOrEmpty(u.Text));
        Assert.Equal("The solution is 42.", assistantUpdate.Text);

        // Verify usage
        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(10, usage.Details.InputTokenCount);
        Assert.Equal(25, usage.Details.OutputTokenCount);
        Assert.Equal(35, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task ReasoningItemNullStatus_Streaming()
    {
        // Some OpenAI-compatible endpoints serialize the optional "status" property of a reasoning item
        // as null rather than omitting it. Regression test for the SSE stream being torn down with
        // ArgumentOutOfRangeException ("Unknown ReasoningStatus value") when deserializing such items.
        const string Input = """
            {
              "input":[{
                "type":"message",
                "role":"user",
                "content":[{
                  "type":"input_text",
                  "text":"Solve this problem step by step."
                }]
              }],
              "reasoning": {
                "effort": "medium"
              },
              "model": "o4-mini",
              "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[],"reasoning":{"effort":"medium"}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","status":null,"text":""}}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":3,"item_id":"rs_reasoning123","output_index":0,"delta":"First, ","status":null}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":4,"item_id":"rs_reasoning123","output_index":0,"delta":"let's analyze "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":5,"item_id":"rs_reasoning123","output_index":0,"delta":"the problem."}

            event: response.reasoning_text.done
            data: {"type":"response.reasoning_text.done","sequence_number":6,"item_id":"rs_reasoning123","output_index":0,"text":"First, let's analyze the problem."}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":7,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","status":null,"text":"First, let's analyze the problem."}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":8,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":null,"content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":9,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":10,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"delta":"The solution is 42."}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":11,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"text":"The solution is 42."}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":12,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":"The solution is 42."}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":13,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":14,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"completed","model":"o4-mini-2025-04-16","output":[{"id":"rs_reasoning123","type":"reasoning","status":null,"text":"First, let's analyze the problem."},{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}],"usage":{"input_tokens":10,"output_tokens":25,"total_tokens":35}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Solve this problem step by step.", new()
        {
            RawRepresentationFactory = options => new CreateResponseOptions
            {
                ReasoningOptions = new()
                {
                    ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium
                }
            }
        }))
        {
            updates.Add(update);
        }

        Assert.Equal(15, updates.Count);

        // Verify reasoning text delta updates (sequence 3-5) survived the null-status items
        var reasoningUpdates = updates.Where((u, idx) => idx >= 3 && idx <= 5).ToList();
        Assert.Equal(3, reasoningUpdates.Count);
        Assert.All(reasoningUpdates, u =>
        {
            Assert.Single(u.Contents);
            Assert.Null(u.Role);
            var reasoning = Assert.IsType<TextReasoningContent>(u.Contents.Single());
            Assert.NotNull(reasoning.Text);
        });
    }

    [Fact]
    public async Task ReasoningTextDelta_Streaming_PreservesEncryptedContent()
    {
        const string Input = """
            {
              "input":[{
                "type":"message",
                "role":"user",
                "content":[{
                  "type":"input_text",
                  "text":"Solve this problem step by step."
                }]
              }],
              "reasoning": {
                "effort": "medium"
              },
              "model": "o4-mini",
              "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[],"reasoning":{"effort":"medium"}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":""}}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":3,"item_id":"rs_reasoning123","output_index":0,"delta":"First, "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":4,"item_id":"rs_reasoning123","output_index":0,"delta":"let's analyze "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":5,"item_id":"rs_reasoning123","output_index":0,"delta":"the problem."}

            event: response.reasoning_text.done
            data: {"type":"response.reasoning_text.done","sequence_number":6,"item_id":"rs_reasoning123","output_index":0,"text":"First, let's analyze the problem."}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":7,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem.","encrypted_content":"secret-encrypted-data-abc123"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":8,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":9,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":10,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"delta":"The solution is 42."}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":11,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"text":"The solution is 42."}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":12,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"text":"The solution is 42."}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":13,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":14,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"completed","model":"o4-mini-2025-04-16","output":[{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem.","encrypted_content":"secret-encrypted-data-abc123"},{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}],"usage":{"input_tokens":10,"output_tokens":25,"total_tokens":35}}}

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Solve this problem step by step.", new()
        {
            RawRepresentationFactory = options => new CreateResponseOptions
            {
                ReasoningOptions = new()
                {
                    ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium
                }
            }
        }))
        {
            updates.Add(update);
        }

        var reasoningContents = updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().ToList();
        Assert.True(reasoningContents.Count >= 1, "Should have at least one reasoning content");
        Assert.Contains(reasoningContents, r => r.ProtectedData == "secret-encrypted-data-abc123");

        List<ChatMessage> chatHistory = [];
        chatHistory.AddMessages(updates);
        var assistantMessage = chatHistory.Single(m => m.Role == ChatRole.Assistant);
        var coalescedReasoning = assistantMessage.Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("First, let's analyze the problem.", coalescedReasoning.Text);
        Assert.Equal("secret-encrypted-data-abc123", coalescedReasoning.ProtectedData);
    }

    [Fact]
    public async Task EncryptedReasoning_Streaming_RoundTripsReasoningItemId()
    {
        // Streamed response containing a reasoning item (text deltas + encrypted_content + id),
        // followed by an assistant message. Mirrors a stateless (store=false) turn that included
        // reasoning.encrypted_content.
        const string FirstResponse = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"in_progress","model":"o4-mini-2025-04-16","output":[],"reasoning":{"effort":"medium"}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":""}}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":2,"item_id":"rs_reasoning123","output_index":0,"delta":"First, "}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","sequence_number":3,"item_id":"rs_reasoning123","output_index":0,"delta":"let's analyze the problem."}

            event: response.reasoning_text.done
            data: {"type":"response.reasoning_text.done","sequence_number":4,"item_id":"rs_reasoning123","output_index":0,"text":"First, let's analyze the problem."}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":5,"output_index":0,"item":{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem.","encrypted_content":"secret-encrypted-data-abc123"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":6,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":7,"item_id":"msg_reasoning123","output_index":1,"content_index":0,"delta":"The solution is 42."}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":8,"output_index":1,"item":{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":9,"response":{"id":"resp_reasoning123","object":"response","created_at":1756752900,"status":"completed","model":"o4-mini-2025-04-16","output":[{"id":"rs_reasoning123","type":"reasoning","text":"First, let's analyze the problem.","encrypted_content":"secret-encrypted-data-abc123"},{"id":"msg_reasoning123","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"The solution is 42."}],"role":"assistant"}],"usage":{"input_tokens":10,"output_tokens":25,"total_tokens":35}}}

            """;

        const string SecondResponse = """
            {"id":"resp_followup","object":"response","created_at":1756752901,"status":"completed","model":"o4-mini-2025-04-16","output":[{"id":"msg_followup","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"text":"Confirmed."}],"role":"assistant"}],"usage":{"input_tokens":5,"output_tokens":2,"total_tokens":7}}
            """;

        using CapturingHttpHandler handler = new([FirstResponse, SecondResponse]);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        // Turn 1: stream a response with encrypted reasoning and collect the history.
        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Solve this problem step by step.", new()
        {
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                ReasoningOptions = new() { ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium },
            },
        }))
        {
            updates.Add(update);
        }

        List<ChatMessage> chatHistory = [];
        chatHistory.AddMessages(updates);

        // Simulate persisting and rehydrating the session between turns (as a hosted, human-in-the-loop
        // approval flow does). This drops RawRepresentation (which is [JsonIgnore]), so the reasoning item's
        // id must survive via a serialized field (AdditionalProperties) to roundtrip.
        string serializedHistory = JsonSerializer.Serialize(chatHistory, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(List<ChatMessage>)));
        chatHistory = (List<ChatMessage>)JsonSerializer.Deserialize(serializedHistory, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(List<ChatMessage>)))!;
        chatHistory.Add(new ChatMessage(ChatRole.User, "Are you sure?"));

        // Turn 2: send the history back. The reasoning item must roundtrip with its id, otherwise
        // a stateless (store=false) service rejects the request (see dotnet/extensions issue and
        // microsoft/agent-framework#7067).
        await client.GetResponseAsync(chatHistory, new()
        {
            RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        });

        Assert.Equal(2, handler.RequestBodies.Count);

        using JsonDocument secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        JsonElement reasoningItem = secondRequest.RootElement.GetProperty("input").EnumerateArray()
            .Single(item => item.TryGetProperty("type", out var t) && t.GetString() == "reasoning");

        Assert.Equal("rs_reasoning123", reasoningItem.GetProperty("id").GetString());
        Assert.Equal("secret-encrypted-data-abc123", reasoningItem.GetProperty("encrypted_content").GetString());
    }

    [Fact]
    public async Task BasicRequestResponse_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" How"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" can"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" I"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" assist"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" you"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":" today"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"?"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello! How can I assist you today?"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello! How can I assist you today?","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello! How can I assist you today?","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello! How can I assist you today?","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

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

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_741_892_091);
        Assert.Equal(17, updates.Count);

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ConversationId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("gpt-4o-mini-2024-07-18", updates[i].ModelId);
            Assert.Null(updates[i].AdditionalProperties);
            Assert.Equal((i >= 4 && i <= 12) || i == 16 ? 1 : 0, updates[i].Contents.Count);
            Assert.Equal(i < updates.Count - 1 ? null : ChatFinishReason.Stop, updates[i].FinishReason);
        }

        for (int i = 4; i < updates.Count; i++)
        {
            Assert.Equal(ChatRole.Assistant, updates[i].Role);
        }

        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(26, usage.Details.InputTokenCount);
        Assert.Equal(10, usage.Details.OutputTokenCount);
        Assert.Equal(36, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task MissingAbstractionResponse_NonStreaming()
    {
        const string Input =
            """
            {
                "model": "computer-use-preview",
                "reasoning":{"summary":"concise"},
                "tools": [
                    {
                        "type": "computer_use_preview",
                        "environment": "browser",
                        "display_width": 1024,
                        "display_height": 768
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Search the web for the temperature today in Fremont"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output =
            """
            {
              "id": "resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": 20,
              "model": "computer-use-preview-2025-03-11",
              "output": [
                {
                  "type": "reasoning",
                  "id": "rs_67cc...",
                  "summary": [
                    {
                        "type": "summary_text",
                        "text": "Clicking on the browser address bar."
                    }
                  ]
                },
                {
                  "type": "computer_call",
                  "id": "cu_67cc...",
                  "call_id": "call_zw3...",
                  "action": {
                    "type": "click",
                    "button": "left",
                    "x": 156,
                    "y": 50
                  },
                  "pending_safety_checks": [],
                  "status": "completed"
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "generate_summary": "concise"
              },
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 18,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 53,
                "output_tokens_details": {
                  "reasoning_tokens": 12
                },
                "total_tokens": 71
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "computer-use-preview");

        ChatOptions chatOptions = new()
        {
#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
            Tools = [ResponseTool.CreateComputerTool(ComputerToolEnvironment.Browser, 1024, 768).AsAITool()],
#pragma warning restore OPENAICUA001

            RawRepresentationFactory = options => new CreateResponseOptions
            {
                ReasoningOptions = new() { ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Concise },
            }
        };
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Search the web for the temperature today in Fremont")], chatOptions);
        Assert.NotNull(response);

        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ResponseId);
        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ConversationId);
        Assert.Empty(response.Text);
        Assert.Equal("computer-use-preview-2025-03-11", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_741_891_428), response.CreatedAt);
        Assert.Null(response.FinishReason);
        ChatMessage responseMessage = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, responseMessage.Role);
        Assert.Equal(2, responseMessage.Contents.Count);
        Assert.Single(responseMessage.Contents, content => content is TextReasoningContent);
        AIContent computerUserItem = Assert.Single(responseMessage.Contents, content => content is ToolCallContent);
        Assert.NotNull(computerUserItem.RawRepresentation);
#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
        Assert.IsType<ComputerCallResponseItem>(computerUserItem.RawRepresentation);
#pragma warning restore OPENAICUA001
        ToolCallContent computerToolCall = Assert.IsType<ToolCallContent>(computerUserItem);
        Assert.Equal("call_zw3...", computerToolCall.CallId);
        Assert.NotNull(response.Usage);
        Assert.Equal(18, response.Usage.InputTokenCount);
        Assert.Equal(53, response.Usage.OutputTokenCount);
        Assert.Equal(71, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task MissingAbstractionResponse_Streaming()
    {
        const string Input =
            """
            {
                "model": "computer-use-preview",
                "reasoning": {
                    "summary": "concise"
                },
                "tools": [
                    {
                        "type": "computer_use_preview",
                        "environment": "browser",
                        "display_width": 1024,
                        "display_height": 768
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Search the web for the temperature today in Fremont"
                            }
                        ]
                    }
                ],
                "stream":true
            }
            """;

        const string Output =
            """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_0b949080ec8e4b8a006920e0661d00819383ed81438ab11299","object":"response","created_at":1763762278,"status":"in_progress","background":false,"content_filters":null,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"computer-use-preview-2025-03-11","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"medium","generate_summary":"concise","summary":"concise"},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":null,"tools":[{"type":"computer-use-preview-2025-03-11","environment":"browser","display_width":1024,"display_height":768}],"top_logprobs":0,"top_p":1.0,"truncation":"auto","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_0b949080ec8e4b8a006920e0661d00819383ed81438ab11299","object":"response","created_at":1763762278,"status":"in_progress","background":false,"content_filters":null,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"computer-use-preview-2025-03-11","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"medium","generate_summary":"concise","summary":"concise"},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":null,"tools":[{"type":"computer_use_preview","environment":"browser","display_width":1024,"display_height":768}],"top_logprobs":0,"top_p":1.0,"truncation":"auto","usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"type":"computer_call","id":"cu_0b949080ec8e4b8a006920e067a7c0819384d047d21b484357","status":"in_progress","call_id":"call_p7K8YjFwNjqMgkKhSiExgFH6","action":{"type":"screenshot"},"pending_safety_checks":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":3,"output_index":0,"item":{"type":"computer_call","id":"cu_0b949080ec8e4b8a006920e067a7c0819384d047d21b484357","status":"completed","call_id":"call_p7K8YjFwNjqMgkKhSiExgFH6","action":{"type":"screenshot"},"pending_safety_checks":[]}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":4,"response":{"id":"resp_0b949080ec8e4b8a006920e0661d00819383ed81438ab11299","object":"response","created_at":1763762278,"status":"completed","background":false,"content_filters":null,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"computer-use-preview-2025-03-11","output":[{"type":"computer_call","id":"cu_0b949080ec8e4b8a006920e067a7c0819384d047d21b484357","status":"completed","call_id":"call_p7K8YjFwNjqMgkKhSiExgFH6","action":{"type":"screenshot"},"pending_safety_checks":[]}],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":"medium","generate_summary":"concise","summary":"concise"},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":null,"tools":[{"type":"computer-use-preview-2025-03-11","environment":"browser","display_width":1024,"display_height":768}],"top_logprobs":0,"top_p":1.0,"truncation":"auto","usage":{"input_tokens":18,"input_tokens_details":{"cached_tokens":0},"output_tokens":53,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":71},"user":null,"metadata":{}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "computer-use-preview");

#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
        ChatOptions chatOptions = new()
        {
            Tools = [ResponseTool.CreateComputerTool(ComputerToolEnvironment.Browser, 1024, 768).AsAITool()],
            RawRepresentationFactory = options => new CreateResponseOptions
            {
                ReasoningOptions = new() { ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Concise },
            }
        };
#pragma warning restore OPENAICUA001

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Search the web for the temperature today in Fremont")], chatOptions))
        {
            updates.Add(update);
        }

        Assert.Equal(5, updates.Count);
        Assert.All(updates, u => Assert.Empty(u.Text));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_0b949080ec8e4b8a006920e0661d00819383ed81438ab11299", updates[i].ResponseId);
            Assert.Equal("resp_0b949080ec8e4b8a006920e0661d00819383ed81438ab11299", updates[i].ConversationId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_763_762_278), updates[i].CreatedAt);
            Assert.Equal("computer-use-preview-2025-03-11", updates[i].ModelId);
            Assert.Null(updates[i].AdditionalProperties);
            Assert.Equal(i >= 3 ? 1 : 0, updates[i].Contents.Count);
            Assert.Equal(i >= 4 ? ChatFinishReason.Stop : null, updates[i].FinishReason);
            Assert.Null(updates[i].Role);
        }

        ToolCallContent content = Assert.IsType<ToolCallContent>(Assert.Single(updates[3].Contents));
        Assert.Equal("call_p7K8YjFwNjqMgkKhSiExgFH6", content.CallId);
#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
        var ccri = Assert.IsType<ComputerCallResponseItem>(content.RawRepresentation);
#pragma warning restore OPENAICUA001

        UsageContent usage = Assert.IsType<UsageContent>(Assert.Single(updates[4].Contents));
        Assert.Equal(18, usage.Details.InputTokenCount);
        Assert.Equal(53, usage.Details.OutputTokenCount);
        Assert.Equal(71, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task ChatOptions_StrictRespected()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "function",
                        "name": "GetPersonAge",
                        "description": "Gets the age of the specified person.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d",
              "object": "response",
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

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
                "temperature": 0.5,
                "top_p": 0.5,
                "previous_response_id": "resp_42",
                "model": "gpt-4o-mini",
                "max_output_tokens": 10,
                "text": {
                    "format": {
                        "type": "text"
                    }
                },
                "tools": [
                    {
                        "type": "function",
                        "name": "GetPersonAge",
                        "description": "Gets the age of the specified person.",
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
                        },
                        "strict": null
                    },
                    {
                        "type": "function",
                        "name": "GetPersonAge",
                        "description": "Gets the age of the specified person.",
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
                        },
                        "strict": null
                    }
                ],
                "tool_choice": "auto",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "parallel_tool_calls": true
            }
            """;

        const string Output = """
            {
              "id": "resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": 20,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "generate_summary": null
              },
              "store": true,
              "temperature": 0.5,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 26,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 10,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 36
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, modelId: "gpt-4o-mini");
        AIFunction tool = AIFunctionFactory.Create(([Description("The person whose age is being requested")] string personName) => 42, "GetPersonAge", "Gets the age of the specified person.");

        ChatOptions chatOptions = new()
        {
            RawRepresentationFactory = (c) =>
            {
                CreateResponseOptions openAIOptions = new()
                {
                    MaxOutputTokenCount = 10,
                    PreviousResponseId = "resp_42",
                    TopP = 0.5f,
                    Temperature = 0.5f,
                    ParallelToolCallsEnabled = true,
                    ToolChoice = ResponseToolChoice.CreateAutoChoice(),
                    TextOptions = new ResponseTextOptions
                    {
                        TextFormat = ResponseTextFormat.CreateTextFormat()
                    },
                };
                openAIOptions.Tools.Add(tool.AsOpenAIResponseTool());
                return openAIOptions;
            },
            ModelId = null,
            MaxOutputTokens = 1,
            ConversationId = "foo",
            TopP = 0.125f,
            Temperature = 0.125f,
            AllowMultipleToolCalls = false,
            Tools = [tool],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await client.GetResponseAsync("hello", chatOptions);
        Assert.NotNull(response);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
    }

    [Fact]
    public async Task MultipleOutputItems_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": 20,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello!",
                      "annotations": []
                    }
                  ]
                },
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a182e",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": " How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "generate_summary": null
              },
              "store": true,
              "temperature": 0.5,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 26,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 10,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 36
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
        });
        Assert.NotNull(response);

        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ResponseId);
        Assert.Equal("resp_67d327649b288191aeb46a824e49dc40058a5e08c46a181d", response.ConversationId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_741_891_428), response.CreatedAt);
        Assert.Null(response.FinishReason);

        Assert.Equal(2, response.Messages.Count);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.Equal("Hello!", response.Messages[0].Text);
        Assert.Equal(ChatRole.Assistant, response.Messages[1].Role);
        Assert.Equal(" How can I assist you today?", response.Messages[1].Text);

        Assert.NotNull(response.Usage);
        Assert.Equal(26, response.Usage.InputTokenCount);
        Assert.Equal(10, response.Usage.OutputTokenCount);
        Assert.Equal(36, response.Usage.TotalTokenCount);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("tool")]
    public async Task McpToolCall_ApprovalRequired_NonStreaming(string role)
    {
        string input = """
            {
              "model": "gpt-4o-mini",
              "tools": [
                {
                  "type": "mcp",
                  "server_label": "deepwiki",
                  "server_url": "https://mcp.deepwiki.com/mcp"
                }
              ],
              "input": [
                {
                  "type": "message",
                  "role": "user",
                  "content": [
                    {
                      "type": "input_text",
                      "text": "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository"
                    }
                  ]
                }
              ]
            }
            """;

        string output = """
            {
              "id": "resp_04e29d5bdd80bd9f0068e6b01f786081a29148febb92892aee",
              "object": "response",
              "created_at": 1759948831,
              "status": "completed",
              "background": false,
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "max_tool_calls": null,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "id": "mcpr_04e29d5bdd80bd9f0068e6b022a9c081a2ae898104b7a75051",
                  "type": "mcp_approval_request",
                  "arguments": "{\"repoName\":\"dotnet/extensions\"}",
                  "name": "ask_question",
                  "server_label": "deepwiki"
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "prompt_cache_key": null,
              "reasoning": {
                "effort": null,
                "summary": null
              },
              "safety_identifier": null,
              "service_tier": "default",
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                },
                "verbosity": "medium"
              },
              "tools": [
                {
                  "type": "mcp",
                  "allowed_tools": null,
                  "headers": null,
                  "require_approval": "always",
                  "server_description": null,
                  "server_label": "deepwiki",
                  "server_url": "https://mcp.deepwiki.com/<redacted>"
                }
              ],
              "top_logprobs": 0,
              "top_p": 1.0,
              "truncation": "disabled",
              "usage": {
                "input_tokens": 193,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 23,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 216
              },
              "user": null,
              "metadata": {}
            }
            """;

        var chatOptions = new ChatOptions
        {
            Tools = [new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))]
        };
        ToolApprovalRequestContent approvalRequest;

        using (VerbatimHttpHandler handler = new(input, output))
        using (HttpClient httpClient = new(handler))
        using (IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini"))
        {
            var response = await client.GetResponseAsync(
                "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository",
                chatOptions);

            approvalRequest = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
            chatOptions.ConversationId = response.ConversationId;
        }

        input = $$"""
            {
                "previous_response_id": "resp_04e29d5bdd80bd9f0068e6b01f786081a29148febb92892aee",
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "deepwiki",
                        "server_url": "https://mcp.deepwiki.com/mcp"
                    }
                ],
                "input": [
                    {
                        "type": "mcp_approval_response",
                        "approval_request_id": "mcpr_04e29d5bdd80bd9f0068e6b022a9c081a2ae898104b7a75051",
                        "approve": true
                    }
                ]
            }
            """;

        output = """
            {
              "id": "resp_06ee3b1962eeb8470068e6b21c377081a3a20dbf60eee7a736",
              "object": "response",
              "created_at": 1759949340,
              "status": "completed",
              "background": false,
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "max_tool_calls": null,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "id": "mcp_06ee3b1962eeb8470068e6b21cbaa081a3b5aa2a6c989f4c6f",
                  "type": "mcp_call",
                  "status": "completed",
                  "approval_request_id": "mcpr_06ee3b1962eeb8470068e6b192985c81a383a16059ecd8230e",
                  "arguments": "{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}",
                  "error": null,
                  "name": "ask_question",
                  "output": "The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` within the `dotnet/extensions` repository.  This file provides an overview of the package, including installation instructions and usage examples for its core interfaces like `IChatClient` and `IEmbeddingGenerator`. \n\n## Path to README.md\n\nThe specific path to the `README.md` file for the `Microsoft.Extensions.AI.Abstractions` project is `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md`.  This path is also referenced in the `AI Extensions Framework` wiki page as a relevant source file. \n\n## Notes\n\nThe `Packaging.targets` file in the `eng/MSBuild` directory indicates that `README.md` files are included in packages when `IsPackable` and `IsShipping` properties are true.  This suggests that the `README.md` file located at `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` is intended to be part of the distributed NuGet package for `Microsoft.Extensions.AI.Abstractions`. \n\nWiki pages you might want to explore:\n- [AI Extensions Framework (dotnet/extensions)](/wiki/dotnet/extensions#3)\n- [Chat Completion (dotnet/extensions)](/wiki/dotnet/extensions#3.3)\n\nView this search on DeepWiki: https://deepwiki.com/search/what-is-the-path-to-the-readme_315595bd-9b39-4f04-9fa3-42dc778fa9f3\n",
                  "server_label": "deepwiki"
                },
                {
                  "id": "msg_06ee3b1962eeb8470068e6b226ab0081a39fccce9aa47aedbc",
                  "type": "message",
                  "status": "completed",
                  "content": [
                    {
                      "type": "output_text",
                      "annotations": [],
                      "logprobs": [],
                      "text": "The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the `Microsoft.Extensions.AI.Abstractions` package, including installation instructions and usage examples for its core interfaces like `IChatClient` and `IEmbeddingGenerator`."
                    }
                  ],
                  "role": "assistant"
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": "resp_06ee3b1962eeb8470068e6b18e0db881a3bdfd255a60327cdc",
              "prompt_cache_key": null,
              "reasoning": {
                "effort": null,
                "summary": null
              },
              "safety_identifier": null,
              "service_tier": "default",
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                },
                "verbosity": "medium"
              },
              "tool_choice": "auto",
              "tools": [
                {
                  "type": "mcp",
                  "allowed_tools": null,
                  "headers": null,
                  "require_approval": "always",
                  "server_description": null,
                  "server_label": "deepwiki",
                  "server_url": "https://mcp.deepwiki.com/<redacted>"
                }
              ],
              "top_logprobs": 0,
              "top_p": 1.0,
              "truncation": "disabled",
              "usage": {
                "input_tokens": 542,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 72,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 614
              },
              "user": null,
              "metadata": {}
            }
            """;

        using (VerbatimHttpHandler handler = new(input, output))
        using (HttpClient httpClient = new(handler))
        using (IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini"))
        {
            var response = await client.GetResponseAsync(
                new ChatMessage(new ChatRole(role), [approvalRequest.CreateResponse(true)]), chatOptions);

            Assert.NotNull(response);

            Assert.Equal("resp_06ee3b1962eeb8470068e6b21c377081a3a20dbf60eee7a736", response.ResponseId);
            Assert.Equal("resp_06ee3b1962eeb8470068e6b21c377081a3a20dbf60eee7a736", response.ConversationId);
            Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_759_949_340), response.CreatedAt);
            Assert.Null(response.FinishReason);

            var message = Assert.Single(response.Messages);
            Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
            Assert.Equal("The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the `Microsoft.Extensions.AI.Abstractions` package, including installation instructions and usage examples for its core interfaces like `IChatClient` and `IEmbeddingGenerator`.", response.Messages[0].Text);

            Assert.Equal(3, message.Contents.Count);

            var call = Assert.IsType<McpServerToolCallContent>(message.Contents[0]);
            Assert.Equal("mcp_06ee3b1962eeb8470068e6b21cbaa081a3b5aa2a6c989f4c6f", call.CallId);
            Assert.Equal("deepwiki", call.ServerName);
            Assert.Equal("ask_question", call.Name);
            Assert.NotNull(call.Arguments);
            Assert.Equal(2, call.Arguments.Count);
            Assert.Equal("dotnet/extensions", ((JsonElement)call.Arguments["repoName"]!).GetString());
            Assert.Equal("What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?", ((JsonElement)call.Arguments["question"]!).GetString());

            var result = Assert.IsType<McpServerToolResultContent>(message.Contents[1]);
            Assert.Equal("mcp_06ee3b1962eeb8470068e6b21cbaa081a3b5aa2a6c989f4c6f", result.CallId);
            Assert.StartsWith("The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at", Assert.IsType<TextContent>(Assert.Single(result.Outputs!)).Text);

            Assert.NotNull(response.Usage);
            Assert.Equal(542, response.Usage.InputTokenCount);
            Assert.Equal(72, response.Usage.OutputTokenCount);
            Assert.Equal(614, response.Usage.TotalTokenCount);
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("tool")]
    public async Task ToolApprovalResponse_NonMcpToolCall_SilentlyIgnored_NonStreaming(string role)
    {
        // When a ToolApprovalResponseContent wraps a non-MCP tool call (e.g. CodeInterpreterToolCallContent
        // or ImageGenerationToolCallContent), the client should silently drop it from the request payload.
        // For the user role, the text content is preserved; for the tool role, the function result is preserved.
        string input = role == "user"
            ? """
            {
                "model": "gpt-4o-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ]
            }
            """
            : """
            {
                "model": "gpt-4o-mini",
                "input": [
                    {
                        "type": "function_call_output",
                        "call_id": "call-1",
                        "output": "tool result"
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_fallthrough_test_001",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "max_tool_calls": null,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_fallthrough_test_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I help?",
                      "annotations": []
                    }
                  ]
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "summary": null
              },
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 10,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 8,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 18
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        // Build a message with role-appropriate content + non-MCP approval responses that should be silently dropped.
        var codeInterpreterApproval = new ToolApprovalResponseContent("req-ci-1", true, new CodeInterpreterToolCallContent("ci-call-1"));
        var imageGenApproval = new ToolApprovalResponseContent("req-ig-1", false, new ImageGenerationToolCallContent("ig-call-1"));

        AIContent anchorContent = role == "user"
            ? new TextContent("hello")
            : new FunctionResultContent("call-1", "tool result");

        var messages = new List<ChatMessage>
        {
            new(new ChatRole(role), [anchorContent, codeInterpreterApproval, imageGenApproval]),
        };

        var response = await client.GetResponseAsync(messages);

        Assert.NotNull(response);
        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("Hello! How can I help?", message.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpToolCall_ApprovalNotRequired_NonStreaming(bool rawTool)
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "deepwiki",
                        "server_url": "https://mcp.deepwiki.com/mcp",
                        "require_approval": "never"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
                "id": "resp_68be416397ec81918c48ef286530b8140384f747588fc3f5",
                "object": "response",
                "created_at": 1757299043,
                "status": "completed",
                "background": false,
                "error": null,
                "incomplete_details": null,
                "instructions": null,
                "max_output_tokens": null,
                "max_tool_calls": null,
                "model": "gpt-4o-mini-2024-07-18",
                "output": [
                    {
                        "id": "mcpl_68be4163aa80819185e792abdcde71670384f747588fc3f5",
                        "type": "mcp_list_tools",
                        "server_label": "deepwiki",
                        "tools": [
                            {
                                "annotations": {
                                    "read_only": false
                                },
                                "description": "Get a list of documentation topics for a GitHub repository",
                                "input_schema": {
                                    "type": "object",
                                    "properties": {
                                        "repoName": {
                                            "type": "string",
                                            "description": "GitHub repository: owner/repo (e.g. \"facebook/react\")"
                                        }
                                    },
                                    "required": [
                                        "repoName"
                                    ],
                                    "additionalProperties": false,
                                    "$schema": "http://json-schema.org/draft-07/schema#"
                                },
                                "name": "read_wiki_structure"
                            },
                            {
                                "annotations": {
                                    "read_only": false
                                },
                                "description": "View documentation about a GitHub repository",
                                "input_schema": {
                                    "type": "object",
                                    "properties": {
                                        "repoName": {
                                            "type": "string",
                                            "description": "GitHub repository: owner/repo (e.g. \"facebook/react\")"
                                        }
                                    },
                                    "required": [
                                        "repoName"
                                    ],
                                    "additionalProperties": false,
                                    "$schema": "http://json-schema.org/draft-07/schema#"
                                },
                                "name": "read_wiki_contents"
                            },
                            {
                                "annotations": {
                                    "read_only": false
                                },
                                "description": "Ask any question about a GitHub repository",
                                "input_schema": {
                                    "type": "object",
                                    "properties": {
                                        "repoName": {
                                            "type": "string",
                                            "description": "GitHub repository: owner/repo (e.g. \"facebook/react\")"
                                        },
                                        "question": {
                                            "type": "string",
                                            "description": "The question to ask about the repository"
                                        }
                                    },
                                    "required": [
                                        "repoName",
                                        "question"
                                    ],
                                    "additionalProperties": false,
                                    "$schema": "http://json-schema.org/draft-07/schema#"
                                },
                                "name": "ask_question"
                            }
                        ]
                    },
                    {
                        "id": "mcp_68be4166acfc8191bc5e0a751eed358b0384f747588fc3f5",
                        "type": "mcp_call",
                        "approval_request_id": null,
                        "arguments": "{\"repoName\":\"dotnet/extensions\"}",
                        "error": null,
                        "name": "read_wiki_structure",
                        "output": "Available pages for dotnet/extensions:\n\n- 1 Overview\n- 2 Build System and CI/CD\n- 3 AI Extensions Framework\n  - 3.1 Core Abstractions\n  - 3.2 AI Function System\n  - 3.3 Chat Completion\n  - 3.4 Caching System\n  - 3.5 Evaluation and Reporting\n- 4 HTTP Resilience and Diagnostics\n  - 4.1 Standard Resilience\n  - 4.2 Hedging Strategies\n- 5 Telemetry and Compliance\n- 6 Testing Infrastructure\n  - 6.1 AI Service Integration Testing\n  - 6.2 Time Provider Testing",
                        "server_label": "deepwiki"
                    },
                    {
                        "id": "mcp_68be416900f88191837ae0718339a4ce0384f747588fc3f5",
                        "type": "mcp_call",
                        "approval_request_id": null,
                        "arguments": "{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}",
                        "error": null,
                        "name": "ask_question",
                        "output": "The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` within the `dotnet/extensions` repository.  This file provides an overview of the `Microsoft.Extensions.AI.Abstractions` package, including installation instructions and usage examples for its core interfaces like `IChatClient` and `IEmbeddingGenerator`. \n\n## Path to README.md\n\nThe specific path to the `README.md` file for the `Microsoft.Extensions.AI.Abstractions` project is `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md`.  This path is also referenced in the `AI Extensions Framework` wiki page as a relevant source file. \n\n## Notes\n\nThe `Packaging.targets` file in the `eng/MSBuild` directory indicates that `README.md` files are included in packages when `IsPackable` and `IsShipping` properties are true.  This suggests that the `README.md` file located at `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` is intended to be part of the distributed NuGet package for `Microsoft.Extensions.AI.Abstractions`. \n\nWiki pages you might want to explore:\n- [AI Extensions Framework (dotnet/extensions)](/wiki/dotnet/extensions#3)\n- [Chat Completion (dotnet/extensions)](/wiki/dotnet/extensions#3.3)\n\nView this search on DeepWiki: https://deepwiki.com/search/what-is-the-path-to-the-readme_315595bd-9b39-4f04-9fa3-42dc778fa9f3\n",
                        "server_label": "deepwiki"
                    },
                    {
                        "id": "msg_68be416fb43c819194a1d4ace2643a7e0384f747588fc3f5",
                        "type": "message",
                        "status": "completed",
                        "content": [
                            {
                                "type": "output_text",
                                "annotations": [],
                                "logprobs": [],
                                "text": "The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file includes an overview, installation instructions, and usage examples related to the package."
                            }
                        ],
                        "role": "assistant"
                    }
                ],
                "parallel_tool_calls": true,
                "previous_response_id": null,
                "prompt_cache_key": null,
                "reasoning": {
                    "effort": null,
                    "summary": null
                },
                "safety_identifier": null,
                "service_tier": "default",
                "store": true,
                "temperature": 1,
                "text": {
                    "format": {
                        "type": "text"
                    },
                    "verbosity": "medium"
                },
                "tool_choice": "auto",
                "tools": [
                    {
                        "type": "mcp",
                        "allowed_tools": null,
                        "headers": null,
                        "require_approval": "never",
                        "server_description": null,
                        "server_label": "deepwiki",
                        "server_url": "https://mcp.deepwiki.com/<redacted>"
                    }
                ],
                "top_logprobs": 0,
                "top_p": 1,
                "truncation": "disabled",
                "usage": {
                    "input_tokens": 1329,
                    "input_tokens_details": {
                        "cached_tokens": 0
                    },
                    "output_tokens": 123,
                    "output_tokens_details": {
                        "reasoning_tokens": 0
                    },
                    "total_tokens": 1452
                },
                "user": null,
                "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        AITool mcpTool = rawTool ?
            ResponseTool.CreateMcpTool("deepwiki", serverUri: new("https://mcp.deepwiki.com/mcp"), toolCallApprovalPolicy: new McpToolCallApprovalPolicy(DefaultMcpToolCallApprovalPolicy.NeverRequireApproval)).AsAITool() :
            new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
            {
                ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
            };

        ChatOptions chatOptions = new()
        {
            Tools = [mcpTool],
        };

        var response = await client.GetResponseAsync("Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository", chatOptions);
        Assert.NotNull(response);

        Assert.Equal("resp_68be416397ec81918c48ef286530b8140384f747588fc3f5", response.ResponseId);
        Assert.Equal("resp_68be416397ec81918c48ef286530b8140384f747588fc3f5", response.ConversationId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_757_299_043), response.CreatedAt);
        Assert.Null(response.FinishReason);

        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.Equal("The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file includes an overview, installation instructions, and usage examples related to the package.", response.Messages[0].Text);

        Assert.Equal(6, message.Contents.Count);

        var firstCall = Assert.IsType<McpServerToolCallContent>(message.Contents[1]);
        Assert.Equal("mcp_68be4166acfc8191bc5e0a751eed358b0384f747588fc3f5", firstCall.CallId);
        Assert.Equal("deepwiki", firstCall.ServerName);
        Assert.Equal("read_wiki_structure", firstCall.Name);
        Assert.NotNull(firstCall.Arguments);
        Assert.Single(firstCall.Arguments);
        Assert.Equal("dotnet/extensions", ((JsonElement)firstCall.Arguments["repoName"]!).GetString());

        var firstResult = Assert.IsType<McpServerToolResultContent>(message.Contents[2]);
        Assert.Equal("mcp_68be4166acfc8191bc5e0a751eed358b0384f747588fc3f5", firstResult.CallId);
        Assert.StartsWith("Available pages for dotnet/extensions", Assert.IsType<TextContent>(Assert.Single(firstResult.Outputs!)).Text);

        var secondCall = Assert.IsType<McpServerToolCallContent>(message.Contents[3]);
        Assert.Equal("mcp_68be416900f88191837ae0718339a4ce0384f747588fc3f5", secondCall.CallId);
        Assert.Equal("deepwiki", secondCall.ServerName);
        Assert.Equal("ask_question", secondCall.Name);
        Assert.NotNull(secondCall.Arguments);
        Assert.Equal("dotnet/extensions", ((JsonElement)secondCall.Arguments["repoName"]!).GetString());
        Assert.Equal("What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?", ((JsonElement)secondCall.Arguments["question"]!).GetString());

        var secondResult = Assert.IsType<McpServerToolResultContent>(message.Contents[4]);
        Assert.Equal("mcp_68be416900f88191837ae0718339a4ce0384f747588fc3f5", secondResult.CallId);
        Assert.StartsWith("The `README.md` file for `Microsoft.Extensions.AI.Abstractions` is located at", Assert.IsType<TextContent>(Assert.Single(secondResult.Outputs!)).Text);

        Assert.NotNull(response.Usage);
        Assert.Equal(1329, response.Usage.InputTokenCount);
        Assert.Equal(123, response.Usage.OutputTokenCount);
        Assert.Equal(1452, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task McpToolCall_ApprovalNotRequired_Streaming()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "deepwiki",
                        "server_url": "https://mcp.deepwiki.com/mcp",
                        "require_approval": "never"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository"
                            }
                        ]
                    }
                ],
                "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_68be44fd7298819e82fd82c8516e970d03a2537be0e84a54","object":"response","created_at":1757299965,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"mcp","allowed_tools":null,"headers":null,"require_approval":"never","server_description":null,"server_label":"deepwiki","server_url":"https://mcp.deepwiki.com/<redacted>"}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_68be44fd7298819e82fd82c8516e970d03a2537be0e84a54","object":"response","created_at":1757299965,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"mcp","allowed_tools":null,"headers":null,"require_approval":"never","server_description":null,"server_label":"deepwiki","server_url":"https://mcp.deepwiki.com/<redacted>"}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"mcpl_68be44fd8f68819eba7a74a2f6d27a5a03a2537be0e84a54","type":"mcp_list_tools","server_label":"deepwiki","tools":[]}}

            event: response.mcp_list_tools.in_progress
            data: {"type":"response.mcp_list_tools.in_progress","sequence_number":3,"output_index":0,"item_id":"mcpl_68be44fd8f68819eba7a74a2f6d27a5a03a2537be0e84a54"}

            event: response.mcp_list_tools.completed
            data: {"type":"response.mcp_list_tools.completed","sequence_number":4,"output_index":0,"item_id":"mcpl_68be44fd8f68819eba7a74a2f6d27a5a03a2537be0e84a54"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":5,"output_index":0,"item":{"id":"mcpl_68be44fd8f68819eba7a74a2f6d27a5a03a2537be0e84a54","type":"mcp_list_tools","server_label":"deepwiki","tools":[{"annotations":{"read_only":false},"description":"Get a list of documentation topics for a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"}},"required":["repoName"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"read_wiki_structure"},{"annotations":{"read_only":false},"description":"View documentation about a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"}},"required":["repoName"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"read_wiki_contents"},{"annotations":{"read_only":false},"description":"Ask any question about a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"},"question":{"type":"string","description":"The question to ask about the repository"}},"required":["repoName","question"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"ask_question"}]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":6,"output_index":1,"item":{"id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"","error":null,"name":"read_wiki_structure","output":null,"server_label":"deepwiki"}}

            event: response.mcp_call.in_progress
            data: {"type":"response.mcp_call.in_progress","sequence_number":7,"output_index":1,"item_id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54"}

            event: response.mcp_call_arguments.delta
            data: {"type":"response.mcp_call_arguments.delta","sequence_number":8,"output_index":1,"item_id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54","delta":"{\"repoName\":\"dotnet/extensions\"}","obfuscation":""}

            event: response.mcp_call_arguments.done
            data: {"type":"response.mcp_call_arguments.done","sequence_number":9,"output_index":1,"item_id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54","arguments":"{\"repoName\":\"dotnet/extensions\"}"}

            event: response.mcp_call.completed
            data: {"type":"response.mcp_call.completed","sequence_number":10,"output_index":1,"item_id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":11,"output_index":1,"item":{"id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"{\"repoName\":\"dotnet/extensions\"}","error":null,"name":"read_wiki_structure","output":"Available pages for dotnet/extensions:\n\n- 1 Overview\n- 2 Build System and CI/CD\n- 3 AI Extensions Framework\n  - 3.1 Core Abstractions\n  - 3.2 AI Function System\n  - 3.3 Chat Completion\n  - 3.4 Caching System\n  - 3.5 Evaluation and Reporting\n- 4 HTTP Resilience and Diagnostics\n  - 4.1 Standard Resilience\n  - 4.2 Hedging Strategies\n- 5 Telemetry and Compliance\n- 6 Testing Infrastructure\n  - 6.1 AI Service Integration Testing\n  - 6.2 Time Provider Testing","server_label":"deepwiki"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":12,"output_index":2,"item":{"id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"","error":null,"name":"ask_question","output":null,"server_label":"deepwiki"}}

            event: response.mcp_call.in_progress
            data: {"type":"response.mcp_call.in_progress","sequence_number":13,"output_index":2,"item_id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54"}

            event: response.mcp_call_arguments.delta
            data: {"type":"response.mcp_call_arguments.delta","sequence_number":14,"output_index":2,"item_id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54","delta":"{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}","obfuscation":"IT"}

            event: response.mcp_call_arguments.done
            data: {"type":"response.mcp_call_arguments.done","sequence_number":15,"output_index":2,"item_id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54","arguments":"{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}"}

            event: response.mcp_call.completed
            data: {"type":"response.mcp_call.completed","sequence_number":16,"output_index":2,"item_id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":17,"output_index":2,"item":{"id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}","error":null,"name":"ask_question","output":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` is `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` . This file provides an overview of the `Microsoft.Extensions.AI.Abstractions` library, including installation instructions and usage examples for its core components like `IChatClient` and `IEmbeddingGenerator`   .\n\n## README.md Content Overview\nThe `README.md` file for `Microsoft.Extensions.AI.Abstractions` details the purpose of the library, which is to provide abstractions for generative AI components . It includes instructions on how to install the NuGet package `Microsoft.Extensions.AI.Abstractions` .\n\nThe document also provides usage examples for the `IChatClient` interface, which defines methods for interacting with AI services that offer \"chat\" capabilities . This includes examples for requesting both complete and streaming chat responses  .\n\nFurthermore, the `README.md` explains the `IEmbeddingGenerator` interface, which is used for generating vector embeddings from input values . It demonstrates how to use `GenerateAsync` to create embeddings . The file also discusses how both `IChatClient` and `IEmbeddingGenerator` implementations can be layered to create pipelines of functionality, incorporating features like caching and telemetry  .\n\nNotes:\nThe user's query specifically asked for the path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions`. The provided codebase context, particularly the wiki page for \"AI Extensions Framework\", directly lists this file as a relevant source file . The content of the `README.md` file itself further confirms its relevance to the `Microsoft.Extensions.AI.Abstractions` library.\n\nWiki pages you might want to explore:\n- [AI Extensions Framework (dotnet/extensions)](/wiki/dotnet/extensions#3)\n- [Chat Completion (dotnet/extensions)](/wiki/dotnet/extensions#3.3)\n\nView this search on DeepWiki: https://deepwiki.com/search/what-is-the-path-to-the-readme_bb6bee43-3136-4b21-bc5d-02ca1611d857\n","server_label":"deepwiki"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":18,"output_index":3,"item":{"id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":19,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":20,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"The","logprobs":[],"obfuscation":"a5sNdjeWpJXIK"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":21,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" path","logprobs":[],"obfuscation":"2oWbALsHrtv"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":22,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" to","logprobs":[],"obfuscation":"K8lRBCaiusvjP"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":23,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" the","logprobs":[],"obfuscation":"LP7Xp4jDWA5w"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":24,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" `","logprobs":[],"obfuscation":"2rUNEj0h3wLlee"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":25,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"README","logprobs":[],"obfuscation":"PSbOrCj8y6"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":26,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".md","logprobs":[],"obfuscation":"Do0BCY4kJ6wQW"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":27,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"`","logprobs":[],"obfuscation":"3fTPkjHu1Oq83DT"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":28,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" file","logprobs":[],"obfuscation":"CI9PXx3sH06"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":29,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" for","logprobs":[],"obfuscation":"fJuaoSPsMge8"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":30,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" `","logprobs":[],"obfuscation":"O1h4Q0T72OM4e7"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":31,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"Microsoft","logprobs":[],"obfuscation":"E2YPgfE"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":32,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".Extensions","logprobs":[],"obfuscation":"vfVX8"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":33,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".A","logprobs":[],"obfuscation":"EwDmSMHqymBRl1"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":34,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"I","logprobs":[],"obfuscation":"QQfjze1z7QhvcJE"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":35,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".A","logprobs":[],"obfuscation":"7fLbFXKbxOMkBi"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":36,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"bst","logprobs":[],"obfuscation":"3p1svK7Jd1N7C"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":37,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"ractions","logprobs":[],"obfuscation":"Cl2xCwTC"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":38,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"`","logprobs":[],"obfuscation":"ObDOKE72QOlXSx9"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":39,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" in","logprobs":[],"obfuscation":"FJwPbDYgh4XjL"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":40,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" the","logprobs":[],"obfuscation":"e8cV5qt7hEsz"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":41,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" `","logprobs":[],"obfuscation":"Hf8ZQDFLfImh3e"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":42,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"dot","logprobs":[],"obfuscation":"0lh2vLiYye2JI"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":43,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"net","logprobs":[],"obfuscation":"g5fzb2qtk4Piz"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":44,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"/extensions","logprobs":[],"obfuscation":"egpos"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":45,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"`","logprobs":[],"obfuscation":"gXw3bKveEVIKXux"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":46,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" repository","logprobs":[],"obfuscation":"rqhlC"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":47,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" is","logprobs":[],"obfuscation":"YZq9zsRja0g2M"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":48,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":":\n\n","logprobs":[],"obfuscation":"mhDAmaHJUvLGl"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":49,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"``","logprobs":[],"obfuscation":"3XmO5YTsWjzHHf"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":50,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"`\n","logprobs":[],"obfuscation":"4fmXZmdkPxNn8K"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":51,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"src","logprobs":[],"obfuscation":"ifGf4yLEg5pMZ"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":52,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"/L","logprobs":[],"obfuscation":"C1k1toBElpgxyW"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":53,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"ibraries","logprobs":[],"obfuscation":"fdOTYTyp"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":54,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"/M","logprobs":[],"obfuscation":"DyscJIQYaPJugC"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":55,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"icrosoft","logprobs":[],"obfuscation":"PQxU7muP"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":56,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".Extensions","logprobs":[],"obfuscation":"RCJB8"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":57,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".A","logprobs":[],"obfuscation":"i92CWxnAkwS4C9"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":58,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"I","logprobs":[],"obfuscation":"qfH8wVJN74vCfBM"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":59,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".A","logprobs":[],"obfuscation":"LcuBP89lZVCCH9"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":60,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"bst","logprobs":[],"obfuscation":"I8rKDbKN0zylv"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":61,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"ractions","logprobs":[],"obfuscation":"tOgiCPs5"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":62,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"/","logprobs":[],"obfuscation":"jgJjLruTbFJGDhU"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":63,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"README","logprobs":[],"obfuscation":"D5VSEFNde7"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":64,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".md","logprobs":[],"obfuscation":"7ZGJO5sZOTPBs"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":65,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"\n","logprobs":[],"obfuscation":"7Sv80haKTTwfEWj"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":66,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"``","logprobs":[],"obfuscation":"m1JSvZ8rrpJnH5"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":67,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"`\n\n","logprobs":[],"obfuscation":"U93PMKtCB5Pb5"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":68,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"This","logprobs":[],"obfuscation":"f5veTGedo9nM"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":69,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" file","logprobs":[],"obfuscation":"oEBwvP5FnPK"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":70,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" provides","logprobs":[],"obfuscation":"IVNCYwr"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":71,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" an","logprobs":[],"obfuscation":"3x6WquURIJ3ld"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":72,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" overview","logprobs":[],"obfuscation":"VR9yeiD"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":73,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" of","logprobs":[],"obfuscation":"z46dC1o2FC8Rs"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":74,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" the","logprobs":[],"obfuscation":"YfZGabvmgyoI"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":75,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" library","logprobs":[],"obfuscation":"TamElgEp"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":76,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":",","logprobs":[],"obfuscation":"VfVfqbnHAfsJyJn"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":77,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" installation","logprobs":[],"obfuscation":"CGR"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":78,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" instructions","logprobs":[],"obfuscation":"xst"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":79,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":",","logprobs":[],"obfuscation":"3u5wqRA2RXh2QP8"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":80,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" and","logprobs":[],"obfuscation":"tD4WZmOhepzQ"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":81,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" usage","logprobs":[],"obfuscation":"SadOK826mZ"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":82,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" examples","logprobs":[],"obfuscation":"5VpLKav"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":83,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" for","logprobs":[],"obfuscation":"xPvtjDSUic9E"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":84,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" its","logprobs":[],"obfuscation":"6duK61DX14vx"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":85,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" core","logprobs":[],"obfuscation":"Cz8trPLsCWu"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":86,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" components","logprobs":[],"obfuscation":"Gexuy"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":87,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":".","logprobs":[],"obfuscation":"HVeWkHoX1cc6hVh"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":88,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" If","logprobs":[],"obfuscation":"G1TOxxwvSEq4L"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":89,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" you","logprobs":[],"obfuscation":"xQlKeOixd1hv"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":90,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" have","logprobs":[],"obfuscation":"bX6P0qgFPnR"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":91,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" any","logprobs":[],"obfuscation":"KxH8EiMzXa1N"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":92,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" more","logprobs":[],"obfuscation":"kA0kxRPPqru"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":93,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" questions","logprobs":[],"obfuscation":"9HRCyD"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":94,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" about","logprobs":[],"obfuscation":"yYFZhtsSfc"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":95,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" it","logprobs":[],"obfuscation":"zpyEAwPWl8Ozh"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":96,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":",","logprobs":[],"obfuscation":"ivjn00lbmzDHiFU"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":97,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" feel","logprobs":[],"obfuscation":"O2edXDmkBqt"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":98,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" free","logprobs":[],"obfuscation":"MlpWh7p0P1F"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":99,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" to","logprobs":[],"obfuscation":"uMNfozGkKe6xW"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":100,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":" ask","logprobs":[],"obfuscation":"6rMOxwXhR8RY"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":101,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"delta":"!","logprobs":[],"obfuscation":"QPZMdhS0e5vYuRl"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":102,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"text":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` in the `dotnet/extensions` repository is:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the library, installation instructions, and usage examples for its core components. If you have any more questions about it, feel free to ask!","logprobs":[]}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":103,"item_id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","output_index":3,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` in the `dotnet/extensions` repository is:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the library, installation instructions, and usage examples for its core components. If you have any more questions about it, feel free to ask!"}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":104,"output_index":3,"item":{"id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` in the `dotnet/extensions` repository is:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the library, installation instructions, and usage examples for its core components. If you have any more questions about it, feel free to ask!"}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":105,"response":{"id":"resp_68be44fd7298819e82fd82c8516e970d03a2537be0e84a54","object":"response","created_at":1757299965,"status":"completed","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-mini-2024-07-18","output":[{"id":"mcpl_68be44fd8f68819eba7a74a2f6d27a5a03a2537be0e84a54","type":"mcp_list_tools","server_label":"deepwiki","tools":[{"annotations":{"read_only":false},"description":"Get a list of documentation topics for a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"}},"required":["repoName"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"read_wiki_structure"},{"annotations":{"read_only":false},"description":"View documentation about a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"}},"required":["repoName"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"read_wiki_contents"},{"annotations":{"read_only":false},"description":"Ask any question about a GitHub repository","input_schema":{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repository: owner/repo (e.g. \"facebook/react\")"},"question":{"type":"string","description":"The question to ask about the repository"}},"required":["repoName","question"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"},"name":"ask_question"}]},{"id":"mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"{\"repoName\":\"dotnet/extensions\"}","error":null,"name":"read_wiki_structure","output":"Available pages for dotnet/extensions:\n\n- 1 Overview\n- 2 Build System and CI/CD\n- 3 AI Extensions Framework\n  - 3.1 Core Abstractions\n  - 3.2 AI Function System\n  - 3.3 Chat Completion\n  - 3.4 Caching System\n  - 3.5 Evaluation and Reporting\n- 4 HTTP Resilience and Diagnostics\n  - 4.1 Standard Resilience\n  - 4.2 Hedging Strategies\n- 5 Telemetry and Compliance\n- 6 Testing Infrastructure\n  - 6.1 AI Service Integration Testing\n  - 6.2 Time Provider Testing","server_label":"deepwiki"},{"id":"mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54","type":"mcp_call","approval_request_id":null,"arguments":"{\"repoName\":\"dotnet/extensions\",\"question\":\"What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?\"}","error":null,"name":"ask_question","output":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` is `src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md` . This file provides an overview of the `Microsoft.Extensions.AI.Abstractions` library, including installation instructions and usage examples for its core components like `IChatClient` and `IEmbeddingGenerator`   .\n\n## README.md Content Overview\nThe `README.md` file for `Microsoft.Extensions.AI.Abstractions` details the purpose of the library, which is to provide abstractions for generative AI components . It includes instructions on how to install the NuGet package `Microsoft.Extensions.AI.Abstractions` .\n\nThe document also provides usage examples for the `IChatClient` interface, which defines methods for interacting with AI services that offer \"chat\" capabilities . This includes examples for requesting both complete and streaming chat responses  .\n\nFurthermore, the `README.md` explains the `IEmbeddingGenerator` interface, which is used for generating vector embeddings from input values . It demonstrates how to use `GenerateAsync` to create embeddings . The file also discusses how both `IChatClient` and `IEmbeddingGenerator` implementations can be layered to create pipelines of functionality, incorporating features like caching and telemetry  .\n\nNotes:\nThe user's query specifically asked for the path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions`. The provided codebase context, particularly the wiki page for \"AI Extensions Framework\", directly lists this file as a relevant source file . The content of the `README.md` file itself further confirms its relevance to the `Microsoft.Extensions.AI.Abstractions` library.\n\nWiki pages you might want to explore:\n- [AI Extensions Framework (dotnet/extensions)](/wiki/dotnet/extensions#3)\n- [Chat Completion (dotnet/extensions)](/wiki/dotnet/extensions#3.3)\n\nView this search on DeepWiki: https://deepwiki.com/search/what-is-the-path-to-the-readme_bb6bee43-3136-4b21-bc5d-02ca1611d857\n","server_label":"deepwiki"},{"id":"msg_68be450c39e8819eb9bf6fcb9fd16ecb03a2537be0e84a54","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The path to the `README.md` file for `Microsoft.Extensions.AI.Abstractions` in the `dotnet/extensions` repository is:\n\n```\nsrc/Libraries/Microsoft.Extensions.AI.Abstractions/README.md\n```\n\nThis file provides an overview of the library, installation instructions, and usage examples for its core components. If you have any more questions about it, feel free to ask!"}],"role":"assistant"}],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"mcp","allowed_tools":null,"headers":null,"require_approval":"never","server_description":null,"server_label":"deepwiki","server_url":"https://mcp.deepwiki.com/<redacted>"}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":{"input_tokens":1420,"input_tokens_details":{"cached_tokens":0},"output_tokens":149,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":1569},"user":null,"metadata":{}}}
            
            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        ChatOptions chatOptions = new()
        {
            Tools = [new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
                {
                    ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
                }
            ],
        };

        var response = await client.GetStreamingResponseAsync("Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository", chatOptions)
            .ToChatResponseAsync();
        Assert.NotNull(response);

        Assert.Equal("resp_68be44fd7298819e82fd82c8516e970d03a2537be0e84a54", response.ResponseId);
        Assert.Equal("resp_68be44fd7298819e82fd82c8516e970d03a2537be0e84a54", response.ConversationId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_757_299_965), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);

        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.StartsWith("The path to the `README.md` file", response.Messages[0].Text);

        Assert.Equal(6, message.Contents.Count);

        var firstCall = Assert.IsType<McpServerToolCallContent>(message.Contents[1]);
        Assert.Equal("mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54", firstCall.CallId);
        Assert.Equal("deepwiki", firstCall.ServerName);
        Assert.Equal("read_wiki_structure", firstCall.Name);
        Assert.NotNull(firstCall.Arguments);
        Assert.Single(firstCall.Arguments);
        Assert.Equal("dotnet/extensions", ((JsonElement)firstCall.Arguments["repoName"]!).GetString());

        var firstResult = Assert.IsType<McpServerToolResultContent>(message.Contents[2]);
        Assert.Equal("mcp_68be4503d45c819e89cb574361c8eba003a2537be0e84a54", firstResult.CallId);
        Assert.StartsWith("Available pages for dotnet/extensions", Assert.IsType<TextContent>(Assert.Single(firstResult.Outputs!)).Text);

        var secondCall = Assert.IsType<McpServerToolCallContent>(message.Contents[3]);
        Assert.Equal("mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54", secondCall.CallId);
        Assert.Equal("deepwiki", secondCall.ServerName);
        Assert.Equal("ask_question", secondCall.Name);
        Assert.NotNull(secondCall.Arguments);
        Assert.Equal("dotnet/extensions", ((JsonElement)secondCall.Arguments["repoName"]!).GetString());
        Assert.Equal("What is the path to the README.md file for Microsoft.Extensions.AI.Abstractions?", ((JsonElement)secondCall.Arguments["question"]!).GetString());

        var secondResult = Assert.IsType<McpServerToolResultContent>(message.Contents[4]);
        Assert.Equal("mcp_68be4505f134819e806c002f27cce0c303a2537be0e84a54", secondResult.CallId);
        Assert.StartsWith("The path to the `README.md` file", Assert.IsType<TextContent>(Assert.Single(secondResult.Outputs!)).Text);

        Assert.NotNull(response.Usage);
        Assert.Equal(1420, response.Usage.InputTokenCount);
        Assert.Equal(149, response.Usage.OutputTokenCount);
        Assert.Equal(1569, response.Usage.TotalTokenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpToolCall_ErrorResponse_NonStreaming(bool rawTool)
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "mymcp",
                        "server_url": "https://mcp.example.com/mcp",
                        "require_approval": "never"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Test error handling"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
                "id": "resp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0",
                "object": "response",
                "created_at": 1757299100,
                "status": "completed",
                "background": false,
                "error": null,
                "incomplete_details": null,
                "instructions": null,
                "max_output_tokens": null,
                "max_tool_calls": null,
                "model": "gpt-4o-mini-2024-07-18",
                "output": [
                    {
                        "id": "mcpl_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0",
                        "type": "mcp_list_tools",
                        "server_label": "mymcp",
                        "tools": [
                            {
                                "annotations": {
                                    "read_only": false
                                },
                                "description": "A tool that always errors",
                                "input_schema": {
                                    "type": "object",
                                    "properties": {},
                                    "additionalProperties": false,
                                    "$schema": "http://json-schema.org/draft-07/schema#"
                                },
                                "name": "test_error"
                            }
                        ]
                    },
                    {
                        "id": "mcp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0",
                        "type": "mcp_call",
                        "approval_request_id": null,
                        "arguments": "{}",
                        "error": {
                            "type": "mcp_tool_execution_error",
                            "content": [
                                {
                                    "type": "text",
                                    "text": "An error occurred invoking 'test_error'.",
                                    "annotations": null,
                                    "meta": null
                                }
                            ]
                        },
                        "name": "test_error",
                        "output": null,
                        "server_label": "mymcp"
                    },
                    {
                        "id": "msg_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0",
                        "type": "message",
                        "status": "completed",
                        "content": [
                            {
                                "type": "output_text",
                                "annotations": [],
                                "logprobs": [],
                                "text": "The tool encountered an error during execution."
                            }
                        ],
                        "role": "assistant"
                    }
                ],
                "parallel_tool_calls": true,
                "previous_response_id": null,
                "prompt_cache_key": null,
                "reasoning": {
                    "effort": null,
                    "summary": null
                },
                "safety_identifier": null,
                "service_tier": "default",
                "store": true,
                "temperature": 1,
                "text": {
                    "format": {
                        "type": "text"
                    },
                    "verbosity": "medium"
                },
                "tool_choice": "auto",
                "tools": [
                    {
                        "type": "mcp",
                        "allowed_tools": null,
                        "headers": null,
                        "require_approval": "never",
                        "server_description": null,
                        "server_label": "mymcp",
                        "server_url": "https://mcp.example.com/<redacted>"
                    }
                ],
                "top_logprobs": 0,
                "top_p": 1,
                "truncation": "disabled",
                "usage": {
                    "input_tokens": 500,
                    "input_tokens_details": {
                        "cached_tokens": 0
                    },
                    "output_tokens": 50,
                    "output_tokens_details": {
                        "reasoning_tokens": 0
                    },
                    "total_tokens": 550
                },
                "user": null,
                "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        AITool mcpTool = rawTool ?
            ResponseTool.CreateMcpTool("mymcp", serverUri: new("https://mcp.example.com/mcp"), toolCallApprovalPolicy: new McpToolCallApprovalPolicy(DefaultMcpToolCallApprovalPolicy.NeverRequireApproval)).AsAITool() :
            new HostedMcpServerTool("mymcp", new Uri("https://mcp.example.com/mcp"))
            {
                ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
            };

        ChatOptions chatOptions = new()
        {
            Tools = [mcpTool],
        };

        var response = await client.GetResponseAsync("Test error handling", chatOptions);
        Assert.NotNull(response);

        Assert.Equal("resp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0", response.ResponseId);
        Assert.Equal("resp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0", response.ConversationId);
        Assert.Equal("gpt-4o-mini-2024-07-18", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_757_299_100), response.CreatedAt);
        Assert.Null(response.FinishReason);

        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.Equal("The tool encountered an error during execution.", response.Messages[0].Text);

        Assert.Equal(4, message.Contents.Count);

        var toolCall = Assert.IsType<McpServerToolCallContent>(message.Contents[1]);
        Assert.Equal("mcp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0", toolCall.CallId);
        Assert.Equal("mymcp", toolCall.ServerName);
        Assert.Equal("test_error", toolCall.Name);
        Assert.NotNull(toolCall.Arguments);
        Assert.Empty(toolCall.Arguments);

        var toolResult = Assert.IsType<McpServerToolResultContent>(message.Contents[2]);
        Assert.Equal("mcp_689023b0fa88819f99f48aff343d5ad50475557f6fefb5f0", toolResult.CallId);
        var errorContent = Assert.IsType<ErrorContent>(Assert.Single(toolResult.Outputs!));
        Assert.Contains("An error occurred invoking 'test_error'.", errorContent.Message);

        Assert.NotNull(response.Usage);
        Assert.Equal(500, response.Usage.InputTokenCount);
        Assert.Equal(50, response.Usage.OutputTokenCount);
        Assert.Equal(550, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task McpToolCall_WithAuthorizationTokenAndCustomHeaders_IncludesInRequest()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "deepwiki",
                        "server_url": "https://mcp.deepwiki.com/mcp",
                        "headers": {
                            "Authorization": "Bearer test-auth-token-12345",
                            "X-Custom-Header": "custom-value"
                        },
                        "require_approval": "never"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
                "id": "resp_auth01",
                "object": "response",
                "created_at": 1757299043,
                "status": "completed",
                "model": "gpt-4o-mini-2024-07-18",
                "output": [
                    {
                        "id": "msg_auth01",
                        "type": "message",
                        "status": "completed",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "Hi!"
                            }
                        ]
                    }
                ],
                "usage": {
                    "input_tokens": 10,
                    "output_tokens": 2,
                    "total_tokens": 12
                }
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var mcpTool = new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-auth-token-12345",
                ["X-Custom-Header"] = "custom-value"
            }
        };

        var response = await client.GetResponseAsync("hello", new ChatOptions { Tools = [mcpTool] });

        Assert.NotNull(response);
        Assert.Equal("resp_auth01", response.ResponseId);
        var message = Assert.Single(response.Messages);
        Assert.Equal("Hi!", message.Text);
    }

    [Theory]
    [InlineData("bearer test-auth-token-12345")]
    [InlineData("BEARER test-auth-token-12345")]
    [InlineData("BeArEr test-auth-token-12345")]
    [InlineData("  Bearer test-auth-token-12345")]
    [InlineData("Bearer test-auth-token-12345  ")]
    [InlineData("  Bearer test-auth-token-12345  ")]
    [InlineData("Bearer   test-auth-token-12345")]
    public async Task McpToolCall_WithCaseInsensitiveBearerToken_ExtractsToken(string authHeaderValue)
    {
        // Use a connector ID (non-URL) to trigger the Bearer token extraction code path
        string expectedToken = "test-auth-token-12345";
        string expectedInput = $$"""
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "mcp",
                        "server_label": "my-connector",
                        "connector_id": "connector-id-123",
                        "authorization": "{{expectedToken}}",
                        "require_approval": "never"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
                "id": "resp_bearer01",
                "object": "response",
                "created_at": 1757299043,
                "status": "completed",
                "model": "gpt-4o-mini-2024-07-18",
                "output": [
                    {
                        "id": "msg_bearer01",
                        "type": "message",
                        "status": "completed",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "Hi!"
                            }
                        ]
                    }
                ],
                "usage": {
                    "input_tokens": 10,
                    "output_tokens": 2,
                    "total_tokens": 12
                }
            }
            """;

        using VerbatimHttpHandler handler = new(expectedInput, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        // Use string constructor with non-URL to trigger connector ID path
        var mcpTool = new HostedMcpServerTool("my-connector", "connector-id-123")
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = authHeaderValue,
            }
        };

        var response = await client.GetResponseAsync("hello", new ChatOptions { Tools = [mcpTool] });

        Assert.NotNull(response);
        Assert.Equal("resp_bearer01", response.ResponseId);
    }

    [Fact]
    public async Task GetResponseAsync_BackgroundResponses_FirstCall()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "background":true,
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed7",
              "object": "response",
              "created_at": 1758712522,
              "status": "queued",
              "background": true,
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "model": "gpt-4o-mini-2024-07-18",
              "output": [],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "generate_summary": null
              },
              "store": true,
              "temperature": 0.5,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": null,
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            AllowBackgroundResponses = true,
        });
        Assert.NotNull(response);

        Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed7", response.ResponseId);
        Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed7", response.ConversationId);
        Assert.Empty(response.Messages);

        Assert.NotNull(response.ContinuationToken);
        var responsesContinuationToken = TestOpenAIResponsesContinuationToken.FromToken(response.ContinuationToken);
        Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed7", responsesContinuationToken.ResponseId);
        Assert.Null(responsesContinuationToken.SequenceNumber);
    }

    [Theory]
    [InlineData(ResponseStatus.Queued)]
    [InlineData(ResponseStatus.InProgress)]
    [InlineData(ResponseStatus.Completed)]
    [InlineData(ResponseStatus.Cancelled)]
    [InlineData(ResponseStatus.Failed)]
    [InlineData(ResponseStatus.Incomplete)]
    public async Task GetResponseAsync_BackgroundResponses_PollingCall(ResponseStatus expectedStatus)
    {
        var expectedInput = new HttpHandlerExpectedInput
        {
            Uri = new Uri("https://api.openai.com/v1/responses/resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8"),
            Method = HttpMethod.Get,
        };

        string output = $$""""
            {
              "id": "resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8",
              "object": "response",
              "created_at": 1758712522,
              "status": "{{ResponseStatusToRequestValue(expectedStatus)}}",
              "background": true,
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "model": "gpt-4o-mini-2024-07-18",
              "output": {{(expectedStatus is (ResponseStatus.Queued or ResponseStatus.InProgress)
                ? "[]"
                : """
                    [{
                      "type": "message",
                      "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                      "status": "completed",
                      "role": "assistant",
                      "content": [
                        {
                          "type": "output_text",
                          "text": "The background response result.",
                          "annotations": []
                        }
                      ]
                    }]
                    """
              )}},
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {
                "effort": null,
                "generate_summary": null
              },
              "store": true,
              "temperature": 0.5,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": null,
              "user": null,
              "metadata": {}
            }
            """";

        using VerbatimHttpHandler handler = new(expectedInput, output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var continuationToken = new TestOpenAIResponsesContinuationToken("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8");

        var response = await client.GetResponseAsync([], new()
        {
            ContinuationToken = continuationToken,
            AllowBackgroundResponses = true,
        });
        Assert.NotNull(response);

        Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8", response.ResponseId);
        Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8", response.ConversationId);

        switch (expectedStatus)
        {
            case ResponseStatus.Queued:
            case ResponseStatus.InProgress:
            {
                Assert.NotNull(response.ContinuationToken);

                var responsesContinuationToken = TestOpenAIResponsesContinuationToken.FromToken(response.ContinuationToken);
                Assert.Equal("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8", responsesContinuationToken.ResponseId);
                Assert.Null(responsesContinuationToken.SequenceNumber);

                Assert.Empty(response.Messages);
                break;
            }

            case ResponseStatus.Completed:
            case ResponseStatus.Cancelled:
            case ResponseStatus.Failed:
            case ResponseStatus.Incomplete:
            {
                Assert.Null(response.ContinuationToken);

                Assert.Equal("The background response result.", response.Text);
                Assert.Single(response.Messages.Single().Contents);
                Assert.Equal(ChatRole.Assistant, response.Messages.Single().Role);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(expectedStatus), expectedStatus, null);
        }
    }

    [Fact]
    public async Task GetResponseAsync_BackgroundResponses_PollingCall_WithMessages()
    {
        using VerbatimHttpHandler handler = new(string.Empty, string.Empty);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var options = new ChatOptions
        {
            ContinuationToken = new TestOpenAIResponsesContinuationToken("resp_68d3d2c9ef7c8195863e4e2b2ec226a205007262ecbbfed8"),
            AllowBackgroundResponses = true,
        };

        // A try to update a background response with new messages should fail.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.GetResponseAsync("Please book hotel as well", options);
        });
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses()
    {
        const string Input = """
            {
              "model": "gpt-4o-2024-08-06",
              "background": true,
              "input":[{
                "type":"message",
                "role":"user",
                "content":[{
                  "type":"input_text",
                  "text":"hello"
                }]
              }],
              
              "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559","object":"response","created_at":1758724519,"status":"queued","background":true,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.queued
            data: {"type":"response.queued","sequence_number":1,"response":{"id":"resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559","object":"response","created_at":1758724519,"status":"queued","background":true,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":2,"response":{"truncation":"disabled","id":"resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559","tool_choice":"auto","temperature":1.0,"top_p":1.0,"status":"in_progress","top_logprobs":0,"usage":null,"object":"response","created_at":1758724519,"prompt_cache_key":null,"text":{"format":{"type":"text"},"verbosity":"medium"},"incomplete_details":null,"model":"gpt-4o-2024-08-06","previous_response_id":null,"safety_identifier":null,"metadata":{},"store":true,"output":[],"parallel_tool_calls":true,"error":null,"background":true,"instructions":null,"service_tier":"auto","max_tool_calls":null,"max_output_tokens":null,"tools":[],"user":null,"reasoning":{"effort":null,"summary":null}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":3,"item":{"id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content":[],"role":"assistant","status":"in_progress","type":"message"},"output_index":0}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":4,"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"part":{"text":"","logprobs":[],"type":"output_text","annotations":[]},"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":5,"delta":"Hello","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":6,"delta":"!","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":7,"delta":" How","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":8,"delta":" can","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":9,"delta":" I","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":10,"delta":" assist","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":11,"delta":" you","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":12,"delta":" today","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":13,"delta":"?","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":14,"text":"Hello! How can I assist you today?","logprobs":[],"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"output_index":0}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":15,"item_id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content_index":0,"part":{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]},"output_index":0}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":16,"item":{"id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content":[{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]}],"role":"assistant","status":"completed","type":"message"},"output_index":0}

            event: response.completed
            data: {"type":"response.completed","sequence_number":17,"response":{"truncation":"disabled","id":"resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559","tool_choice":"auto","temperature":1.0,"top_p":1.0,"status":"completed","top_logprobs":0,"usage":{"total_tokens":18,"input_tokens_details":{"cached_tokens":0},"output_tokens_details":{"reasoning_tokens":0},"output_tokens":10,"input_tokens":8},"object":"response","created_at":1758724519,"prompt_cache_key":null,"text":{"format":{"type":"text"},"verbosity":"medium"},"incomplete_details":null,"model":"gpt-4o-2024-08-06","previous_response_id":null,"safety_identifier":null,"metadata":{},"store":true,"output":[{"id":"msg_68d401aa78d481a2ab30776a79c691a6073420ed59d5f559","content":[{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]}],"role":"assistant","status":"completed","type":"message"}],"parallel_tool_calls":true,"error":null,"background":true,"instructions":null,"service_tier":"default","max_tool_calls":null,"max_output_tokens":null,"tools":[],"user":null,"reasoning":{"effort":null,"summary":null}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            AllowBackgroundResponses = true,
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello! How can I assist you today?", string.Concat(updates.Select(u => u.Text)));
        Assert.Equal(18, updates.Count);

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_758_724_519);

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559", updates[i].ResponseId);
            Assert.Equal("resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559", updates[i].ConversationId);
            Assert.Equal(createdAt, updates[i].CreatedAt);
            Assert.Equal("gpt-4o-2024-08-06", updates[i].ModelId);
            Assert.Null(updates[i].AdditionalProperties);

            if (i < updates.Count - 1)
            {
                Assert.NotNull(updates[i].ContinuationToken);
                var responsesContinuationToken = TestOpenAIResponsesContinuationToken.FromToken(updates[i].ContinuationToken!);
                Assert.Equal("resp_68d401a7b36c81a288600e95a5a119d4073420ed59d5f559", responsesContinuationToken.ResponseId);
                Assert.Equal(i, responsesContinuationToken.SequenceNumber);
                Assert.Null(updates[i].FinishReason);
            }
            else
            {
                Assert.Null(updates[i].ContinuationToken);
                Assert.Equal(ChatFinishReason.Stop, updates[i].FinishReason);
            }

            Assert.Equal((i >= 5 && i <= 13) || i == 17 ? 1 : 0, updates[i].Contents.Count);
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses_StreamResumption()
    {
        var expectedInput = new HttpHandlerExpectedInput
        {
            Uri = new Uri("https://api.openai.com/v1/responses/resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b?stream=true&starting_after=9"),
            Method = HttpMethod.Get,
        };

        const string Output = """
            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":10,"delta":" assist","logprobs":[],"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":11,"delta":" you","logprobs":[],"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":12,"delta":" today","logprobs":[],"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"output_index":0}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":13,"delta":"?","logprobs":[],"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"output_index":0}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":14,"text":"Hello! How can I assist you today?","logprobs":[],"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"output_index":0}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":15,"item_id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content_index":0,"part":{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]},"output_index":0}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":16,"item":{"id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content":[{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]}],"role":"assistant","status":"completed","type":"message"},"output_index":0}

            event: response.completed
            data: {"type":"response.completed","sequence_number":17,"response":{"truncation":"disabled","id":"resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b","tool_choice":"auto","temperature":1.0,"top_p":1.0,"status":"completed","top_logprobs":0,"usage":{"total_tokens":18,"input_tokens_details":{"cached_tokens":0},"output_tokens_details":{"reasoning_tokens":0},"output_tokens":10,"input_tokens":8},"object":"response","created_at":1758727622,"prompt_cache_key":null,"text":{"format":{"type":"text"},"verbosity":"medium"},"incomplete_details":null,"model":"gpt-4o-2024-08-06","previous_response_id":null,"safety_identifier":null,"metadata":{},"store":true,"output":[{"id":"msg_68d40dcb2d34819c88f5d6a8ca7b0308029e611c3cc4a34b","content":[{"text":"Hello! How can I assist you today?","logprobs":[],"type":"output_text","annotations":[]}],"role":"assistant","status":"completed","type":"message"}],"parallel_tool_calls":true,"error":null,"background":true,"instructions":null,"service_tier":"default","max_tool_calls":null,"max_output_tokens":null,"tools":[],"user":null,"reasoning":{"effort":null,"summary":null}}}

            
            """;

        using VerbatimHttpHandler handler = new(expectedInput, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        // Emulating resumption of the stream after receiving the first 9 updates that provided the text "Hello! How can I"
        var continuationToken = new TestOpenAIResponsesContinuationToken("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b")
        {
            SequenceNumber = 9
        };

        var chatOptions = new ChatOptions
        {
            AllowBackgroundResponses = true,
            ContinuationToken = continuationToken,
        };

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync([], chatOptions))
        {
            updates.Add(update);
        }

        // Receiving the remaining updates to complete the response "Hello! How can I assist you today?"
        Assert.Equal(" assist you today?", string.Concat(updates.Select(u => u.Text)));
        Assert.Equal(8, updates.Count);

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_758_727_622);

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b", updates[i].ResponseId);
            Assert.Equal("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b", updates[i].ConversationId);

            var sequenceNumber = i + 10;

            if (sequenceNumber is (>= 10 and <= 13))
            {
                // Text deltas
                Assert.NotNull(updates[i].ContinuationToken);
                var responsesContinuationToken = TestOpenAIResponsesContinuationToken.FromToken(updates[i].ContinuationToken!);
                Assert.Equal("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b", responsesContinuationToken.ResponseId);
                Assert.Equal(sequenceNumber, responsesContinuationToken.SequenceNumber);

                Assert.Single(updates[i].Contents);
            }
            else if (sequenceNumber is (>= 14 and <= 16))
            {
                // Response Complete and Assistant message updates
                Assert.NotNull(updates[i].ContinuationToken);
                var responsesContinuationToken = TestOpenAIResponsesContinuationToken.FromToken(updates[i].ContinuationToken!);
                Assert.Equal("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b", responsesContinuationToken.ResponseId);
                Assert.Equal(sequenceNumber, responsesContinuationToken.SequenceNumber);

                Assert.Empty(updates[i].Contents);
            }
            else
            {
                // The last update with the response completion
                Assert.Null(updates[i].ContinuationToken);
                Assert.Single(updates[i].Contents);
            }
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses_StreamResumption_WithMessages()
    {
        using VerbatimHttpHandler handler = new(string.Empty, string.Empty);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        // Emulating resumption of the stream after receiving the first 9 updates that provided the text "Hello! How can I"
        var chatOptions = new ChatOptions
        {
            AllowBackgroundResponses = true,
            ContinuationToken = new TestOpenAIResponsesContinuationToken("resp_68d40dc671a0819cb0ee920078333451029e611c3cc4a34b")
            {
                SequenceNumber = 9
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("Please book a hotel for me", chatOptions))
#pragma warning disable S108 // Nested blocks of code should not be left empty
            {
            }
#pragma warning restore S108 // Nested blocks of code should not be left empty
        });
    }

    [Fact]
    public async Task CodeInterpreterTool_NonStreaming()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"Calculate the sum of numbers from 1 to 5"}]
                }],
                "tools":[{
                    "type":"code_interpreter",
                    "container":{"type":"auto"}
                }]
            }
            """;

        const string Output = """
            {
              "id": "resp_0e599e83cc6642210068fb7475165481a08efc750483c7048f",
              "object": "response",
              "created_at": 1761309813,
              "status": "completed",
              "background": false,
              "error": null,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "max_tool_calls": null,
              "model": "gpt-4o-2024-08-06",
              "output": [
                {
                  "id": "ci_0e599e83cc6642210068fb7477fb9881a0811e8b0dc054b2fa",
                  "type": "code_interpreter_call",
                  "status": "completed",
                  "code": "# Calculating the sum of numbers from 1 to 5\nresult = sum(range(1, 6))\nresult",
                  "container_id": "cntr_68fb7476c384819186524b78cdc3180000a9a0fdd06b3cd4",
                  "outputs": null
                },
                {
                  "id": "msg_0e599e83cc6642210068fb747e118081a08c3ed46daa9d9dcb",
                  "type": "message",
                  "status": "completed",
                  "content": [
                    {
                      "type": "output_text",
                      "annotations": [],
                      "logprobs": [],
                      "text": "15"
                    }
                  ],
                  "role": "assistant"
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "prompt_cache_key": null,
              "reasoning": {
                "effort": null,
                "summary": null
              },
              "safety_identifier": null,
              "service_tier": "default",
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                },
                "verbosity": "medium"
              },
              "tool_choice": "auto",
              "tools": [
                {
                  "type": "code_interpreter",
                  "container": {
                    "type": "auto"
                  }
                }
              ],
              "top_logprobs": 0,
              "top_p": 1.0,
              "truncation": "disabled",
              "usage": {
                "input_tokens": 225,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 34,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 259
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        var response = await client.GetResponseAsync("Calculate the sum of numbers from 1 to 5", new()
        {
            Tools = [new HostedCodeInterpreterTool()],
        });

        Assert.NotNull(response);
        Assert.Single(response.Messages);

        var message = response.Messages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal(3, message.Contents.Count);

        var codeCall = Assert.IsType<CodeInterpreterToolCallContent>(message.Contents[0]);
        Assert.NotNull(codeCall.Inputs);
        var codeInput = Assert.IsType<DataContent>(Assert.Single(codeCall.Inputs));
        Assert.Equal("text/x-python", codeInput.MediaType);
        Assert.Equal("# Calculating the sum of numbers from 1 to 5\nresult = sum(range(1, 6))\nresult", Encoding.UTF8.GetString(codeInput.Data.ToArray()));

        var codeResult = Assert.IsType<CodeInterpreterToolResultContent>(message.Contents[1]);
        Assert.Equal(codeCall.CallId, codeResult.CallId);

        var textContent = Assert.IsType<TextContent>(message.Contents[2]);
        Assert.Equal("15", textContent.Text);
    }

    [Fact]
    public async Task CodeInterpreterTool_Streaming()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"Calculate the sum of numbers from 1 to 10 using Python"}]
                }],
                "tools":[{
                    "type":"code_interpreter",
                    "container":{"type":"auto"}
                }],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"in_progress","code":"","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null}}

            event: response.code_interpreter_call.in_progress
            data: {"type":"response.code_interpreter_call.in_progress","sequence_number":3,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":4,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"#","obfuscation":"sl1L6kjYGbL3W7b"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":5,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" Calculate","obfuscation":"nXS0Oz"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":6,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" the","obfuscation":"BeywG4wkYSPO"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":7,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" sum","obfuscation":"lQQwYY1jVUku"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":8,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" of","obfuscation":"B7ZYHyd1bTIIr"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":9,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" numbers","obfuscation":"c9P1UFe4"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":10,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" from","obfuscation":"jARdbqvpfdt"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":11,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" ","obfuscation":"wciF7LWnjGSdWPb"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":12,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"1","obfuscation":"KLuWFhT8xPOyTNH"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":13,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" to","obfuscation":"5QCNr2nNt72Hg"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":14,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" ","obfuscation":"F3vctEo2cPUvnhe"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":15,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"10","obfuscation":"JBwcgWLYbSTskz"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":16,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"\n","obfuscation":"AgU5f5WddGwHDJg"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":17,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"sum","obfuscation":"Vey8vqQPTbewO"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":18,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"_of","obfuscation":"Lyrmc5oOwdmsp"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":19,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"_numbers","obfuscation":"YxvseUG4"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":20,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" =","obfuscation":"yoo1CBUhRbgI36"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":21,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" sum","obfuscation":"pKTBmkNEE4SA"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":22,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"(range","obfuscation":"BixU5bs5ms"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":23,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"(","obfuscation":"nsarVNP46dpVYMb"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":24,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"1","obfuscation":"qkth7DCPzS6mfEd"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":25,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":",","obfuscation":"J5uAitISxtjRSQA"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":26,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" ","obfuscation":"thr4ylmBRbAk0PY"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":27,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"11","obfuscation":"FWxcmwOFHJKEPJ"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":28,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"))\n","obfuscation":"ifI2JoREexe3t"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":29,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"sum","obfuscation":"VI7RlYoKWGMKP"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":30,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"_of","obfuscation":"lkv27YflY8GLq"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":31,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"_numbers","obfuscation":"xAQFrVav"}

            event: response.code_interpreter_call_code.done
            data: {"type":"response.code_interpreter_call_code.done","sequence_number":32,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","code":"# Calculate the sum of numbers from 1 to 10\nsum_of_numbers = sum(range(1, 11))\nsum_of_numbers"}

            event: response.code_interpreter_call.interpreting
            data: {"type":"response.code_interpreter_call.interpreting","sequence_number":33,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3"}

            event: response.code_interpreter_call.completed
            data: {"type":"response.code_interpreter_call.completed","sequence_number":34,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":35,"output_index":0,"item":{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"completed","code":"# Calculate the sum of numbers from 1 to 10\nsum_of_numbers = sum(range(1, 11))\nsum_of_numbers","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":36,"output_index":1,"item":{"id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","type":"message","status":"in_progress","content":[],"role":"assistant"}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","sequence_number":37,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":""}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":38,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":"The","logprobs":[],"obfuscation":"r7iIr1QJ50aER"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":39,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" sum","logprobs":[],"obfuscation":"AQlXzWBYj2nu"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":40,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" of","logprobs":[],"obfuscation":"PpVAep2w6YBTd"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":41,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" numbers","logprobs":[],"obfuscation":"q2oosiA3"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":42,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" from","logprobs":[],"obfuscation":"BBLhSYyDDUG"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":43,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" ","logprobs":[],"obfuscation":"itOENAMFwzo6ESp"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":44,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":"1","logprobs":[],"obfuscation":"g93CJ2MyMSbq26V"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":45,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" to","logprobs":[],"obfuscation":"WyzXmwaUVATTs"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":46,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" ","logprobs":[],"obfuscation":"DBgQSKk2myfDWpq"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":47,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":"10","logprobs":[],"obfuscation":"RA4RYQSLNug4pg"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":48,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" is","logprobs":[],"obfuscation":"CgEfKJVj1DJtz"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":49,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":" ","logprobs":[],"obfuscation":"TVg4ccd4ZMwEiru"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":50,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":"55","logprobs":[],"obfuscation":"CVN92VujTbUiZ0"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","sequence_number":51,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"delta":".","logprobs":[],"obfuscation":"7YegRUzag3K6fdV"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","sequence_number":52,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"text":"The sum of numbers from 1 to 10 is 55.","logprobs":[]}

            event: response.content_part.done
            data: {"type":"response.content_part.done","sequence_number":53,"item_id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","output_index":1,"content_index":0,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of numbers from 1 to 10 is 55."}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":54,"output_index":1,"item":{"id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of numbers from 1 to 10 is 55."}],"role":"assistant"}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":55,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"completed","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"completed","code":"# Calculate the sum of numbers from 1 to 10\nsum_of_numbers = sum(range(1, 11))\nsum_of_numbers","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null},{"id":"msg_05d8f42f04f94cb80068fc3b86cc0c819ebae29aac8563d48d","type":"message","status":"completed","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"The sum of numbers from 1 to 10 is 55."}],"role":"assistant"}],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":{"input_tokens":219,"input_tokens_details":{"cached_tokens":0},"output_tokens":50,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":269},"user":null,"metadata":{}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        var response = await client.GetStreamingResponseAsync("Calculate the sum of numbers from 1 to 10 using Python", new()
        {
            Tools = [new HostedCodeInterpreterTool()],
        }).ToChatResponseAsync();

        Assert.NotNull(response);
        Assert.Single(response.Messages);

        var message = response.Messages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal(3, message.Contents.Count);

        var codeCall = Assert.IsType<CodeInterpreterToolCallContent>(message.Contents[0]);
        Assert.NotNull(codeCall.Inputs);
        var codeInput = Assert.IsType<DataContent>(Assert.Single(codeCall.Inputs));
        Assert.Equal("text/x-python", codeInput.MediaType);
        Assert.Equal("# Calculate the sum of numbers from 1 to 10\nsum_of_numbers = sum(range(1, 11))\nsum_of_numbers", Encoding.UTF8.GetString(codeInput.Data.ToArray()));

        var codeResult = Assert.IsType<CodeInterpreterToolResultContent>(message.Contents[1]);
        Assert.Equal(codeCall.CallId, codeResult.CallId);

        var textContent = Assert.IsType<TextContent>(message.Contents[2]);
        Assert.Equal("The sum of numbers from 1 to 10 is 55.", textContent.Text);

        Assert.NotNull(response.Usage);
        Assert.Equal(219, response.Usage.InputTokenCount);
        Assert.Equal(50, response.Usage.OutputTokenCount);
        Assert.Equal(269, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task CodeInterpreterTool_Streaming_YieldsCodeDeltas()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"Calculate the sum of numbers from 1 to 10 using Python"}]
                }],
                "tools":[{
                    "type":"code_interpreter",
                    "container":{"type":"auto"}
                }],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"in_progress","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"in_progress","code":"","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null}}

            event: response.code_interpreter_call.in_progress
            data: {"type":"response.code_interpreter_call.in_progress","sequence_number":3,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":4,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"#"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":5,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":" sum"}

            event: response.code_interpreter_call_code.delta
            data: {"type":"response.code_interpreter_call_code.delta","sequence_number":6,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","delta":"(range(1, 11))"}

            event: response.code_interpreter_call_code.done
            data: {"type":"response.code_interpreter_call_code.done","sequence_number":7,"output_index":0,"item_id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","code":"# sum(range(1, 11))"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","sequence_number":8,"output_index":0,"item":{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"completed","code":"# sum(range(1, 11))","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":9,"response":{"id":"resp_05d8f42f04f94cb80068fc3b7e07bc819eaf0d6e2c1923e564","object":"response","created_at":1761360766,"status":"completed","background":false,"error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[{"id":"ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3","type":"code_interpreter_call","status":"completed","code":"# sum(range(1, 11))","container_id":"cntr_68fc3b80043c8191990a5837d7617af704511ed77cec9447","outputs":null}],"parallel_tool_calls":true,"previous_response_id":null,"prompt_cache_key":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"code_interpreter","container":{"type":"auto"}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":{"input_tokens":100,"input_tokens_details":{"cached_tokens":0},"output_tokens":20,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":120},"user":null,"metadata":{}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Calculate the sum of numbers from 1 to 10 using Python", new()
        {
            Tools = [new HostedCodeInterpreterTool()],
        }))
        {
            updates.Add(update);
        }

        // Verify we got streaming updates with code interpreter call content (from deltas)
        var codeInterpreterCallUpdates = updates.Where(u =>
            u.Contents != null && u.Contents.Any(c => c is CodeInterpreterToolCallContent)).ToList();

        // Should have exactly 3 delta updates (one for each delta event)
        Assert.Equal(3, codeInterpreterCallUpdates.Count);

        // Verify the deltas have the expected call ID and concatenate the code
        StringBuilder codeBuilder = new();
        foreach (var update in codeInterpreterCallUpdates)
        {
            var content = update.Contents.OfType<CodeInterpreterToolCallContent>().First();
            Assert.Equal("ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3", content.CallId);

            // Concatenate the delta code
            if (content.Inputs is { Count: > 0 })
            {
                var dataContent = content.Inputs.OfType<DataContent>().FirstOrDefault();
                if (dataContent is not null)
                {
                    codeBuilder.Append(Encoding.UTF8.GetString(dataContent.Data.ToArray()));
                }
            }
        }

        // Verify the concatenated deltas match the expected code
        Assert.Equal("# sum(range(1, 11))", codeBuilder.ToString());

        // Verify we also got the CodeInterpreterToolResultContent from OutputItemDone
        var resultUpdates = updates.Where(u =>
            u.Contents != null && u.Contents.Any(c => c is CodeInterpreterToolResultContent)).ToList();
        Assert.Single(resultUpdates);

        // Verify that streaming deltas are properly coalesced when creating a ChatResponse
        var response = updates.ToChatResponse();
        Assert.Single(response.Messages);

        var message = response.Messages[0];
        Assert.Equal(2, message.Contents.Count);

        // First content should be the coalesced CodeInterpreterToolCallContent
        var codeCall = Assert.IsType<CodeInterpreterToolCallContent>(message.Contents[0]);
        Assert.Equal("ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3", codeCall.CallId);
        Assert.NotNull(codeCall.Inputs);
        var codeInput = Assert.IsType<DataContent>(Assert.Single(codeCall.Inputs));
        Assert.Equal("text/x-python", codeInput.MediaType);
        Assert.Equal("# sum(range(1, 11))", Encoding.UTF8.GetString(codeInput.Data.ToArray()));

        // Second content should be the CodeInterpreterToolResultContent
        var codeResult = Assert.IsType<CodeInterpreterToolResultContent>(message.Contents[1]);
        Assert.Equal("ci_05d8f42f04f94cb80068fc3b80fba8819ea3bfbdd36e94bcf3", codeResult.CallId);
    }

    [Fact]
    public async Task RequestHeaders_UserAgent_ContainsMEAI()
    {
        using var handler = new ThrowUserAgentExceptionHandler();
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        InvalidOperationException e = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("hello"));

        Assert.StartsWith("User-Agent header: OpenAI", e.Message);
        Assert.Contains("MEAI", e.Message);
    }

    [Fact]
    public async Task ConversationId_AsResponseId_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "previous_response_id":"resp_12345",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "resp_12345",
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("resp_67890", response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_AsConversationId_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "conversation":{"id":"conv_12345"},
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "conv_12345",
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("conv_12345", response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_WhenStoreDisabled_ReturnsNull_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "store":false,
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "store": false,
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                StoredOutputEnabled = false
            }
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Null(response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_ChatOptionsOverridesRawRepresentationResponseId_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "previous_response_id":"resp_override",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "resp_override",
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                PreviousResponseId = null
            }
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("resp_67890", response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_RawRepresentationPreviousResponseIdTakesPrecedence_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "previous_response_id":"resp_fromraw",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "conv_ignored",
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                PreviousResponseId = "resp_fromraw"
            }
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("resp_67890", response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_WhenStoreExplicitlyTrue_UsesResponseId_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "store":true,
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "store": true,
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                StoredOutputEnabled = true
            }
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("resp_67890", response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_WhenStoreExplicitlyTrue_UsesResponseId_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "store":true,
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                StoredOutputEnabled = true
            }
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ConversationId);
        }
    }

    [Fact]
    public async Task ConversationId_WhenStoreDisabled_ReturnsNull_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "store":false,
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            RawRepresentationFactory = (c) => new CreateResponseOptions
            {
                StoredOutputEnabled = false
            }
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Null(updates[i].ConversationId);
        }
    }

    [Fact]
    public async Task ConversationId_ReturnsNullWhenResponseStoreFieldFalse_NonStreaming()
    {
        // Simulates a provider that returns "store":false in the response
        // even though the request didn't explicitly set StoredOutputEnabled = false.
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "store": false,
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Null(response.ConversationId);
    }

    [Fact]
    public async Task ConversationId_ReturnsNullWhenResponseStoreFieldFalse_Streaming()
    {
        // Simulates a provider (e.g. OpenRouter) that returns "store":false in streamed response events
        // even though the request didn't explicitly set StoredOutputEnabled = false.
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":false,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Null(updates[i].ConversationId);
        }
    }

    [Fact]
    public async Task ConversationId_AsConversationId_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "conversation":{"id":"conv_12345"},
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "conv_12345",
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Equal("conv_12345", updates[i].ConversationId);
        }
    }

    [Fact]
    public async Task ConversationId_AsResponseId_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o-mini",
                "previous_response_id":"resp_12345",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-mini-2024-07-18","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "resp_12345",
        }))
        {
            updates.Add(update);
        }

        Assert.Equal("Hello!", string.Concat(updates.Select(u => u.Text)));

        for (int i = 0; i < updates.Count; i++)
        {
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ResponseId);
            Assert.Equal("resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77", updates[i].ConversationId);
        }
    }

    [Fact]
    public async Task ConversationId_RawRepresentationConversationIdTakesPrecedence_NonStreaming()
    {
        const string Input = """
        {
            "temperature":0.5,
            "model":"gpt-4o-mini",
            "conversation":{"id":"conv_12345"},
            "input": [{
                "type":"message",
                "role":"user",
                "content":[{"type":"input_text","text":"hello"}]
            }],
            "max_output_tokens":20
        }
        """;

        const string Output = """
        {
            "id": "resp_67890",
            "object": "response",
            "created_at": 1741891428,
            "status": "completed",
            "model": "gpt-4o-mini-2024-07-18",
            "output": [
            {
                "type": "message",
                "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                "status": "completed",
                "role": "assistant",
                "content": [
                {
                    "type": "output_text",
                    "text": "Hello! How can I assist you today?",
                    "annotations": []
                }
                ]
            }
            ]
        }
        """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ConversationId = "conv_ignored",
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                ConversationOptions = new("conv_12345"),
            }
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("conv_12345", response.ConversationId);
    }

    [Fact]
    public async Task ChatOptions_ModelId_OverridesClientModel_NonStreaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o",
                "input": [{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"hello"}]
                }],
                "max_output_tokens":20
            }
            """;

        const string Output = """
            {
              "id": "resp_67890",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-2024-08-06",
              "output": [
                {
                  "type": "message",
                  "id": "msg_67d32764fcdc8191bcf2e444d4088804058a5e08c46a181d",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello! How can I assist you today?",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            MaxOutputTokens = 20,
            Temperature = 0.5f,
            ModelId = "gpt-4o",
        });

        Assert.NotNull(response);
        Assert.Equal("resp_67890", response.ResponseId);
        Assert.Equal("gpt-4o-2024-08-06", response.ModelId);
        Assert.Equal("Hello! How can I assist you today?", response.Text);
    }

    [Fact]
    public async Task ChatOptions_ModelId_OverridesClientModel_Streaming()
    {
        const string Input = """
            {
                "temperature":0.5,
                "model":"gpt-4o",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"hello"}]
                    }
                ],
                "stream":true,
                "max_output_tokens":20
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"in_progress","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":null,"user":null,"metadata":{}}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"Hello"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"delta":"!"}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"text":"Hello!"}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","output_index":0,"content_index":0,"part":{"type":"output_text","text":"Hello!","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_67d329fbc87c81919f8952fe71dafc96029dabe3ee19bb77","object":"response","created_at":1741892091,"status":"completed","error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":20,"model":"gpt-4o-2024-08-06","output":[{"type":"message","id":"msg_67d329fc0c0081919696b8ab36713a41029dabe3ee19bb77","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!","annotations":[]}]}],"parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"generate_summary":null},"store":true,"temperature":0.5,"text":{"format":{"type":"text"}},"tool_choice":"auto","tools":[],"top_p":1.0,"usage":{"input_tokens":26,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":36},"user":null,"metadata":{}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

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

    [Fact]
    public async Task ToolCallResult_SingleTextContent_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_123",
                        "output":[{"type":"input_text","text":"Result text"}]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_001",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Done","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_123", new TextContent("Result text"))])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Done", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_MultipleTextContents_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_456",
                        "output":[
                            {"type":"input_text","text":"First part. "},
                            {"type":"input_text","text":"Second part."}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_002",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_002",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_456", new List<AIContent>
                {
                    new TextContent("First part. "),
                    new TextContent("Second part.")
                })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_DataContent_SerializesAsInputImage()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_789",
                        "output":[
                            {"type":"input_image","image_url":"data:image/png;base64,iVBORw0KGgo="}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_003",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_003",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Image processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var imageData = Convert.FromBase64String("iVBORw0KGgo=");
        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_789", new DataContent(imageData, "image/png"))
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Image processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_UriContent_SerializesAsInputImage()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_uri",
                        "output":[
                            {"type":"input_image","image_url":"https://example.com/image.png"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_004",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_004",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"URI processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_uri", new UriContent(new Uri("https://example.com/image.png"), "image/png"))
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("URI processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_HostedFileContent_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_file",
                        "output":[
                            {"type":"input_image","file_id":"file-abc123","filename":"result.png"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_005",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_005",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"File processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_file", new HostedFileContent("file-abc123") { MediaType = "image/png", Name = "result.png" })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("File processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_MixedContent_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_mixed",
                        "output":[
                            {"type":"input_text","text":"Text result: "},
                            {"type":"input_image","image_url":"https://example.com/result.png"},
                            {"type":"input_text","text":" - Image uploaded"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_006",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_006",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Mixed content processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_mixed", new List<AIContent>
                {
                    new TextContent("Text result: "),
                    new UriContent(new Uri("https://example.com/result.png"), "image/png"),
                    new TextContent(" - Image uploaded")
                })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Mixed content processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_DataContentPDF_SerializesAsInputFile()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_pdf",
                        "output":[
                            {"type":"input_file","file_data":"data:application/pdf;base64,cGRmZGF0YQ==","filename":"report.pdf"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_007",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_007",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"PDF processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var pdfData = Encoding.UTF8.GetBytes("pdfdata");
        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_pdf", new DataContent(pdfData, "application/pdf") { Name = "report.pdf" })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("PDF processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_ObjectSerialization_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_obj",
                        "output":"{\"name\":\"John\",\"age\":30}"
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_obj",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_obj",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Object processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_obj", new { name = "John", age = 30 })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Object processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_StringFallback_SerializesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_string",
                        "output":"Simple string result"
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_008",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_008",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"String processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_string", "Simple string result")
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("String processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_UriContentNonImage_SerializesAsInputFile()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_file_uri",
                        "output":[
                            {"type":"input_file","file_url":"https://example.com/document.pdf"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_009",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_009",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"File URI processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_file_uri", new UriContent(new Uri("https://example.com/document.pdf"), "application/pdf"))
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("File URI processed", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_HostedFileContentNonImage_SerializesAsInputFile()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[{"type":"input_text","text":"test"}]
                    },
                    {
                        "type":"function_call_output",
                        "call_id":"call_hosted_file",
                        "output":[
                            {"type":"input_file","file_id":"file-xyz789","filename":"document.txt"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_010",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_010",
                  "status":"completed",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"Hosted file processed","annotations":[]}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_hosted_file", new HostedFileContent("file-xyz789") { MediaType = "text/plain", Name = "document.txt" })
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Hosted file processed", response.Text);
    }

    [Fact]
    public async Task ResponseWithEndUserId_IncludesInAdditionalProperties()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}],
              "user":"user_123"
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("test");

        Assert.NotNull(response.AdditionalProperties);
        Assert.Equal("user_123", response.AdditionalProperties["EndUserId"]);
    }

    [Fact]
    public async Task ResponseWithError_IncludesInAdditionalPropertiesAndMessage()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"failed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Processing","annotations":[]}]}],
              "error":{"code":"rate_limit_exceeded","message":"Rate limit exceeded"}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("test");

        Assert.NotNull(response.AdditionalProperties);
        Assert.NotNull(response.AdditionalProperties["Error"]);

        var lastMessage = response.Messages.Last();
        var errorContent = lastMessage.Contents.OfType<ErrorContent>().FirstOrDefault();
        Assert.NotNull(errorContent);
        Assert.Equal("Rate limit exceeded", errorContent.Message);
        Assert.Equal("rate_limit_exceeded", errorContent.ErrorCode);
    }

    [Fact]
    public async Task ResponseWithUsageDetails_ParsesTokenCounts()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}],
              "usage":{
                "input_tokens":50,
                "input_tokens_details":{"cached_tokens":10},
                "output_tokens":25,
                "output_tokens_details":{"reasoning_tokens":5},
                "total_tokens":75
              }
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("test");

        Assert.NotNull(response.Usage);
        Assert.Null(response.Usage.AdditionalCounts);
        Assert.Equal(50, response.Usage.InputTokenCount);
        Assert.Equal(25, response.Usage.OutputTokenCount);
        Assert.Equal(75, response.Usage.TotalTokenCount);
        Assert.Equal(10, response.Usage.CachedInputTokenCount);
        Assert.Equal(5, response.Usage.ReasoningTokenCount);
    }

    [Fact]
    public async Task UserMessageWithVariousContentTypes_ConvertsCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[
                            {"type":"input_text","text":"Check this image: "},
                            {"type":"input_image","image_url":"https://example.com/image.png"},
                            {"type":"input_image","image_url":"https://example.com/image.png","detail":"high"},
                            {"type":"input_image","image_url":"data:image/png;base64,iVBORw0KGgo="},
                            {"type":"input_image","image_url":"data:image/png;base64,iVBORw0KGgo=","detail":"low"},
                            {"type":"input_file","file_data":"data:application/pdf;base64,cGRmZGF0YQ==","filename":"doc.pdf"},
                            {"type":"input_image","file_id":"file-img-456"},
                            {"type":"input_image","file_id":"file-img-789","detail":"high"},
                            {"type":"input_file","file_id":"file-123"},
                            {"type":"refusal","refusal":"I cannot process this"}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var imageData = Convert.FromBase64String("iVBORw0KGgo=");
        var pdfData = Convert.FromBase64String("cGRmZGF0YQ==");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, [
                new TextContent("Check this image: "),
                new UriContent(new Uri("https://example.com/image.png"), "image/png"),
                new UriContent(new Uri("https://example.com/image.png"), "image/png") { AdditionalProperties = new AdditionalPropertiesDictionary { ["detail"] = "high" }},
                new DataContent(imageData, "image/png"),
                new DataContent(imageData, "image/png") { AdditionalProperties = new AdditionalPropertiesDictionary { ["detail"] = ResponseImageDetailLevel.Low }},
                new DataContent(pdfData, "application/pdf") { Name = "doc.pdf" },
                new HostedFileContent("file-img-456") { MediaType = "image/png" },
                new HostedFileContent("file-img-789") { MediaType = "image/jpeg", AdditionalProperties = new AdditionalPropertiesDictionary { ["detail"] = "high" }},
                new HostedFileContent("file-123"),
                new ErrorContent("I cannot process this") { ErrorCode = "Refusal" }
            ])
        ]);

        Assert.NotNull(response);
        Assert.Equal("Done", response.Text);
    }

    [Fact]
    public async Task UserMessageWithInvalidImageDetail_OmitsDetail()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[
                    {
                        "type":"message",
                        "role":"user",
                        "content":[
                            {"type":"input_image","image_url":"data:image/png;base64,iVBORw0KGgo="}
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, [
                new DataContent(Convert.FromBase64String("iVBORw0KGgo="), "image/png")
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary { ["detail"] = 42 }
                }
            ])
        ]);

        Assert.Equal("Done", response.Text);
    }

    [Fact]
    public async Task NonStreamingResponseWithIncompleteReason_MapsFinishReason()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"incomplete",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Partial","annotations":[]}]}],
              "incomplete_details":{"reason":"max_output_tokens"}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("test");

        Assert.NotNull(response);
        Assert.Equal("Partial", response.Text);
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public async Task StreamingResponseWithQueuedUpdate_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.queued
            data: {"type":"response.queued","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"queued","model":"gpt-4o-mini","output":[]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}]}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 3);
        Assert.All(updates, u => Assert.Equal("resp_001", u.ResponseId));
    }

    [Fact]
    public async Task StreamingResponseWithFailedUpdate_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.failed
            data: {"type":"response.failed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"failed","model":"gpt-4o-mini","output":[],"error":{"code":"internal_error","message":"Internal error"}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 2);
        Assert.All(updates, u => Assert.Equal("resp_001", u.ResponseId));
    }

    [Fact]
    public async Task StreamingResponseWithIncompleteUpdate_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.incomplete
            data: {"type":"response.incomplete","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"incomplete","model":"gpt-4o-mini","output":[],"incomplete_details":{"reason":"max_output_tokens"}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 2);
        Assert.All(updates, u => Assert.Equal("resp_001", u.ResponseId));
    }

    [Fact]
    public async Task StreamingResponseWithInProgressUpdate_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Done","annotations":[]}]}]}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 3);
        Assert.All(updates, u => Assert.Equal("resp_001", u.ResponseId));
    }

    [Fact]
    public async Task StreamingResponseWithRefusalUpdate_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.refusal.done
            data: {"type":"response.refusal.done","refusal":"I cannot provide that information"}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[]}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        var refusalUpdate = updates.FirstOrDefault(u => u.Contents.Any(c => c is ErrorContent ec && ec.ErrorCode == "Refusal"));
        Assert.NotNull(refusalUpdate);

        var errorContent = refusalUpdate.Contents.OfType<ErrorContent>().First();
        Assert.Equal("I cannot provide that information", errorContent.Message);
        Assert.Equal("Refusal", errorContent.ErrorCode);
    }

    [Fact]
    public async Task GetContinuationToken_WithMessages_ThrowsException()
    {
        using HttpClient httpClient = new();
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var token = new TestOpenAIResponsesContinuationToken("resp_123");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "test")],
                new ChatOptions { ContinuationToken = token });
        });
    }

    [Fact]
    public async Task StreamingErrorUpdate_DocumentedFormat_ParsesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: error
            data: {"type":"error","sequence_number":1,"message":"Rate limit exceeded","code":"rate_limit_exceeded","param":"requests"}

            event: response.failed
            data: {"type":"response.failed","sequence_number":2,"response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"failed","model":"gpt-4o-mini","output":[],"error":{"code":"rate_limit_exceeded","message":"Rate limit exceeded"}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        var errorUpdate = updates.FirstOrDefault(u => u.Contents.Any(c => c is ErrorContent));
        Assert.NotNull(errorUpdate);

        var errorContent = errorUpdate.Contents.OfType<ErrorContent>().First();
        Assert.Equal("Rate limit exceeded", errorContent.Message);
        Assert.Equal("rate_limit_exceeded", errorContent.ErrorCode);
        Assert.Equal("requests", errorContent.Details);
    }

    [Fact]
    public async Task StreamingErrorUpdate_ActualErroneousFormat_ParsesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_002","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: error
            data: {"type":"error","sequence_number":1,"error":{"message":"Content filter triggered","code":"content_filter","param":"safety"}}

            event: response.failed
            data: {"type":"response.failed","sequence_number":2,"response":{"id":"resp_002","object":"response","created_at":1741892091,"status":"failed","model":"gpt-4o-mini","output":[],"error":{"code":"content_filter","message":"Content filter triggered"}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        var errorUpdate = updates.FirstOrDefault(u => u.Contents.Any(c => c is ErrorContent));
        Assert.NotNull(errorUpdate);

        var errorContent = errorUpdate.Contents.OfType<ErrorContent>().First();
        Assert.Equal("Content filter triggered", errorContent.Message);
        Assert.Equal("content_filter", errorContent.ErrorCode);
        Assert.Equal("safety", errorContent.Details);
    }

    [Fact]
    public async Task StreamingErrorUpdate_NoErrorInformation_HandlesGracefully()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_003","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: error
            data: {"type":"error","sequence_number":1}

            event: response.failed
            data: {"type":"response.failed","sequence_number":2,"response":{"id":"resp_003","object":"response","created_at":1741892091,"status":"failed","model":"gpt-4o-mini","output":[]}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        var errorUpdate = updates.FirstOrDefault(u => u.Contents.Any(c => c is ErrorContent));
        Assert.NotNull(errorUpdate);

        var errorContent = errorUpdate.Contents.OfType<ErrorContent>().First();
        Assert.True(string.IsNullOrEmpty(errorContent.Message));
        Assert.True(string.IsNullOrEmpty(errorContent.ErrorCode));
        Assert.True(string.IsNullOrEmpty(errorContent.Details));
    }

    [Fact]
    public async Task StreamingResponseWithAnnotations_HandlesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","response_id":"resp_001","output_index":0,"item":{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Annotated text","annotations":[{"type":"file_citation","file_id":"file_123","start_index":0,"end_index":14}]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Annotated text","annotations":[{"type":"file_citation","file_id":"file_123","start_index":0,"end_index":14}]}]}]}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        var annotatedUpdate = updates.FirstOrDefault(u => u.Contents.Any(c => c.Annotations?.Count > 0));
        Assert.NotNull(annotatedUpdate);
        Assert.NotEmpty(annotatedUpdate.Contents.First().Annotations!);
    }

    [Fact]
    public async Task StreamingResponseWithFunctionCallOutput_YieldsFunctionResultContent()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"test"}]}],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"function_call","id":"fc_001","call_id":"call_123","name":"get_weather","arguments":"","status":"in_progress"}}

            event: response.function_call_arguments.delta
            data: {"type":"response.function_call_arguments.delta","item_id":"fc_001","output_index":0,"delta":"{\"city\":\"Paris\"}"}

            event: response.function_call_arguments.done
            data: {"type":"response.function_call_arguments.done","item_id":"fc_001","output_index":0,"arguments":"{\"city\":\"Paris\"}"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"function_call","id":"fc_001","call_id":"call_123","name":"get_weather","arguments":"{\"city\":\"Paris\"}","status":"completed"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":1,"item":{"type":"function_call_output","id":"fc_output_001","call_id":"call_123","output":"25°C and sunny"}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":1,"item":{"type":"function_call_output","id":"fc_output_001","call_id":"call_123","output":"25°C and sunny"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":2,"item":{"type":"message","id":"msg_001","status":"in_progress","role":"assistant","content":[]}}

            event: response.content_part.added
            data: {"type":"response.content_part.added","item_id":"msg_001","output_index":2,"content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","item_id":"msg_001","output_index":2,"content_index":0,"delta":"It's 25°C and sunny in Paris."}

            event: response.output_text.done
            data: {"type":"response.output_text.done","item_id":"msg_001","output_index":2,"content_index":0,"text":"It's 25°C and sunny in Paris."}

            event: response.content_part.done
            data: {"type":"response.content_part.done","item_id":"msg_001","output_index":2,"content_index":0,"part":{"type":"output_text","text":"It's 25°C and sunny in Paris.","annotations":[]}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":2,"item":{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"It's 25°C and sunny in Paris.","annotations":[]}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_001","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[{"type":"function_call","id":"fc_001","call_id":"call_123","name":"get_weather","arguments":"{\"city\":\"Paris\"}","status":"completed"},{"type":"function_call_output","id":"fc_output_001","call_id":"call_123","output":"25°C and sunny"},{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"It's 25°C and sunny in Paris.","annotations":[]}]}],"usage":{"input_tokens":10,"output_tokens":15,"total_tokens":25}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("test"))
        {
            updates.Add(update);
        }

        // Verify FunctionCallContent is produced from the function_call item
        var functionCallUpdate = updates.FirstOrDefault(u => u.Contents.OfType<FunctionCallContent>().Any());
        Assert.NotNull(functionCallUpdate);
        Assert.Equal(ChatRole.Assistant, functionCallUpdate.Role);
        var fcc = functionCallUpdate.Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("call_123", fcc.CallId);
        Assert.Equal("get_weather", fcc.Name);

        // Verify FunctionResultContent is produced from the function_call_output item
        var functionResultUpdate = updates.FirstOrDefault(u => u.Contents.OfType<FunctionResultContent>().Any());
        Assert.NotNull(functionResultUpdate);
        Assert.Equal(ChatRole.Assistant, functionResultUpdate.Role);
        var frc = functionResultUpdate.Contents.OfType<FunctionResultContent>().Single();
        Assert.Equal("call_123", frc.CallId);
        Assert.Equal("25°C and sunny", frc.Result);
        Assert.IsType<FunctionCallOutputResponseItem>(frc.RawRepresentation);

        // Verify the text message is also present
        Assert.Equal("It's 25°C and sunny in Paris.", string.Concat(updates.Select(u => u.Text)));

        // Verify ToChatResponse coalesces updates correctly
        var response = updates.ToChatResponse();
        Assert.Equal("resp_001", response.ResponseId);
        Assert.Equal("gpt-4o-mini", response.ModelId);
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);

        // Message should contain: FunctionCallContent, FunctionResultContent, and TextContent
        Assert.Single(message.Contents.OfType<FunctionCallContent>());
        Assert.Single(message.Contents.OfType<FunctionResultContent>());
        var textContent = Assert.Single(message.Contents.OfType<TextContent>());
        Assert.Equal("It's 25°C and sunny in Paris.", textContent.Text);

        // Verify usage
        UsageContent usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single();
        Assert.Equal(10, usage.Details.InputTokenCount);
        Assert.Equal(15, usage.Details.OutputTokenCount);
        Assert.Equal(25, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task UserMessageWithEmptyText_CreatesEmptyInputPart()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":""}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[{"type":"message","id":"msg_001","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Ok","annotations":[]}]}]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "")]);

        Assert.NotNull(response);
        Assert.Equal("Ok", response.Text);
    }

    [Fact]
    public async Task ResponseWithRefusalContent_ParsesCorrectly()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"harmful request"}]}]
            }
            """;

        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_001",
                  "status":"completed",
                  "role":"assistant",
                  "content":[
                    {"type":"refusal","refusal":"I cannot help with that request"}
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("harmful request");

        Assert.NotNull(response);
        var errorContent = response.Messages.Last().Contents.OfType<ErrorContent>().FirstOrDefault();
        Assert.NotNull(errorContent);
        Assert.Equal("I cannot help with that request", errorContent.Message);
        Assert.Equal("Refusal", errorContent.ErrorCode);
    }

    [Fact]
    public async Task ResponseWithInputImageHttpUrl_ParsesAsUriContent()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"What is in this image?"}]}]
            }
            """;

        // The output includes a message with input_image content that has an image_url property with HTTP URL.
        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_001",
                  "status":"completed",
                  "role":"user",
                  "content":[
                    {"type":"input_image","image_url":"https://example.com/image.png"}
                  ]
                },
                {
                  "type":"message",
                  "id":"msg_002",
                  "status":"completed",
                  "role":"assistant",
                  "content":[
                    {"type":"output_text","text":"This is a cat.","annotations":[]}
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("What is in this image?");

        Assert.NotNull(response);

        var userMessage = response.Messages.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.NotNull(userMessage);

        // HTTP URL should be returned as UriContent
        var imageContent = userMessage.Contents.OfType<UriContent>().FirstOrDefault();
        Assert.NotNull(imageContent);
        Assert.Equal("https://example.com/image.png", imageContent.Uri.ToString());
        Assert.Equal("image/png", imageContent.MediaType);

        var assistantMessage = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        Assert.NotNull(assistantMessage);
        Assert.Equal("This is a cat.", assistantMessage.Text);
    }

    [Fact]
    public async Task ResponseWithInputImageDataUri_ParsesAsDataContent()
    {
        const string Input = """
            {
                "model":"gpt-4o-mini",
                "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"What is in this image?"}]}]
            }
            """;

        // The output includes a message with input_image content that has an image_url property with a data URI.
        const string Output = """
            {
              "id":"resp_001",
              "object":"response",
              "created_at":1741892091,
              "status":"completed",
              "model":"gpt-4o-mini",
              "output":[
                {
                  "type":"message",
                  "id":"msg_001",
                  "status":"completed",
                  "role":"user",
                  "content":[
                    {"type":"input_image","image_url":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}
                  ]
                },
                {
                  "type":"message",
                  "id":"msg_002",
                  "status":"completed",
                  "role":"assistant",
                  "content":[
                    {"type":"output_text","text":"This is a red pixel.","annotations":[]}
                  ]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("What is in this image?");

        Assert.NotNull(response);

        var userMessage = response.Messages.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.NotNull(userMessage);

        // Data URI should be returned as DataContent
        var imageContent = userMessage.Contents.OfType<DataContent>().FirstOrDefault();
        Assert.NotNull(imageContent);
        Assert.Equal("image/png", imageContent.MediaType);
        Assert.True(imageContent.Data.Length > 0);

        var assistantMessage = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        Assert.NotNull(assistantMessage);
        Assert.Equal("This is a red pixel.", assistantMessage.Text);
    }

    [Fact]
    public async Task HostedImageGenerationTool_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "size": "1024x1024",
                        "output_format": "png"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a cat"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_abc123",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-2024-11-20",
              "output": [
                {
                  "type": "image_generation_call",
                  "id": "img_call_abc123",
                  "result": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
                }
              ],
              "usage": {
                "input_tokens": 15,
                "output_tokens": 0,
                "total_tokens": 15
              }
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o");

        var imageTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                ImageSize = new(1024, 1024),
                MediaType = "image/png"
            }
        };
        var response = await client.GetResponseAsync("Generate an image of a cat", new ChatOptions
        {
            Tools = [imageTool]
        });

        Assert.NotNull(response);
        Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);

        var contents = response.Messages[0].Contents;
        Assert.Equal(2, contents.Count);

        // First content should be the tool call
        var toolCall = contents[0] as ImageGenerationToolCallContent;
        Assert.NotNull(toolCall);
        Assert.Equal("img_call_abc123", toolCall.CallId);

        // Second content should be the result with image data
        var toolResult = contents[1] as ImageGenerationToolResultContent;
        Assert.NotNull(toolResult);
        Assert.Equal("img_call_abc123", toolResult.CallId);
        Assert.Single(toolResult.Outputs!);

        var imageData = toolResult.Outputs![0] as DataContent;
        Assert.NotNull(imageData);
        Assert.Equal("image/png", imageData.MediaType);
        Assert.True(imageData.Data.Length > 0);
    }

    [Fact]
    public async Task HostedImageGenerationTool_Streaming()
    {
        const string Input = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "size": "1024x1024",
                        "output_format": "png"
                    }
                ],
                "stream": true,
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a dog"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_def456","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[],"tools":[{"type":"image_generation","image_generation":{"model":"gpt-image-1","size":{"width":1024,"height":1024},"output_format":"png"}}]}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_def456","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"image_generation_call","id":"img_call_def456","status":"in_progress"}}

            event: response.image_generation_call.in_progress
            data: {"type":"response.image_generation_call.in_progress","item_id":"img_call_def456","output_index":0}

            event: response.image_generation_call.generating
            data: {"type":"response.image_generation_call.generating","item_id":"img_call_def456","output_index":0}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_def456","output_index":0,"partial_image_index":0,"partial_image_b64":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"image_generation_call","id":"img_call_def456","result":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_def456","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-2024-11-20","output":[{"type":"image_generation_call","id":"img_call_def456","result":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}],"usage":{"input_tokens":15,"output_tokens":0,"total_tokens":15}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o");

        List<ChatResponseUpdate> updates = [];
        var imageTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                ImageSize = new(1024, 1024),
                MediaType = "image/png"
            }
        };
        await foreach (var update in client.GetStreamingResponseAsync("Generate an image of a dog", new ChatOptions
        {
            Tools = [imageTool]
        }))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 6);

        // Should have updates for: created, in_progress, tool call start, generating, partial image, completion
        var createdUpdate = updates.First(u => u.CreatedAt.HasValue);
        Assert.Equal("resp_def456", createdUpdate.ResponseId);
        Assert.Equal("gpt-4o-2024-11-20", createdUpdate.ModelId);

        // Should have tool call content
        var toolCallUpdate = updates.FirstOrDefault(u =>
            u.Contents != null && u.Contents.Any(c => c is ImageGenerationToolCallContent));
        Assert.NotNull(toolCallUpdate);
        var toolCall = toolCallUpdate.Contents.OfType<ImageGenerationToolCallContent>().First();
        Assert.Equal("img_call_def456", toolCall.CallId);

        // Should have partial image content
        var partialImageUpdate = updates.FirstOrDefault(u =>
            u.Contents != null && u.Contents.Any(c => c is ImageGenerationToolResultContent result &&
                result.Outputs != null && result.Outputs.Any(o => o.AdditionalProperties != null && o.AdditionalProperties.ContainsKey("PartialImageIndex"))));
        Assert.NotNull(partialImageUpdate);

        // Should have final completion with usage
        var completionUpdate = updates.FirstOrDefault(u =>
            u.Contents != null && u.Contents.Any(c => c is UsageContent));
        Assert.NotNull(completionUpdate);
        var usage = completionUpdate.Contents.OfType<UsageContent>().First();
        Assert.Equal(15, usage.Details.InputTokenCount);
        Assert.Equal(0, usage.Details.OutputTokenCount);
        Assert.Equal(15, usage.Details.TotalTokenCount);
    }

    [Fact]
    public async Task HostedImageGenerationTool_StreamingMultipleImages()
    {
        const string Input = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "size": "512x512",
                        "output_format": "webp",
                        "partial_images": 3
                    }
                ],
                "stream": true,
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a sunset"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_ghi789","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[],"tools":[{"type":"image_generation","image_generation":{"model":"gpt-image-1","size":{"width":512,"height":512},"output_format":"webp","partial_images":3}}]}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_ghi789","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"image_generation_call","id":"img_call_ghi789","status":"in_progress"}}

            event: response.image_generation_call.in_progress
            data: {"type":"response.image_generation_call.in_progress","item_id":"img_call_ghi789","output_index":0}

            event: response.image_generation_call.generating
            data: {"type":"response.image_generation_call.generating","item_id":"img_call_ghi789","output_index":0}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_ghi789","output_index":0,"partial_image_index":0,"partial_image_b64":"SGVsbG8x"}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_ghi789","output_index":0,"partial_image_index":1,"partial_image_b64":"SGVsbG8y"}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_ghi789","output_index":0,"partial_image_index":2,"partial_image_b64":"SGVsbG8z"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"image_generation_call","id":"img_call_ghi789","result":"SGVsbG8z"}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_ghi789","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-2024-11-20","output":[{"type":"image_generation_call","id":"img_call_ghi789","result":"SGVsbG8z"}],"usage":{"input_tokens":18,"output_tokens":0,"total_tokens":18}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o");

        List<ChatResponseUpdate> updates = [];
        var imageTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                ImageSize = new(512, 512),
                MediaType = "image/webp",
                StreamingCount = 3
            }
        };
        await foreach (var update in client.GetStreamingResponseAsync("Generate an image of a sunset", new ChatOptions
        {
            Tools = [imageTool]
        }))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 8); // Should have multiple partial image updates plus generating event

        // Should have multiple partial images with different indices
        var partialImageUpdates = updates.Where(u =>
            u.Contents != null && u.Contents.Any(c => c is ImageGenerationToolResultContent result &&
                result.Outputs != null && result.Outputs.Any(o => o.AdditionalProperties != null && o.AdditionalProperties.ContainsKey("PartialImageIndex")))).ToList();

        Assert.True(partialImageUpdates.Count >= 3);

        // Verify partial images have correct indices and WebP format
        for (int i = 0; i < 3; i++)
        {
            var partialUpdate = partialImageUpdates.FirstOrDefault(u =>
                u.Contents.OfType<ImageGenerationToolResultContent>().Any(result =>
                    HasPartialImageWithIndex(result, i)));
            Assert.NotNull(partialUpdate);
        }

        static bool HasPartialImageWithIndex(ImageGenerationToolResultContent result, int index)
        {
            if (result.Outputs == null)
            {
                return false;
            }

            return result.Outputs.Any(o => HasCorrectImageData(o, index));
        }

        static bool HasCorrectImageData(AIContent o, int index)
        {
            if (o.AdditionalProperties == null)
            {
                return false;
            }

            if (!o.AdditionalProperties.TryGetValue("PartialImageIndex", out var imageIndex))
            {
                return false;
            }

            if (imageIndex == null || !imageIndex.Equals(index))
            {
                return false;
            }

            return o is DataContent dataContent && dataContent.MediaType == "image/webp";
        }

        // Verify tool call uses correct settings
        var toolCallUpdate = updates.FirstOrDefault(u =>
            u.Contents != null && u.Contents.Any(c => c is ImageGenerationToolCallContent));
        Assert.NotNull(toolCallUpdate);
        var toolCall = toolCallUpdate.Contents.OfType<ImageGenerationToolCallContent>().First();
        Assert.Equal("img_call_ghi789", toolCall.CallId);
    }

    [Fact]
    public async Task HostedImageGenerationTool_Streaming_YieldsFinalImageAfterPartialImages()
    {
        const string Input = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "size": "1024x1024",
                        "output_format": "png",
                        "partial_images": 3
                    }
                ],
                "stream": true,
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a lake"
                            }
                        ]
                    }
                ]
            }
            """;

        // Partial images are "Partial1", "Partial2" and "Partial3"; the final image is "Final".
        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_final1","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"image_generation_call","id":"img_call_final1","status":"in_progress"}}

            event: response.image_generation_call.in_progress
            data: {"type":"response.image_generation_call.in_progress","item_id":"img_call_final1","output_index":0}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_final1","output_index":0,"partial_image_index":0,"partial_image_b64":"UGFydGlhbDE="}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_final1","output_index":0,"partial_image_index":1,"partial_image_b64":"UGFydGlhbDI="}

            event: response.image_generation_call.partial_image
            data: {"type":"response.image_generation_call.partial_image","item_id":"img_call_final1","output_index":0,"partial_image_index":2,"partial_image_b64":"UGFydGlhbDM="}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"image_generation_call","id":"img_call_final1","status":"completed","result":"RmluYWw="}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_final1","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-2024-11-20","output":[{"type":"image_generation_call","id":"img_call_final1","status":"completed","result":"RmluYWw="}],"usage":{"input_tokens":15,"output_tokens":0,"total_tokens":15}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o");

        var imageTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                ImageSize = new(1024, 1024),
                MediaType = "image/png",
                StreamingCount = 3
            }
        };

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Generate an image of a lake", new ChatOptions
        {
            Tools = [imageTool]
        }))
        {
            updates.Add(update);
        }

        // The three partial images and the final image should each be yielded, in that order.
        var results = updates.SelectMany(u => u.Contents).OfType<ImageGenerationToolResultContent>().ToList();
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Equal("img_call_final1", r.CallId));
        for (int i = 0; i < 3; i++)
        {
            var partialImage = Assert.IsType<DataContent>(Assert.Single(results[i].Outputs!));
            Assert.Equal($"Partial{i + 1}", Encoding.UTF8.GetString(partialImage.Data.ToArray()));
            Assert.Equal(i, partialImage.AdditionalProperties?["PartialImageIndex"]);
        }

        // The final image comes from the completed ImageGenerationCallResponseItem, not from a partial image.
        var finalResult = results[3];
        Assert.IsType<ImageGenerationCallResponseItem>(finalResult.RawRepresentation);
        var finalImage = Assert.IsType<DataContent>(Assert.Single(finalResult.Outputs!));
        Assert.Equal("image/png", finalImage.MediaType);
        Assert.Equal("Final", Encoding.UTF8.GetString(finalImage.Data.ToArray()));
        Assert.False(finalImage.AdditionalProperties?.ContainsKey("PartialImageIndex") ?? false);

        // Coalescing into a ChatResponse should keep only the final image.
        var response = updates.ToChatResponse();
        var coalescedResult = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<ImageGenerationToolResultContent>());
        Assert.Equal("Final", Encoding.UTF8.GetString(Assert.IsType<DataContent>(Assert.Single(coalescedResult.Outputs!)).Data.ToArray()));
    }

    [Fact]
    public async Task HostedImageGenerationTool_Streaming_WithoutPartialImages_YieldsFinalImage()
    {
        const string Input = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "output_format": "jpeg"
                    }
                ],
                "stream": true,
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a tree"
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_final2","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"image_generation_call","id":"img_call_final2","status":"in_progress"}}

            event: response.image_generation_call.in_progress
            data: {"type":"response.image_generation_call.in_progress","item_id":"img_call_final2","output_index":0}

            event: response.image_generation_call.generating
            data: {"type":"response.image_generation_call.generating","item_id":"img_call_final2","output_index":0}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"image_generation_call","id":"img_call_final2","status":"completed","result":"RmluYWw="}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_final2","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-2024-11-20","output":[{"type":"image_generation_call","id":"img_call_final2","status":"completed","result":"RmluYWw="}],"usage":{"input_tokens":15,"output_tokens":0,"total_tokens":15}}}


            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o");

        var imageTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                MediaType = "image/jpeg"
            }
        };

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("Generate an image of a tree", new ChatOptions
        {
            Tools = [imageTool]
        }))
        {
            updates.Add(update);
        }

        // Exactly one image result should be yielded: the final image.
        var streamedResult = Assert.Single(updates.SelectMany(u => u.Contents).OfType<ImageGenerationToolResultContent>());
        Assert.IsType<ImageGenerationCallResponseItem>(streamedResult.RawRepresentation);

        var response = updates.ToChatResponse();
        var contents = response.Messages.SelectMany(m => m.Contents).ToList();

        var toolCall = Assert.Single(contents.OfType<ImageGenerationToolCallContent>());
        Assert.Equal("img_call_final2", toolCall.CallId);

        var toolResult = Assert.Single(contents.OfType<ImageGenerationToolResultContent>());
        Assert.Equal("img_call_final2", toolResult.CallId);
        var image = Assert.IsType<DataContent>(Assert.Single(toolResult.Outputs!));
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal("Final", Encoding.UTF8.GetString(image.Data.ToArray()));
    }

    [Theory]
    [InlineData("""{"type":"image_generation_call","id":"img_call_same1","status":"completed","result":"RmluYWw="}""")]
    [InlineData("""{"type":"image_generation_call","id":"img_call_same1","status":"failed"}""")]
    public async Task HostedImageGenerationTool_StreamingAndNonStreaming_ProduceSameImageContents(string imageGenerationCall)
    {
        const string NonStreamingInput = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "output_format": "png"
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a house"
                            }
                        ]
                    }
                ]
            }
            """;

        string nonStreamingOutput = $$"""
            {
              "id": "resp_same1",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-2024-11-20",
              "output": [{{imageGenerationCall}}],
              "usage": {
                "input_tokens": 15,
                "output_tokens": 0,
                "total_tokens": 15
              }
            }
            """;

        const string StreamingInput = """
            {
                "model": "gpt-4o",
                "tools": [
                    {
                        "type": "image_generation",
                        "model": "gpt-image-1",
                        "output_format": "png"
                    }
                ],
                "stream": true,
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "Generate an image of a house"
                            }
                        ]
                    }
                ]
            }
            """;

        string streamingOutput = $$$$"""
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_same1","object":"response","created_at":1741891428,"status":"in_progress","model":"gpt-4o-2024-11-20","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"image_generation_call","id":"img_call_same1","status":"in_progress"}}

            event: response.image_generation_call.in_progress
            data: {"type":"response.image_generation_call.in_progress","item_id":"img_call_same1","output_index":0}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{{{{imageGenerationCall}}}}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_same1","object":"response","created_at":1741891428,"status":"completed","model":"gpt-4o-2024-11-20","output":[{{{{imageGenerationCall}}}}],"usage":{"input_tokens":15,"output_tokens":0,"total_tokens":15}}}


            """;

        static ChatOptions CreateOptions() => new()
        {
            Tools = [new HostedImageGenerationTool { Options = new ImageGenerationOptions { ModelId = "gpt-image-1", MediaType = "image/png" } }]
        };

        ChatResponse nonStreamingResponse;
        using (VerbatimHttpHandler handler = new(NonStreamingInput, nonStreamingOutput))
        using (HttpClient httpClient = new(handler))
        using (IChatClient client = CreateResponseClient(httpClient, "gpt-4o"))
        {
            nonStreamingResponse = await client.GetResponseAsync("Generate an image of a house", CreateOptions());
        }

        ChatResponse streamingResponse;
        using (VerbatimHttpHandler handler = new(StreamingInput, streamingOutput))
        using (HttpClient httpClient = new(handler))
        using (IChatClient client = CreateResponseClient(httpClient, "gpt-4o"))
        {
            streamingResponse = await client.GetStreamingResponseAsync("Generate an image of a house", CreateOptions()).ToChatResponseAsync();
        }

        var nonStreamingContents = nonStreamingResponse.Messages.SelectMany(m => m.Contents)
            .Where(c => c is ImageGenerationToolCallContent or ImageGenerationToolResultContent).ToList();
        var streamingContents = streamingResponse.Messages.SelectMany(m => m.Contents)
            .Where(c => c is ImageGenerationToolCallContent or ImageGenerationToolResultContent).ToList();

        // Both paths should produce a tool call followed by a tool result carrying the same final image,
        // including when the completed item carries no image bytes.
        Assert.Equal(nonStreamingContents.Select(c => c.GetType()), streamingContents.Select(c => c.GetType()));
        Assert.Equal(
            nonStreamingContents.OfType<ImageGenerationToolCallContent>().Single().CallId,
            streamingContents.OfType<ImageGenerationToolCallContent>().Single().CallId);

        var nonStreamingResult = nonStreamingContents.OfType<ImageGenerationToolResultContent>().Single();
        var streamingResult = streamingContents.OfType<ImageGenerationToolResultContent>().Single();
        Assert.Equal(nonStreamingResult.CallId, streamingResult.CallId);
        Assert.IsType<ImageGenerationCallResponseItem>(nonStreamingResult.RawRepresentation);
        Assert.IsType<ImageGenerationCallResponseItem>(streamingResult.RawRepresentation);

        var nonStreamingImage = Assert.IsType<DataContent>(Assert.Single(nonStreamingResult.Outputs!));
        var streamingImage = Assert.IsType<DataContent>(Assert.Single(streamingResult.Outputs!));
        Assert.Equal(nonStreamingImage.MediaType, streamingImage.MediaType);
        Assert.Equal(nonStreamingImage.Data.ToArray(), streamingImage.Data.ToArray());
    }

    [Theory]
    [InlineData(ReasoningEffort.None, ReasoningOutput.Summary, "none", "concise")]
    [InlineData(ReasoningEffort.Low, ReasoningOutput.Summary, "low", "concise")]
    [InlineData(ReasoningEffort.Medium, ReasoningOutput.Full, "medium", "detailed")]
    [InlineData(ReasoningEffort.High, ReasoningOutput.Summary, "high", "concise")]
    [InlineData(ReasoningEffort.ExtraHigh, ReasoningOutput.Full, "xhigh", "detailed")]
    public async Task ReasoningOptions_EffortAndOutput_ProducesExpectedJson(
        ReasoningEffort effort,
        ReasoningOutput output,
        string expectedEffortString,
        string expectedSummaryString)
    {
        string input = $$"""
            {
                "model": "o4-mini",
                "input": [{
                    "type": "message",
                    "role": "user",
                    "content": [{"type": "input_text", "text": "hello"}]
                }],
                "reasoning": {
                    "effort": "{{expectedEffortString}}",
                    "summary": "{{expectedSummaryString}}"
                }
            }
            """;

        const string Output = """
            {
              "id": "resp_test",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "o4-mini",
              "output": [
                {
                  "id": "msg_test",
                  "type": "message",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!"}]
                }
              ],
              "usage": {
                "input_tokens": 10,
                "output_tokens": 5,
                "total_tokens": 15
              }
            }
            """;

        using VerbatimHttpHandler handler = new(input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "o4-mini");

        Assert.NotNull(await client.GetResponseAsync("hello", new()
        {
            Reasoning = new ReasoningOptions { Effort = effort, Output = output }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAIApiTypeTag_SetToResponses(bool streaming)
    {
        const string NonStreamingOutput = """
            {
              "id": "resp_test",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini",
              "service_tier": "default",
              "output": [
                {
                  "id": "msg_test",
                  "type": "message",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello!"
                    }
                  ]
                }
              ],
              "usage": {
                "input_tokens": 8,
                "output_tokens": 2,
                "total_tokens": 10
              }
            }
            """;

        const string StreamingOutput = """
            event: response.created
            data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_test","object":"response","created_at":1741891428,"status":"in_progress","model":"gpt-4o-mini","service_tier":"default","output":[]}}

            event: response.completed
            data: {"type":"response.completed","sequence_number":1,"response":{"id":"resp_test","object":"response","created_at":1741891428,"status":"completed","model":"gpt-4o-mini","service_tier":"default","output":[{"id":"msg_test","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hello!"}]}],"usage":{"input_tokens":8,"output_tokens":2,"total_tokens":10}}}

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
            .GetResponsesClient()
            .AsIChatClient("gpt-4o-mini")
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
        Assert.Equal("responses", activity.GetTagItem("openai.api.type"));
        Assert.Equal("default", activity.GetTagItem("openai.response.service_tier"));
    }

    [Fact]
    public async Task ApplyPatchTool_NonStreaming()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"Create a hello world file"}]
                }],
                "tools":[{"type":"apply_patch"}]
            }
            """;

        const string Output = """
            {
              "id": "resp_patch_001",
              "object": "response",
              "created_at": 1770553457,
              "status": "completed",
              "model": "gpt-4o-2024-08-06",
              "output": [
                {
                  "type": "apply_patch_call",
                  "id": "ap_001",
                  "call_id": "call_patch_001",
                  "status": "completed",
                  "patch": "*** hello.txt\n+++ hello.txt\n@@ -0,0 +1 @@\n+Hello World"
                },
                {
                  "type": "apply_patch_call_output",
                  "id": "apo_001",
                  "call_id": "call_patch_001",
                  "status": "completed",
                  "output": "Applied patch successfully."
                }
              ],
              "usage": {
                "input_tokens": 100,
                "input_tokens_details": { "cached_tokens": 0 },
                "output_tokens": 50,
                "output_tokens_details": { "reasoning_tokens": 0 },
                "total_tokens": 150
              },
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        var response = await client.GetResponseAsync("Create a hello world file", new()
        {
            Tools = [new ApplyPatchTool().AsAITool()],
        });

        Assert.NotNull(response);
        var message = Assert.Single(response.Messages);

        // ApplyPatchCallItem should produce a ToolCallContent
        var toolCall = Assert.Single(message.Contents.OfType<ToolCallContent>());
        Assert.Equal("call_patch_001", toolCall.CallId);
        Assert.IsType<ApplyPatchCallItem>(toolCall.RawRepresentation);

        // ApplyPatchCallOutputItem should produce a ToolResultContent
        var toolResult = Assert.Single(message.Contents.OfType<ToolResultContent>());
        Assert.Equal("call_patch_001", toolResult.CallId);
        Assert.IsType<ApplyPatchCallOutputItem>(toolResult.RawRepresentation);
    }

    private static IChatClient CreateResponseClient(HttpClient httpClient, string modelId) =>
        new OpenAIClient(
            new ApiKeyCredential("apikey"),
            new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) })
        .GetResponsesClient()
        .AsIChatClient(modelId);

    // Returns the supplied responses in order, one per request, capturing each request body.
    private sealed class CapturingHttpHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
#pragma warning disable CA2016 // ReadAsStringAsync(CancellationToken) is unavailable on all target frameworks
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
#pragma warning restore CA2016
            string body = responses[Math.Min(_index++, responses.Count - 1)];
            return new HttpResponseMessage { Content = new StringContent(body) };
        }
    }

    private static string ResponseStatusToRequestValue(ResponseStatus status)
    {
        if (status == ResponseStatus.InProgress)
        {
            return "in_progress";
        }

        return status.ToString().ToLowerInvariant();
    }

    private sealed class TestOpenAIResponsesContinuationToken : ResponseContinuationToken
    {
        internal TestOpenAIResponsesContinuationToken(string responseId)
        {
            ResponseId = responseId;
        }

        /// <summary>Gets or sets the Id of the response.</summary>
        internal string ResponseId { get; set; }

        /// <summary>Gets or sets the sequence number of a streamed update.</summary>
        internal int? SequenceNumber { get; set; }

        internal static TestOpenAIResponsesContinuationToken FromToken(ResponseContinuationToken token)
        {
            if (token is TestOpenAIResponsesContinuationToken testOpenAIResponsesContinuationToken)
            {
                return testOpenAIResponsesContinuationToken;
            }

            ReadOnlyMemory<byte> data = token.ToBytes();

            Utf8JsonReader reader = new(data.Span);

            string responseId = null!;
            int? startAfter = null;

            _ = reader.Read();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                string propertyName = reader.GetString()!;

                switch (propertyName)
                {
                    case "responseId":
                        _ = reader.Read();
                        responseId = reader.GetString()!;
                        break;
                    case "sequenceNumber":
                        _ = reader.Read();
                        startAfter = reader.GetInt32();
                        break;
                    default:
                        throw new JsonException($"Unrecognized property '{propertyName}'.");
                }
            }

            return new(responseId)
            {
                SequenceNumber = startAfter
            };
        }

        public override ReadOnlyMemory<byte> ToBytes()
        {
            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream);

            writer.WriteStartObject();

            writer.WriteString("responseId", ResponseId);

            if (SequenceNumber.HasValue)
            {
                writer.WriteNumber("sequenceNumber", SequenceNumber.Value);
            }

            writer.WriteEndObject();

            writer.Flush();
            stream.Position = 0;

            return stream.ToArray();
        }
    }

    [Fact]
    public async Task WebSearchTool_NonStreaming()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"What is the latest news about .NET?"}]
                }],
                "tools":[{
                    "type":"web_search"
                }]
            }
            """;

        const string Output = """
            {
              "id": "resp_0ed9cd9f8606486b006988807187cc8190a1089cf7510bdd4c",
              "object": "response",
              "created_at": 1770553457,
              "status": "completed",
              "background": false,
              "billing": {
                "payer": "developer"
              },
              "completed_at": 1770553468,
              "error": null,
              "frequency_penalty": 0.0,
              "incomplete_details": null,
              "instructions": null,
              "max_output_tokens": null,
              "max_tool_calls": null,
              "model": "gpt-4o-2024-08-06",
              "output": [
                {
                  "id": "ws_0ed9cd9f8606486b0069888072b3d08190818b61da9cf032a7",
                  "type": "web_search_call",
                  "status": "completed",
                  "action": {
                    "type": "search",
                    "queries": [
                      ".NET latest news 2023"
                    ],
                    "query": ".NET latest news 2023",
                    "sources": [
                      {
                        "type": "url",
                        "url": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-10",
                        "title": "Announcing .NET 10 - .NET Blog"
                      },
                      {
                        "type": "url",
                        "url": "https://dotnet.microsoft.com/en-us/download/dotnet/10.0",
                        "title": "Download .NET 10"
                      }
                    ]
                  }
                },
                {
                  "id": "msg_0ed9cd9f8606486b0069888075248081908bf6a2a2fdc5d7c3",
                  "type": "message",
                  "status": "completed",
                  "content": [
                    {
                      "type": "output_text",
                      "annotations": [
                        {
                          "type": "url_citation",
                          "end_index": 555,
                          "start_index": 448,
                          "title": "Announcing .NET 10 - .NET Blog",
                          "url": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"
                        },
                        {
                          "type": "url_citation",
                          "end_index": 1322,
                          "start_index": 1170,
                          "title": "Download .NET 10",
                          "url": "https://dotnet.microsoft.com/en-us/download/dotnet/10.0?utm_source=openai"
                        }
                      ],
                      "logprobs": [],
                      "text": ".NET 10 was officially released in November 2025 as an LTS release."
                    }
                  ],
                  "role": "assistant"
                }
              ],
              "parallel_tool_calls": true,
              "presence_penalty": 0.0,
              "previous_response_id": null,
              "prompt_cache_key": null,
              "prompt_cache_retention": null,
              "reasoning": {
                "effort": null,
                "summary": null
              },
              "safety_identifier": null,
              "service_tier": "default",
              "store": true,
              "temperature": 1.0,
              "text": {
                "format": {
                  "type": "text"
                },
                "verbosity": "medium"
              },
              "tool_choice": "auto",
              "tools": [
                {
                  "type": "web_search",
                  "filters": null,
                  "search_context_size": "medium",
                  "user_location": {
                    "type": "approximate",
                    "city": null,
                    "country": "US",
                    "region": null,
                    "timezone": null
                  }
                }
              ],
              "top_logprobs": 0,
              "top_p": 1.0,
              "truncation": "disabled",
              "usage": {
                "input_tokens": 17071,
                "input_tokens_details": {
                  "cached_tokens": 0
                },
                "output_tokens": 1449,
                "output_tokens_details": {
                  "reasoning_tokens": 0
                },
                "total_tokens": 18520
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        var response = await client.GetResponseAsync("What is the latest news about .NET?", new()
        {
            Tools = [new HostedWebSearchTool()],
        });

        Assert.NotNull(response);
        Assert.Single(response.Messages);

        var message = response.Messages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal(3, message.Contents.Count);

        var wsCall = Assert.IsType<WebSearchToolCallContent>(message.Contents[0]);
        Assert.Equal("ws_0ed9cd9f8606486b0069888072b3d08190818b61da9cf032a7", wsCall.CallId);
        Assert.Equal([".NET latest news 2023"], wsCall.Queries);

        var wsResult = Assert.IsType<WebSearchToolResultContent>(message.Contents[1]);
        Assert.Equal("ws_0ed9cd9f8606486b0069888072b3d08190818b61da9cf032a7", wsResult.CallId);
        Assert.NotNull(wsResult.RawRepresentation);
        Assert.NotNull(wsResult.Outputs);
        Assert.Equal(2, wsResult.Outputs.Count);

        var source0 = Assert.IsType<UriContent>(wsResult.Outputs[0]);
        Assert.Equal(new Uri("https://devblogs.microsoft.com/dotnet/announcing-dotnet-10"), source0.Uri);
        var source0Raw = Assert.IsType<WebSearchActionUriSource>(source0.RawRepresentation);
        Assert.Equal("\"Announcing .NET 10 - .NET Blog\"", ReadPatchValue(source0Raw, "$.title"u8));

        var source1 = Assert.IsType<UriContent>(wsResult.Outputs[1]);
        Assert.Equal(new Uri("https://dotnet.microsoft.com/en-us/download/dotnet/10.0"), source1.Uri);
        var source1Raw = Assert.IsType<WebSearchActionUriSource>(source1.RawRepresentation);
        Assert.Equal("\"Download .NET 10\"", ReadPatchValue(source1Raw, "$.title"u8));

        var textContent = Assert.IsType<TextContent>(message.Contents[2]);
        Assert.Equal(".NET 10 was officially released in November 2025 as an LTS release.", textContent.Text);
        Assert.NotNull(textContent.Annotations);
        Assert.Equal(2, textContent.Annotations.Count);
        var citation = Assert.IsType<CitationAnnotation>(textContent.Annotations[0]);
        Assert.Equal(new Uri("https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"), citation.Url);
        Assert.Equal("Announcing .NET 10 - .NET Blog", citation.Title);

        static string ReadPatchValue(WebSearchActionUriSource source, ReadOnlySpan<byte> path)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
#pragma warning disable SCME0001 // JsonPatch is experimental
                source.Patch.WriteTo(writer, path);
#pragma warning restore SCME0001
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    [Fact]
    public async Task WebSearchTool_Streaming()
    {
        const string Input = """
            {
                "model":"gpt-4o-2024-08-06",
                "input":[{
                    "type":"message",
                    "role":"user",
                    "content":[{"type":"input_text","text":"What is the latest news about .NET?"}]
                }],
                "tools":[{
                    "type":"web_search"
                }],
                "stream":true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_02441a08b3f3bf4b006988809034f4819e8525bfe16c85a287","object":"response","created_at":1770553488,"status":"in_progress","background":false,"completed_at":null,"error":null,"frequency_penalty":0.0,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"presence_penalty":0.0,"previous_response_id":null,"prompt_cache_key":null,"prompt_cache_retention":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"web_search","filters":null,"search_context_size":"medium","user_location":{"type":"approximate","city":null,"country":"US","region":null,"timezone":null}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}},"sequence_number":0}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_02441a08b3f3bf4b006988809034f4819e8525bfe16c85a287","object":"response","created_at":1770553488,"status":"in_progress","background":false,"completed_at":null,"error":null,"frequency_penalty":0.0,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[],"parallel_tool_calls":true,"presence_penalty":0.0,"previous_response_id":null,"prompt_cache_key":null,"prompt_cache_retention":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"auto","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"web_search","filters":null,"search_context_size":"medium","user_location":{"type":"approximate","city":null,"country":"US","region":null,"timezone":null}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":null,"user":null,"metadata":{}},"sequence_number":1}

            event: response.output_item.added
            data: {"type":"response.output_item.added","item":{"id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","type":"web_search_call","status":"in_progress","action":{"type":"search"}},"output_index":0,"sequence_number":2}

            event: response.web_search_call.in_progress
            data: {"type":"response.web_search_call.in_progress","item_id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","output_index":0,"sequence_number":3}

            event: response.web_search_call.searching
            data: {"type":"response.web_search_call.searching","item_id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","output_index":0,"sequence_number":4}

            event: response.web_search_call.completed
            data: {"type":"response.web_search_call.completed","item_id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","output_index":0,"sequence_number":5}

            event: response.output_item.done
            data: {"type":"response.output_item.done","item":{"id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","type":"web_search_call","status":"completed","action":{"type":"search","queries":[".NET latest news October 2023"],"query":".NET latest news October 2023","sources":[{"type":"url","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10","title":"Announcing .NET 10 - .NET Blog"}]}},"output_index":0,"sequence_number":6}

            event: response.output_item.added
            data: {"type":"response.output_item.added","item":{"id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","type":"message","status":"in_progress","content":[],"role":"assistant"},"output_index":1,"sequence_number":7}

            event: response.content_part.added
            data: {"type":"response.content_part.added","content_index":0,"item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","output_index":1,"part":{"type":"output_text","annotations":[],"logprobs":[],"text":""},"sequence_number":8}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","content_index":0,"delta":".NET 10 was ","item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","logprobs":[],"output_index":1,"sequence_number":9}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","content_index":0,"delta":"officially released.","item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","logprobs":[],"output_index":1,"sequence_number":10}

            event: response.output_text.annotation.added
            data: {"type":"response.output_text.annotation.added","annotation":{"type":"url_citation","end_index":538,"start_index":434,"title":"Announcing .NET 10 - .NET Blog","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"},"annotation_index":0,"content_index":0,"item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","output_index":1,"sequence_number":11}

            event: response.output_text.done
            data: {"type":"response.output_text.done","content_index":0,"item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","logprobs":[],"output_index":1,"sequence_number":12,"text":".NET 10 was officially released."}

            event: response.content_part.done
            data: {"type":"response.content_part.done","content_index":0,"item_id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","output_index":1,"part":{"type":"output_text","annotations":[{"type":"url_citation","end_index":538,"start_index":434,"title":"Announcing .NET 10 - .NET Blog","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"}],"logprobs":[],"text":".NET 10 was officially released."},"sequence_number":13}

            event: response.output_item.done
            data: {"type":"response.output_item.done","item":{"id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","type":"message","status":"completed","content":[{"type":"output_text","annotations":[{"type":"url_citation","end_index":538,"start_index":434,"title":"Announcing .NET 10 - .NET Blog","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"}],"logprobs":[],"text":".NET 10 was officially released."}],"role":"assistant"},"output_index":1,"sequence_number":14}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_02441a08b3f3bf4b006988809034f4819e8525bfe16c85a287","object":"response","created_at":1770553488,"status":"completed","background":false,"completed_at":1770553497,"error":null,"frequency_penalty":0.0,"incomplete_details":null,"instructions":null,"max_output_tokens":null,"max_tool_calls":null,"model":"gpt-4o-2024-08-06","output":[{"id":"ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3","type":"web_search_call","status":"completed","action":{"type":"search","queries":[".NET latest news October 2023"],"query":".NET latest news October 2023","sources":[{"type":"url","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10","title":"Announcing .NET 10 - .NET Blog"}]}},{"id":"msg_02441a08b3f3bf4b0069888093b5d4819e8c89ede1d425e487","type":"message","status":"completed","content":[{"type":"output_text","annotations":[{"type":"url_citation","end_index":538,"start_index":434,"title":"Announcing .NET 10 - .NET Blog","url":"https://devblogs.microsoft.com/dotnet/announcing-dotnet-10?utm_source=openai"}],"logprobs":[],"text":".NET 10 was officially released."}],"role":"assistant"}],"parallel_tool_calls":true,"presence_penalty":0.0,"previous_response_id":null,"prompt_cache_key":null,"prompt_cache_retention":null,"reasoning":{"effort":null,"summary":null},"safety_identifier":null,"service_tier":"default","store":true,"temperature":1.0,"text":{"format":{"type":"text"},"verbosity":"medium"},"tool_choice":"auto","tools":[{"type":"web_search","filters":null,"search_context_size":"medium","user_location":{"type":"approximate","city":null,"country":"US","region":null,"timezone":null}}],"top_logprobs":0,"top_p":1.0,"truncation":"disabled","usage":{"input_tokens":17013,"input_tokens_details":{"cached_tokens":0},"output_tokens":1138,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":18151},"user":null,"metadata":{}},"sequence_number":15}

            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-2024-08-06");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync("What is the latest news about .NET?", new()
        {
            Tools = [new HostedWebSearchTool()],
        }))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);

        // The stream should yield two WebSearchToolCallContent updates:
        // 1) From the in-progress event (has RawRepresentation, no Queries)
        // 2) From the output_item.done event (has Queries, no RawRepresentation)
        var wsCallUpdates = updates.Where(u => u.Contents.OfType<WebSearchToolCallContent>().Any()).ToList();
        Assert.Equal(2, wsCallUpdates.Count);

        var wsCallInProgress = wsCallUpdates[0].Contents.OfType<WebSearchToolCallContent>().Single();
        Assert.Equal("ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3", wsCallInProgress.CallId);
        Assert.NotNull(wsCallInProgress.RawRepresentation);
        Assert.Null(wsCallInProgress.Queries);

        var wsCallDone = wsCallUpdates[1].Contents.OfType<WebSearchToolCallContent>().Single();
        Assert.Equal("ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3", wsCallDone.CallId);
        Assert.Null(wsCallDone.RawRepresentation);
        Assert.Equal([".NET latest news October 2023"], wsCallDone.Queries);

        // Find web search result content in the stream
        var wsResultUpdate = updates.FirstOrDefault(u => u.Contents.OfType<WebSearchToolResultContent>().Any());
        Assert.NotNull(wsResultUpdate);
        var wsResult = wsResultUpdate.Contents.OfType<WebSearchToolResultContent>().First();
        Assert.Equal("ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3", wsResult.CallId);
        Assert.NotNull(wsResult.RawRepresentation);
        Assert.NotNull(wsResult.Outputs);
        Assert.Single(wsResult.Outputs);
        var streamSource = Assert.IsType<UriContent>(wsResult.Outputs[0]);
        Assert.Equal(new Uri("https://devblogs.microsoft.com/dotnet/announcing-dotnet-10"), streamSource.Uri);
        Assert.IsType<WebSearchActionUriSource>(streamSource.RawRepresentation);

        // Find text content
        var textUpdates = updates.Where(u => u.Contents.OfType<TextContent>().Any()).ToList();
        Assert.NotEmpty(textUpdates);

        // Verify that streaming updates coalesce correctly into a ChatResponse.
        // The in-progress update yields a WebSearchToolCallContent without queries,
        // and the output_item.done yields another with queries — these must merge.
        var response = updates.ToChatResponse();
        var message = Assert.Single(response.Messages);

        var coalescedWsCall = message.Contents.OfType<WebSearchToolCallContent>().Single();
        Assert.Equal("ws_02441a08b3f3bf4b00698880914730819eb48b3ae0c359bff3", coalescedWsCall.CallId);
        Assert.Equal([".NET latest news October 2023"], coalescedWsCall.Queries);

        var coalescedWsResult = message.Contents.OfType<WebSearchToolResultContent>().Single();
        Assert.Equal(coalescedWsCall.CallId, coalescedWsResult.CallId);
        Assert.NotNull(coalescedWsResult.RawRepresentation);

        var textContent = message.Contents.OfType<TextContent>().Single();
        Assert.Equal(".NET 10 was officially released.", textContent.Text);
    }

    [Fact]
    public async Task ToolSearchTool_OnlyToolSearch_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-4o-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools = [new HostedToolSearchTool()],
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_SearchableFunctionsDeferred_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "GetWeather",
                        "description": "Gets the weather.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true,
                        "defer_loading": true
                    },
                    {
                        "type": "function",
                        "name": "GetForecast",
                        "description": "Gets the forecast.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true,
                        "defer_loading": true
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-4o-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        var getWeather = AIFunctionFactory.Create(() => 42, "GetWeather", "Gets the weather.");
        var getForecast = AIFunctionFactory.Create(() => 42, "GetForecast", "Gets the forecast.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool(),
                getWeather,
                getForecast,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_MixedDeferredAndNonDeferredFunctions_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "GetWeather",
                        "description": "Gets the weather.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true,
                        "defer_loading": true
                    },
                    {
                        "type": "function",
                        "name": "ImportantTool",
                        "description": "An important tool.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getWeather = AIFunctionFactory.Create(() => 42, "GetWeather", "Gets the weather.");
        var importantTool = AIFunctionFactory.Create(() => 42, "ImportantTool", "An important tool.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { DeferredTools = ["GetWeather"] },
                getWeather,
                importantTool,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NoFunctionTools_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "web_search"
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool(),
                new HostedWebSearchTool(),
            ],
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceGrouping_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "ImportantTool",
                        "description": "An important tool.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "ListOrders",
                                "description": "Lists orders.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var listOrders = AIFunctionFactory.Create(() => 42, "ListOrders", "Lists orders.");
        var importantTool = AIFunctionFactory.Create(() => 42, "ImportantTool", "An important tool.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "crm", DeferredTools = ["GetCustomer", "ListOrders"] },
                getCustomer,
                listOrders,
                importantTool,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceAllDeferred_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "utilities",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetWeather",
                                "description": "Gets the weather.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "GetTime",
                                "description": "Gets the time.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getWeather = AIFunctionFactory.Create(() => 42, "GetWeather", "Gets the weather.");
        var getTime = AIFunctionFactory.Create(() => 42, "GetTime", "Gets the time.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "utilities" },
                getWeather,
                getTime,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_MultipleNamespaces_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    },
                    {
                        "type": "namespace",
                        "name": "weather",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetWeather",
                                "description": "Gets the weather.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var getWeather = AIFunctionFactory.Create(() => 42, "GetWeather", "Gets the weather.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "crm", DeferredTools = ["GetCustomer"] },
                new HostedToolSearchTool { Namespace = "weather", DeferredTools = ["GetWeather"] },
                getCustomer,
                getWeather,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_SameNamespaceMerged_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "ListOrders",
                                "description": "Lists orders.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var listOrders = AIFunctionFactory.Create(() => 42, "ListOrders", "Lists orders.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "crm", DeferredTools = ["GetCustomer"] },
                new HostedToolSearchTool { Namespace = "crm", DeferredTools = ["ListOrders"] },
                getCustomer,
                listOrders,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_ToolClaimedByFirstNamespace_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "primary",
                        "tools": [
                            {
                                "type": "function",
                                "name": "SharedTool",
                                "description": "A shared tool.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var sharedTool = AIFunctionFactory.Create(() => 42, "SharedTool", "A shared tool.");

        // SharedTool is claimed by both namespaces — first one ("primary") wins.
        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "primary" },
                new HostedToolSearchTool { Namespace = "secondary" },
                sharedTool,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_McpToolNamespaceGrouping_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "LocalFunc",
                        "description": "A local function.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    },
                    {
                        "type": "namespace",
                        "name": "remote",
                        "tools": [
                            {
                                "type": "mcp",
                                "server_label": "my-mcp-server",
                                "server_url": "http://localhost:8000/",
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var mcpTool = new HostedMcpServerTool("my-mcp-server", "http://localhost:8000");
        var localFunc = AIFunctionFactory.Create(() => 42, "LocalFunc", "A local function.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "remote", DeferredTools = ["my-mcp-server"] },
                mcpTool,
                localFunc,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NonDeferrableToolStaysTopLevel_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "code_interpreter",
                        "container": {
                            "type": "auto"
                        }
                    },
                    {
                        "type": "function",
                        "name": "LocalFunc",
                        "description": "A local function.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var codeTool = new HostedCodeInterpreterTool();
        var localFunc = AIFunctionFactory.Create(() => 42, "LocalFunc", "A local function.");

        // code_interpreter is not deferrable — it stays top-level even when listed in DeferredTools with a namespace.
        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool { Namespace = "sandbox", DeferredTools = ["code_interpreter"] },
                codeTool,
                localFunc,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceWithDescription_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "ImportantTool",
                        "description": "An important tool.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "description": "Customer relationship management tools.",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "ListOrders",
                                "description": "Lists orders.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var listOrders = AIFunctionFactory.Create(() => 42, "ListOrders", "Lists orders.");
        var importantTool = AIFunctionFactory.Create(() => 42, "ImportantTool", "An important tool.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool
                {
                    Namespace = "crm",
                    NamespaceDescription = "Customer relationship management tools.",
                    DeferredTools = ["GetCustomer", "ListOrders"],
                },
                getCustomer,
                listOrders,
                importantTool,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceDescriptionAloneDoesNotWrap_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "function",
                        "name": "GetWeather",
                        "description": "Gets the weather.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true,
                        "defer_loading": true
                    },
                    {
                        "type": "function",
                        "name": "ImportantTool",
                        "description": "An important tool.",
                        "parameters": {
                            "type": "object",
                            "required": [],
                            "properties": {},
                            "additionalProperties": false
                        },
                        "strict": true
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getWeather = AIFunctionFactory.Create(() => 42, "GetWeather", "Gets the weather.");
        var importantTool = AIFunctionFactory.Create(() => 42, "ImportantTool", "An important tool.");

        // NamespaceDescription set without Namespace must NOT produce a namespace wrapper.
        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool
                {
                    NamespaceDescription = "should not appear in payload",
                    DeferredTools = ["GetWeather"],
                },
                getWeather,
                importantTool,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceWithDescription_FirstWins_NonStreaming()
    {
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "description": "first description wins",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "ListOrders",
                                "description": "Lists orders.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var listOrders = AIFunctionFactory.Create(() => 42, "ListOrders", "Lists orders.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool
                {
                    Namespace = "crm",
                    NamespaceDescription = "first description wins",
                    DeferredTools = ["GetCustomer"],
                },
                new HostedToolSearchTool
                {
                    Namespace = "crm",
                    NamespaceDescription = "second description should be ignored",
                    DeferredTools = ["ListOrders"],
                },
                getCustomer,
                listOrders,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task ToolSearchTool_NamespaceDescription_FirstNonEmptyWins_NonStreaming()
    {
        // The first HostedToolSearchTool for "crm" omits NamespaceDescription; a later one
        // supplies it. The supplied description should be used for the namespace wrapper.
        const string Input = """
            {
                "model": "gpt-5.4-mini",
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [
                            {
                                "type": "input_text",
                                "text": "hello"
                            }
                        ]
                    }
                ],
                "tools": [
                    {
                        "type": "tool_search"
                    },
                    {
                        "type": "namespace",
                        "name": "crm",
                        "description": "supplied by second tool",
                        "tools": [
                            {
                                "type": "function",
                                "name": "GetCustomer",
                                "description": "Gets a customer.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            },
                            {
                                "type": "function",
                                "name": "ListOrders",
                                "description": "Lists orders.",
                                "parameters": {
                                    "type": "object",
                                    "required": [],
                                    "properties": {},
                                    "additionalProperties": false
                                },
                                "strict": true,
                                "defer_loading": true
                            }
                        ]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_001",
              "object": "response",
              "created_at": 1741892091,
              "status": "completed",
              "model": "gpt-5.4-mini",
              "output": [
                {
                  "type": "message",
                  "id": "msg_001",
                  "status": "completed",
                  "role": "assistant",
                  "content": [{"type": "output_text", "text": "Hello!", "annotations": []}]
                }
              ]
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-5.4-mini");

        var getCustomer = AIFunctionFactory.Create(() => 42, "GetCustomer", "Gets a customer.");
        var listOrders = AIFunctionFactory.Create(() => 42, "ListOrders", "Lists orders.");

        var response = await client.GetResponseAsync("hello", new()
        {
            Tools =
            [
                new HostedToolSearchTool
                {
                    Namespace = "crm",
                    DeferredTools = ["GetCustomer"],
                },
                new HostedToolSearchTool
                {
                    Namespace = "crm",
                    NamespaceDescription = "supplied by second tool",
                    DeferredTools = ["ListOrders"],
                },
                getCustomer,
                listOrders,
            ],
            AdditionalProperties = new() { ["strict"] = true },
        });

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task Tool_SchemaWithObjectAdditionalProperties_FunctionCallArgumentsReceived()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "function",
                        "name": "TagResource",
                        "description": "Tags a resource with key-value labels.",
                        "parameters": {
                            "type": "object",
                            "required": ["resource_name"],
                            "properties": {
                                "resource_name": {
                                    "type": "string",
                                    "description": "Name of the resource."
                                }
                            },
                            "additionalProperties": {"type": "string"}
                        },
                        "strict": null
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [{"type": "input_text", "text": "Tag my-server with env=production."}]
                    }
                ]
            }
            """;

        const string Output = """
            {
              "id": "resp_tag_001",
              "object": "response",
              "created_at": 1741891428,
              "status": "completed",
              "model": "gpt-4o-mini-2024-07-18",
              "output": [
                {
                  "type": "function_call",
                  "id": "fc_tag_001",
                  "call_id": "call_tag_abc",
                  "name": "TagResource",
                  "arguments": "{\"resource_name\":\"my-server\",\"env\":\"production\"}",
                  "status": "completed"
                }
              ],
              "parallel_tool_calls": true,
              "previous_response_id": null,
              "reasoning": {"effort": null, "summary": null},
              "store": true,
              "temperature": 1.0,
              "text": {"format": {"type": "text"}},
              "tool_choice": "auto",
              "tools": [],
              "top_p": 1.0,
              "usage": {
                "input_tokens": 30,
                "input_tokens_details": {"cached_tokens": 0},
                "output_tokens": 20,
                "output_tokens_details": {"reasoning_tokens": 0},
                "total_tokens": 50
              },
              "user": null,
              "metadata": {}
            }
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        const string ExplicitSchema = """
            {
                "type": "object",
                "properties": {
                    "resource_name": {
                        "type": "string",
                        "description": "Name of the resource."
                    }
                },
                "required": ["resource_name"],
                "additionalProperties": {"type": "string"}
            }
            """;

        var tool = AIFunctionFactory.CreateDeclaration("TagResource", "Tags a resource with key-value labels.", JsonDocument.Parse(ExplicitSchema).RootElement);

        var response = await client.GetResponseAsync(
            "Tag my-server with env=production.",
            new ChatOptions { Tools = [tool] });

        Assert.NotNull(response);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[0].Contents));
        Assert.Equal("TagResource", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("my-server", ((JsonElement)call.Arguments["resource_name"]!).GetString());
        Assert.Equal("production", ((JsonElement)call.Arguments["env"]!).GetString());
    }

    [Fact]
    public async Task Tool_SchemaWithObjectAdditionalProperties_Streaming_FunctionCallArgumentsReceived()
    {
        const string Input = """
            {
                "model": "gpt-4o-mini",
                "tools": [
                    {
                        "type": "function",
                        "name": "TagResource",
                        "description": "Tags a resource with key-value labels.",
                        "parameters": {
                            "type": "object",
                            "required": ["resource_name"],
                            "properties": {
                                "resource_name": {
                                    "type": "string",
                                    "description": "Name of the resource."
                                }
                            },
                            "additionalProperties": {"type": "string"}
                        },
                        "strict": null
                    }
                ],
                "input": [
                    {
                        "type": "message",
                        "role": "user",
                        "content": [{"type": "input_text", "text": "Tag my-server with env=production."}]
                    }
                ],
                "stream": true
            }
            """;

        const string Output = """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_tag_002","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.in_progress
            data: {"type":"response.in_progress","response":{"id":"resp_tag_002","object":"response","created_at":1741892091,"status":"in_progress","model":"gpt-4o-mini","output":[]}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"function_call","id":"fc_tag_002","call_id":"call_tag_def","name":"TagResource","arguments":"","status":"in_progress"}}

            event: response.function_call_arguments.delta
            data: {"type":"response.function_call_arguments.delta","item_id":"fc_tag_002","output_index":0,"delta":"{\"resource_name\":\"my-server\",\"env\":\"production\"}"}

            event: response.function_call_arguments.done
            data: {"type":"response.function_call_arguments.done","item_id":"fc_tag_002","output_index":0,"arguments":"{\"resource_name\":\"my-server\",\"env\":\"production\"}"}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"function_call","id":"fc_tag_002","call_id":"call_tag_def","name":"TagResource","arguments":"{\"resource_name\":\"my-server\",\"env\":\"production\"}","status":"completed"}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_tag_002","object":"response","created_at":1741892091,"status":"completed","model":"gpt-4o-mini","output":[{"type":"function_call","id":"fc_tag_002","call_id":"call_tag_def","name":"TagResource","arguments":"{\"resource_name\":\"my-server\",\"env\":\"production\"}","status":"completed"}],"usage":{"input_tokens":30,"output_tokens":20,"total_tokens":50}}}

            
            """;

        using VerbatimHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using IChatClient client = CreateResponseClient(httpClient, "gpt-4o-mini");

        const string ExplicitSchema = """
            {
                "type": "object",
                "properties": {
                    "resource_name": {
                        "type": "string",
                        "description": "Name of the resource."
                    }
                },
                "required": ["resource_name"],
                "additionalProperties": {"type": "string"}
            }
            """;

        var tool = AIFunctionFactory.CreateDeclaration("TagResource", "Tags a resource with key-value labels.", JsonDocument.Parse(ExplicitSchema).RootElement);

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync(
            "Tag my-server with env=production.",
            new ChatOptions { Tools = [tool] }))
        {
            updates.Add(update);
        }

        var functionCallUpdate = updates.FirstOrDefault(u => u.Contents.OfType<FunctionCallContent>().Any());
        Assert.NotNull(functionCallUpdate);
        var call = functionCallUpdate.Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("call_tag_def", call.CallId);
        Assert.Equal("TagResource", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("my-server", ((JsonElement)call.Arguments["resource_name"]!).GetString());
        Assert.Equal("production", ((JsonElement)call.Arguments["env"]!).GetString());

        var response = updates.ToChatResponse();
        Assert.Equal("resp_tag_002", response.ResponseId);
        var coalesced = response.Messages[0].Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("TagResource", coalesced.Name);
        Assert.NotNull(coalesced.Arguments);
        Assert.Equal("my-server", ((JsonElement)coalesced.Arguments["resource_name"]!).GetString());
        Assert.Equal("production", ((JsonElement)coalesced.Arguments["env"]!).GetString());
    }
}
