// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Http.Diagnostics;

internal sealed class HttpRouteParser : IHttpRouteParser
{
#if NETCOREAPP3_1_OR_GREATER
    private const char ForwardSlash = '/';
#else
#pragma warning disable IDE1006 // Naming Styles
    private static readonly char[] ForwardSlash = new[] { '/' };
#pragma warning restore IDE1006 // Naming Styles
#endif

    private readonly IRedactorProvider _redactorProvider;
    private readonly ConcurrentDictionary<string, ParsedRouteSegments> _routeTemplateSegmentsCache = new();

    public HttpRouteParser(IRedactorProvider redactorProvider)
    {
        _redactorProvider = redactorProvider;
    }

    public bool TryExtractParameters(
        string httpPath,
        in ParsedRouteSegments routeSegments,
        HttpRouteParameterRedactionMode redactionMode,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        ref HttpRouteParameter[] httpRouteParameters)
    {
        int paramCount = routeSegments.ParameterCount;

        if (httpRouteParameters == null || httpRouteParameters.Length < paramCount)
        {
            return false;
        }

        var httpPathAsSpan = httpPath.AsSpan();
        httpPathAsSpan = httpPathAsSpan.TrimStart(ForwardSlash);

        if (paramCount > 0)
        {
            int offset = 0;
            int index = 0;

            for (int i = 0; i < routeSegments.Segments.Length; i++)
            {
                var segment = routeSegments.Segments[i];
                if (segment.IsParam)
                {
                    var startIndex = segment.Start + offset;

                    // If we exceed a length of the http path it means that the appropriate http route
                    // has optional parameters or parameters with default values, and these parameters
                    // are omitted in the http path. In this case we return a default value of the
                    // omitted parameter.
                    string parameterValue = segment.DefaultValue;

                    bool isRedacted = false;

                    if (startIndex < httpPathAsSpan.Length)
                    {
                        var parameterContent = segment.Content;
                        var parameterTemplateLength = parameterContent.Length + 2;

                        var length = GetParameterLength(httpPathAsSpan.Slice(startIndex), routeSegments.Segments, i);

                        if (length == -1)
                        {
                            length = httpPathAsSpan.Slice(startIndex).IndexOf(ForwardSlash);
                        }

                        if (segment.IsCatchAll || length == -1)
                        {
                            length = httpPathAsSpan.Slice(startIndex).Length;
                        }

                        offset += length - parameterTemplateLength;

                        parameterValue = GetRedactedParameterValue(httpPathAsSpan, segment, startIndex, length, redactionMode, parametersToRedact, ref isRedacted);
                    }

                    httpRouteParameters[index++] = new HttpRouteParameter(segment.ParamName, parameterValue, isRedacted);
                }
            }
        }

        return true;
    }

    public ParsedRouteSegments ParseRoute(string httpRoute)
    {
        return _routeTemplateSegmentsCache.GetOrAdd(httpRoute, httpRoute =>
        {
            httpRoute = httpRoute.TrimStart(ForwardSlash);

            var pos = 0;
            var len = httpRoute.Length;
            var start = 0;
            char ch;

            var segments = new List<Segment>();

            while (pos < len)
            {
                ch = httpRoute[pos];

                // Start of a parameter segment.
                if (ch == '{')
                {
                    // End of the current text segment.
                    if (pos > start)
                    {
                        segments.Add(new Segment(
                            start: start,
                            end: pos,
                            content: GetSegmentContent(httpRoute, start, pos),
                            isParam: false));
                    }

                    segments.Add(GetParameterSegment(httpRoute, ref pos));
                    start = pos + 1;
                }

                // Start of the query parameters sections.
                else if (ch == '?')
                {
                    // Remove the query parameters from the template.
                    httpRoute = httpRoute.Substring(0, pos);
                    break;
                }

                pos++;
            }

            // End the last text segment if any.
            if (start < pos)
            {
                segments.Add(new Segment(
                    start: start,
                    end: pos,
                    content: GetSegmentContent(httpRoute, start, pos),
                    isParam: false));
            }

            return new ParsedRouteSegments(httpRoute, segments.ToArray());
        });
    }

    // Returns the length of the value of the parameter segments[index] (httpPath starts at the value) when the parameter
    // is followed by literals in the same path segment. Like ASP.NET Core routing, the literals are matched from right
    // to left, ignoring case, e.g. route "{token}.{format}" with path "a.b.json" gives token = "a.b" and format = "json".
    // Returns -1 when not applicable or not matched.
    internal static int GetParameterLength(ReadOnlySpan<char> httpPath, Segment[] segments, int index)
    {
        var last = index + 1;
        if (last == segments.Length || segments[last].IsParam || segments[last].Content[0] == '/')
        {
            return -1;
        }

        while (last < segments.Length - 1 && (segments[last].IsParam || segments[last].Content.AsSpan().IndexOf('/') < 0))
        {
            last++;
        }

        var length = httpPath.IndexOf('/');
        if (length == -1)
        {
            length = httpPath.Length;
        }

        for (var i = last; i > index; i--)
        {
            var literal = segments[i].Content.AsSpan();
            var slashIndex = literal.IndexOf('/');
            if (segments[i].IsParam || slashIndex == 0)
            {
                continue;
            }

            // Leave at least one character for the parameter value that follows the literal.
            var end = i < last ? length - 1 : length;
            var value = slashIndex > 0 ? literal.Slice(0, slashIndex) : literal;
#if NET
            length = end > 0 ? httpPath.Slice(0, end).LastIndexOf(value, StringComparison.OrdinalIgnoreCase) : -1;
#else
            length = end > 0 ? httpPath.Slice(0, end).ToString().LastIndexOf(value.ToString(), StringComparison.OrdinalIgnoreCase) : -1;
#endif

            if (length == -1)
            {
                return -1;
            }
        }

        return length;
    }

