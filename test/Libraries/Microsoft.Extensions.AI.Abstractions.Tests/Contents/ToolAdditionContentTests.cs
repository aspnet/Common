// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolAdditionContentTests
{
    [Fact]
    public void Constructor_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>("tool", () => new ToolAdditionContent(null!));
    }

    [Fact]
    public void Constructor_PropsDefault()
    {
        AIFunction function = AIFunctionFactory.Create((string text) => text, "reply_to_user");
        ToolAdditionContent c = new(function);

        Assert.Same(function, c.Tool);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
    }

    [Fact]
    public void Serialization_Roundtrips_AsDeclaration()
    {
        AIFunction function = AIFunctionFactory.Create((string text) => text, "reply_to_user", "Replies to the user.");
        ToolAdditionContent content = new(function);

        string json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        ToolAdditionContent? deserialized = JsonSerializer.Deserialize<ToolAdditionContent>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.IsNotAssignableFrom<AIFunction>(deserialized.Tool);
        Assert.Equal("reply_to_user", deserialized.Tool.Name);
        Assert.Equal("Replies to the user.", deserialized.Tool.Description);
        Assert.True(JsonElement.DeepEquals(function.JsonSchema, deserialized.Tool.JsonSchema));
        Assert.Equal(function.ReturnJsonSchema.HasValue, deserialized.Tool.ReturnJsonSchema.HasValue);
    }

    [Fact]
    public void Serialization_Roundtrips_AsAIContent()
    {
        AIFunctionDeclaration declaration = AIFunctionFactory.CreateDeclaration(
            "get_weather", null, JsonElement.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}"""));
        AIContent content = new ToolAdditionContent(declaration);

        string json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        Assert.Contains("\"$type\": \"toolAddition\"", json);

        AIContent? deserialized = JsonSerializer.Deserialize<AIContent>(json, AIJsonUtilities.DefaultOptions);
        ToolAdditionContent addition = Assert.IsType<ToolAdditionContent>(deserialized);
        Assert.Equal("get_weather", addition.Tool.Name);
        Assert.Equal(string.Empty, addition.Tool.Description);
        Assert.True(JsonElement.DeepEquals(declaration.JsonSchema, addition.Tool.JsonSchema));
        Assert.Null(addition.Tool.ReturnJsonSchema);
    }

    [Fact]
    public void Deserialization_WithoutName_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ToolAdditionContent>("""{"tool":{"jsonSchema":{}}}""", AIJsonUtilities.DefaultOptions));
    }
}
