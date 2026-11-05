// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001 // Experimental OpenAI APIs

namespace Microsoft.Extensions.AI;

public class OpenAIResponseClientIntegrationTests : ChatClientIntegrationTests
{
    protected override IChatClient? CreateChatClient() =>
        IntegrationTestHelpers.GetOpenAIClient()
        ?.GetResponsesClient()
        .AsIChatClient(TestRunnerConfiguration.Instance["OpenAI:ChatModel"] ?? "gpt-4o-mini");

    private static string ReasoningModel => "gpt-5-nano";

    public override bool FunctionInvokingChatClientSetsConversationId => true;

    // Test structure doesn't make sense with Responses.
    public override Task Caching_AfterFunctionInvocation_FunctionOutputUnchangedAsync() => Task.CompletedTask;

    [Fact]
    public async Task UseCodeInterpreter_ProducesCodeExecutionResults()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync("Use the code interpreter to calculate the square root of 42. Return only the nearest integer value and no other text.", new()
        {
            Tools = [new HostedCodeInterpreterTool()],
        });
        Assert.NotNull(response);

        ChatMessage message = Assert.Single(response.Messages);

        Assert.Equal("6", message.Text);

        // Validate CodeInterpreterToolCallContent
        var toolCallContent = response.Messages.SelectMany(m => m.Contents).OfType<CodeInterpreterToolCallContent>().SingleOrDefault();
        Assert.NotNull(toolCallContent);
        Assert.NotNull(toolCallContent.CallId);
        Assert.NotEmpty(toolCallContent.CallId);
        Assert.NotNull(toolCallContent.Inputs);
        Assert.NotEmpty(toolCallContent.Inputs);

        var codeInput = toolCallContent.Inputs.OfType<DataContent>().FirstOrDefault();
        Assert.NotNull(codeInput);
        Assert.Equal("text/x-python", codeInput.MediaType);
        Assert.NotEmpty(codeInput.Data.ToArray());

        // Validate CodeInterpreterToolResultContent
        var toolResultContent = response.Messages.SelectMany(m => m.Contents).OfType<CodeInterpreterToolResultContent>().FirstOrDefault();
        Assert.NotNull(toolResultContent);
        Assert.NotNull(toolResultContent.CallId);
        Assert.NotEmpty(toolResultContent.CallId);

