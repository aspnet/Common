// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolResultContentTests
{
    [Fact]
    public void Constructor_PropsDefault()
    {
        ToolResultContent c = new("callId1");

        Assert.Equal("callId1", c.CallId);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
    }

    [Fact]
    public void Constructor_NullCallId_Throws()
    {
        Assert.Throws<ArgumentNullException>("callId", () => new ToolResultContent(null!));
    }

    [Fact]
    public void Constructor_PropsRoundtrip()
    {
        ToolResultContent c = new("callId1");

        Assert.Null(c.RawRepresentation);
        object raw = new();
        c.RawRepresentation = raw;
        Assert.Same(raw, c.RawRepresentation);

        Assert.Null(c.AdditionalProperties);
        AdditionalPropertiesDictionary props = new() { { "key", "value" } };
        c.AdditionalProperties = props;
        Assert.Same(props, c.AdditionalProperties);

        Assert.Equal("callId1", c.CallId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialization_DerivedTypes_Roundtrips(bool useBuiltInJsonContext)
    {
        JsonSerializerOptions options = useBuiltInJsonContext ? AIJsonUtilities.DefaultOptions : TestJsonSerializerContext.Default.Options;

        ChatMessage message = new(ChatRole.Tool,
        [
            new FunctionResultContent("call1", "result1"),
            new McpServerToolResultContent("call2"),
            new CodeInterpreterToolResultContent("call3"),
            new ImageGenerationToolResultContent("call4"),
            new WebSearchToolResultContent("call5"),
        ]);

        // Verify each element roundtrips individually
        foreach (var content in message.Contents)
        {
            var serialized = JsonSerializer.Serialize(content, options);
            var deserialized = JsonSerializer.Deserialize<ToolResultContent>(serialized, options);
            Assert.NotNull(deserialized);
            Assert.Equal(content.GetType(), deserialized.GetType());
        }

        var serializedMessage = JsonSerializer.Serialize(message, options);
        ChatMessage? deserialized2 = JsonSerializer.Deserialize<ChatMessage>(serializedMessage, options);
        Assert.NotNull(deserialized2);

        Assert.Equal(message.Role, deserialized2.Role);
        Assert.Equal(message.Contents.Count, deserialized2.Contents.Count);
        for (int i = 0; i < message.Contents.Count; i++)
        {
            Assert.NotNull(deserialized2.Contents[i]);
            Assert.Equal(message.Contents[i].GetType(), deserialized2.Contents[i].GetType());
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "toolResult",
              "callId": "tr1",
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var toolResult = Assert.IsType<ToolResultContent>(result);
        Assert.Equal("tr1", toolResult.CallId);
        Assert.NotNull(toolResult.AdditionalProperties);
        Assert.Equal("val", toolResult.AdditionalProperties["key"]?.ToString());
    }
}
