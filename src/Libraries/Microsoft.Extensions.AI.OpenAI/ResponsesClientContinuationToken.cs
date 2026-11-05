// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Represents a continuation token for OpenAI responses.</summary>
/// <remarks>
/// The token is used for resuming streamed background responses and continuing
/// non-streamed background responses until completion.
/// </remarks>
internal sealed class ResponsesClientContinuationToken : ResponseContinuationToken
{
    /// <summary>Initializes a new instance of the <see cref="ResponsesClientContinuationToken"/> class.</summary>
    internal ResponsesClientContinuationToken(string responseId)
    {
        ResponseId = responseId;
    }

    /// <summary>Gets the Id of the response.</summary>
    internal string ResponseId { get; }

    /// <summary>Gets or sets the sequence number of a streamed update.</summary>
    internal int? SequenceNumber { get; set; }

    /// <inheritdoc/>
    public override ReadOnlyMemory<byte> ToBytes()
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);

        writer.WriteStartObject();

        writer.WriteString("responseId", ResponseId);

        if (SequenceNumber.HasValue)
        {
            writer.WriteNumber("sequenceNumber", SequenceNumber.Value);
        }

        writer.WriteEndObject();

        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>Create a new instance of <see cref="ResponsesClientContinuationToken"/> from the provided <paramref name="token"/>.
    /// </summary>
    /// <param name="token">The token to create the <see cref="ResponsesClientContinuationToken"/> from.</param>
    /// <returns>A <see cref="ResponsesClientContinuationToken"/> equivalent of the provided <paramref name="token"/>.</returns>
    internal static ResponsesClientContinuationToken FromToken(ResponseContinuationToken token)
    {
        if (token is ResponsesClientContinuationToken openAIResponsesContinuationToken)
        {
            return openAIResponsesContinuationToken;
        }

        ReadOnlyMemory<byte> data = token.ToBytes();

        if (data.Length == 0)
        {
            Throw.ArgumentException(nameof(token), "Failed to create OpenAI continuation token from provided token because it does not contain any data.");
        }

        Utf8JsonReader reader = new(data.Span);

        string? responseId = null;
        int? sequenceNumber = null;

        _ = reader.Read();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            string propertyName = reader.GetString()!;

            switch (propertyName)
            {
                case "responseId":
                    _ = reader.Read();
                    responseId = reader.GetString();
                    break;
                case "sequenceNumber":
                    _ = reader.Read();
                    sequenceNumber = reader.GetInt32();
                    break;
                default:
                    Throw.ArgumentException(nameof(token), $"Unrecognized property '{propertyName}'.");
                    break;
            }
        }

        if (responseId is null)
        {
            Throw.ArgumentException(nameof(token), "Failed to create OpenAI continuation token from provided token because it does not contain a responseId.");
        }

        return new(responseId)
        {
            SequenceNumber = sequenceNumber
        };
    }
}