        if (toolResultContent.Outputs is not null)
        {
            Assert.NotEmpty(toolResultContent.Outputs);
            if (toolResultContent.Outputs.OfType<TextContent>().FirstOrDefault() is { } resultOutput)
            {
                Assert.NotEmpty(resultOutput.Text);
            }
        }
    }

    [Fact]
    public async Task UseWebSearch_AnnotationsReflectResults()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
            "Write a paragraph about .NET based on at least three recent news articles. Cite your sources.",
            new()
            {
                Tools = [new HostedWebSearchTool()],
                RawRepresentationFactory = _ =>
                {
                    var cro = new CreateResponseOptions();
                    cro.IncludedProperties.Add("web_search_call.action.sources");
                    return cro;
                },
            });

        ChatMessage m = Assert.Single(response.Messages);

        // Verify that the web search tool call and result content are present.
        var wsCall = m.Contents.OfType<WebSearchToolCallContent>().FirstOrDefault();
        Assert.NotNull(wsCall);
        Assert.NotNull(wsCall.CallId);
        Assert.NotNull(wsCall.Queries);
        Assert.NotEmpty(wsCall.Queries);

        var wsResult = m.Contents.OfType<WebSearchToolResultContent>().FirstOrDefault();
        Assert.NotNull(wsResult);
        Assert.Equal(wsCall.CallId, wsResult.CallId);

        // Verify that sources are populated when opted in.
        Assert.NotNull(wsResult.Outputs);
        Assert.NotEmpty(wsResult.Outputs);
        Assert.All(wsResult.Outputs, r =>
        {
            var uriContent = Assert.IsType<UriContent>(r);
            Assert.NotNull(uriContent.Uri);
        });

        TextContent tc = m.Contents.OfType<TextContent>().First();
        Assert.NotNull(tc.Annotations);
        Assert.NotEmpty(tc.Annotations);
        Assert.All(tc.Annotations, a =>
        {
            CitationAnnotation ca = Assert.IsType<CitationAnnotation>(a);
            var regions = Assert.IsType<List<AnnotatedRegion>>(ca.AnnotatedRegions);
            Assert.NotNull(regions);
            Assert.Single(regions);
            var region = Assert.IsType<TextSpanAnnotatedRegion>(regions[0]);
            Assert.NotNull(region);
            Assert.NotNull(region.StartIndex);
            Assert.NotNull(region.EndIndex);
            Assert.NotNull(ca.Url);
            Assert.NotNull(ca.Title);
            Assert.NotEmpty(ca.Title);
        });
    }

    [Theory]
    [InlineData(false, "gpt-image-1-mini")]
    [InlineData(true, "gpt-image-2")]
    public async Task UseImageGeneration_ProducesImageContent(bool streaming, string imageModel)
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Image generation tool requires gpt-5.4 or later.");

        var chatOptions = new ChatOptions
        {
            Tools =
            [
                new HostedImageGenerationTool
                {
                    Options = new ImageGenerationOptions { ModelId = imageModel },
                },
            ],
        };

        ChatResponse response = streaming
            ? await ChatClient.GetStreamingResponseAsync("Generate an image of a simple blue circle on a white background.", chatOptions).ToChatResponseAsync()
            : await ChatClient.GetResponseAsync("Generate an image of a simple blue circle on a white background.", chatOptions);

        Assert.NotNull(response);

        ChatMessage m = Assert.Single(response.Messages);

        // Verify that the image generation tool call and result content are present.
        var igCall = m.Contents.OfType<ImageGenerationToolCallContent>().FirstOrDefault();
        Assert.NotNull(igCall);
        Assert.NotNull(igCall.CallId);

        var igResult = m.Contents.OfType<ImageGenerationToolResultContent>().FirstOrDefault();
        Assert.NotNull(igResult);
        Assert.Equal(igCall.CallId, igResult.CallId);

        // Verify that the result contains image data.
        Assert.NotNull(igResult.Outputs);
        Assert.NotEmpty(igResult.Outputs);
        var imageContent = Assert.Single(igResult.Outputs.OfType<DataContent>());
        Assert.False(imageContent.Data.IsEmpty);
        Assert.StartsWith("image/", imageContent.MediaType, StringComparison.Ordinal);

        // Save to temp file for manual inspection.
        string extension = imageContent.MediaType == "image/png" ? ".png" : ".webp";
        string tempPath = Path.Combine(Path.GetTempPath(), $"image_gen_test_{imageModel}{extension}");
        File.WriteAllBytes(tempPath, imageContent.Data.ToArray());
    }

    [Fact]
    public async Task RemoteMCP_ListTools()
    {
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            Tools = [new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp")) { ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire }],
        };

        ChatResponse response = await CreateChatClient()!.GetResponseAsync("Which tools are available on the wiki_tools MCP server?", chatOptions);

        Assert.Contains("read_wiki_structure", response.Text);
        Assert.Contains("read_wiki_contents", response.Text);
        Assert.Contains("ask_question", response.Text);
    }

    [Fact]
    public async Task RemoteMCP_CallTool_ApprovalNeverRequired()
    {
        SkipIfNotEnabled();

        await RunAsync(false, false);
        await RunAsync(true, true);

        async Task RunAsync(bool streaming, bool requireSpecific)
        {
            ChatOptions chatOptions = new()
            {
                Tools = [new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
                    {
                        ApprovalMode = requireSpecific ?
                            HostedMcpServerToolApprovalMode.RequireSpecific(null, ["read_wiki_structure", "ask_question"]) :
                            HostedMcpServerToolApprovalMode.NeverRequire,
                    }
                ],
            };

            using var client = CreateChatClient()!;

            const string Prompt = "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository";

            ChatResponse response = streaming ?
                await client.GetStreamingResponseAsync(Prompt, chatOptions).ToChatResponseAsync() :
                await client.GetResponseAsync(Prompt, chatOptions);

            Assert.NotNull(response);
            Assert.NotEmpty(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolCallContent>());
            Assert.NotEmpty(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolResultContent>());
            Assert.Empty(response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());

            Assert.Contains("src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md", response.Text);
        }
    }

    [Fact]
    public async Task RemoteMCP_CallTool_ApprovalRequired()
    {
        SkipIfNotEnabled();

        await RunAsync(false, false, false);
        await RunAsync(true, true, false);
        await RunAsync(false, false, true);
        await RunAsync(true, true, true);

        async Task RunAsync(bool streaming, bool requireSpecific, bool useConversationId)
        {
            ChatOptions chatOptions = new()
            {
                Tools = [new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
                    {
                        ApprovalMode = requireSpecific ?
                            HostedMcpServerToolApprovalMode.RequireSpecific(["read_wiki_structure", "ask_question"], null) :
                            HostedMcpServerToolApprovalMode.AlwaysRequire,
                    }
                ],
            };

            using var client = CreateChatClient()!;

            // Initial request
            List<ChatMessage> input = [new ChatMessage(ChatRole.User, "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository")];
            ChatResponse response = streaming ?
                await client.GetStreamingResponseAsync(input, chatOptions).ToChatResponseAsync() :
                await client.GetResponseAsync(input, chatOptions);

            // Handle approvals of up to two rounds of tool calls
            int approvalsCount = 0;
            for (int i = 0; i < 2; i++)
            {
                if (useConversationId)
                {
                    chatOptions.ConversationId = response.ConversationId;
                    input.Clear();
                }
                else
                {
                    input.AddRange(response.Messages);
                }

                var approvalResponse = new ChatMessage(ChatRole.Tool,
                    response.Messages
                            .SelectMany(m => m.Contents)
                            .OfType<ToolApprovalRequestContent>()
                            .Select(c => c.CreateResponse(true))
                            .ToArray());
                if (approvalResponse.Contents.Count == 0)
                {
                    break;
                }

                approvalsCount += approvalResponse.Contents.Count;
                input.Add(approvalResponse);
                response = streaming ?
                    await client.GetStreamingResponseAsync(input, chatOptions).ToChatResponseAsync() :
                    await client.GetResponseAsync(input, chatOptions);
            }

            // Validate final response
            Assert.Equal(2, approvalsCount);
            Assert.Contains("src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md", response.Text);
        }
    }

    [Fact]
    public async Task RemoteMCP_DeferLoadingTools()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        var mcpTool = new HostedMcpServerTool("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"))
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
        };

        ChatOptions chatOptions = new()
        {
            Tools =
            [
                new HostedToolSearchTool(),
                mcpTool,
            ],
        };

        ChatResponse response = await ChatClient.GetResponseAsync(
            "Tell me the path to the README.md file for Microsoft.Extensions.AI.Abstractions in the dotnet/extensions repository",
            chatOptions);

        Assert.NotNull(response);
        Assert.Contains("src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md", response.Text);
        Assert.NotEmpty(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolCallContent>());
        Assert.NotEmpty(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolResultContent>());

        // Verify tool_search response items are present via RawRepresentation,
        // since the OpenAI SDK doesn't expose dedicated types for them yet.
        var allContents = response.Messages.SelectMany(m => m.Contents).ToList();
        var rawJsons = allContents
            .Where(c => c.RawRepresentation is ResponseItem)
            .Select(c => ModelReaderWriter.Write((ResponseItem)c.RawRepresentation!, ModelReaderWriterOptions.Json).ToString())
            .ToList();
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_call\"") || json.Contains("\"type\": \"tool_search_call\""));
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_output\"") || json.Contains("\"type\": \"tool_search_output\""));
    }

    [Fact]
    public async Task GetResponseAsync_BackgroundResponses()
    {
        SkipIfNotEnabled();

        var chatOptions = new ChatOptions
        {
            AllowBackgroundResponses = true,
        };

        // Get initial response with continuation token
        var response = await ChatClient.GetResponseAsync("What's the biggest animal?", chatOptions);
        Assert.NotNull(response.ContinuationToken);
        Assert.Empty(response.Messages);

        int attempts = 0;

        // Continue to poll until we get the final response
        while (response.ContinuationToken is not null && ++attempts < 10)
        {
            chatOptions.ContinuationToken = response.ContinuationToken;
            response = await ChatClient.GetResponseAsync([], chatOptions);
            await Task.Delay(1000);
        }

        Assert.Contains("whale", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetResponseAsync_BackgroundResponses_WithFunction()
    {
        SkipIfNotEnabled();

        int callCount = 0;

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        var chatOptions = new ChatOptions
        {
            AllowBackgroundResponses = true,
            Tools = [AIFunctionFactory.Create(() => { callCount++; return "5:43"; }, new AIFunctionFactoryOptions { Name = "GetCurrentTime" })]
        };

        // Get initial response with continuation token
        var response = await chatClient.GetResponseAsync("What time is it?", chatOptions);
        Assert.NotNull(response.ContinuationToken);
        Assert.Empty(response.Messages);

        int attempts = 0;

        // Poll until the result is received
        while (response.ContinuationToken is not null && ++attempts < 10)
        {
            chatOptions.ContinuationToken = response.ContinuationToken;

            response = await chatClient.GetResponseAsync([], chatOptions);
            await Task.Delay(1000);
        }

        Assert.Contains("5:43", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses()
    {
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            AllowBackgroundResponses = true,
        };

        string responseText = "";

        await foreach (var update in ChatClient.GetStreamingResponseAsync("What is the capital of France?", chatOptions))
        {
            responseText += update;
        }

        // Assert
        Assert.Contains("Paris", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses_StreamResumption()
    {
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            AllowBackgroundResponses = true,
        };

        int updateNumber = 0;
        string responseText = "";
        ResponseContinuationToken? continuationToken = null;

        await foreach (var update in ChatClient.GetStreamingResponseAsync("What is the capital of France?", chatOptions))
        {
            responseText += update;

            // Simulate an interruption after receiving 8 updates.
            if (updateNumber++ == 8)
            {
                continuationToken = update.ContinuationToken;
                break;
            }
        }

        Assert.DoesNotContain("Paris", responseText);

        // Resume streaming from the point of interruption captured by the continuation token.
        chatOptions.ContinuationToken = continuationToken;
        await foreach (var update in ChatClient.GetStreamingResponseAsync([], chatOptions))
        {
            responseText += update;
        }

        Assert.Contains("Paris", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BackgroundResponses_WithFunction()
    {
        SkipIfNotEnabled();

        int callCount = 0;

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        var chatOptions = new ChatOptions
        {
            AllowBackgroundResponses = true,
            Tools = [AIFunctionFactory.Create(() => { callCount++; return "5:43"; }, new AIFunctionFactoryOptions { Name = "GetCurrentTime" })]
        };

        string responseText = "";

        await foreach (var update in chatClient.GetStreamingResponseAsync("What time is it?", chatOptions))
        {
            responseText += update;
        }

        Assert.Contains("5:43", responseText);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RemoteMCP_Connector()
    {
        SkipIfNotEnabled();

        if (TestRunnerConfiguration.Instance["RemoteMCP:ConnectorAccessToken"] is not string { Length: > 0 } accessToken)
        {
            Assert.Skip(
                "To run this test, set a value for RemoteMCP:ConnectorAccessToken. " +
                "You can obtain one by following https://platform.openai.com/docs/guides/tools-connectors-mcp?quickstart-panels=connector#authorizing-a-connector.");
            return; // Unreachable, but needed for definite assignment of 'accessToken'.
        }

        await RunAsync(false, false);
        await RunAsync(true, true);

        async Task RunAsync(bool streaming, bool approval)
        {
            ChatOptions chatOptions = new()
            {
                Tools = [new HostedMcpServerTool("calendar", "connector_googlecalendar")
                    {
                        ApprovalMode = approval ?
                            HostedMcpServerToolApprovalMode.AlwaysRequire :
                            HostedMcpServerToolApprovalMode.NeverRequire,
                        Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" },
                    }
                ],
            };

            using var client = CreateChatClient()!;

            List<ChatMessage> input = [new ChatMessage(ChatRole.User, "What is on my calendar for today?")];

            ChatResponse response = streaming ?
                await client.GetStreamingResponseAsync(input, chatOptions).ToChatResponseAsync() :
                await client.GetResponseAsync(input, chatOptions);

            if (approval)
            {
                input.AddRange(response.Messages);
                var approvalRequest = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
                var mcpCallContent = Assert.IsType<McpServerToolCallContent>(approvalRequest.ToolCall);
                Assert.Equal("search_events", mcpCallContent.Name);
                input.Add(new ChatMessage(ChatRole.Tool, [approvalRequest.CreateResponse(true)]));

                response = streaming ?
                    await client.GetStreamingResponseAsync(input, chatOptions).ToChatResponseAsync() :
                    await client.GetResponseAsync(input, chatOptions);
            }

            Assert.NotNull(response);
            var toolCall = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolCallContent>());
            Assert.Equal("search_events", toolCall.Name);

            var toolResult = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<McpServerToolResultContent>());
            var content = Assert.IsType<TextContent>(Assert.Single(toolResult.Outputs!));
            Assert.Equal(@"{""events"": [], ""next_page_token"": null}", content.Text);
        }
    }

    [Fact]
    public async Task ToolCallResult_TextContent()
    {
        SkipIfNotEnabled();

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((int a, int b) => new TextContent($"The sum is {a + b}"), "AddNumbers", "Adds two numbers together")]
        };

        using var client = new FunctionInvokingChatClient(ChatClient);

        var response = await client.GetResponseAsync("What is 25 plus 17? Use the AddNumbers function.", chatOptions);

        Assert.NotNull(response);

        // The model should have called the function and received "The sum is 42"
        Assert.Contains("42", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_MultipleAIContents()
    {
        SkipIfNotEnabled();

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string city) => new List<AIContent>
            {
                new TextContent($"Weather in {city}: "),
                new TextContent("Sunny, 72°F")
            }, "GetWeather", "Gets the weather for a city")]
        };

        using var client = new FunctionInvokingChatClient(ChatClient);

        var response = await client.GetResponseAsync("What's the weather in Seattle? Use GetWeather.", chatOptions);

        Assert.NotNull(response);

        // Verify the function was called and both parts were included
        var messages = response.Messages.ToList();
        Assert.NotEmpty(messages);

        // Check that we got a response mentioning the weather data
        Assert.Contains("72", response.Text);
    }

    [Fact]
    public async Task ToolCallResult_ImageDataContent()
    {
        SkipIfNotEnabled();

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => new DataContent(ImageDataUri.GetImageDataUri(), "image/png"), "GetDotnetLogo", "Returns the .NET logo image")]
        };

        using var client = new FunctionInvokingChatClient(ChatClient);

        var response = await client.GetResponseAsync("Call GetDotnetLogo and tell me what you see in the image.", chatOptions);

        Assert.NotNull(response);

        // The model should describe seeing the .NET logo or purple/related colors
        Assert.True(
            response.Text.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("purple", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("dot", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("net", StringComparison.OrdinalIgnoreCase),
            $"Expected response to mention logo or colors, but got: {response.Text}");
    }

    [Fact]
    public async Task ToolCallResult_PdfDataContent()
    {
        SkipIfNotEnabled();

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => new DataContent(ImageDataUri.GetPdfDataUri(), "application/pdf") { Name = "document.pdf" }, "GetDocument", "Returns a PDF document")]
        };

        using var client = new FunctionInvokingChatClient(ChatClient);

        var response = await client.GetResponseAsync("Call GetDocument and tell me what text is in the PDF.", chatOptions);

        Assert.NotNull(response);

        // The PDF contains "Hello World!" text
        Assert.Contains("Hello World", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolCallResult_MixedContentWithImage()
    {
        SkipIfNotEnabled();

        var imageUri = ImageDataUri.GetImageDataUri();
        var imageBytes = Convert.FromBase64String(imageUri.ToString().Split(',')[1]);

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => new List<AIContent>
            {
                new TextContent("Analysis result: "),
                new DataContent(imageBytes, "image/png"),
                new TextContent(" - Image provided above")
            }, "AnalyzeImage", "Analyzes an image and returns results")]
        };

        using var client = new FunctionInvokingChatClient(ChatClient);

        var response = await client.GetResponseAsync("Call AnalyzeImage and describe what you see.", chatOptions);

        Assert.NotNull(response);

        // Should mention the analysis and describe the image
        Assert.True(
            response.Text.Contains("analysis", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("logo", StringComparison.OrdinalIgnoreCase),
            $"Expected response to mention analysis or image content, but got: {response.Text}");
    }

    [Fact]
    public async Task ReasoningContent_NonStreaming_RoundtripsEncryptedContent()
    {
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            ModelId = ReasoningModel,
            Reasoning = new()
            {
                Effort = ReasoningEffort.Low,
                Output = ReasoningOutput.Full,
            },
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                IncludedProperties = { IncludedResponseProperty.ReasoningEncryptedContent },
            },
        };

        // 1. First request: Get initial response with encrypted content
        List<ChatMessage> chatHistory = [new ChatMessage(ChatRole.User, "What is 2+2? Think step by step but be very brief.")];

        var response1 = await ChatClient.GetResponseAsync(chatHistory, chatOptions);
        Assert.NotNull(response1);

        // Verify we got reasoning content with encrypted data and RawRepresentation
        var reasoningContent = response1.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .FirstOrDefault();

        Assert.NotNull(reasoningContent);
        Assert.NotNull(reasoningContent.ProtectedData);
        Assert.NotEmpty(reasoningContent.ProtectedData);
        Assert.NotNull(reasoningContent.RawRepresentation);

        // 2. Second request: Uses raw representation
        chatHistory.AddMessages(response1);
        chatHistory.Add(new ChatMessage(ChatRole.User, "What is 3+3?"));

        var response2 = await ChatClient.GetResponseAsync(chatHistory, chatOptions);
        Assert.NotNull(response2);
        Assert.True(response2.Text.Contains("6") || response2.Text.Contains("six"));

        // 3. Serialize/deserialize to drop RawRepresentations, then make third request
        string json = JsonSerializer.Serialize(chatHistory, AIJsonUtilities.DefaultOptions);
        var deserializedHistory = JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions)!;

        // Verify RawRepresentation was dropped but ProtectedData preserved
        var deserializedReasoning = deserializedHistory
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .First(r => !string.IsNullOrEmpty(r.ProtectedData));

        Assert.Null(deserializedReasoning.RawRepresentation);
        Assert.Equal(reasoningContent.ProtectedData, deserializedReasoning.ProtectedData);

        deserializedHistory.Add(new ChatMessage(ChatRole.User, "What is 4+4?"));

        var response3 = await ChatClient.GetResponseAsync(deserializedHistory, chatOptions);
        Assert.NotNull(response3);
        Assert.Contains("8", response3.Text);

        // 4. Corrupt the encrypted content and verify fourth request fails
        foreach (var reasoning in deserializedHistory
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Where(r => !string.IsNullOrEmpty(r.ProtectedData)))
        {
            reasoning.ProtectedData = "completely_invalid_encrypted_content_that_should_fail";
        }

        deserializedHistory.Add(new ChatMessage(ChatRole.User, "What is 5+5?"));

        var ex = await Assert.ThrowsAsync<ClientResultException>(
            () => ChatClient.GetResponseAsync(deserializedHistory, chatOptions));
        Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReasoningContent_Streaming_RoundtripsEncryptedContent()
    {
        // This test requires a reasoning model with encrypted content support.
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            ModelId = ReasoningModel,
            Reasoning = new()
            {
                Effort = ReasoningEffort.Low,
                Output = ReasoningOutput.Full,
            },
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                IncludedProperties = { IncludedResponseProperty.ReasoningEncryptedContent },
            },
        };

        // 1. First request: Get initial response with encrypted content via streaming
        List<ChatMessage> chatHistory = [new ChatMessage(ChatRole.User, "What is 2+2? Think step by step but be very brief.")];

        var response1 = await ChatClient.GetStreamingResponseAsync(chatHistory, chatOptions).ToChatResponseAsync();
        Assert.NotNull(response1);

        // Verify we got reasoning content with encrypted data
        // Note: After coalescing streaming updates, RawRepresentation is not preserved
        var reasoningContent = response1.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .FirstOrDefault();

        Assert.NotNull(reasoningContent);
        Assert.NotNull(reasoningContent.ProtectedData);
        Assert.NotEmpty(reasoningContent.ProtectedData);

        // 2. Second request: Uses the coalesced content (no RawRepresentation after coalescing)
        chatHistory.AddMessages(response1);
        chatHistory.Add(new ChatMessage(ChatRole.User, "What is 3+3?"));

        var response2 = await ChatClient.GetStreamingResponseAsync(chatHistory, chatOptions).ToChatResponseAsync();
        Assert.NotNull(response2);
        Assert.Contains("6", response2.Text);

        // 3. Serialize/deserialize to ensure ProtectedData survives, then make third request
        string json = JsonSerializer.Serialize(chatHistory, AIJsonUtilities.DefaultOptions);
        var deserializedHistory = JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions)!;

        // Verify ProtectedData preserved after serialization
        var deserializedReasoning = deserializedHistory
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .First(r => !string.IsNullOrEmpty(r.ProtectedData));

        Assert.Null(deserializedReasoning.RawRepresentation);
        Assert.Equal(reasoningContent.ProtectedData, deserializedReasoning.ProtectedData);

        deserializedHistory.Add(new ChatMessage(ChatRole.User, "What is 4+4?"));

        var response3 = await ChatClient.GetStreamingResponseAsync(deserializedHistory, chatOptions).ToChatResponseAsync();
        Assert.NotNull(response3);
        Assert.Contains("8", response3.Text);

        // 4. Corrupt the encrypted content and verify fourth request fails
        foreach (var reasoning in deserializedHistory
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Where(r => !string.IsNullOrEmpty(r.ProtectedData)))
        {
            reasoning.ProtectedData = "completely_invalid_encrypted_content_that_should_fail";
        }

        deserializedHistory.Add(new ChatMessage(ChatRole.User, "What is 5+5?"));

        var ex = await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await foreach (var update in ChatClient.GetStreamingResponseAsync(deserializedHistory, chatOptions))
            {
                _ = update;
            }
        });
        Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseToolSearch_WithDeferredFunctions()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        AIFunction getWeather = AIFunctionFactory.Create(() => "Sunny, 72°F", "GetWeather", "Gets the current weather.");
        AIFunction getTime = AIFunctionFactory.Create(() => "3:00 PM", "GetTime", "Gets the current time.");

        using var client = new FunctionInvokingChatClient(ChatClient);
        var response = await client.GetResponseAsync(
            "What's the weather like? Just respond with the weather info, nothing else.",
            new()
            {
                Tools =
                [
                    new HostedToolSearchTool(),
                    getWeather,
                    getTime,
                ],
            });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);

        // Verify tool_search response items occurred.
        var rawJsons = response.Messages
            .SelectMany(m => m.Contents)
            .Where(c => c.RawRepresentation is ResponseItem)
            .Select(c => ModelReaderWriter.Write((ResponseItem)c.RawRepresentation!, ModelReaderWriterOptions.Json).ToString())
            .ToList();
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_call\"") || json.Contains("\"type\": \"tool_search_call\""));
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_output\"") || json.Contains("\"type\": \"tool_search_output\""));
    }

    [Fact]
    public async Task UseToolSearch_OnlyToolSearchNoFunctions_Throws()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        // HostedToolSearchTool with no deferred tools — the API rejects this with 400
        // because tool_search requires at least one tool with defer_loading.
        await Assert.ThrowsAsync<ClientResultException>(() =>
            ChatClient.GetResponseAsync(
                "Say hello.",
                new()
                {
                    Tools = [new HostedToolSearchTool()],
                }));
    }

    [Fact]
    public async Task UseToolSearch_WithNonDeferredFunctionsOnly_Throws()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        // HostedToolSearchTool with DeferredTools explicitly set to empty — no tools are deferred.
        // The API rejects this with 400 because tool_search requires at least one deferred tool.
        AIFunction getWeather = AIFunctionFactory.Create(() => "Sunny, 72°F", "GetWeather", "Gets the current weather.");

        await Assert.ThrowsAsync<ClientResultException>(() =>
            ChatClient.GetResponseAsync(
                "What's the weather? Reply with just the weather info.",
                new()
                {
                    Tools =
                    [
                        new HostedToolSearchTool { DeferredTools = [] },
                        getWeather,
                    ],
                }));
    }

    [Fact]
    public async Task UseToolSearch_DeferLoadingOnNonDeferrableTool_Throws()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        // Force defer_loading on a code_interpreter tool via Patch — the API should reject this.
        var codeTool = new HostedCodeInterpreterTool();
        var responseTool = codeTool.AsOpenAIResponseTool()!;
