// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Http.Logging.Internal;

internal sealed class HttpRequestReader : IHttpRequestReader
{
    private readonly IHttpRouteFormatter _routeFormatter;
    private readonly IHttpRouteParser _httpRouteParser;
    private readonly IHttpHeadersReader _httpHeadersReader;
    private readonly FrozenDictionary<string, DataClassification> _defaultSensitiveParameters;
    private readonly FrozenDictionary<string, DataClassification> _queryParameterDataClasses;

    private readonly bool _logRequestBody;
    private readonly bool _logResponseBody;
    private readonly bool _logRequestQueryParameters;

    private readonly bool _logRequestHeaders;
    private readonly bool _logResponseHeaders;

    private readonly HttpRouteParameterRedactionMode _routeParameterRedactionMode;

    // These are not registered in DI as handler today is public, and we would need to make all of those types public.
    // They are not implemented as statics to simplify design and pass less arguments around.
    // Also wanted to encapsulate logic of reading each part of the request to simplify handler logic itself.
    private readonly HttpRequestBodyReader _httpRequestBodyReader;
    private readonly HttpResponseBodyReader _httpResponseBodyReader;

    private readonly OutgoingPathLoggingMode _outgoingPathLogMode;
    private readonly IOutgoingRequestContext _requestMetadataContext;
    private readonly HttpDependencyMetadataResolver? _dependencyMetadataResolver;

    public HttpRequestReader(
        IServiceProvider serviceProvider,
        IOptionsMonitor<LoggingOptions> optionsMonitor,
        IHttpRouteFormatter routeFormatter,
        IHttpRouteParser httpRouteParser,
        IOutgoingRequestContext requestMetadataContext,
        HttpDependencyMetadataResolver? dependencyMetadataResolver = null,
        [ServiceKey] string? serviceKey = null)
        : this(
              optionsMonitor.GetKeyedOrCurrent(serviceKey),
              routeFormatter,
              httpRouteParser,
              serviceProvider.GetRequiredOrKeyedRequiredService<IHttpHeadersReader>(serviceKey),
              requestMetadataContext,
              dependencyMetadataResolver)
    {
    }

    internal HttpRequestReader(
        LoggingOptions options,
        IHttpRouteFormatter routeFormatter,
        IHttpRouteParser httpRouteParser,
        IHttpHeadersReader httpHeadersReader,
        IOutgoingRequestContext requestMetadataContext,
        HttpDependencyMetadataResolver? dependencyMetadataResolver = null)
    {
        _outgoingPathLogMode = Throw.IfOutOfRange(options.RequestPathLoggingMode);
        _httpHeadersReader = httpHeadersReader;

        _routeFormatter = routeFormatter;
        _httpRouteParser = httpRouteParser;
        _requestMetadataContext = requestMetadataContext;
        _dependencyMetadataResolver = dependencyMetadataResolver;

        _defaultSensitiveParameters = options.RouteParameterDataClasses.ToFrozenDictionary(StringComparer.Ordinal);
        _queryParameterDataClasses = options.RequestQueryParametersDataClasses.ToFrozenDictionary(StringComparer.Ordinal);

        if (options.LogBody)
        {
            _logRequestBody = options.RequestBodyContentTypes.Count > 0;
            _logResponseBody = options.ResponseBodyContentTypes.Count > 0;
        }

        _logRequestHeaders = options.RequestHeadersDataClasses.Count > 0;
        _logResponseHeaders = options.ResponseHeadersDataClasses.Count > 0;
        _logRequestQueryParameters = options.RequestQueryParametersDataClasses.Count > 0;

        _httpRequestBodyReader = new HttpRequestBodyReader(options);
        _httpResponseBodyReader = new HttpResponseBodyReader(options);

        _routeParameterRedactionMode = options.RequestPathParameterRedactionMode;
    }

    public async Task ReadResponseAsync(LogRecord logRecord, HttpResponseMessage response,
        List<KeyValuePair<string, string>>? responseHeadersBuffer,
        CancellationToken cancellationToken)
    {
        if (_logResponseHeaders)
        {
            _httpHeadersReader.ReadResponseHeaders(response, responseHeadersBuffer);
            logRecord.ResponseHeaders = responseHeadersBuffer;
        }

        if (_logResponseBody)
        {
            logRecord.ResponseBody = await _httpResponseBodyReader.ReadAsync(response, cancellationToken).ConfigureAwait(false);
        }

        logRecord.StatusCode = (int)response.StatusCode;
    }

