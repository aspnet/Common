// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable S103 // Lines should not be too long

namespace Microsoft.Extensions.AI;

public partial class ChatClientStructuredOutputExtensionsTests
{
    [Fact]
    public async Task SuccessUsage_Default()
    {
        var expectedResult = new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Animal)))
        {
            ResponseId = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            ModelId = "someModel",
            RawRepresentation = new object(),
            Usage = new(),
        };

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
                Assert.NotNull(responseFormat.Schema);
                AssertDeepEquals(JsonDocument.Parse("""
                    {
                        "$schema": "https://json-schema.org/draft/2020-12/schema",
                        "description": "Some test description",
                        "type": "object",
                        "properties": {
                            "id": {
                                "type": "integer"
                            },
                            "fullName": {
                                "type": [
                                    "string",
                                    "null"
                                ]
                            },
                            "species": {
                                "type": "string",
                                "enum": [
                                    "Bear",
                                    "Tiger",
                                    "Walrus"
                                ]
                            }
                        }
                    }
                    """).RootElement, responseFormat.Schema.Value);
                Assert.Equal(nameof(Animal), responseFormat.SchemaName);
                Assert.Equal("Some test description", responseFormat.SchemaDescription);

                // The inner client receives the prompt with no augmentation
                Assert.Collection(messages,
                    message => Assert.Equal("Hello", message.Text));

                return Task.FromResult(expectedResponse);
            },
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        // The response contains the deserialized result and other response properties
        Assert.Equal(1, response.Result.Id);
        Assert.Equal("Tigger", response.Result.FullName);
        Assert.Equal(Species.Tiger, response.Result.Species);
        Assert.Equal(expectedResponse.ResponseId, response.ResponseId);
        Assert.Equal(expectedResponse.CreatedAt, response.CreatedAt);
        Assert.Equal(expectedResponse.ModelId, response.ModelId);
        Assert.Same(expectedResponse.RawRepresentation, response.RawRepresentation);
        Assert.Same(expectedResponse.Usage, response.Usage);

        // TryGetResult returns the same value
        Assert.True(response.TryGetResult(out var tryGetResultOutput));
        Assert.Same(response.Result, tryGetResultOutput);

        // Doesn't mutate history (or at least, reverts any changes)
        Assert.Equal("Hello", Assert.Single(chatHistory).Text);
    }

    [Fact]
    public async Task SuccessUsage_NoJsonSchema()
    {
        var expectedResult = new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options)))
        {
            ResponseId = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            ModelId = "someModel",
            RawRepresentation = new object(),
            Usage = new(),
        };

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
                Assert.Null(responseFormat.Schema);
                Assert.Null(responseFormat.SchemaName);
                Assert.Null(responseFormat.SchemaDescription);

                // The inner client receives a trailing "user" message with the schema instruction
                Assert.Collection(messages,
                    message => Assert.Equal("Hello", message.Text),
                    message =>
                    {
                        Assert.Equal(ChatRole.User, message.Role);
                        Assert.Contains("Respond with a JSON value", message.Text);
                        Assert.Contains("https://json-schema.org/draft/2020-12/schema", message.Text);
                        foreach (Species v in Enum.GetValues(typeof(Species)))
                        {
                            Assert.Contains(v.ToString(), message.Text); // All enum values are described as strings
                        }
                    });

                return Task.FromResult(expectedResponse);
            },
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, useJsonSchemaResponseFormat: false, serializerOptions: JsonContext2.Default.Options);

        // The response contains the deserialized result and other response properties
        Assert.Equal(1, response.Result.Id);
        Assert.Equal("Tigger", response.Result.FullName);
        Assert.Equal(Species.Tiger, response.Result.Species);
        Assert.Equal(expectedResponse.ResponseId, response.ResponseId);
        Assert.Equal(expectedResponse.CreatedAt, response.CreatedAt);
        Assert.Equal(expectedResponse.ModelId, response.ModelId);
        Assert.Same(expectedResponse.RawRepresentation, response.RawRepresentation);
        Assert.Same(expectedResponse.Usage, response.Usage);

        // TryGetResult returns the same value
        Assert.True(response.TryGetResult(out var tryGetResultOutput));
        Assert.Same(response.Result, tryGetResultOutput);

        // Doesn't mutate history (or at least, reverts any changes)
        Assert.Equal("Hello", Assert.Single(chatHistory).Text);
    }

    [Fact]
    public async Task WrapsNonObjectValuesInDataProperty()
    {
        var expectedResult = new Envelope<int> { data = 123 };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options)));

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
                Assert.Equal("""
                    {
                      "$schema": "https://json-schema.org/draft/2020-12/schema",
                      "type": "object",
                      "properties": {
                        "data": {
                          "$schema": "https://json-schema.org/draft/2020-12/schema",
                          "type": "integer"
                        }
                      },
                      "additionalProperties": false,
                      "required": [
                        "data"
                      ]
                    }
                    """, responseFormat.Schema.ToString());
                return Task.FromResult(expectedResponse);
            },
        };

        var response = await client.GetResponseAsync<int>("Hello");
        Assert.Equal(123, response.Result);
    }

    [Fact]
    public async Task OnlyUsesLastMessage()
    {
        var expectedResult = new Envelope<int> { data = 123 };
        var expectedResponse = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new TextContent("I'm going to invoke a function to get the data."),
                new FunctionCallContent("callid123", "get_data"),
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callid123", "result")]),
            new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options))
        ]);

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
                Assert.Equal("""
                    {
                      "$schema": "https://json-schema.org/draft/2020-12/schema",
                      "type": "object",
                      "properties": {
                        "data": {
                          "$schema": "https://json-schema.org/draft/2020-12/schema",
                          "type": "integer"
                        }
                      },
                      "additionalProperties": false,
                      "required": [
                        "data"
                      ]
                    }
                    """, responseFormat.Schema.ToString());
                return Task.FromResult(expectedResponse);
            },
        };

        var response = await client.GetResponseAsync<int>("Hello");
        Assert.Equal(123, response.Result);
    }

    [Fact]
    public async Task FailureUsage_InvalidJson()
    {
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "This is not valid JSON"));
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromResult(expectedResponse),
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        var ex = Assert.Throws<JsonException>(() => response.Result);
        Assert.Contains("invalid", ex.Message);

        Assert.False(response.TryGetResult(out var tryGetResult));
        Assert.Null(tryGetResult);
    }

    [Fact]
    public async Task FailureUsage_NullJson()
    {
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "null"));
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromResult(expectedResponse),
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        var ex = Assert.Throws<InvalidOperationException>(() => response.Result);
        Assert.Equal("The deserialized response is null.", ex.Message);

        Assert.False(response.TryGetResult(out var tryGetResult));
        Assert.Null(tryGetResult);
    }

    [Fact]
    public async Task FailureUsage_NoJsonInResponse()
    {
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, [new UriContent("https://example.com", "image/*")]));
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromResult(expectedResponse),
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        var ex = Assert.Throws<InvalidOperationException>(() => response.Result);
        Assert.Equal("The response did not contain JSON to be deserialized.", ex.Message);

        Assert.False(response.TryGetResult(out var tryGetResult));
        Assert.Null(tryGetResult);
    }

    [Fact]
    public async Task CanUseNativeStructuredOutputWithSanitizedTypeName()
    {
        var expectedResult = new Data<Animal> { Value = new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger } };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options)));

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);

                Assert.Matches("Data_1", responseFormat.SchemaName);

                return Task.FromResult(expectedResponse);
            },
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Data<Animal>>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        // The response contains the deserialized result and other response properties
        Assert.Equal(1, response.Result!.Value!.Id);
        Assert.Equal("Tigger", response.Result.Value.FullName);
        Assert.Equal(Species.Tiger, response.Result.Value.Species);

        // TryGetResult returns the same value
        Assert.True(response.TryGetResult(out var tryGetResultOutput));
        Assert.Same(response.Result, tryGetResultOutput);

        // History remains unmutated
        Assert.Equal("Hello", Assert.Single(chatHistory).Text);
    }

    [Fact]
    public async Task CanUseNativeStructuredOutputWithArray()
    {
        var expectedResult = new[] { new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger } };
        var payload = new Envelope<Animal[]> { data = expectedResult };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(payload, JsonContext2.Default.Options)));

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromResult(expectedResponse)
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal[]>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        // The response contains the deserialized result and other response properties
        Assert.Single(response.Result!);
        Assert.Equal("Tigger", response.Result[0].FullName);
        Assert.Equal(Species.Tiger, response.Result[0].Species);

        // TryGetResult returns the same value
        Assert.True(response.TryGetResult(out var tryGetResultOutput));
        Assert.Same(response.Result, tryGetResultOutput);

        // History remains unmutated
        Assert.Equal("Hello", Assert.Single(chatHistory).Text);
    }

    [Fact]
    public async Task WrappedSchemaKeepsRefPointersResolvable()
    {
        var payload = new Envelope<TreeNode[]> { data = [new TreeNode { Name = "root", Children = [new TreeNode { Name = "leaf" }] }] };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(payload, JsonContext2.Default.Options)));

        JsonElement? schema = null;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                schema = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat).Schema;
                return Task.FromResult(expectedResponse);
            }
        };

        var response = await client.GetResponseAsync<TreeNode[]>("Hello", serializerOptions: JsonContext2.Default.Options);
        Assert.Equal("leaf", Assert.Single(Assert.Single(response.Result!).Children!).Name);

        Assert.NotNull(schema);
        List<string> refs = [];
        CollectRefs(schema.Value, refs);
        Assert.NotEmpty(refs);
        foreach (string pointer in refs)
        {
            Assert.True(TryResolvePointer(schema.Value, pointer, out _), $"$ref '{pointer}' does not resolve in schema:{Environment.NewLine}{schema.Value}");
        }

        static void CollectRefs(JsonElement element, List<string> refs)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                        {
                            refs.Add(property.Value.GetString()!);
                        }
                        else
                        {
                            CollectRefs(property.Value, refs);
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        CollectRefs(item, refs);
                    }

                    break;
            }
        }

        static bool TryResolvePointer(JsonElement root, string pointer, out JsonElement target)
        {
            target = root;
            if (pointer == "#")
            {
                return true;
            }

            foreach (string segment in pointer.Substring(2).Split('/'))
            {
                string name = segment.Replace("~1", "/").Replace("~0", "~");
                if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(name, out JsonElement next))
                {
                    target = next;
                }
                else if (target.ValueKind == JsonValueKind.Array && int.TryParse(name, out int index) && index < target.GetArrayLength())
                {
                    target = target[index];
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }

    [Fact]
    public async Task CanSpecifyCustomJsonSerializationOptions()
    {
        var jso = new JsonSerializerOptions(JsonContext2.Default.Options)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new JsonNumberEnumConverter<Species>() },
        };
        var expectedResult = new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger };
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(expectedResult, jso)));

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                // In the schema below, note that:
                //  - The property is named full_name, because we specified SnakeCaseLower
                //  - The species value is an integer instead of a string, because we didn't use enum-to-string conversion
                var responseFormat = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
                Assert.NotNull(responseFormat.Schema);
                AssertDeepEquals(JsonDocument.Parse("""
                    {
                        "$schema": "https://json-schema.org/draft/2020-12/schema",
                        "description": "Some test description",
                        "type": "object",
                        "properties": {
                            "id": {
                                "type": "integer"
                            },
                            "full_name": {
                                "type": [
                                    "string",
                                    "null"
                                ]
                            },
                            "species": {
                                "type": "integer"
                            }
                        }
                    }
                    """).RootElement, responseFormat.Schema.Value);

                return Task.FromResult(expectedResponse);
            },
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, jso);

        // The response contains the deserialized result and other response properties
        Assert.Equal(1, response.Result.Id);
        Assert.Equal("Tigger", response.Result.FullName);
        Assert.Equal(Species.Tiger, response.Result.Species);
    }

    [Fact]
    public async Task HandlesBackendReturningMultipleObjects()
    {
        // A very common failure mode for GPT 3.5 Turbo is that instead of returning a single top-level JSON object,
        // it may return multiple, particularly when function calling is involved.
        // See https://community.openai.com/t/2-json-objects-returned-when-using-function-calling-and-json-mode/574348
        // Fortunately we can work around this without breaking any cases of valid output.

        var expectedResult = new Animal { Id = 1, FullName = "Tigger", Species = Species.Tiger };
        var resultDuplicatedJson = JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options) + Environment.NewLine + JsonSerializer.Serialize(expectedResult, JsonContext2.Default.Options);

        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, resultDuplicatedJson)));
            },
        };

        var chatHistory = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync<Animal>(chatHistory, serializerOptions: JsonContext2.Default.Options);

        // The response contains the deserialized result and other response properties
        Assert.Equal(1, response.Result.Id);
        Assert.Equal("Tigger", response.Result.FullName);
        Assert.Equal(Species.Tiger, response.Result.Species);
    }

    [Description("Some test description")]
    private class Animal
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public Species Species { get; set; }
    }

    private class Data<T>
    {
        public T? Value { get; set; }
    }

    private enum Species
    {
        Bear,
        Tiger,
        Walrus,
    }

    private class Envelope<T>
    {
        public T? data { get; set; }
    }

    private class TreeNode
    {
        public string? Name { get; set; }
        public TreeNode[]? Children { get; set; }
    }

    [JsonSourceGenerationOptions(UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Animal))]
    [JsonSerializable(typeof(Envelope<int>))]
    [JsonSerializable(typeof(Envelope<Animal[]>))]
    [JsonSerializable(typeof(Envelope<TreeNode[]>))]
    [JsonSerializable(typeof(Data<Animal>))]
    private partial class JsonContext2 : JsonSerializerContext;

    private static void AssertDeepEquals(JsonElement element1, JsonElement element2)
    {
#pragma warning disable SA1118 // Parameter should not span multiple lines
        Assert.True(DeepEquals(element1, element2), $"""
            Elements are not equal.
            Expected:
            {element1}
            Actual:
            {element2}
            """);
#pragma warning restore SA1118 // Parameter should not span multiple lines
    }

    private static bool DeepEquals(JsonElement element1, JsonElement element2)
    {
        return JsonElement.DeepEquals(element1, element2);
    }
}
