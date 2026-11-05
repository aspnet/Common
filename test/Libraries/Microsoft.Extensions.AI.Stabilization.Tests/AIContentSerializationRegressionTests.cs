// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class AIContentSerializationRegressionTests
{
    // The real guarantee here is compile-time: AIContentSerializationRegressionContext will not build
    // if an experimental member leaks into its source-generated metadata. This test exercises the
    // generated context at runtime to prove it is wired up and that a List<AIContent> round-trips
    // through it. (The intentional persistence of the experimental RequiresConfirmation member is
    // covered separately by ToolApprovalRequestContentTests.RequiresConfirmation_RoundtripsThroughJson,
    // in a project that suppresses MEAI001 and can therefore reference the experimental member directly.)
    [Fact]
    public void ListAIContent_RoundtripsThroughSourceGeneratedContext()
    {
        List<AIContent> contents =
        [
            new TextContent("hello"),
            new UsageContent(new UsageDetails { InputTokenCount = 1, OutputTokenCount = 2 }),
        ];

        string json = JsonSerializer.Serialize(contents, AIContentSerializationRegressionContext.Default.ListAIContent);
        List<AIContent>? roundtripped = JsonSerializer.Deserialize(json, AIContentSerializationRegressionContext.Default.ListAIContent);

        Assert.NotNull(roundtripped);
        Assert.Equal(2, roundtripped.Count);
        Assert.IsType<TextContent>(roundtripped[0]);
        Assert.IsType<UsageContent>(roundtripped[1]);
    }
}
