// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ResponseContinuationTokenTests
{
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    public void Bytes_Roundtrip(byte[] testBytes)
    {
        ResponseContinuationToken token = ResponseContinuationToken.FromBytes(testBytes);

        Assert.NotNull(token);
        Assert.Equal(testBytes, token.ToBytes().ToArray());
    }

    [Theory]
    [InlineData(new byte[0], "\"\"")]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 }, "\"AQIDBAU=\"")]
    public void JsonSerialization_Roundtrips(byte[] testBytes, string expectedJson)
    {
        ResponseContinuationToken originalToken = ResponseContinuationToken.FromBytes(testBytes);

        // Act
        string json = JsonSerializer.Serialize(originalToken, AIJsonUtilities.DefaultOptions);

        ResponseContinuationToken? deserializedToken = JsonSerializer.Deserialize<ResponseContinuationToken>(json, AIJsonUtilities.DefaultOptions);

        // Assert
        Assert.Equal(expectedJson, json);
        Assert.NotNull(deserializedToken);
        Assert.Equal(originalToken.ToBytes().ToArray(), deserializedToken.ToBytes().ToArray());
        Assert.NotSame(originalToken, deserializedToken);
    }

    [Fact]
    public void DefaultOptions_ContainsMetadataForResponseContinuationToken()
    {
        Assert.True(AIJsonUtilities.DefaultOptions.TryGetTypeInfo(typeof(ResponseContinuationToken), out var info));
        Assert.NotNull(info);
        Assert.Equal(typeof(ResponseContinuationToken), info.Type);
    }
}
