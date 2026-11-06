// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Http.Diagnostics;

internal sealed class HttpRouteFormatter : IHttpRouteFormatter
{
    private const char ForwardSlashSymbol = '/';

#if NET6_0_OR_GREATER
    private const char ForwardSlash = ForwardSlashSymbol;
#else
#pragma warning disable IDE1006 // Naming Styles
    private static readonly char[] ForwardSlash = new[] { ForwardSlashSymbol };
#pragma warning restore IDE1006 // Naming Styles
#endif

    private readonly IHttpRouteParser _httpRouteParser;
    private readonly IRedactorProvider _redactorProvider;

    public HttpRouteFormatter(IHttpRouteParser httpRouteParser, IRedactorProvider redactorProvider)
    {
        _httpRouteParser = httpRouteParser;
        _redactorProvider = redactorProvider;
    }

    public string Format(string httpRoute, string httpPath, HttpRouteParameterRedactionMode redactionMode, IReadOnlyDictionary<string, DataClassification> parametersToRedact)
    {
        var routeSegments = _httpRouteParser.ParseRoute(httpRoute);
        return Format(routeSegments, httpPath, redactionMode, parametersToRedact);
    }

    public string Format(
        in ParsedRouteSegments routeSegments,
        string httpPath,
        HttpRouteParameterRedactionMode redactionMode,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact)
    {
        if (routeSegments.ParameterCount == 0 ||
            !IsRedactionRequired(routeSegments, redactionMode, parametersToRedact))
        {
            return httpPath.Trim(ForwardSlash);
        }

        var httpPathAsSpan = httpPath.AsSpan().TrimStart(ForwardSlash);
        var pathStringBuilder = PoolFactory.SharedStringBuilderPool.Get();

        try
        {
            int offset = 0;

            for (var i = 0; i < routeSegments.Segments.Length; i++)
            {
                var segment = routeSegments.Segments[i];

                if (segment.IsParam)
                {
                    var parameterContent = segment.Content;
                    var parameterTemplateLength = parameterContent.Length + 2;

                    var startIndex = segment.Start + offset;

                    // If we exceed a length of the http path it means that the appropriate http route
                    // has optional parameters or parameters with default values, and these parameters
                    // are omitted in the http path. In this case we stop processing and return resulting
                    // http path.
                    if (startIndex >= httpPathAsSpan.Length)
                    {
                        break;
                    }

                    int length;

                    if (i < routeSegments.Segments.Length - 1)
                    {
                        length = HttpRouteParser.GetParameterLength(httpPathAsSpan.Slice(startIndex), routeSegments.Segments, i);

                        if (length == -1)
                        {
                            length = httpPathAsSpan.Slice(startIndex).IndexOf(routeSegments.Segments[i + 1].Content[0]);
                        }
                    }
                    else
                    {
                        length = httpPathAsSpan.Slice(startIndex).IndexOf(ForwardSlash);
                    }

                    if (segment.IsCatchAll || length == -1)
                    {
                        length = httpPathAsSpan.Slice(startIndex).Length;
                    }

                    offset += length - parameterTemplateLength;

                    FormatParameter(httpPathAsSpan, segment, startIndex, length, redactionMode, parametersToRedact, pathStringBuilder);
                }
                else
                {
                    _ = pathStringBuilder.Append(segment.Content);
                }
            }

            RemoveTrailingForwardSlash(pathStringBuilder);

            return pathStringBuilder.ToString();
        }
        finally
        {
            PoolFactory.SharedStringBuilderPool.Return(pathStringBuilder);
        }
    }

    private static bool IsRedactionRequired(
        in ParsedRouteSegments routeSegments, HttpRouteParameterRedactionMode redactionMode, IReadOnlyDictionary<string, DataClassification> parametersToRedact)
    {
        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            return false;
        }

