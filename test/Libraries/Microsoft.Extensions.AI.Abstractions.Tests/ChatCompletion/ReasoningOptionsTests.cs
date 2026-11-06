// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ReasoningOptionsTests
{
    [Fact]
    public void Constructor_Default_PropertiesAreNull()
    {
        ReasoningOptions options = new();

        Assert.Null(options.Effort);
        Assert.Null(options.Output);
    }

    [Theory]
    [InlineData(ReasoningEffort.None)]
    [InlineData(ReasoningEffort.Low)]
    [InlineData(ReasoningEffort.Medium)]
    [InlineData(ReasoningEffort.High)]
    [InlineData(ReasoningEffort.ExtraHigh)]
    [InlineData(ReasoningEffort.Max)]
    public void Effort_Roundtrips(ReasoningEffort effort)
    {
        ReasoningOptions options = new() { Effort = effort };
        Assert.Equal(effort, options.Effort);
    }

    [Theory]
    [InlineData(ReasoningOutput.None)]
    [InlineData(ReasoningOutput.Summary)]
    [InlineData(ReasoningOutput.Full)]
    public void Output_Roundtrips(ReasoningOutput output)
    {
        ReasoningOptions options = new() { Output = output };
        Assert.Equal(output, options.Output);
    }

    [Fact]
    public void JsonSerialization_Roundtrips()
    {
        ReasoningOptions options = new()
        {
            Effort = ReasoningEffort.High,
            Output = ReasoningOutput.Full,
        };

        string json = JsonSerializer.Serialize(options, TestJsonSerializerContext.Default.ReasoningOptions);
        ReasoningOptions? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(options.Effort, deserialized.Effort);
        Assert.Equal(options.Output, deserialized.Output);
    }

    [Fact]
    public void JsonSerialization_NullProperties_Roundtrips()
    {
        ReasoningOptions options = new();

        string json = JsonSerializer.Serialize(options, TestJsonSerializerContext.Default.ReasoningOptions);
        ReasoningOptions? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Effort);
        Assert.Null(deserialized.Output);
    }

    [Fact]
    public void JsonSerialization_EffortOnly_Roundtrips()
    {
        ReasoningOptions options = new() { Effort = ReasoningEffort.Medium };

        string json = JsonSerializer.Serialize(options, TestJsonSerializerContext.Default.ReasoningOptions);
        ReasoningOptions? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(ReasoningEffort.Medium, deserialized.Effort);
        Assert.Null(deserialized.Output);
    }

    [Fact]
    public void JsonSerialization_OutputOnly_Roundtrips()
    {
        ReasoningOptions options = new() { Output = ReasoningOutput.Summary };

        string json = JsonSerializer.Serialize(options, TestJsonSerializerContext.Default.ReasoningOptions);
        ReasoningOptions? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Effort);
        Assert.Equal(ReasoningOutput.Summary, deserialized.Output);
    }

    [Fact]
    public void JsonSerialization_AllEffortValues_SerializeAsStrings()
    {
        // Test all ReasoningEffort values serialize correctly
        foreach (ReasoningEffort effort in new[] { ReasoningEffort.None, ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High, ReasoningEffort.ExtraHigh, ReasoningEffort.Max })
        {
            string json = JsonSerializer.Serialize(effort, TestJsonSerializerContext.Default.ReasoningEffort);
            ReasoningEffort? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningEffort);
            Assert.Equal(effort, deserialized);
        }
    }

    [Fact]
    public void JsonSerialization_AllOutputValues_SerializeAsStrings()
    {
        // Test all ReasoningOutput values serialize correctly
        foreach (ReasoningOutput output in new[] { ReasoningOutput.None, ReasoningOutput.Summary, ReasoningOutput.Full })
        {
            string json = JsonSerializer.Serialize(output, TestJsonSerializerContext.Default.ReasoningOutput);
            ReasoningOutput? deserialized = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.ReasoningOutput);
            Assert.Equal(output, deserialized);
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "effort": "High",
              "output": "Full"
            }
            """;

        ReasoningOptions? result = JsonSerializer.Deserialize(Json, TestJsonSerializerContext.Default.ReasoningOptions);

        Assert.NotNull(result);
        Assert.Equal(ReasoningEffort.High, result.Effort);
        Assert.Equal(ReasoningOutput.Full, result.Output);
    }
}
