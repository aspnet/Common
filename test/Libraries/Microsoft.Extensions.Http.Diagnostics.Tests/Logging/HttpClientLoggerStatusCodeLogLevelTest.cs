// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Diagnostics.Test.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Test.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Telemetry.Internal;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpClientLoggerStatusCodeLogLevelTest
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, LogLevel.Warning)]
    [InlineData(HttpStatusCode.BadRequest, LogLevel.Warning)]
    [InlineData(HttpStatusCode.InternalServerError, LogLevel.Error)]
    public async Task StatusCodeLogLevelRules_MatchesConfiguredRule(HttpStatusCode statusCode, LogLevel expectedLevel)
    {
        var options = new LoggingOptions
        {
            StatusCodeLogLevelRules =
            [
                new HttpStatusCodeLogLevelRule { FromStatusCode = 400, ToStatusCode = 499, LogLevel = LogLevel.Warning },
                new HttpStatusCodeLogLevelRule { FromStatusCode = 500, ToStatusCode = 599, LogLevel = LogLevel.Error },
            ]
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var httpResponseMessage = new HttpResponseMessage(statusCode);

        using var handler = CreateHandler(fakeLogger, options, httpResponseMessage);
        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await client.SendAsync(httpRequestMessage, CancellationToken.None);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(expectedLevel, logRecord.Level);
    }

    [Fact]
    public async Task StatusCodeLogLevelRules_FirstMatchWins()
    {
        var options = new LoggingOptions
        {
            StatusCodeLogLevelRules =
            [
                new HttpStatusCodeLogLevelRule { FromStatusCode = 404, LogLevel = LogLevel.Debug },
                new HttpStatusCodeLogLevelRule { FromStatusCode = 400, ToStatusCode = 499, LogLevel = LogLevel.Warning },
            ]
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound);

        using var handler = CreateHandler(fakeLogger, options, httpResponseMessage);
        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await client.SendAsync(httpRequestMessage, CancellationToken.None);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Debug, logRecord.Level);
    }

    [Fact]
    public async Task StatusCodeLogLevelRules_NoMatch_FallsBackToDefaultBehavior()
    {
        var options = new LoggingOptions
        {
            StatusCodeLogLevelRules =
            [
                new HttpStatusCodeLogLevelRule { FromStatusCode = 404, LogLevel = LogLevel.Debug },
            ]
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        using var handler = CreateHandler(fakeLogger, options, httpResponseMessage);
        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await client.SendAsync(httpRequestMessage, CancellationToken.None);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Error, logRecord.Level);
    }

    [Fact]
    public async Task StatusCodeLogLevelRules_EmptyRules_UsesDefaultBehavior()
    {
        var options = new LoggingOptions();

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.OK);

        using var handler = CreateHandler(fakeLogger, options, httpResponseMessage);
        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await client.SendAsync(httpRequestMessage, CancellationToken.None);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Information, logRecord.Level);
    }

    [Fact]
    public async Task ExceptionLogLevel_UsesConfiguredLevel()
    {
        var options = new LoggingOptions
        {
            ExceptionLogLevel = LogLevel.Warning,
        };

        var exception = new HttpRequestException("test");
        var fakeLogger = new FakeLogger<HttpClientLogger>();

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                Mock.Of<IHttpRequestReader>(),
                [],
                options),
            new TestingHandlerStub((_, _) => throw exception));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(httpRequestMessage, CancellationToken.None));

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Warning, logRecord.Level);
    }

    [Fact]
    public async Task ExceptionLogLevel_DefaultIsError()
    {
        var options = new LoggingOptions();

        var exception = new HttpRequestException("test");
        var fakeLogger = new FakeLogger<HttpClientLogger>();

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                Mock.Of<IHttpRequestReader>(),
                [],
                options),
            new TestingHandlerStub((_, _) => throw exception));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(httpRequestMessage, CancellationToken.None));

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Error, logRecord.Level);
    }

    [Fact]
    public async Task StatusCodeLogLevelRules_ExactMatch_WithNullToStatusCode()
    {
        var options = new LoggingOptions
        {
            StatusCodeLogLevelRules =
            [
                new HttpStatusCodeLogLevelRule { FromStatusCode = 429, ToStatusCode = null, LogLevel = LogLevel.Warning },
            ]
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var httpResponseMessage = new HttpResponseMessage((HttpStatusCode)429);

        using var handler = CreateHandler(fakeLogger, options, httpResponseMessage);
        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");

        await client.SendAsync(httpRequestMessage, CancellationToken.None);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Warning, logRecord.Level);
    }

    private static TestLoggingHandler CreateHandler(
        FakeLogger<HttpClientLogger> fakeLogger,
        LoggingOptions options,
        HttpResponseMessage response)
    {
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns("Redacted");

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        return new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    Mock.Of<IHttpRouteFormatter>(),
                    Mock.Of<IHttpRouteParser>(),
                    headersReader,
                    Mock.Of<IOutgoingRequestContext>()),
                [],
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(response)));
    }
}
