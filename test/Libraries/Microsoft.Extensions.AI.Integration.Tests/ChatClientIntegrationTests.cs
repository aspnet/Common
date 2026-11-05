// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Trace;
using Xunit;

#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2214 // Do not call overridable methods in constructors
#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'
#pragma warning disable S103 // Lines should not be too long
#pragma warning disable S1144 // Unused private types or members should be removed
#pragma warning disable S3604 // Member initializer values should not be redundant
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line

namespace Microsoft.Extensions.AI;

public abstract class ChatClientIntegrationTests : IDisposable
{
    protected ChatClientIntegrationTests()
    {
        ChatClient = CreateChatClient();
    }

    protected IChatClient? ChatClient { get; }

    public void Dispose()
    {
        ChatClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract IChatClient? CreateChatClient();

    [Fact]
    public virtual async Task GetResponseAsync_SingleRequestMessage()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync("What's the biggest animal?");

        Assert.Contains("whale", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public virtual async Task GetResponseAsync_MultipleRequestMessages()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
        [
            new(ChatRole.User, "Pick a city, any city"),
            new(ChatRole.Assistant, "Seattle"),
            new(ChatRole.User, "And another one"),
            new(ChatRole.Assistant, "Jakarta"),
            new(ChatRole.User, "What continent are they each in?"),
        ]);

        Assert.Contains("America", response.Text);
        Assert.Contains("Asia", response.Text);
    }