    public async Task ReadRequestAsync(LogRecord logRecord, HttpRequestMessage request,
            List<KeyValuePair<string, string>>? requestHeadersBuffer, CancellationToken
            cancellationToken)
    {
        logRecord.Host = request.RequestUri?.Host ?? TelemetryConstants.Unknown;
        logRecord.Method = request.Method;
        GetRedactedPathAndParameters(request, logRecord);

        if (_logRequestHeaders)
        {
            _httpHeadersReader.ReadRequestHeaders(request, requestHeadersBuffer);
            logRecord.RequestHeaders = requestHeadersBuffer;
        }

        if (_logRequestBody)
        {
            logRecord.RequestBody = await _httpRequestBodyReader.ReadAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_logRequestQueryParameters && !string.IsNullOrEmpty(request.RequestUri?.Query))
        {
            logRecord.QueryString = ExtractAndRedactQueryParameters(request.RequestUri!.Query);
        }
        else
        {
            logRecord.QueryString = string.Empty;
        }
    }

    private static string UnescapeDataString(ReadOnlySpan<char> value)
    {
#if NET9_0_OR_GREATER
        return Uri.UnescapeDataString(value);
#else
        return Uri.UnescapeDataString(value.ToString());
#endif
    }

    private string ExtractAndRedactQueryParameters(string query)
    {
        var stringBuilder = PoolFactory.SharedStringBuilderPool.Get();
        try
        {
            ReadOnlySpan<char> querySpan = query.AsSpan();
            int length = querySpan.Length;
            int start = 0;

            // Remove leading '?'
            if (length > 0 && querySpan[0] == '?')
            {
                start = 1;
            }

            while (start < length)
            {
                int amp = querySpan.Slice(start).IndexOf('&');
                int end = amp == -1 ? length : start + amp;

                int eq = querySpan.Slice(start, end - start).IndexOf('=');
                if (eq >= 0)
                {
                    var keySpan = querySpan.Slice(start, eq);
                    var valueSpan = querySpan.Slice(start + eq + 1, end - (start + eq + 1));

                    string key = UnescapeDataString(keySpan);
                    string value = UnescapeDataString(valueSpan);

                    // Only process if the key is in the classification dictionary and value is not empty
                    if (!string.IsNullOrEmpty(value) && _queryParameterDataClasses.TryGetValue(key, out var classification))
                    {
                        string redacted = _httpHeadersReader.RedactValue(value, classification);

                        // Append to string builder directly with proper encoding
                        if (stringBuilder.Length > 0)
                        {
                            _ = stringBuilder.Append('&');
                        }

                        _ = stringBuilder.Append(Uri.EscapeDataString(key))
                            .Append('=')
                            .Append(Uri.EscapeDataString(redacted));
                    }
                }

                if (amp == -1)
                {
                    break;
                }

                start = end + 1;
            }

            return stringBuilder.ToString();
        }
        finally
        {
            PoolFactory.SharedStringBuilderPool.Return(stringBuilder);
        }
    }

    private void GetRedactedPathAndParameters(HttpRequestMessage request, LogRecord logRecord)
    {
        logRecord.PathParameters = null;
        if (request.RequestUri is null)
        {
            logRecord.Path = TelemetryConstants.Unknown;
            return;
        }

        if (_routeParameterRedactionMode == HttpRouteParameterRedactionMode.None)
        {
            logRecord.Path = request.RequestUri.AbsolutePath;
            return;
        }

        var requestMetadata = request.GetRequestMetadata() ??
            _requestMetadataContext.RequestMetadata ??
            _dependencyMetadataResolver?.GetRequestMetadata(request);

        if (requestMetadata == null)
        {
            logRecord.Path = TelemetryConstants.Redacted;
            return;
        }

        var route = requestMetadata.RequestRoute;
        if (route == TelemetryConstants.Unknown)
        {
            logRecord.Path = requestMetadata.RequestName == TelemetryConstants.Unknown
                ? TelemetryConstants.Redacted
                : requestMetadata.RequestName;
            return;
        }

        var routeSegments = _httpRouteParser.ParseRoute(route);

        if (_outgoingPathLogMode == OutgoingPathLoggingMode.Formatted)
        {
            logRecord.Path = _routeFormatter.Format(in routeSegments, request.RequestUri.AbsolutePath, _routeParameterRedactionMode, _defaultSensitiveParameters);
            logRecord.PathParameters = null;
        }
        else
        {
            // Case when logging mode is "OutgoingPathLoggingMode.Structured"
            logRecord.Path = route;
            var routeParams = ArrayPool<HttpRouteParameter>.Shared.Rent(routeSegments.ParameterCount);

            // Setting this value right away to be able to return it back to pool in a callee's "finally" block:
            logRecord.PathParameters = routeParams;
            if (_httpRouteParser.TryExtractParameters(request.RequestUri.AbsolutePath, in routeSegments, _routeParameterRedactionMode, _defaultSensitiveParameters, ref routeParams))
            {
                logRecord.PathParametersCount = routeSegments.ParameterCount;
            }
        }
    }
}