#pragma warning disable SCME0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
        responseTool.Patch.Set("$.defer_loading"u8, "true"u8);
#pragma warning restore SCME0001

        await Assert.ThrowsAsync<ClientResultException>(() =>
            ChatClient.GetResponseAsync(
                "Use code interpreter to calculate 2+2.",
                new()
                {
                    Tools =
                    [
                        responseTool.AsAITool(),
                    ],
                }));
    }

    [Fact]
    public async Task UseToolSearch_NamespaceWithDescription_RoundTrips()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        AIFunction getWeather = AIFunctionFactory.Create(() => "Sunny, 72°F", "GetWeather", "Gets the current weather.");
        AIFunction getTime = AIFunctionFactory.Create(() => "3:00 PM", "GetTime", "Gets the current time.");

        using var client = new FunctionInvokingChatClient(ChatClient);
        var response = await client.GetResponseAsync(
            "What's the weather like? Just respond with the weather info, nothing else.",
            new()
            {
                Tools =
                [
                    new HostedToolSearchTool
                    {
                        Namespace = "weather_and_time",
                        NamespaceDescription = "Tools for getting current weather and time.",
                        DeferredTools = ["GetWeather", "GetTime"],
                    },
                    getWeather,
                    getTime,
                ],
            });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);

        // Verify tool_search response items occurred (the namespace wrapper must have been
        // accepted by the service for tool search to fire).
        var rawJsons = response.Messages
            .SelectMany(m => m.Contents)
            .Where(c => c.RawRepresentation is ResponseItem)
            .Select(c => ModelReaderWriter.Write((ResponseItem)c.RawRepresentation!, ModelReaderWriterOptions.Json).ToString())
            .ToList();
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_call\"") || json.Contains("\"type\": \"tool_search_call\""));
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_output\"") || json.Contains("\"type\": \"tool_search_output\""));
    }

    [Fact]
    public async Task UseToolSearch_TwoNamespacesWithDescriptions_RoundTrips()
    {
        SkipIfNotEnabled();

        Assert.SkipUnless(TestRunnerConfiguration.Instance["OpenAI:ChatModel"]?.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) is true, "Tool search requires gpt-5.4 or later.");

        AIFunction getWeather = AIFunctionFactory.Create(() => "Sunny, 72°F", "GetWeather", "Gets the current weather.");
        AIFunction getTime = AIFunctionFactory.Create(() => "3:00 PM", "GetTime", "Gets the current time.");
        AIFunction getCustomer = AIFunctionFactory.Create((string id) => $"Customer {id}", "GetCustomer", "Gets a customer by id.");

        using var client = new FunctionInvokingChatClient(ChatClient);
        var response = await client.GetResponseAsync(
            "What's the weather like? Just respond with the weather info, nothing else.",
            new()
            {
                Tools =
                [
                    new HostedToolSearchTool
                    {
                        Namespace = "weather_and_time",
                        NamespaceDescription = "Tools for getting current weather and time.",
                        DeferredTools = ["GetWeather", "GetTime"],
                    },
                    new HostedToolSearchTool
                    {
                        Namespace = "crm",
                        NamespaceDescription = "Customer relationship management tools.",
                        DeferredTools = ["GetCustomer"],
                    },
                    getWeather,
                    getTime,
                    getCustomer,
                ],
            });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);

        // Verify tool_search response items occurred (both namespace wrappers must have been
        // accepted by the service for tool search to fire).
        var rawJsons = response.Messages
            .SelectMany(m => m.Contents)
            .Where(c => c.RawRepresentation is ResponseItem)
            .Select(c => ModelReaderWriter.Write((ResponseItem)c.RawRepresentation!, ModelReaderWriterOptions.Json).ToString())
            .ToList();
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_call\"") || json.Contains("\"type\": \"tool_search_call\""));
        Assert.Contains(rawJsons, json => json.Contains("\"type\":\"tool_search_output\"") || json.Contains("\"type\": \"tool_search_output\""));
    }
}