    private static Segment GetParameterSegment(string httpRoute, ref int pos)
    {
        const int PositionNotFound = -1;

        int start = pos++;
        int paramNameEnd = PositionNotFound;
        int paramNameStart = start + 1;
        bool catchAllParamFound = false;
        int defaultValueStart = PositionNotFound;

        char ch;

        while ((ch = httpRoute[pos]) != '}')
        {
            // The segment has a default value '='. The character indicates
            // that we've met the end of the segment's parameter name and
            // the start of the default value.
            if (ch == '=')
            {
                if (paramNameEnd == PositionNotFound)
                {
                    paramNameEnd = pos;
                }

                defaultValueStart = pos + 1;
            }

            // The segment is optional '?' or has a constraint ':'.
            // When we meet one of the above characters it indicates
            // that we've met the end of the segment's parameter name.
            else if (ch == '?' || ch == ':')
            {
                if (paramNameEnd == PositionNotFound)
                {
                    paramNameEnd = pos;
                }
            }

            // The segment has '*' catch all parameter.
            // When we meet the character it indicates param start position needs to be adjusted, so that we capture 'param' instead of '*param'
            // *param can only appear after opening curly brace and position needs to be adjusted only once
            else if (!catchAllParamFound && ch == '*' && pos > 0 && httpRoute[pos - 1] == '{')
            {
                paramNameStart++;

                // Catch all parameters can start with one or two '*' characters.
                if (httpRoute[paramNameStart] == '*')
                {
                    paramNameStart++;
                }

                catchAllParamFound = true;
            }

            pos++;
        }

        // Throw an ArgumentException if the segment is a catch-all parameter and not the last segment.
        // The current position should be either the end of the route or the second to last position followed by a '/'.
        if (catchAllParamFound)
        {
            bool isLastPosition = pos == httpRoute.Length - 1;
            bool isSecondToLastPosition = pos == httpRoute.Length - 2;

            if (!(isLastPosition || (isSecondToLastPosition && httpRoute[pos + 1] == '/')))
            {
                Throw.ArgumentException(nameof(httpRoute), "A catch-all parameter must be the last segment in the route.");
            }
        }

        string content = GetSegmentContent(httpRoute, paramNameStart, pos);
        string paramName = paramNameEnd == PositionNotFound
            ? content
            : GetSegmentContent(httpRoute, paramNameStart, paramNameEnd);
        string defaultValue = defaultValueStart == PositionNotFound
            ? string.Empty
            : GetSegmentContent(httpRoute, defaultValueStart, pos);

        // Remove the opening and closing curly braces when getting content.
        return new Segment(
            start: start,
            end: pos + 1,
            content: content,
            isParam: true,
            paramName: paramName,
            defaultValue: defaultValue,
            isCatchAll: catchAllParamFound);
    }

    private static string GetSegmentContent(string httpRoute, int start, int end)
    {
        return httpRoute.Substring(start, end - start);
    }

    private string GetRedactedParameterValue(
        ReadOnlySpan<char> httpPath,
        in Segment segment,
        int startIndex,
        int length,
        HttpRouteParameterRedactionMode redactionMode,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        ref bool isRedacted)
    {
        return redactionMode switch
        {
            HttpRouteParameterRedactionMode.None => httpPath.Slice(startIndex, length).ToString(),
            HttpRouteParameterRedactionMode.Strict => GetRedactedParameterInStrictMode(httpPath, segment, startIndex, length, parametersToRedact, ref isRedacted),
            HttpRouteParameterRedactionMode.Loose => GetRedactedParameterInLooseMode(httpPath, segment, startIndex, length, parametersToRedact, ref isRedacted),
            _ => throw new InvalidOperationException(TelemetryCommonExtensions.UnsupportedEnumValueExceptionMessage)
        };
    }

    private string GetRedactedParameterInStrictMode(
        ReadOnlySpan<char> httpPathAsSpan,
        Segment segment,
        int startIndex,
        int length,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        ref bool isRedacted)
    {
        if (parametersToRedact.TryGetValue(segment.ParamName, out DataClassification classification))
        {
            if (classification != DataClassification.None)
            {
                var redactor = _redactorProvider.GetRedactor(classification);
                isRedacted = true;

                return redactor.Redact(httpPathAsSpan.Slice(startIndex, length));
            }

            return httpPathAsSpan.Slice(startIndex, length).ToString();
        }

        if (Segment.IsKnownUnredactableParameter(segment.ParamName))
        {
            return httpPathAsSpan.Slice(startIndex, length).ToString();
        }

        isRedacted = true;

        return TelemetryConstants.Redacted;
    }

    private string GetRedactedParameterInLooseMode(
        ReadOnlySpan<char> httpPathAsSpan,
        Segment segment,
        int startIndex,
        int length,
        IReadOnlyDictionary<string, DataClassification> parametersToRedact,
        ref bool isRedacted)
    {
        if (parametersToRedact.TryGetValue(segment.ParamName, out DataClassification classification)
            && classification != DataClassification.None)
        {
            var redactor = _redactorProvider.GetRedactor(classification);
            isRedacted = true;

            return redactor.Redact(httpPathAsSpan.Slice(startIndex, length));
        }

        return httpPathAsSpan.Slice(startIndex, length).ToString();
    }
}
