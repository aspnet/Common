// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
#if NET8_0_OR_GREATER
using System.Buffers.Text;
#endif
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Shared.Diagnostics;

#pragma warning disable CA1307 // Specify StringComparison for clarity

namespace Microsoft.Extensions.AI;

/// <summary>
/// Minimal data URI parser based on RFC 2397: https://datatracker.ietf.org/doc/html/rfc2397.
/// </summary>
internal static class DataUriParser
{
    public static string Scheme => "data:";

    /// <summary>
    /// The default media type per RFC 2397 when the media type is omitted.
    /// </summary>
    public const string DefaultMediaType = "text/plain;charset=US-ASCII";

    public static DataUri Parse(ReadOnlyMemory<char> dataUri)
    {
        // Validate, then trim off the "data:" scheme.
        if (!dataUri.Span.StartsWith(Scheme.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            throw new UriFormatException("Invalid data URI format: the data URI must start with 'data:'.");
        }

        dataUri = dataUri.Slice(Scheme.Length);

        // Find the comma separating the metadata from the data.
        int commaPos = dataUri.Span.IndexOf(',');
        if (commaPos < 0)
        {
            throw new UriFormatException("Invalid data URI format: the data URI must contain a comma separating the metadata and the data.");
        }

        ReadOnlyMemory<char> metadata = dataUri.Slice(0, commaPos);

        ReadOnlyMemory<char> data = dataUri.Slice(commaPos + 1);
        bool isBase64 = false;

        // Determine whether the data is Base64-encoded or percent-encoded (Uri-encoded).
        // If it's base64-encoded, validate it. If it's Uri-encoded, there's nothing to validate,
        // as WebUtility.UrlDecodeToBytes will successfully decode any input with no sequence considered invalid.
        if (metadata.Span.EndsWith(";base64".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            metadata = metadata.Slice(0, metadata.Length - ";base64".Length);
            isBase64 = true;
            if (!IsValidBase64Data(data.Span))
            {
                throw new UriFormatException("Invalid data URI format: the data URI is base64-encoded, but the data is not a valid base64 string.");
            }
        }

        // Validate the media type, if present.
        // Per RFC 2397, if the media type is omitted, it defaults to "text/plain;charset=US-ASCII".
        ReadOnlySpan<char> span = metadata.Span.Trim();
        string? mediaType = null;
        if (span.IsEmpty)
        {
            mediaType = DefaultMediaType;
        }
        else if (!IsValidMediaType(span, ref mediaType))
        {
            throw new UriFormatException("Invalid data URI format: the media type is not a valid.");
        }

        return new DataUri(data, isBase64, mediaType);
    }

    public static string ThrowIfInvalidMediaType(
        string mediaType, [CallerArgumentExpression(nameof(mediaType))] string parameterName = "")
    {
        _ = Throw.IfNullOrWhitespace(mediaType, parameterName);

        if (!IsValidMediaType(mediaType))
        {
            Throw.ArgumentException(parameterName, $"An invalid media type was specified: '{mediaType}'");
        }

        return mediaType;
    }

    public static bool IsValidMediaType(string mediaType) =>
        IsValidMediaType(mediaType.AsSpan(), ref mediaType);

    /// <summary>Validates that a media type is valid, and if successful, ensures we have it as a string.</summary>
    public static bool IsValidMediaType(ReadOnlySpan<char> mediaTypeSpan, [NotNull] ref string? mediaType)
    {
        // For common media types, we can avoid both allocating a string for the span and avoid parsing overheads.
        string? knownType = mediaTypeSpan switch
        {
            DefaultMediaType => DefaultMediaType,
            "application/json" => "application/json",
            "application/octet-stream" => "application/octet-stream",
            "application/pdf" => "application/pdf",
            "application/xml" => "application/xml",
            "audio/mpeg" => "audio/mpeg",
            "audio/ogg" => "audio/ogg",
            "audio/wav" => "audio/wav",
            "image/apng" => "image/apng",
            "image/avif" => "image/avif",
            "image/bmp" => "image/bmp",
            "image/gif" => "image/gif",
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            "image/svg+xml" => "image/svg+xml",
            "image/tiff" => "image/tiff",
            "image/webp" => "image/webp",
            "text/css" => "text/css",
            "text/csv" => "text/csv",
            "text/html" => "text/html",
            "text/javascript" => "text/javascript",
            "text/plain" => "text/plain",
            "text/plain;charset=UTF-8" => "text/plain;charset=UTF-8",
            "text/xml" => "text/xml",
            _ => null,
        };
        if (knownType is not null)
        {
            mediaType = knownType;
            return true;
        }

        // Otherwise, do the full validation using the same logic as HttpClient.
        mediaType ??= mediaTypeSpan.ToString();
        return MediaTypeHeaderValue.TryParse(mediaType, out _);
    }

    public static bool HasTopLevelMediaType(string mediaType, string topLevelMediaType)
    {
        int slashIndex = mediaType.IndexOf('/');

        ReadOnlySpan<char> span = slashIndex < 0 ? mediaType.AsSpan() : mediaType.AsSpan(0, slashIndex);
        span = span.Trim();

        return span.Equals(topLevelMediaType.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Test whether the value is a base64 string without whitespace.</summary>
    private static bool IsValidBase64Data(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return true;
        }

#if NET8_0_OR_GREATER
        return Base64.IsValid(value) && !value.ContainsAny(" \t\r\n");
#else
        if (value!.Length % 4 != 0)
        {
            return false;
        }

        var index = value.Length - 1;

        // Step back over one or two padding chars
        if (value[index] == '=')
        {
            index--;
        }

        if (value[index] == '=')
        {
            index--;
        }

        // Now traverse over characters
        for (var i = 0; i <= index; i++)
        {
            bool validChar = value[i] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/';
            if (!validChar)
            {
                return false;
            }
        }

        return true;
#endif
    }

    /// <summary>Provides the parts of a parsed data URI.</summary>
    public sealed class DataUri(ReadOnlyMemory<char> data, bool isBase64, string? mediaType)
    {
        public string? MediaType { get; } = mediaType;

        public ReadOnlyMemory<char> Data { get; } = data;

        public bool IsBase64 { get; } = isBase64;

        public byte[] ToByteArray()
        {
            if (IsBase64)
            {
                return Convert.FromBase64String(Data.ToString());
            }

            // Each %xx escape is the octet it names, and '+' is a literal plus, not a space as in form encoding.
            byte[] bytes = Encoding.UTF8.GetBytes(Data.ToString().Replace("+", "%2B"));
            return WebUtility.UrlDecodeToBytes(bytes, 0, bytes.Length)!;
        }
    }
}
