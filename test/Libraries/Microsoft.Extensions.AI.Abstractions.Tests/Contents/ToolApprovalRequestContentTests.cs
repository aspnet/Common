// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI.Contents;

public class ToolApprovalRequestContentTests
{
    [Fact]
    public void Constructor_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>("requestId", () => new ToolApprovalRequestContent(null!, new FunctionCallContent("FCC1", "TestFunction")));
        Assert.Throws<ArgumentException>("requestId", () => new ToolApprovalRequestContent("", new FunctionCallContent("FCC1", "TestFunction")));
        Assert.Throws<ArgumentException>("requestId", () => new ToolApprovalRequestContent("\r\t\n ", new FunctionCallContent("FCC1", "TestFunction")));
        Assert.Throws<ArgumentNullException>("toolCall", () => new ToolApprovalRequestContent("id", null!));
    }

    public static TheoryData<ToolCallContent> ToolCallContentInstances => new()
    {
        new FunctionCallContent("FCC1", "TestFunction", new Dictionary<string, object?> { { "param1", 123 } }),
        new McpServerToolCallContent("MCC1", "TestTool", "TestServer") { Arguments = new Dictionary<string, object?> { { "arg1", "value1" } } },
        new CodeInterpreterToolCallContent("CI1") { Inputs = [new DataContent("print('hello')"u8.ToArray(), "text/x-python")] },
        new ImageGenerationToolCallContent("IG1"),
    };

    [Theory]
    [MemberData(nameof(ToolCallContentInstances), DisableDiscoveryEnumeration = true)]
    public void Constructor_Roundtrips(ToolCallContent toolCall)
    {
        string id = "req-1";
        ToolApprovalRequestContent content = new(id, toolCall);

        Assert.Same(id, content.RequestId);
        Assert.Same(toolCall, content.ToolCall);
    }

    [Theory]
    [MemberData(nameof(ToolCallContentInstances), DisableDiscoveryEnumeration = true)]
    public void CreateResponse_ReturnsExpectedResponse(ToolCallContent toolCall)
    {
        string id = "req-1";
        ToolApprovalRequestContent content = new(id, toolCall);

        var response = content.CreateResponse(approved: true);

        Assert.NotNull(response);
        Assert.Same(id, response.RequestId);
        Assert.True(response.Approved);
        Assert.Same(toolCall, response.ToolCall);
        Assert.Null(response.Reason);
    }

    [Theory]
    [InlineData(true, "Approved for testing")]
    [InlineData(false, "Rejected due to security concerns")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void CreateResponse_WithReason_ReturnsExpectedResponse(bool approved, string? reason)
    {
        string id = "req-1";
        FunctionCallContent functionCall = new("FCC1", "TestFunction");

        ToolApprovalRequestContent content = new(id, functionCall);

        var response = content.CreateResponse(approved, reason);

        Assert.NotNull(response);
        Assert.Same(id, response.RequestId);
        Assert.Equal(approved, response.Approved);
        Assert.Same(functionCall, response.ToolCall);
        Assert.Equal(reason, response.Reason);
    }

    [Theory]
    [MemberData(nameof(ToolCallContentInstances), DisableDiscoveryEnumeration = true)]
    public void Serialization_Roundtrips(ToolCallContent toolCall)
    {
        var content = new ToolApprovalRequestContent("request123", toolCall);

        AssertSerializationRoundtrips<ToolApprovalRequestContent>(content);
        AssertSerializationRoundtrips<InputRequestContent>(content);
        AssertSerializationRoundtrips<AIContent>(content);

        static void AssertSerializationRoundtrips<T>(ToolApprovalRequestContent content)
            where T : AIContent
        {
            T contentAsT = (T)(object)content;
            string json = JsonSerializer.Serialize(contentAsT, AIJsonUtilities.DefaultOptions);
            T? deserialized = JsonSerializer.Deserialize<T>(json, AIJsonUtilities.DefaultOptions);
            Assert.NotNull(deserialized);
            var deserializedContent = Assert.IsType<ToolApprovalRequestContent>(deserialized);
            Assert.Equal(content.RequestId, deserializedContent.RequestId);
            Assert.NotNull(deserializedContent.ToolCall);
            Assert.IsType(content.ToolCall.GetType(), deserializedContent.ToolCall);
            Assert.Equal(content.ToolCall.CallId, deserializedContent.ToolCall.CallId);
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "toolApprovalRequest",
              "requestId": "req-abc123",
              "toolCall": {
                "$type": "functionCall",
                "callId": "call1",
                "name": "myFunc"
              },
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var approvalRequest = Assert.IsType<ToolApprovalRequestContent>(result);
        Assert.Equal("req-abc123", approvalRequest.RequestId);
        Assert.NotNull(approvalRequest.ToolCall);
        var funcCall = Assert.IsType<FunctionCallContent>(approvalRequest.ToolCall);
        Assert.Equal("call1", funcCall.CallId);
        Assert.Equal("myFunc", funcCall.Name);
        Assert.NotNull(approvalRequest.AdditionalProperties);
        Assert.Equal("val", approvalRequest.AdditionalProperties["key"]?.ToString());
    }

    [Fact]
    public void RequiresConfirmation_DefaultsToTrue()
    {
        var content = new ToolApprovalRequestContent("req-1", new FunctionCallContent("call1", "Func"));
        Assert.True(content.RequiresConfirmation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiresConfirmation_RoundtripsThroughJson(bool value)
    {
        var content = new ToolApprovalRequestContent("req-1", new FunctionCallContent("call1", "Func"))
        {
            RequiresConfirmation = value,
        };

        string json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolApprovalRequestContent>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(value, deserialized!.RequiresConfirmation);
    }
}
