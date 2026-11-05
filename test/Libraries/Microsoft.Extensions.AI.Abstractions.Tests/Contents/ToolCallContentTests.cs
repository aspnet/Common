// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolCallContentTests
{
    [Fact]
    public void Constructor_PropsDefault()
    {
        ToolCallContent c = new("callId1");

        Assert.Equal("callId1", c.CallId);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
    }

    [Fact]
    public void Constructor_NullCallId_Throws()
    {
        Assert.Throws<ArgumentNullException>("callId", () => new ToolCallContent(null!));
    }

    [Fact]
    public void Constructor_PropsRoundtrip()
    {
        ToolCallContent c = new("callId1");

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

        ChatMessage message = new(ChatRole.Assistant,
        [
            new FunctionCallContent("call1", "function1", new Dictionary<string, object?> { { "param1", 123 } }),
            new McpServerToolCallContent("call2", "myTool", "myServer"),
            new CodeInterpreterToolCallContent("call3"),
            new ImageGenerationToolCallContent("call4"),
            new WebSearchToolCallContent("call5"),
        ]);

        // Verify each element roundtrips individually
        foreach (var content in message.Contents)
        {
            var serialized = JsonSerializer.Serialize(content, options);
            var deserialized = JsonSerializer.Deserialize<ToolCallContent>(serialized, options);
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
              "$type": "toolCall",
              "callId": "tc1",
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var toolCall = Assert.IsType<ToolCallContent>(result);
        Assert.Equal("tc1", toolCall.CallId);
        Assert.NotNull(toolCall.AdditionalProperties);
        Assert.Equal("val", toolCall.AdditionalProperties["key"]?.ToString());
    }
}