        foreach (var segment in routeSegments.Segments)
        {
            if (!segment.IsParam)
            {
                continue;
            }

            if (redactionMode == HttpRouteParameterRedactionMode.Strict)
            {
                // If no data class exists for a parameter, and the parameter is not a well known parameter, then we redact it.
                // If data class exists and it's anything other than DataClassification.None, then also we redact it.
                if ((!parametersToRedact.TryGetValue(segment.ParamName, out DataClassification classification) &&
                    !Segment.IsKnownUnredactableParameter(segment.ParamName)) ||
                    classification != DataClassification.None)
                {
                    return true;
                }
            }
            else if (redactionMode == HttpRouteParameterRedactionMode.Loose)
            {
                // If data class exists for a parameter and it's anything other than DataClassification.None, then we redact it.
                if (parametersToRedact.TryGetValue(segment.ParamName, out DataClassification classification) && classification != DataClassification.None)
                {
                    return true;
                }
            }
            else
            {
                throw new InvalidOperationException(TelemetryCommonExtensions.UnsupportedEnumValueExceptionMessage);
            }
        }

        return false;
    }

    private static void RemoveTrailingForwardSlash(StringBuilder formattedHttpPath)
    {
        if (formattedHttpPath.Length > 1)
        {
            int index = formattedHttpPath.Length - 1;

            if (formattedHttpPath[index] == ForwardSlashSymbol)
            {
                _ = formattedHttpPath.Remove(index, 1);
            }
        }
    }

    private void FormatParameter(
        ReadOnlySpan<char> httpPath,
        in Segment segment,
        int startIndex,
        int length,
        HttpRouteParameterRedactionMode redactionMode,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        StringBuilder outputBuffer)
    {
        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            FormatParameterInStrictMode(httpPath, segment, startIndex, length, parametersToRedact, outputBuffer);
            return;
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            FormatParameterInLooseMode(httpPath, segment, startIndex, length, parametersToRedact, outputBuffer);
        }
    }

    private void FormatParameterInStrictMode(
        ReadOnlySpan<char> httpPath,
        Segment httpRouteSegment,
        int startIndex,
        int length,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        StringBuilder outputBuffer)
    {
        if (parametersToRedact.TryGetValue(httpRouteSegment.ParamName, out var classification))
        {
            if (classification != DataClassification.None)
            {
                var redactor = _redactorProvider.GetRedactor(classification);
                _ = outputBuffer.AppendRedacted(redactor, httpPath.Slice(startIndex, length));
            }
            else
            {
#if NETCOREAPP3_1_OR_GREATER
                _ = outputBuffer.Append(httpPath.Slice(startIndex, length));
#else
                _ = outputBuffer.Append(httpPath.Slice(startIndex, length).ToString());
#endif
            }
        }
        else if (Segment.IsKnownUnredactableParameter(httpRouteSegment.ParamName))
        {
#if NETCOREAPP3_1_OR_GREATER
            _ = outputBuffer.Append(httpPath.Slice(startIndex, length));
#else
            _ = outputBuffer.Append(httpPath.Slice(startIndex, length).ToString());
#endif
        }
        else
        {
#if NETCOREAPP3_1_OR_GREATER
            _ = outputBuffer.Append(TelemetryConstants.Redacted.AsSpan());
#else
            _ = outputBuffer.Append(TelemetryConstants.Redacted);
#endif
        }
    }

    private void FormatParameterInLooseMode(
        ReadOnlySpan<char> httpPath,
        Segment httpRouteSegment,
        int startIndex,
        int length,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        StringBuilder outputBuffer)
    {
        if (parametersToRedact.TryGetValue(httpRouteSegment.ParamName, out DataClassification classification)
            && classification != DataClassification.None)
        {
            var redactor = _redactorProvider.GetRedactor(classification);
            _ = outputBuffer.AppendRedacted(redactor, httpPath.Slice(startIndex, length));
        }
        else
        {
#if NETCOREAPP3_1_OR_GREATER
            _ = outputBuffer.Append(httpPath.Slice(startIndex, length));
#else
            _ = outputBuffer.Append(httpPath.Slice(startIndex, length).ToString());
#endif
        }
    }
}