    [Fact]
    public virtual async Task GetResponseAsync_WithEmptyMessage()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
        [
            new(ChatRole.System, []),
            new(ChatRole.User, []),
            new(ChatRole.Assistant, []),
            new(ChatRole.User, "What is 1 + 2? Reply with a single number."),
        ]);

        Assert.Contains("3", response.Text);
    }

    [Fact]
    public virtual async Task GetStreamingResponseAsync()
    {
        SkipIfNotEnabled();

        IList<ChatMessage> chatHistory =
        [
            new(ChatRole.User, "Quote, word for word, Neil Armstrong's famous words.")
        ];

        StringBuilder sb = new();
        await foreach (var chunk in ChatClient.GetStreamingResponseAsync(chatHistory))
        {
            sb.Append(chunk.Text);
        }

        string responseText = sb.ToString();
        Assert.Contains("one small step", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one giant leap", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public virtual async Task GetResponseAsync_UsageDataAvailable()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync("Explain in 10 words how AI works");

        Assert.True(response.Usage?.InputTokenCount > 1);
        Assert.True(response.Usage?.OutputTokenCount > 1);
        Assert.Equal(response.Usage?.InputTokenCount + response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount);
    }

    [Fact]
    public virtual async Task GetStreamingResponseAsync_UsageDataAvailable()
    {
        SkipIfNotEnabled();

        var response = ChatClient.GetStreamingResponseAsync("Explain in 10 words how AI works", new()
        {
            AdditionalProperties = new()
            {
                ["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true, },
            },
        });

        List<ChatResponseUpdate> chunks = [];
        await foreach (var chunk in response)
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks.Count > 1);

        UsageContent usage = chunks.SelectMany(c => c.Contents).OfType<UsageContent>().Single();
        Assert.True(usage.Details.InputTokenCount > 1);
        Assert.True(usage.Details.OutputTokenCount > 1);
        Assert.Equal(usage.Details.InputTokenCount + usage.Details.OutputTokenCount, usage.Details.TotalTokenCount);
    }

    [Fact]
    public virtual async Task GetStreamingResponseAsync_AppendToHistory()
    {
        SkipIfNotEnabled();

        List<ChatMessage> history = [new(ChatRole.User, "Explain in 100 words how AI works")];

        var streamingResponse = ChatClient.GetStreamingResponseAsync(history);

        Assert.Single(history);
        await history.AddMessagesAsync(streamingResponse);
        Assert.Equal(2, history.Count);
        Assert.Equal(ChatRole.Assistant, history[1].Role);

        var singleTextContent = (TextContent)history[1].Contents.Single();
        Assert.NotEmpty(singleTextContent.Text);
        Assert.Equal(history[1].Text, singleTextContent.Text);
    }

    protected virtual string? GetModel_MultiModal_DescribeImage() => null;

    [Fact]
    public virtual async Task MultiModal_DescribeImage()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
            [
                new(ChatRole.User,
                [
                    new TextContent("What does this logo say?"),
                    new DataContent(ImageDataUri.GetImageDataUri(), "image/png"),
                ])
            ],
            new() { ModelId = GetModel_MultiModal_DescribeImage() });

        Assert.True(response.Text.IndexOf("net", StringComparison.OrdinalIgnoreCase) >= 0, response.Text);
    }

    [Fact]
    public virtual async Task MultiModal_DescribePdf()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
            [
                new(ChatRole.User,
                [
                    new TextContent("What text does this document contain?"),
                    new DataContent(ImageDataUri.GetPdfDataUri(), "application/pdf") { Name = "sample.pdf" },
                ])
            ],
            new() { ModelId = GetModel_MultiModal_DescribeImage() });

        Assert.True(response.Text.IndexOf("hello", StringComparison.OrdinalIgnoreCase) >= 0, response.Text);
    }

    [Fact]
    public virtual async Task FunctionInvocation_AutomaticallyInvokeFunction_Parameterless()
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var chatClient = new FunctionInvokingChatClient(
            new OpenTelemetryChatClient(ChatClient, sourceName: sourceName));

        int secretNumber = 42;

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "What is the current secret number?")
        ];

        var response = await chatClient.GetResponseAsync(messages, new()
        {
            Tools = [AIFunctionFactory.Create(() => secretNumber, "GetSecretNumber")]
        });

        Assert.Contains(secretNumber.ToString(), response.Text);
        AssertUsageAgainstActivities(response, activities);
    }

    [Fact]
    public virtual async Task FunctionInvocation_AutomaticallyInvokeFunction_WithParameters_NonStreaming()
    {
        SkipIfNotEnabled();

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        var response = await chatClient.GetResponseAsync("What is the result of SecretComputation on 42 and 84?", new()
        {
            Tools = [AIFunctionFactory.Create((int a, int b) => a * b, "SecretComputation")]
        });

        Assert.Contains("3528", response.Text);
    }

    [Fact]
    public virtual async Task FunctionInvocation_AutomaticallyInvokeFunction_WithParameters_Streaming()
    {
        SkipIfNotEnabled();

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        var response = chatClient.GetStreamingResponseAsync("What is the result of SecretComputation on 42 and 84?", new()
        {
            Tools = [AIFunctionFactory.Create((int a, int b) => a * b, "SecretComputation")]
        });

        StringBuilder sb = new();
        await foreach (var chunk in response)
        {
            sb.Append(chunk.Text);
        }

        Assert.Contains("3528", sb.ToString());
    }

    [Fact]
    public virtual async Task FunctionInvocation_OptionalParameter()
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var chatClient = new FunctionInvokingChatClient(
            new OpenTelemetryChatClient(ChatClient, sourceName: sourceName));

        int secretNumber = 42;

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "What is the secret number for id foo?")
        ];

        AIFunction func = AIFunctionFactory.Create((string id = "defaultId") => id is "foo" ? secretNumber : -1, "GetSecretNumberById");
        var response = await chatClient.GetResponseAsync(messages, new()
        {
            Tools = [func]
        });

        Assert.Contains(secretNumber.ToString(), response.Text);
        AssertUsageAgainstActivities(response, activities);
    }

    [Fact]
    public virtual async Task FunctionInvocation_NestedParameters()
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var chatClient = new FunctionInvokingChatClient(
            new OpenTelemetryChatClient(ChatClient, sourceName: sourceName));

        int secretNumber = 42;

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "What is the secret number for John aged 19?")
        ];

        AIFunction func = AIFunctionFactory.Create((PersonRecord person) => person.Name is "John" ? secretNumber + person.Age : -1, "GetSecretNumberByPerson");
        var response = await chatClient.GetResponseAsync(messages, new()
        {
            Tools = [func]
        });

        Assert.Contains((secretNumber + 19).ToString(), response.Text);
        AssertUsageAgainstActivities(response, activities);
    }

    [Fact]
    public virtual async Task FunctionInvocation_ArrayParameter()
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var chatClient = new FunctionInvokingChatClient(
            new OpenTelemetryChatClient(ChatClient, sourceName: sourceName));

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "Can you add bacon, lettuce, and tomatoes to Peter's shopping cart?")
        ];

        string? shopperName = null;
        List<string> shoppingCart = [];
        AIFunction func = AIFunctionFactory.Create((string[] items, string shopperId) => { shoppingCart.AddRange(items); shopperName = shopperId; }, "AddItemsToShoppingCart");
        var response = await chatClient.GetResponseAsync(messages, new()
        {
            Tools = [func]
        });

        Assert.Equal("Peter", shopperName);
        Assert.Equal(["bacon", "lettuce", "tomatoes"], shoppingCart);
        AssertUsageAgainstActivities(response, activities);
    }

    private static void AssertUsageAgainstActivities(ChatResponse response, List<Activity> activities)
    {
        // If the underlying IChatClient provides usage data, function invocation should aggregate the
        // usage data across all calls to produce a single Usage value on the final response.
        // The FunctionInvokingChatClient then itself creates a span that will also be tagged with a sum
        // across all consituent calls, which means our final answer will be double.
        if (response.Usage is { } finalUsage)
        {
            var totalInputTokens = activities.Sum(a => (int?)a.GetTagItem("gen_ai.usage.input_tokens")!);
            var totalOutputTokens = activities.Sum(a => (int?)a.GetTagItem("gen_ai.usage.output_tokens")!);
            Assert.Equal(totalInputTokens, finalUsage.InputTokenCount * 2);
            Assert.Equal(totalOutputTokens, finalUsage.OutputTokenCount * 2);
        }
    }

    public record PersonRecord(string Name, int Age = 42);

    [Fact]
    public virtual Task AvailableTools_SchemasAreAccepted_Strict() =>
        AvailableTools_SchemasAreAccepted(strict: true);

    [Fact]
    public virtual Task AvailableTools_SchemasAreAccepted_NonStrict() =>
        AvailableTools_SchemasAreAccepted(strict: false);

    private async Task AvailableTools_SchemasAreAccepted(bool strict)
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using var chatClient = new FunctionInvokingChatClient(
            new OpenTelemetryChatClient(ChatClient, sourceName: sourceName));

        int methodCount = 1;
        Func<AIFunctionFactoryOptions> createOptions = () =>
        {
            AIFunctionFactoryOptions aiFuncOptions = new()
            {
                Name = $"Method{methodCount++}",
            };

            if (strict)
            {
                aiFuncOptions.AdditionalProperties = new Dictionary<string, object?> { ["strict"] = true };
            }

            return aiFuncOptions;
        };

        Func<string, AIFunction> createWithSchema = schema =>
        {
            Dictionary<string, object?> additionalProperties = new();

            if (strict)
            {
                additionalProperties["strict"] = true;
            }

            return new CustomAIFunction($"CustomMethod{methodCount++}", schema, additionalProperties);
        };

        ChatOptions options = new()
        {
            MaxOutputTokens = 100,
            Tools =
            [
                // Using AIFunctionFactory
                AIFunctionFactory.Create((int? i) => i, createOptions()),
                AIFunctionFactory.Create((string? s) => s, createOptions()),
                AIFunctionFactory.Create((int? i = null) => i, createOptions()),
                AIFunctionFactory.Create((bool b) => b, createOptions()),
                AIFunctionFactory.Create((double d) => d, createOptions()),
                AIFunctionFactory.Create((decimal d) => d, createOptions()),
                AIFunctionFactory.Create((float f) => f, createOptions()),
                AIFunctionFactory.Create((long l) => l, createOptions()),
                AIFunctionFactory.Create((char c) => c, createOptions()),
                AIFunctionFactory.Create((DateTime dt) => dt, createOptions()),
                AIFunctionFactory.Create((DateTimeOffset? dt) => dt, createOptions()),
                AIFunctionFactory.Create((TimeSpan ts) => ts, createOptions()),
#if NET
                AIFunctionFactory.Create((DateOnly d) => d, createOptions()),
                AIFunctionFactory.Create((TimeOnly t) => t, createOptions()),
#endif
                AIFunctionFactory.Create((Uri uri) => uri, createOptions()),
                AIFunctionFactory.Create((Guid guid) => guid, createOptions()),
                AIFunctionFactory.Create((List<int> list) => list, createOptions()),
                AIFunctionFactory.Create((int[] arr, ComplexObject? co) => arr, createOptions()),
                AIFunctionFactory.Create((string p1 = "str", int p2 = 42, BindingFlags p3 = BindingFlags.IgnoreCase, char p4 = 'x') => p1, createOptions()),
                AIFunctionFactory.Create((string? p1 = "str", int? p2 = 42, BindingFlags? p3 = BindingFlags.IgnoreCase, char? p4 = 'x') => p1, createOptions()),

                // Selection from @modelcontextprotocol/server-everything
                createWithSchema("""
                    {"type":"object","properties":{},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),
                createWithSchema("""
                    {"type":"object","properties":{"duration":{"type":"number","default":10,"description":"Duration of the operation in seconds"},"steps":{"type":"number","default":5,"description":"Number of steps in the operation"}},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),
                createWithSchema("""
                    {"type":"object","properties":{"prompt":{"type":"string","description":"The prompt to send to the LLM"},"maxTokens":{"type":"number","default":100,"description":"Maximum number of tokens to generate"}},"required":["prompt"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),
                createWithSchema("""
                    {"type":"object","properties":{},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),
                createWithSchema("""
                    {"type":"object","properties":{"messageType":{"type":"string","enum":["error","success","debug"],"description":"Type of message to demonstrate different annotation patterns"},"includeImage":{"type":"boolean","default":false,"description":"Whether to include an example image"}},"required":["messageType"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),
                createWithSchema("""
                    {"type":"object","properties":{"resourceId":{"type":"number","minimum":1,"maximum":100,"description":"ID of the resource to reference (1-100)"}},"required":["resourceId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
                    """),

                // Selection from GH MCP server
                createWithSchema("""
                    {"properties":{"body":{"description":"The text of the review comment","type":"string"},"line":{"description":"The line of the blob in the pull request diff that the comment applies to. For multi-line comments, the last line of the range","type":"number"},"owner":{"description":"Repository owner","type":"string"},"path":{"description":"The relative path to the file that necessitates a comment","type":"string"},"pullNumber":{"description":"Pull request number","type":"number"},"repo":{"description":"Repository name","type":"string"},"side":{"description":"The side of the diff to comment on. LEFT indicates the previous state, RIGHT indicates the new state","enum":["LEFT","RIGHT"],"type":"string"},"startLine":{"description":"For multi-line comments, the first line of the range that the comment applies to","type":"number"},"startSide":{"description":"For multi-line comments, the starting side of the diff that the comment applies to. LEFT indicates the previous state, RIGHT indicates the new state","enum":["LEFT","RIGHT"],"type":"string"},"subjectType":{"description":"The level at which the comment is targeted","enum":["FILE","LINE"],"type":"string"}},"required":["owner","repo","pullNumber","path","body","subjectType"],"type":"object"}
                    """),
                createWithSchema("""
                    {"properties":{"commit_message":{"description":"Extra detail for merge commit","type":"string"},"commit_title":{"description":"Title for merge commit","type":"string"},"merge_method":{"description":"Merge method","enum":["merge","squash","rebase"],"type":"string"},"owner":{"description":"Repository owner","type":"string"},"pullNumber":{"description":"Pull request number","type":"number"},"repo":{"description":"Repository name","type":"string"}},"required":["owner","repo","pullNumber"],"type":"object"}
                    """),

                createWithSchema("""
                    {"type":"object","properties":{"labels":{"type":"object"}},"additionalProperties":{"type":"string"}}
                    """),
            ],
        };

        // We don't care about the response, only that we get one and that an exception isn't thrown due to unacceptable schema.
        var response = await chatClient.GetResponseAsync("Briefly, what is the most popular tower in Paris?", options);
        Assert.NotNull(response);
    }

    private sealed class CustomAIFunction(string name, string jsonSchema, IReadOnlyDictionary<string, object?> additionalProperties) : AIFunction
    {
        public override string Name => name;
        public override IReadOnlyDictionary<string, object?> AdditionalProperties => additionalProperties;
        public override JsonElement JsonSchema { get; } = JsonElement.Parse(jsonSchema);
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private class ComplexObject
    {
        [DisplayName("Something cool")]
#if NET
        [DeniedValues("abc", "def", "default")]
#endif
        public string? SomeString { get; set; }

#if NET
        [AllowedValues("abc", "def", "default")]
#endif
        public string AnotherString { get; set; } = "default";

#if NET
        [Range(25, 75)]
#endif
        public int Value { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        [RegularExpression("[abc]")]
        public string? RegexString { get; set; }

        [StringLength(42)]
        public string MeasuredString { get; set; } = "default";

#if NET
        [Length(1, 2)]
#endif
        public int[]? MeasuredArray1 { get; set; }

#if NET
        [MinLength(1)]
#endif
        public int[]? MeasuredArray2 { get; set; }

#if NET
        [MaxLength(10)]
#endif
        public int[]? MeasuredArray3 { get; set; }
    }

    protected virtual bool SupportsParallelFunctionCalling => true;

    [Fact]
    public virtual async Task FunctionInvocation_SupportsMultipleParallelRequests()
    {
        SkipIfNotEnabled();
        Assert.SkipUnless(SupportsParallelFunctionCalling, "Parallel function calling is not supported by this chat client");

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        // The service/model isn't guaranteed to request two calls to GetPersonAge in the same turn, but it's common that it will.
        var response = await chatClient.GetResponseAsync("How much older is Elsa than Anna? Return the age difference as a single number.", new()
        {
            Tools = [AIFunctionFactory.Create((string personName) =>
            {
                return personName switch
                {
                    "Elsa" => 21,
                    "Anna" => 18,
                    _ => 30,
                };
            }, "GetPersonAge")]
        });

        Assert.True(
            Regex.IsMatch(response.Text ?? "", @"\b(3|three)\b", RegexOptions.IgnoreCase),
            $"Doesn't contain three: {response.Text}");
    }

    [Fact]
    public virtual async Task FunctionInvocation_RequireAny()
    {
        SkipIfNotEnabled();

        int callCount = 0;
        var tool = AIFunctionFactory.Create(() =>
        {
            callCount++;
            return 123;
        }, "GetSecretNumber");

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        var response = await chatClient.GetResponseAsync("Are birds real?", new()
        {
            Tools = [tool],
            ToolMode = ChatToolMode.RequireAny,
        });

        Assert.True(callCount >= 1);
    }

    [Fact]
    public virtual async Task FunctionInvocation_RequireSpecific()
    {
        SkipIfNotEnabled();

        bool shieldsUp = false;
        var getSecretNumberTool = AIFunctionFactory.Create(() => 123, "GetSecretNumber");
        var shieldsUpTool = AIFunctionFactory.Create(() => shieldsUp = true, "ShieldsUp");

        using var chatClient = new FunctionInvokingChatClient(ChatClient);

        // Even though the user doesn't ask for the shields to be activated, verify that the tool is invoked
        var response = await chatClient.GetResponseAsync("What's the current secret number?", new()
        {
            Tools = [getSecretNumberTool, shieldsUpTool],
            ToolMode = ChatToolMode.RequireSpecific(shieldsUpTool.Name),
        });

        Assert.True(shieldsUp);
    }

    [Fact]
    public virtual async Task Caching_OutputVariesWithoutCaching()
    {
        SkipIfNotEnabled();

        var message = new ChatMessage(ChatRole.User, "Pick a random number, uniformly distributed between 1 and 1000000");
        var firstResponse = await ChatClient.GetResponseAsync([message]);

        var secondResponse = await ChatClient.GetResponseAsync([message]);
        Assert.NotEqual(firstResponse.Text, secondResponse.Text);
    }

    [Fact]
    public virtual async Task Caching_SamePromptResultsInCacheHit_NonStreaming()
    {
        SkipIfNotEnabled();

        using var chatClient = new DistributedCachingChatClient(
            ChatClient,
            new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())));

        var message = new ChatMessage(ChatRole.User, "Pick a random number, uniformly distributed between 1 and 1000000");
        var firstResponse = await chatClient.GetResponseAsync([message]);

        // No matter what it said before, we should see identical output due to caching
        for (int i = 0; i < 3; i++)
        {
            var secondResponse = await chatClient.GetResponseAsync([message]);
            Assert.Equal(firstResponse.Messages.Select(m => m.Text), secondResponse.Messages.Select(m => m.Text));
        }

        // ... but if the conversation differs, we should see different output
        ((TextContent)message.Contents[0]).Text += "!";
        var thirdResponse = await chatClient.GetResponseAsync([message]);
        Assert.NotEqual(firstResponse.Messages, thirdResponse.Messages);
    }

    [Fact]
    public virtual async Task Caching_SamePromptResultsInCacheHit_Streaming()
    {
        SkipIfNotEnabled();

        using var chatClient = new DistributedCachingChatClient(
            ChatClient,
            new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())));

        var message = new ChatMessage(ChatRole.User, "Pick a random number, uniformly distributed between 1 and 1000000");
        StringBuilder orig = new();
        await foreach (var update in chatClient.GetStreamingResponseAsync([message]))
        {
            orig.Append(update.Text);
        }

        // No matter what it said before, we should see identical output due to caching
        for (int i = 0; i < 3; i++)
        {
            StringBuilder second = new();
            await foreach (var update in chatClient.GetStreamingResponseAsync([message]))
            {
                second.Append(update.Text);
            }

            Assert.Equal(orig.ToString(), second.ToString());
        }

        // ... but if the conversation differs, we should see different output
        ((TextContent)message.Contents[0]).Text += "!";
        StringBuilder third = new();
        await foreach (var update in chatClient.GetStreamingResponseAsync([message]))
        {
            third.Append(update.Text);
        }

        Assert.NotEqual(orig.ToString(), third.ToString());
    }

    [Fact]
    public virtual async Task Caching_BeforeFunctionInvocation_AvoidsExtraCalls()
    {
        SkipIfNotEnabled();

        int functionCallCount = 0;
        var getTemperature = AIFunctionFactory.Create([Description("Gets the current temperature")] () =>
        {
            functionCallCount++;
            return $"{100 + functionCallCount} degrees celsius";
        }, "GetTemperature");

        // First call executes the function and calls the LLM
        using var chatClient = CreateChatClient()!
            .AsBuilder()
            .ConfigureOptions(options => options.Tools = [getTemperature])
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .UseFunctionInvocation()
            .UseCallCounting()
            .Build();

        var llmCallCount = chatClient.GetService<CallCountingChatClient>();
        var message = new ChatMessage(ChatRole.User, "What is the temperature?");
        var response = await chatClient.GetResponseAsync([message]);
        Assert.Contains("101", response.Text);

        // First LLM call tells us to call the function, second deals with the result
        Assert.Equal(2, llmCallCount!.CallCount);

        // Second call doesn't execute the function or call the LLM, but rather just returns the cached result
        var secondResponse = await chatClient.GetResponseAsync([message]);
        Assert.Equal(response.Text, secondResponse.Text);
        Assert.Equal(1, functionCallCount);
        Assert.Equal(2, llmCallCount!.CallCount);
    }

    [Fact]
    public virtual async Task Caching_AfterFunctionInvocation_FunctionOutputUnchangedAsync()
    {
        SkipIfNotEnabled();

        // This means that if the function call produces the same result, we can avoid calling the LLM
        // whereas if the function call produces a different result, we do call the LLM

        var functionCallCount = 0;
        var getTemperature = AIFunctionFactory.Create([Description("Gets the current temperature")] () =>
        {
            functionCallCount++;
            return "58 degrees celsius";
        }, "GetTemperature");

        // First call executes the function and calls the LLM
        using var chatClient = CreateChatClient()!
            .AsBuilder()
            .ConfigureOptions(options => options.Tools = [getTemperature])
            .UseFunctionInvocation()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .UseCallCounting()
            .Build();

        var llmCallCount = chatClient.GetService<CallCountingChatClient>();
        var message = new ChatMessage(ChatRole.User, "What is the temperature?");
        var response = await chatClient.GetResponseAsync([message]);
        Assert.Contains("58", response.Text);

        // First LLM call tells us to call the function, second deals with the result
        Assert.Equal(1, functionCallCount);
        Assert.Equal(2, llmCallCount!.CallCount);

        // Second time, the calls to the LLM don't happen, but the function is called again
        var secondResponse = await chatClient.GetResponseAsync([message]);
        Assert.Equal(2, functionCallCount);
        Assert.Equal(FunctionInvokingChatClientSetsConversationId ? 3 : 2, llmCallCount!.CallCount);
        Assert.Equal(response.Text, secondResponse.Text);
    }

    public virtual bool FunctionInvokingChatClientSetsConversationId => false;

    [Fact]
    public virtual async Task Caching_AfterFunctionInvocation_FunctionOutputChangedAsync()
    {
        SkipIfNotEnabled();

        // This means that if the function call produces the same result, we can avoid calling the LLM
        // whereas if the function call produces a different result, we do call the LLM

        var functionCallCount = 0;
        var getTemperature = AIFunctionFactory.Create([Description("Gets the current temperature")] () =>
        {
            functionCallCount++;
            return $"{80 + functionCallCount} degrees celsius";
        }, "GetTemperature");

        // First call executes the function and calls the LLM
        using var chatClient = CreateChatClient()!
            .AsBuilder()
            .ConfigureOptions(options => options.Tools = [getTemperature])
            .UseFunctionInvocation()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .UseCallCounting()
            .Build();

        var llmCallCount = chatClient.GetService<CallCountingChatClient>();
        var message = new ChatMessage(ChatRole.User, "What is the temperature?");
        var response = await chatClient.GetResponseAsync([message]);
        Assert.Contains("81", response.Text);

        // First LLM call tells us to call the function, second deals with the result
        Assert.Equal(1, functionCallCount);
        Assert.Equal(2, llmCallCount!.CallCount);

        // Second time, the first call to the LLM don't happen, but the function is called again,
        // and since its output now differs, we no longer hit the cache so the second LLM call does happen
        var secondResponse = await chatClient.GetResponseAsync([message]);
        Assert.Contains("82", secondResponse.Text);
        Assert.Equal(2, functionCallCount);
        Assert.Equal(3, llmCallCount!.CallCount);
    }

    [Fact]
    public virtual async Task Logging_LogsCalls_NonStreaming()
    {
        SkipIfNotEnabled();

        var collector = new FakeLogCollector();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Trace));

        using var chatClient = CreateChatClient()!.AsBuilder()
            .UseLogging(loggerFactory)
            .Build();

        await chatClient.GetResponseAsync([new(ChatRole.User, "What's the biggest animal?")]);

        Assert.Collection(collector.GetSnapshot(),
            entry => Assert.Contains("What's the biggest animal?", entry.Message),
            entry => Assert.Contains("whale", entry.Message));
    }

    [Fact]
    public virtual async Task Logging_LogsCalls_Streaming()
    {
        SkipIfNotEnabled();

        var collector = new FakeLogCollector();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Trace));

        using var chatClient = CreateChatClient()!.AsBuilder()
            .UseLogging(loggerFactory)
            .Build();

        await foreach (var update in chatClient.GetStreamingResponseAsync("What's the biggest animal?"))
        {
            // Do nothing with the updates
        }

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("What's the biggest animal?"));
        Assert.Contains(logs, e => e.Message.Contains("whale"));
    }

    [Fact]
    public virtual async Task Logging_LogsFunctionCalls_NonStreaming()
    {
        SkipIfNotEnabled();

        var collector = new FakeLogCollector();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Trace));

        using var chatClient = CreateChatClient()!
            .AsBuilder()
            .UseFunctionInvocation()
            .UseLogging(loggerFactory)
            .Build();

        int secretNumber = 42;
        await chatClient.GetResponseAsync(
            "What is the current secret number?",
            new ChatOptions { Tools = [AIFunctionFactory.Create(() => secretNumber, "GetSecretNumber")] });

        Assert.Collection(collector.GetSnapshot(),
            entry => Assert.Contains("What is the current secret number?", entry.Message),
            entry => Assert.Contains("\"name\": \"GetSecretNumber\"", entry.Message),
            entry => Assert.Contains($"\"result\": {secretNumber}", entry.Message),
            entry => Assert.Contains(secretNumber.ToString(), entry.Message));
    }

    [Fact]
    public virtual async Task Logging_LogsFunctionCalls_Streaming()
    {
        SkipIfNotEnabled();

        var collector = new FakeLogCollector();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Trace));

        using var chatClient = CreateChatClient()!
            .AsBuilder()
            .UseFunctionInvocation()
            .UseLogging(loggerFactory)
            .Build();

        int secretNumber = 42;
        await foreach (var update in chatClient.GetStreamingResponseAsync(
            "What is the current secret number?",
            new ChatOptions { Tools = [AIFunctionFactory.Create(() => secretNumber, "GetSecretNumber")] }))
        {
            // Do nothing with the updates
        }

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("What is the current secret number?"));
        Assert.Contains(logs, e => e.Message.Contains("\"name\": \"GetSecretNumber\""));
        Assert.Contains(logs, e => e.Message.Contains($"\"result\": {secretNumber}"));
    }

    [Fact]
    public virtual async Task OpenTelemetry_CanEmitTracesAndMetrics()
    {
        SkipIfNotEnabled();

        var sourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(activities)
            .Build();

        var chatClient = CreateChatClient()!.AsBuilder()
            .UseOpenTelemetry(sourceName: sourceName)
            .Build();

        var response = await chatClient.GetResponseAsync([new(ChatRole.User, "What's the biggest animal?")]);

        var activity = Assert.Single(activities);
        Assert.StartsWith("chat", activity.DisplayName);
        Assert.Contains(".", (string)activity.GetTagItem("server.address")!);
        Assert.Equal(chatClient.GetService<ChatClientMetadata>()?.ProviderUri?.Port, (int)activity.GetTagItem("server.port")!);
        Assert.NotNull(activity.Id);
        Assert.NotEmpty(activity.Id);
        Assert.NotEqual(0, (int)activity.GetTagItem("gen_ai.usage.input_tokens")!);
        Assert.NotEqual(0, (int)activity.GetTagItem("gen_ai.usage.output_tokens")!);

        Assert.True(activity.Duration.TotalMilliseconds > 0);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutput()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<Person>("""
            Who is described in the following sentence?
            Jimbo Smith is a 35-year-old programmer from Cardiff, Wales.
            """);

        Assert.Equal("Jimbo Smith", response.Result.FullName);
        Assert.Equal(35, response.Result.AgeInYears);
        Assert.Contains("Cardiff", response.Result.HomeTown);
        Assert.Equal(JobType.Programmer, response.Result.Job);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputArray()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<Person[]>("""
            Who are described in the following sentence?
            Jimbo Smith is a 35-year-old software developer from Cardiff, Wales.
            Josh Simpson is a 25-year-old software developer from Newport, Wales.
            """);

        Assert.Equal(2, response.Result.Length);
        Assert.Contains(response.Result, x => x.FullName == "Jimbo Smith");
        Assert.Contains(response.Result, x => x.FullName == "Josh Simpson");
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputInteger()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<int>("""
            There were 14 abstractions for AI programming, which was too many.
            To fix this we added another one. How many are there now?
            """);

        Assert.Equal(15, response.Result);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputString()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<string>("""
            The software developer, Jimbo Smith, is a 35-year-old from Cardiff, Wales.
            What's his full name?
            """);

        Assert.Equal("Jimbo Smith", response.Result);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputBool_True()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<bool>("""
            Jimbo Smith is a 35-year-old software developer from Cardiff, Wales.
            Is there at least one software developer from Cardiff?
            """);

        Assert.True(response.Result);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputBool_False()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<bool>("""
            Jimbo Smith is a 35-year-old software developer from Cardiff, Wales.
            Reply true if the previous statement indicates that he is a medical doctor, otherwise false.
            """);

        Assert.False(response.Result);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutputEnum()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync<JobType>("""
            Taylor Swift is a famous singer and songwriter. What is her job?
            """);

        Assert.Equal(JobType.PopStar, response.Result);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutput_WithFunctions()
    {
        SkipIfNotEnabled();

        var expectedPerson = new Person
        {
            FullName = "Jimbo Smith",
            AgeInYears = 35,
            HomeTown = "Cardiff",
            Job = JobType.Programmer,
        };

        using var chatClient = new FunctionInvokingChatClient(ChatClient);
        var response = await chatClient.GetResponseAsync<Person>(
            "Who is person with ID 123?", new ChatOptions
            {
                Tools = [AIFunctionFactory.Create((int personId) =>
                {
                    Assert.Equal(123, personId);
                    return expectedPerson;
                }, "GetPersonById")]
            });

        Assert.NotSame(expectedPerson, response.Result);
        Assert.Equal(expectedPerson.FullName, response.Result.FullName);
        Assert.Equal(expectedPerson.AgeInYears, response.Result.AgeInYears);
        Assert.Equal(expectedPerson.HomeTown, response.Result.HomeTown);
        Assert.Equal(expectedPerson.Job, response.Result.Job);
    }

    [Fact]
    public virtual async Task GetResponseAsync_StructuredOutput_NonNative()
    {
        SkipIfNotEnabled();

        var capturedOptions = new List<ChatOptions?>();
        var captureOutputChatClient = ChatClient.AsBuilder()
            .Use((messages, options, nextAsync, cancellationToken) =>
            {
                capturedOptions.Add(options);
                return nextAsync(messages, options, cancellationToken);
            })
            .Build();

        var response = await captureOutputChatClient.GetResponseAsync<Person>("""
            Supply an object to represent Jimbo Smith from Cardiff.
            """, useJsonSchemaResponseFormat: false);

        Assert.Equal("Jimbo Smith", response.Result.FullName);
        Assert.Contains("Cardiff", response.Result.HomeTown);

        // Verify it used *non-native* structured output, i.e., response format Json with no schema
        var responseFormat = Assert.IsType<ChatResponseFormatJson>(Assert.Single(capturedOptions)!.ResponseFormat);
        Assert.Null(responseFormat.Schema);
        Assert.Null(responseFormat.SchemaName);
        Assert.Null(responseFormat.SchemaDescription);
    }

    private class Person
    {
#pragma warning disable S1144, S3459 // Unassigned members should be removed
        public string? FullName { get; set; }
        public int AgeInYears { get; set; }
        public string? HomeTown { get; set; }
        public JobType Job { get; set; }
#pragma warning restore S1144, S3459 // Unused private types or members should be removed
    }

    private enum JobType
    {
        Wombat,
        PopStar,
        Programmer,
        Unknown,
    }

    [Fact]
    public virtual async Task SummarizingChatReducer_PreservesConversationContext()
    {
        SkipIfNotEnabled();

        var chatClient = new TestSummarizingChatClient(ChatClient, targetCount: 2, threshold: 1);

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "My name is Alice and I love hiking in the mountains."),
            new(ChatRole.Assistant, "Nice to meet you, Alice! Hiking in the mountains sounds wonderful. Do you have a favorite trail?"),
            new(ChatRole.User, "Yes, I love the Pacific Crest Trail. I hiked a section last summer."),
            new(ChatRole.Assistant, "The Pacific Crest Trail is amazing! Which section did you hike?"),
            new(ChatRole.User, "I hiked the section through the Sierra Nevada. It was challenging but beautiful."),
            new(ChatRole.Assistant, "The Sierra Nevada section is known for its stunning views. How long did it take you?"),
            new(ChatRole.User, "What's my name and what activity do I enjoy?")
        ];

        var response = await chatClient.GetResponseAsync(messages);

        // The summarizer should have reduced the conversation
        Assert.Equal(1, chatClient.SummarizerCallCount);
        Assert.NotNull(chatClient.LastSummarizedConversation);
        Assert.Equal(4, chatClient.LastSummarizedConversation.Count);
        Assert.Collection(chatClient.LastSummarizedConversation,
            m =>
            {
                Assert.Equal(ChatRole.Assistant, m.Role); // Indicates this is the assistant's summary
                Assert.Contains("Alice", m.Text);
            },
            m => Assert.StartsWith("I hiked the section", m.Text, StringComparison.Ordinal),
            m => Assert.StartsWith("The Sierra Nevada section", m.Text, StringComparison.Ordinal),
            m => Assert.StartsWith("What's my name", m.Text, StringComparison.Ordinal));

        // The model should recall details from the summarized conversation
        Assert.Contains("Alice", response.Text);
        Assert.True(
            response.Text.IndexOf("hiking", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("hike", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected 'hiking' or 'hike' in response: {response.Text}");
    }

    [Fact]
    public virtual async Task SummarizingChatReducer_PreservesSystemMessage()
    {
        SkipIfNotEnabled();

        var chatClient = new TestSummarizingChatClient(ChatClient, targetCount: 2, threshold: 0);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a pirate. Always respond in pirate speak."),
            new(ChatRole.User, "Tell me about the weather"),
            new(ChatRole.Assistant, "Ahoy matey! The weather be fine today, with clear skies on the horizon!"),
            new(ChatRole.User, "What about tomorrow?"),
            new(ChatRole.Assistant, "Arr, tomorrow be lookin' a bit cloudy, might be some rain blowin' in from the east!"),
            new(ChatRole.User, "Should I bring an umbrella?"),
            new(ChatRole.Assistant, "Aye, ye best be bringin' yer umbrella, unless ye want to be soaked like a barnacle!"),
            new(ChatRole.User, "What's 2 + 2?")
        ];

        var response = await chatClient.GetResponseAsync(messages);

        // The summarizer should have reduced the conversation
        Assert.Equal(1, chatClient.SummarizerCallCount);
        Assert.NotNull(chatClient.LastSummarizedConversation);
        Assert.Equal(4, chatClient.LastSummarizedConversation.Count);
        Assert.Collection(chatClient.LastSummarizedConversation,
            m =>
            {
                Assert.Equal(ChatRole.System, m.Role);
                Assert.Equal("You are a pirate. Always respond in pirate speak.", m.Text);
            },
            m => Assert.Equal(ChatRole.Assistant, m.Role), // Summary message
            m => Assert.StartsWith("Aye, ye best be bringin'", m.Text, StringComparison.Ordinal),
            m => Assert.Equal("What's 2 + 2?", m.Text));

        // The model should still respond in pirate speak due to preserved system message
        Assert.True(
            response.Text.IndexOf("arr", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("aye", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("matey", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("ye", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected pirate speak in response: {response.Text}");
    }

    [Fact]
    public virtual async Task SummarizingChatReducer_WithFunctionCalls()
    {
        SkipIfNotEnabled();

        int weatherCallCount = 0;
        var getWeather = AIFunctionFactory.Create(([Description("Gets weather for a city")] string city) =>
        {
            weatherCallCount++;
            return city switch
            {
                "Seattle" => "Rainy, 15°C",
                "Miami" => "Sunny, 28°C",
                _ => "Unknown"
            };
        }, "GetWeather");

        TestSummarizingChatClient summarizingChatClient = null!;
        var chatClient = ChatClient
            .AsBuilder()
            .Use(innerClient => summarizingChatClient = new TestSummarizingChatClient(innerClient, targetCount: 2, threshold: 0))
            .UseFunctionInvocation()
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "What's the weather in Seattle?"),
            new(ChatRole.Assistant, "Let me check the weather in Seattle for you."),
            new(ChatRole.User, "And what about Miami?"),
            new(ChatRole.Assistant, "I'll check Miami's weather as well."),
            new(ChatRole.User, "Which city had better weather?")
        ];

        var response = await chatClient.GetResponseAsync(messages, new() { Tools = [getWeather] });

        // The summarizer should have reduced the conversation (function calls are excluded)
        Assert.Equal(1, summarizingChatClient.SummarizerCallCount);
        Assert.NotNull(summarizingChatClient.LastSummarizedConversation);

        // Should have summary + last 2 messages
        Assert.Equal(3, summarizingChatClient.LastSummarizedConversation.Count);

        // The model should have context about both weather queries even after summarization
        Assert.True(response.Text.IndexOf("Miami", StringComparison.OrdinalIgnoreCase) >= 0, $"Expected 'Miami' in response: {response.Text}");
        Assert.True(
            response.Text.IndexOf("sunny", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("better", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.Text.IndexOf("warm", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected weather comparison in response: {response.Text}");
    }

    [Fact]
    public virtual async Task SummarizingChatReducer_Streaming()
    {
        SkipIfNotEnabled();

        var chatClient = new TestSummarizingChatClient(ChatClient, targetCount: 2, threshold: 0);

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "I'm Bob and I work as a software engineer at a startup."),
            new(ChatRole.Assistant, "Nice to meet you, Bob! Working at a startup must be exciting. What kind of software do you develop?"),
            new(ChatRole.User, "We build AI-powered tools for education."),
            new(ChatRole.Assistant, "That sounds impactful! AI in education has so much potential."),
            new(ChatRole.User, "Yes, we focus on personalized learning experiences."),
            new(ChatRole.Assistant, "Personalized learning is the future of education!"),
            new(ChatRole.User, "Was anyone named in the conversation? Provide their name and job.")
        ];

        StringBuilder sb = new();
        await foreach (var chunk in chatClient.GetStreamingResponseAsync(messages))
        {
            sb.Append(chunk.Text);
        }

        // The summarizer should have reduced the conversation
        Assert.Equal(1, chatClient.SummarizerCallCount);
        Assert.NotNull(chatClient.LastSummarizedConversation);
        Assert.Equal(3, chatClient.LastSummarizedConversation.Count);
        Assert.Collection(chatClient.LastSummarizedConversation,
            m =>
            {
                Assert.Equal(ChatRole.Assistant, m.Role); // Summary
                Assert.Contains("Bob", m.Text);
            },
            m => Assert.StartsWith("Personalized learning", m.Text, StringComparison.Ordinal),
            m => Assert.Equal("Was anyone named in the conversation? Provide their name and job.", m.Text));

        string responseText = sb.ToString();
        Assert.Contains("Bob", responseText);
        Assert.True(
            responseText.IndexOf("software", StringComparison.OrdinalIgnoreCase) >= 0 ||
            responseText.IndexOf("engineer", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected 'software' or 'engineer' in response: {responseText}");
    }

    [Fact]
    public virtual async Task SummarizingChatReducer_CustomPrompt()
    {
        SkipIfNotEnabled();

        var chatClient = new TestSummarizingChatClient(ChatClient, targetCount: 2, threshold: 0);
        chatClient.Reducer.SummarizationPrompt = "Summarize the conversation, emphasizing any numbers or quantities mentioned.";

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "I have 3 cats and 2 dogs."),
            new(ChatRole.Assistant, "That's 5 pets total! You must have a lively household."),
            new(ChatRole.User, "Yes, and I spend about $200 per month on pet food."),
            new(ChatRole.Assistant, "That's a significant expense, but I'm sure they're worth it!"),
            new(ChatRole.User, "They eat 10 cans of food per week."),
            new(ChatRole.Assistant, "That's quite a bit of food for your furry friends!"),
            new(ChatRole.User, "How many pets do I have in total?")
        ];

        var response = await chatClient.GetResponseAsync(messages);

        // The summarizer should have reduced the conversation
        Assert.Equal(1, chatClient.SummarizerCallCount);
        Assert.NotNull(chatClient.LastSummarizedConversation);
        Assert.Equal(3, chatClient.LastSummarizedConversation.Count);

        // Verify the summary emphasizes numbers as requested by the custom prompt
        var summaryMessage = chatClient.LastSummarizedConversation[0];
        Assert.Equal(ChatRole.Assistant, summaryMessage.Role);
        Assert.True(
            summaryMessage.Text.IndexOf("3", StringComparison.Ordinal) >= 0 ||
            summaryMessage.Text.IndexOf("5", StringComparison.Ordinal) >= 0 ||
            summaryMessage.Text.IndexOf("200", StringComparison.Ordinal) >= 0 ||
            summaryMessage.Text.IndexOf("10", StringComparison.Ordinal) >= 0,
            $"Expected numbers in summary: {summaryMessage.Text}");

        // The model should recall the specific number from the summarized conversation
        Assert.Contains("5", response.Text);
    }

    private sealed class TestSummarizingChatClient : IChatClient
    {
        private IChatClient _summarizerChatClient;
        private IChatClient _innerChatClient;

        public SummarizingChatReducer Reducer { get; }

        public int SummarizerCallCount { get; private set; }

        public IReadOnlyList<ChatMessage>? LastSummarizedConversation { get; private set; }

        public TestSummarizingChatClient(IChatClient innerClient, int targetCount, int threshold)
        {
            _summarizerChatClient = innerClient.AsBuilder()
                .Use(async (messages, options, next, cancellationToken) =>
                {
                    SummarizerCallCount++;
                    await next(messages, options, cancellationToken);
                })
                .Build();

            Reducer = new SummarizingChatReducer(_summarizerChatClient, targetCount, threshold);

            _innerChatClient = innerClient.AsBuilder()
                .UseChatReducer(Reducer)
                .Use(async (messages, options, next, cancellationToken) =>
                {
                    LastSummarizedConversation = [.. messages];
                    await next(messages, options, cancellationToken);
                })
                .Build();
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _innerChatClient.GetResponseAsync(messages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _innerChatClient.GetStreamingResponseAsync(messages, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => _innerChatClient.GetService(serviceType, serviceKey);

        public void Dispose()
        {
            _summarizerChatClient.Dispose();
            _innerChatClient.Dispose();
        }
    }

    [MemberNotNull(nameof(ChatClient))]
    protected void SkipIfNotEnabled()
    {
        string? skipIntegration = TestRunnerConfiguration.Instance["SkipIntegrationTests"];

        Assert.SkipUnless(skipIntegration is null && ChatClient is not null, "Client is not enabled.");
    }

}
