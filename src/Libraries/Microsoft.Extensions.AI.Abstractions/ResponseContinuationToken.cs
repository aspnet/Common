// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a token used to resume, continue, or rehydrate an operation across multiple scenarios/calls,
/// such as resuming a streamed response from a specific point or retrieving the result of a background operation.
/// Subclasses of this class encapsulate all necessary information within the token to facilitate these actions.
/// </summary>
[JsonConverter(typeof(Converter))]
public class ResponseContinuationToken
{
    /// <summary>Bytes representing this token.</summary>
    private readonly ReadOnlyMemory<byte> _bytes;

    /// <summary>Initializes a new instance of the <see cref="ResponseContinuationToken"/> class.</summary>
    protected ResponseContinuationToken()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResponseContinuationToken"/> class.</summary>
    /// <param name="bytes">Bytes to create the token from.</param>
    protected ResponseContinuationToken(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
    }

    /// <summary>Create a new instance of <see cref="ResponseContinuationToken"/> from the provided <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">Bytes representing the <see cref="ResponseContinuationToken"/>.</param>
    /// <returns>A <see cref="ResponseContinuationToken"/> equivalent to the one from which
    /// the original<see cref="ResponseContinuationToken"/> bytes were obtained.</returns>
    public static ResponseContinuationToken FromBytes(ReadOnlyMemory<byte> bytes) => new(bytes);

    /// <summary>Gets the bytes representing this <see cref="ResponseContinuationToken"/>.</summary>
    /// <returns>Bytes representing the <see cref="ResponseContinuationToken"/>.</returns>"/>
    public virtual ReadOnlyMemory<byte> ToBytes() => _bytes;

    /// <summary>Provides a <see cref="JsonConverter{ResponseContinuationToken}"/> for serializing <see cref="ResponseContinuationToken"/> instances.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ResponseContinuationToken>
    {
        /// <inheritdoc/>
        public override ResponseContinuationToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ResponseContinuationToken.FromBytes(reader.GetBytesFromBase64());
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ResponseContinuationToken value, JsonSerializerOptions options)
        {
            _ = Throw.IfNull(writer);
            _ = Throw.IfNull(value);

            writer.WriteBase64StringValue(value.ToBytes().Span);
        }
    }
}
