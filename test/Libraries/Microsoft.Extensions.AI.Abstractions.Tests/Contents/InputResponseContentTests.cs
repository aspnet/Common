// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class InputResponseContentTests
{
    [Fact]
    public void Constructor_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>("requestId", () => new TestInputResponseContent(null!));
        Assert.Throws<ArgumentException>("requestId", () => new TestInputResponseContent(""));
        Assert.Throws<ArgumentException>("requestId", () => new TestInputResponseContent("\r\t\n "));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("!@#")]
    public void Constructor_Roundtrips(string id)
    {
        TestInputResponseContent content = new(id);
        Assert.Equal(id, content.RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialization_DerivedTypes_Roundtrips(bool useBuiltInJsonContext)
    {
        JsonSerializerOptions options = useBuiltInJsonContext ? AIJsonUtilities.DefaultOptions : TestJsonSerializerContext.Default.Options;

        InputResponseContent[] contents =
        [
            new ToolApprovalResponseContent("request123", true, new FunctionCallContent("call123", "functionName")),
            new ToolApprovalResponseContent("request456", true, new McpServerToolCallContent("call456", "myTool", "myServer")),
        ];

        // Verify each element roundtrips individually
        foreach (var content in contents)
        {
            var serialized = JsonSerializer.Serialize(content, options);
            var deserialized = JsonSerializer.Deserialize<InputResponseContent>(serialized, options);
            Assert.NotNull(deserialized);
            Assert.Equal(content.GetType(), deserialized.GetType());
        }

        // Verify the array roundtrips
        var serializedContents = JsonSerializer.Serialize(contents, TestJsonSerializerContext.Default.InputResponseContentArray);
        var deserializedContents = JsonSerializer.Deserialize(serializedContents, TestJsonSerializerContext.Default.InputResponseContentArray);
        Assert.NotNull(deserializedContents);
        Assert.Equal(contents.Length, deserializedContents.Length);
        for (int i = 0; i < deserializedContents.Length; i++)
        {
            Assert.NotNull(deserializedContents[i]);
            Assert.Equal(contents[i].GetType(), deserializedContents[i].GetType());
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "toolApprovalResponse",
              "requestId": "req-abc123",
              "approved": false,
              "toolCall": {
                "$type": "functionCall",
                "callId": "call1",
                "name": "myFunc"
              },
              "reason": "Denied",
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        InputResponseContent? result = JsonSerializer.Deserialize<InputResponseContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var approvalResponse = Assert.IsType<ToolApprovalResponseContent>(result);
        Assert.Equal("req-abc123", approvalResponse.RequestId);
        Assert.False(approvalResponse.Approved);
        Assert.NotNull(approvalResponse.ToolCall);
        var funcCall = Assert.IsType<FunctionCallContent>(approvalResponse.ToolCall);
        Assert.Equal("call1", funcCall.CallId);
        Assert.Equal("myFunc", funcCall.Name);
        Assert.Equal("Denied", approvalResponse.Reason);
        Assert.NotNull(approvalResponse.AdditionalProperties);
        Assert.Equal("val", approvalResponse.AdditionalProperties["key"]?.ToString());
    }

    private class TestInputResponseContent : InputResponseContent
    {
        public TestInputResponseContent(string requestId)
            : base(requestId)
        {
        }
    }
}

