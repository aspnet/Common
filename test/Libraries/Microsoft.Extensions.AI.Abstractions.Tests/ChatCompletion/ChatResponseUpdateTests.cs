// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ChatResponseUpdateTests
{
    [Fact]
    public void Constructor_PropsDefaulted()
    {
        ChatResponseUpdate update = new();
        Assert.Null(update.AuthorName);
        Assert.Null(update.Role);
        Assert.Empty(update.Text);
        Assert.Empty(update.Contents);
        Assert.Null(update.RawRepresentation);
        Assert.Null(update.AdditionalProperties);
        Assert.Null(update.ResponseId);
        Assert.Null(update.MessageId);
        Assert.Null(update.CreatedAt);
        Assert.Null(update.FinishReason);
        Assert.Equal(string.Empty, update.ToString());
    }

    [Fact]
    public void Properties_Roundtrip()
    {
        ChatResponseUpdate update = new();

        Assert.Null(update.AuthorName);
        update.AuthorName = "author";
        Assert.Equal("author", update.AuthorName);

        Assert.Null(update.Role);
        update.Role = ChatRole.Assistant;
        Assert.Equal(ChatRole.Assistant, update.Role);

        Assert.Empty(update.Contents);
        update.Contents.Add(new TextContent("text"));
        Assert.Single(update.Contents);
        Assert.Equal("text", update.Text);
        Assert.Same(update.Contents, update.Contents);
        IList<AIContent> newList = [new TextContent("text")];
        update.Contents = newList;
        Assert.Same(newList, update.Contents);
        update.Contents = null;
        Assert.NotNull(update.Contents);
        Assert.Empty(update.Contents);

        Assert.Empty(update.Text);

        Assert.Null(update.RawRepresentation);
        object raw = new();
        update.RawRepresentation = raw;
        Assert.Same(raw, update.RawRepresentation);

        Assert.Null(update.AdditionalProperties);
        AdditionalPropertiesDictionary props = new() { ["key"] = "value" };
        update.AdditionalProperties = props;
        Assert.Same(props, update.AdditionalProperties);

        Assert.Null(update.ResponseId);
        update.ResponseId = "id";
        Assert.Equal("id", update.ResponseId);

        Assert.Null(update.MessageId);
        update.MessageId = "messageid";
        Assert.Equal("messageid", update.MessageId);

        Assert.Null(update.CreatedAt);
        update.CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), update.CreatedAt);

        Assert.Null(update.FinishReason);
        update.FinishReason = ChatFinishReason.ContentFilter;
        Assert.Equal(ChatFinishReason.ContentFilter, update.FinishReason);
    }

    [Fact]
    public void Text_Get_UsesAllTextContent()
    {
        ChatResponseUpdate update = new()
        {
            Role = ChatRole.User,
            Contents =
            [
                new DataContent("data:image/audio;base64,aGVsbG8="),
                new DataContent("data:image/image;base64,aGVsbG8="),
                new FunctionCallContent("callId1", "fc1"),
                new TextContent("text-1"),
                new TextContent("text-2"),
                new FunctionResultContent("callId1", "result"),
            ],
        };

        TextContent textContent = Assert.IsType<TextContent>(update.Contents[3]);
        Assert.Equal("text-1", textContent.Text);
        Assert.Equal("text-1text-2", update.Text);
        Assert.Equal("text-1text-2", update.ToString());

        ((TextContent)update.Contents[3]).Text = "text-3";
        Assert.Equal("text-3text-2", update.Text);
        Assert.Same(textContent, update.Contents[3]);
        Assert.Equal("text-3text-2", update.ToString());
    }

    [Fact]
    public void JsonSerialization_Roundtrips()
    {
        ChatResponseUpdate original = new()
        {
            AuthorName = "author",
            Role = ChatRole.Assistant,
            Contents =
            [
                new TextContent("text-1"),
                new DataContent("data:image/png;base64,aGVsbG8="),
                new FunctionCallContent("callId1", "fc1"),
                new DataContent("data"u8.ToArray(), "text/plain"),
                new TextContent("text-2"),
            ],
            RawRepresentation = new object(),
            ResponseId = "id",
            MessageId = "messageid",
            ModelId = "gpt-4",
            ConversationId = "conv123",
            CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FinishReason = ChatFinishReason.ContentFilter,
            AdditionalProperties = new() { ["key"] = "value" },
        };

        string json = JsonSerializer.Serialize(original, TestJsonSerializerContext.Default.ChatResponseUpdate);

        ChatResponseUpdate? result = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ChatResponseUpdate);

        Assert.NotNull(result);
        Assert.Equal(5, result.Contents.Count);

        Assert.IsType<TextContent>(result.Contents[0]);
        Assert.Equal("text-1", ((TextContent)result.Contents[0]).Text);

        Assert.IsType<DataContent>(result.Contents[1]);
        Assert.Equal("data:image/png;base64,aGVsbG8=", ((DataContent)result.Contents[1]).Uri);

        Assert.IsType<FunctionCallContent>(result.Contents[2]);
        Assert.Equal("fc1", ((FunctionCallContent)result.Contents[2]).Name);

        Assert.IsType<DataContent>(result.Contents[3]);
        Assert.Equal("data"u8.ToArray(), ((DataContent)result.Contents[3]).Data.ToArray());

        Assert.IsType<TextContent>(result.Contents[4]);
        Assert.Equal("text-2", ((TextContent)result.Contents[4]).Text);

        Assert.Equal("author", result.AuthorName);
        Assert.Equal(ChatRole.Assistant, result.Role);
        Assert.Equal("id", result.ResponseId);
        Assert.Equal("messageid", result.MessageId);
        Assert.Equal("gpt-4", result.ModelId);
        Assert.Equal("conv123", result.ConversationId);
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAt);
        Assert.Equal(ChatFinishReason.ContentFilter, result.FinishReason);

        Assert.NotNull(result.AdditionalProperties);
        Assert.Single(result.AdditionalProperties);
        Assert.True(result.AdditionalProperties.TryGetValue("key", out object? value));
        Assert.IsType<JsonElement>(value);
        Assert.Equal("value", ((JsonElement)value!).GetString());
    }

    [Fact]
    public void JsonSerialization_Roundtrips_DefaultOptions()
    {
        ChatResponseUpdate original = new()
        {
            AuthorName = "author",
            Role = ChatRole.Assistant,
            Contents =
            [
                new TextContent("text-1"),
                new DataContent("data:image/png;base64,aGVsbG8="),
                new FunctionCallContent("callId1", "fc1"),
                new DataContent("data"u8.ToArray(), "text/plain"),
                new TextContent("text-2"),
            ],
            RawRepresentation = new object(),
            ResponseId = "id",
            MessageId = "messageid",
            ModelId = "gpt-4",
            ConversationId = "conv123",
            CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FinishReason = ChatFinishReason.ContentFilter,
            AdditionalProperties = new() { ["key"] = "value" },
            ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 }),
        };

        string json = JsonSerializer.Serialize(original, AIJsonUtilities.DefaultOptions);

        ChatResponseUpdate? result = JsonSerializer.Deserialize<ChatResponseUpdate>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal(5, result.Contents.Count);

        Assert.IsType<TextContent>(result.Contents[0]);
        Assert.Equal("text-1", ((TextContent)result.Contents[0]).Text);

        Assert.IsType<DataContent>(result.Contents[1]);
        Assert.Equal("data:image/png;base64,aGVsbG8=", ((DataContent)result.Contents[1]).Uri);

        Assert.IsType<FunctionCallContent>(result.Contents[2]);
        Assert.Equal("fc1", ((FunctionCallContent)result.Contents[2]).Name);

        Assert.IsType<DataContent>(result.Contents[3]);
        Assert.Equal("data"u8.ToArray(), ((DataContent)result.Contents[3]).Data.ToArray());

        Assert.IsType<TextContent>(result.Contents[4]);
        Assert.Equal("text-2", ((TextContent)result.Contents[4]).Text);

        Assert.Equal("author", result.AuthorName);
        Assert.Equal(ChatRole.Assistant, result.Role);
        Assert.Equal("id", result.ResponseId);
        Assert.Equal("messageid", result.MessageId);
        Assert.Equal("gpt-4", result.ModelId);
        Assert.Equal("conv123", result.ConversationId);
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAt);
        Assert.Equal(ChatFinishReason.ContentFilter, result.FinishReason);

        Assert.NotNull(result.ContinuationToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.ContinuationToken.ToBytes().ToArray());

        Assert.NotNull(result.AdditionalProperties);
        Assert.Single(result.AdditionalProperties);
        Assert.True(result.AdditionalProperties.TryGetValue("key", out object? value));
        Assert.Equal("value", value?.ToString());
    }

    [Fact]
    public void Clone_CreatesShallowCopy()
    {
        // Arrange
        var originalAdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" };
        var originalContents = new List<AIContent> { new TextContent("text1"), new TextContent("text2") };
        var originalRawRepresentation = new object();
        var originalCreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var originalContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });

        var original = new ChatResponseUpdate
        {
            AdditionalProperties = originalAdditionalProperties,
            AuthorName = "author",
            Contents = originalContents,
            ContinuationToken = originalContinuationToken,
            CreatedAt = originalCreatedAt,
            ConversationId = "conv123",
            FinishReason = ChatFinishReason.ContentFilter,
            MessageId = "msg456",
            ModelId = "model789",
            RawRepresentation = originalRawRepresentation,
            ResponseId = "resp012",
            Role = ChatRole.Assistant,
        };

        // Act
        var clone = original.Clone();

        // Assert - Different instances
        Assert.NotSame(original, clone);

        // Assert - All properties copied correctly
        Assert.Equal(original.AuthorName, clone.AuthorName);
        Assert.Equal(original.Role, clone.Role);
        Assert.Equal(original.CreatedAt, clone.CreatedAt);
        Assert.Equal(original.ConversationId, clone.ConversationId);
        Assert.Equal(original.FinishReason, clone.FinishReason);
        Assert.Equal(original.MessageId, clone.MessageId);
        Assert.Equal(original.ModelId, clone.ModelId);
        Assert.Equal(original.ResponseId, clone.ResponseId);

        // Assert - Reference properties are shallow copied (same references)
        Assert.Same(original.AdditionalProperties, clone.AdditionalProperties);
        Assert.Same(original.Contents, clone.Contents);
        Assert.Same(original.RawRepresentation, clone.RawRepresentation);
        Assert.Same(original.ContinuationToken, clone.ContinuationToken);
    }

    [Fact]
    public void Clone_WithNullProperties_CopiesCorrectly()
    {
        // Arrange
        var original = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            ResponseId = "resp123"
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(ChatRole.User, clone.Role);
        Assert.Equal("resp123", clone.ResponseId);
        Assert.Null(clone.AdditionalProperties);
        Assert.Null(clone.AuthorName);
        Assert.Null(clone.CreatedAt);
        Assert.Null(clone.ConversationId);
        Assert.Null(clone.FinishReason);
        Assert.Null(clone.MessageId);
        Assert.Null(clone.ModelId);
        Assert.Null(clone.RawRepresentation);
        Assert.Empty(clone.Contents); // Contents property initializes to empty list
    }

    [Fact]
    public void Clone_WithDefaultConstructor_CopiesCorrectly()
    {
        // Arrange
        var original = new ChatResponseUpdate();

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Null(clone.AuthorName);
        Assert.Null(clone.Role);
        Assert.Empty(clone.Contents);
        Assert.Null(clone.RawRepresentation);
        Assert.Null(clone.AdditionalProperties);
        Assert.Null(clone.ResponseId);
        Assert.Null(clone.MessageId);
        Assert.Null(clone.CreatedAt);
        Assert.Null(clone.FinishReason);
        Assert.Null(clone.ConversationId);
        Assert.Null(clone.ModelId);
    }

    [Fact]
    public void Clone_ModifyingClone_DoesNotAffectOriginal()
    {
        // Arrange
        var original = new ChatResponseUpdate
        {
            AuthorName = "original_author",
            Role = ChatRole.User,
            ResponseId = "original_id",
            ModelId = "original_model"
        };

        // Act
        var clone = original.Clone();
        clone.AuthorName = "modified_author";
        clone.Role = ChatRole.Assistant;
        clone.ResponseId = "modified_id";
        clone.ModelId = "modified_model";

        // Assert - Original remains unchanged
        Assert.Equal("original_author", original.AuthorName);
        Assert.Equal(ChatRole.User, original.Role);
        Assert.Equal("original_id", original.ResponseId);
        Assert.Equal("original_model", original.ModelId);

        // Assert - Clone has modified values
        Assert.Equal("modified_author", clone.AuthorName);
        Assert.Equal(ChatRole.Assistant, clone.Role);
        Assert.Equal("modified_id", clone.ResponseId);
        Assert.Equal("modified_model", clone.ModelId);
    }

    [Fact]
    public void Clone_ModifyingSharedReferences_AffectsBothInstances()
    {
        // Arrange
        var sharedAdditionalProperties = new AdditionalPropertiesDictionary { ["initial"] = "value" };
        var sharedContents = new List<AIContent> { new TextContent("initial") };

        var original = new ChatResponseUpdate
        {
            AdditionalProperties = sharedAdditionalProperties,
            Contents = sharedContents
        };

        // Act
        var clone = original.Clone();

        // Modify the shared reference objects
        sharedAdditionalProperties["modified"] = "new_value";
        sharedContents.Add(new TextContent("added"));

        // Assert - Both original and clone are affected due to shallow copy
        Assert.Same(original.AdditionalProperties, clone.AdditionalProperties);
        Assert.Same(original.Contents, clone.Contents);
        Assert.Equal(2, original.AdditionalProperties.Count);
        Assert.Equal(2, clone.AdditionalProperties?.Count);
        Assert.Equal(2, original.Contents.Count);
        Assert.Equal(2, clone.Contents.Count);
        Assert.True(original.AdditionalProperties.ContainsKey("modified"));
        Assert.True(clone.AdditionalProperties?.ContainsKey("modified"));
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "authorName": "author",
              "role": "assistant",
              "contents": [
                {
                  "$type": "text",
                  "text": "text-1"
                },
                {
                  "$type": "data",
                  "uri": "data:image/png;base64,aGVsbG8="
                }
              ],
              "responseId": "id",
              "messageId": "messageid",
              "conversationId": "conv123",
              "createdAt": "2022-01-01T00:00:00+00:00",
              "finishReason": "content_filter",
              "modelId": "gpt-4",
              "continuationToken": "AQID",
              "additionalProperties": {
                "key": "value"
              }
            }
            """;

        ChatResponseUpdate? result = JsonSerializer.Deserialize<ChatResponseUpdate>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("author", result.AuthorName);
        Assert.Equal(ChatRole.Assistant, result.Role);

        Assert.Equal(2, result.Contents.Count);
        var textContent = Assert.IsType<TextContent>(result.Contents[0]);
        Assert.Equal("text-1", textContent.Text);
        var dataContent = Assert.IsType<DataContent>(result.Contents[1]);
        Assert.Equal("data:image/png;base64,aGVsbG8=", dataContent.Uri);

        Assert.Equal("id", result.ResponseId);
        Assert.Equal("messageid", result.MessageId);
        Assert.Equal("conv123", result.ConversationId);
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAt);
        Assert.Equal(ChatFinishReason.ContentFilter, result.FinishReason);
        Assert.Equal("gpt-4", result.ModelId);

        Assert.NotNull(result.ContinuationToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.ContinuationToken.ToBytes().ToArray());

        Assert.NotNull(result.AdditionalProperties);
        Assert.Single(result.AdditionalProperties);
        Assert.Equal("value", result.AdditionalProperties["key"]?.ToString());
    }
}
