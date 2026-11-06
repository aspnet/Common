// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolRemovalContentTests
{
    [Fact]
    public void Constructor_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>("toolName", () => new ToolRemovalContent(null!));
        Assert.Throws<ArgumentException>("toolName", () => new ToolRemovalContent(string.Empty));
        Assert.Throws<ArgumentException>("toolName", () => new ToolRemovalContent(" "));
    }

    [Fact]
    public void Constructor_PropsDefault()
    {
        ToolRemovalContent c = new("get_weather");

        Assert.Equal("get_weather", c.ToolName);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
    }

    [Fact]
    public void Serialization_Roundtrips_AsAIContent()
    {
        AIContent content = new ToolRemovalContent("get_weather");

        string json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        Assert.Contains("\"$type\": \"toolRemoval\"", json);

        AIContent? deserialized = JsonSerializer.Deserialize<AIContent>(json, AIJsonUtilities.DefaultOptions);
        Assert.Equal("get_weather", Assert.IsType<ToolRemovalContent>(deserialized).ToolName);
    }
}
