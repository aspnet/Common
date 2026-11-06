// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Http.Logging;

internal sealed class HttpClientLogger : IHttpClientAsyncLogger
{
    private const string SyncLoggingExceptionMessage = "Synchronous logging is not supported";

    private readonly ObjectPool<List<KeyValuePair<string, string>>> _headersPool =
        PoolFactory.CreateListPool<KeyValuePair<string, string>>();

    private readonly ObjectPool<LogRecord> _logRecordPool =
        PoolFactory.CreateResettingPool<LogRecord>();

    private readonly bool _logRequestStart;
    private readonly bool _logResponseHeaders;
    private readonly bool _logRequestHeaders;
    private readonly bool _pathParametersRedactionSkipped;
    private readonly IReadOnlyList<HttpStatusCodeLogLevelRule> _statusCodeLogLevelRules;
    private readonly LogLevel _exceptionLogLevel;
    private ILogger<HttpClientLogger> _logger;
    private IHttpRequestReader _httpRequestReader;
    private IHttpClientLogEnricher[] _enrichers;

    public HttpClientLogger(
        IServiceProvider serviceProvider,
        ILogger<HttpClientLogger> logger,
        IEnumerable<IHttpClientLogEnricher> enrichers,
        IOptionsMonitor<LoggingOptions> optionsMonitor,
        [ServiceKey] string? serviceKey = null)
        : this(
              logger,
              serviceProvider.GetRequiredOrKeyedRequiredService<IHttpRequestReader>(serviceKey),
              enrichers,
              optionsMonitor.GetKeyedOrCurrent(serviceKey))
    {
    }

    internal HttpClientLogger(
        ILogger<HttpClientLogger> logger,
        IHttpRequestReader httpRequestReader,
        IEnumerable<IHttpClientLogEnricher> enrichers,
        LoggingOptions options)
    {
        _logger = logger;
        _httpRequestReader = httpRequestReader;
        _enrichers = enrichers.Where(static x => x is not null).ToArray();
        _logRequestStart = options.LogRequestStart;
        _logResponseHeaders = options.ResponseHeadersDataClasses.Count > 0;
        _logRequestHeaders = options.RequestHeadersDataClasses.Count > 0;
        _pathParametersRedactionSkipped = options.RequestPathParameterRedactionMode == HttpRouteParameterRedactionMode.None;
        _statusCodeLogLevelRules = options.StatusCodeLogLevelRules is { } rules
            ? rules as IReadOnlyList<HttpStatusCodeLogLevelRule> ?? rules.ToArray()
            : [];
        _exceptionLogLevel = options.ExceptionLogLevel;
    }

    public async ValueTask<object?> LogRequestStartAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var logRecord = _logRecordPool.Get();

        List<KeyValuePair<string, string>>? requestHeadersBuffer = null;
        if (_logRequestHeaders)
        {
            requestHeadersBuffer = _headersPool.Get();
        }

        try
        {
            await _httpRequestReader.ReadRequestAsync(logRecord, request, requestHeadersBuffer, cancellationToken).ConfigureAwait(false);

            if (_logRequestStart)
            {
                Log.OutgoingRequest(_logger, LogLevel.Information, logRecord);
            }

            return logRecord;
        }
        catch (Exception ex)
        {
            // If redaction is skipped, we can log unredacted request path; otherwise use "logRecord.Path" (even though it might not be set):
            var pathToLog = _pathParametersRedactionSkipped
                ? request.RequestUri?.AbsolutePath
                : logRecord.Path;

            Log.RequestReadError(_logger, ex, request.Method, request.RequestUri?.Host, pathToLog);

            // Return back pooled objects (since the logRecord wasn't fully prepared):
            _logRecordPool.Return(logRecord);

            if (requestHeadersBuffer is not null)
            {
                _headersPool.Return(requestHeadersBuffer);
            }

            // Recommendation is to swallow the exception (logger shouldn't throw), so we don't re-throw here:
            return null;
        }
    }

    public ValueTask LogRequestStopAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
            => LogResponseAsync(context, request, response, null, elapsed, cancellationToken);

    public ValueTask LogRequestFailedAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
            => LogResponseAsync(context, request, response, exception, elapsed, cancellationToken);

    public object? LogRequestStart(HttpRequestMessage request)
        => throw new NotSupportedException(SyncLoggingExceptionMessage);

    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
        => throw new NotSupportedException(SyncLoggingExceptionMessage);

    public void LogRequestFailed(object? context, HttpRequestMessage request, HttpResponseMessage? response, Exception exception, TimeSpan elapsed)
        => throw new NotSupportedException(SyncLoggingExceptionMessage);

    private LogLevel GetLogLevel(LogRecord logRecord)
    {
        int statusCode = logRecord.StatusCode!.Value;

        for (int i = 0; i < _statusCodeLogLevelRules.Count; i++)
        {
            var rule = _statusCodeLogLevelRules[i];
            int to = rule.ToStatusCode ?? rule.FromStatusCode;

            if (statusCode >= rule.FromStatusCode && statusCode <= to)
            {
                return rule.LogLevel;
            }
        }

        const int HttpErrorsRangeStart = 400;
        const int HttpErrorsRangeEnd = 599;

        if (statusCode >= HttpErrorsRangeStart && statusCode <= HttpErrorsRangeEnd)
        {
            return LogLevel.Error;
        }

        return LogLevel.Information;
    }

    private async ValueTask LogResponseAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception? exception,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        if (context is not LogRecord logRecord)
        {
            var requestState = response is null
                ? "failed"
                : "completed";

            Log.LoggerContextMissing(_logger, exception, requestState, request.Method, request.RequestUri?.Host);
            return;
        }

        LoggerMessageState? loggerMessageState = null;
        List<KeyValuePair<string, string>>? responseHeadersBuffer = null;
        try
        {
            if (response is not null)
            {
                if (_logResponseHeaders)
                {
                    responseHeadersBuffer = _headersPool.Get();
                }

                await _httpRequestReader.ReadResponseAsync(logRecord, response, responseHeadersBuffer, cancellationToken).ConfigureAwait(false);
            }

            loggerMessageState = LoggerMessageHelper.ThreadLocalState;
            FillLogRecord(logRecord, loggerMessageState, in elapsed, request, response, exception);

            if (exception is null)
            {
                Log.OutgoingRequest(_logger, GetLogLevel(logRecord), logRecord);
            }
            else
            {
                Log.OutgoingRequestError(_logger, _exceptionLogLevel, logRecord, exception);
            }
        }
        catch (Exception ex)
        {
            // Logger shouldn't throw, so we just log the exception and don't re-throw it:
            Log.ResponseReadError(_logger, ex, request.Method, logRecord.Host, logRecord.Path);
        }
        finally
        {
            var requestHeadersBuffer = logRecord.RequestHeaders; // Store the value first, and then return logRecord to the pool.
            _logRecordPool.Return(logRecord);
            loggerMessageState?.Clear();

            if (responseHeadersBuffer is not null)
            {
                _headersPool.Return(responseHeadersBuffer);
            }

            if (requestHeadersBuffer is not null)
            {
                _headersPool.Return(requestHeadersBuffer);
            }
        }
    }

    private void FillLogRecord(
        LogRecord logRecord, LoggerMessageState loggerMessageState, in TimeSpan elapsed,
        HttpRequestMessage request, HttpResponseMessage? response, Exception? exception)
    {
        foreach (var enricher in _enrichers)
        {
            try
            {
                enricher.Enrich(loggerMessageState, request, response, exception);
            }
            catch (Exception e)
            {
                Log.EnrichmentError(_logger, e, enricher.GetType().FullName, request.Method, logRecord.Host, logRecord.Path);
            }
        }

        logRecord.EnrichmentTags = loggerMessageState;
        logRecord.Duration = (long)elapsed.TotalMilliseconds;
    }
}
