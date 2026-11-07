// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Diagnostics.Test.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Test.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Telemetry.Internal;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Collections;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpClientLoggerTest
{
    private const string TestRequestHeader = "Request-Header";
    private const string TestResponseHeader = "Response-Header";
    private const string TestExpectedRequestHeaderKey = $"{HttpClientLoggingTagNames.RequestHeaderPrefix}request-header";
    private const string TestExpectedResponseHeaderKey = $"{HttpClientLoggingTagNames.ResponseHeaderPrefix}response-header";

    private const string TextPlain = "text/plain";

    private const string Redacted = "REDACTED";

    private readonly Fixture _fixture;

    public HttpClientLoggerTest()
    {
        _fixture = new();
    }

    [Fact]
    public async Task SendAsync_NullRequest_ThrowsException()
    {
        var responseCode = _fixture.Create<HttpStatusCode>();
        using var httpResponseMessage = new HttpResponseMessage { StatusCode = responseCode };

        var options = new LoggingOptions
        {
            BodyReadTimeout = TimeSpan.FromMinutes(5)
        };

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                NullLogger<HttpClientLogger>.Instance,
                Mock.Of<IHttpRequestReader>(),
                Empty.Enumerable<IHttpClientLogEnricher>(),
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(httpResponseMessage)));

        using var client = new HttpClient(handler);

        var act = async () =>
            await client.SendAsync(null!, It.IsAny<CancellationToken>()).ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentNullException>(act);
    }

    [Fact]
    public async Task SendAsync_HttpRequestException_ThrowsException()
    {
        var input = _fixture.Create<string>();
        var exception = new HttpRequestException();
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new("http://default-uri.com"),
            Content = new StringContent(input, Encoding.UTF8, TextPlain)
        };

        var options = new LoggingOptions();

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                NullLogger<HttpClientLogger>.Instance,
                new HttpRequestReader(options, GetHttpRouteFormatter(), Mock.Of<IHttpRouteParser>(), headersReader, RequestMetadataContext),
                Enumerable.Empty<IHttpClientLogEnricher>(),
                options),
            new TestingHandlerStub((_, _) => throw exception));

        using var client = new HttpClient(handler);

        var act = async () =>
            await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>()).ConfigureAwait(false);

        var actualException = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Same(exception, actualException);
    }

    [Fact]
    public async Task Logger_WhenReadRequestAsyncThrows_DoesntThrow()
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        var input = _fixture.Create<string>();
        var operationCanceledException = new OperationCanceledException();
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new("http://default-uri.com"),
            Content = new StringContent(input, Encoding.UTF8, TextPlain)
        };

        var requestReaderMock = new Mock<IHttpRequestReader>(MockBehavior.Strict);
        requestReaderMock.Setup(e =>
            e.ReadRequestAsync(It.IsAny<LogRecord>(),
                It.IsAny<HttpRequestMessage>(),
                It.IsAny<List<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(operationCanceledException);

        var mockedRequestReader = new MockedRequestReader(requestReaderMock);

        var options = new LoggingOptions();

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                NullLogger<HttpClientLogger>.Instance,
                mockedRequestReader,
                Enumerable.Empty<IHttpClientLogEnricher>(),
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        using var httpClient = new HttpClient(handler);

        var act = async () =>
            await httpClient.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>()).ConfigureAwait(false);

        var exception = await Record.ExceptionAsync(act);
        Assert.Null(exception);
    }

    [Fact]
    public async Task HttpLoggingHandler_AllOptions_LogsOutgoingRequest()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var testRequestHeaderValue = _fixture.Create<string>();
        var testResponseHeaderValue = _fixture.Create<string>();

        var testEnricher = new TestEnricher();

        var testSharedRequestHeaderKey = $"{HttpClientLoggingTagNames.RequestHeaderPrefix}header3";
        var testSharedResponseHeaderKey = $"{HttpClientLoggingTagNames.ResponseHeaderPrefix}header3";

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            StatusCode = 200,
            ResponseHeaders = [new(TestExpectedResponseHeaderKey, Redacted), new(testSharedResponseHeaderKey, Redacted)],
            RequestHeaders = [new(TestExpectedRequestHeaderKey, Redacted), new(testSharedRequestHeaderKey, Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            EnrichmentTags = testEnricher.EnrichmentBag
        };

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = new StringContent(requestContent, Encoding.UTF8, TextPlain)
        };

        httpRequestMessage.Headers.Add(TestRequestHeader, testRequestHeaderValue);
        httpRequestMessage.Headers.Add("Header3", testRequestHeaderValue);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8, TextPlain),
        };

        httpResponseMessage.Headers.Add(TestResponseHeader, testResponseHeaderValue);
        httpResponseMessage.Headers.Add("Header3", testRequestHeaderValue);

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData }, { "Header3", FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData }, { "Header3", FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = false,
            LogBody = true
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        var fakeLogger = new FakeLogger<HttpClientLogger>();

        var logger = new HttpClientLogger(
            fakeLogger,
            new HttpRequestReader(
                options,
                GetHttpRouteFormatter(),
                Mock.Of<IHttpRouteParser>(),
                headersReader, RequestMetadataContext),
            new List<IHttpClientLogEnricher> { testEnricher },
            options);

        using var handler = new TestLoggingHandler(
            logger,
            new TestingHandlerStub((_, _) => Task.FromResult(httpResponseMessage)));

        using var client = new HttpClient(handler);
        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        var logRecordState = logRecord.GetStructuredState();
        logRecordState.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordState.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordState.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordState.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        logRecordState.Contains(HttpClientLoggingTagNames.StatusCode, expectedLogRecord.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
        logRecordState.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordState.Contains(HttpClientLoggingTagNames.ResponseBody, expectedLogRecord.ResponseBody);
        logRecordState.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders[0].Value);
        logRecordState.Contains(TestExpectedResponseHeaderKey, expectedLogRecord.ResponseHeaders[0].Value);
        logRecordState.Contains(testSharedResponseHeaderKey, expectedLogRecord.ResponseHeaders[1].Value);
        logRecordState.Contains(testSharedRequestHeaderKey, expectedLogRecord.RequestHeaders[1].Value);
        logRecordState.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        EnsureLogRecordContainsOriginalFormat(logRecord);
    }

    [Fact]
    public async Task HttpLoggingHandler_UnredactedPath_DoesNotDuplicateSeparator()
    {
        var options = new LoggingOptions
        {
            RequestPathParameterRedactionMode = HttpRouteParameterRedactionMode.None
        };
        var fakeLogger = new FakeLogger<HttpClientLogger>();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), Mock.Of<IHttpHeadersRedactor>());
        var requestReader = new HttpRequestReader(
            options,
            GetHttpRouteFormatter(),
            Mock.Of<IHttpRouteParser>(),
            headersReader,
            RequestMetadataContext);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(fakeLogger, requestReader, Array.Empty<IHttpClientLogEnricher>(), options),
            new TestingHandlerStub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://default-uri.com/foo/bar");

        await client.SendAsync(request, It.IsAny<CancellationToken>());

        var logRecord = Assert.Single(fakeLogger.Collector.GetSnapshot());
        Assert.Equal("GET default-uri.com/foo/bar", logRecord.Message);
        logRecord.GetStructuredState().Contains(HttpClientLoggingTagNames.Path, "/foo/bar");
    }

    [Fact]
    public async Task HttpLoggingHandler_AllOptionsWithLogRequestStart_LogsOutgoingRequestWithTwoRecords()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var requestHeaderValue = _fixture.Create<string>();
        var responseHeaderValue = _fixture.Create<string>();
        var testEnricher = new TestEnricher();

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            StatusCode = 200,
            ResponseHeaders = [new(TestResponseHeader, Redacted)],
            RequestHeaders = [new(TestRequestHeader, Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            EnrichmentTags = testEnricher.EnrichmentBag
        };

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = new StringContent(requestContent, Encoding.UTF8, TextPlain)
        };
        httpRequestMessage.Headers.Add(TestRequestHeader, requestHeaderValue);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8, TextPlain),
        };
        httpResponseMessage.Headers.Add(TestResponseHeader, responseHeaderValue);

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = true,
            LogBody = true
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>(
            new FakeLogCollector(
                Options.Options.Create(
                    new FakeLogCollectorOptions())));

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        var logger = new HttpClientLogger(
            fakeLogger,
            new HttpRequestReader(
                options,
                GetHttpRouteFormatter(),
                Mock.Of<IHttpRouteParser>(),
                headersReader, RequestMetadataContext),
            new List<IHttpClientLogEnricher> { testEnricher },
            options);

        using var handler = new TestLoggingHandler(
            logger,
            new TestingHandlerStub((_, _) => Task.FromResult(httpResponseMessage)));

        using var client = new HttpClient(handler);
        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        Assert.Equal(2, logRecords.Count);

        var logRecordRequest = logRecords[0].GetStructuredState();
        logRecordRequest.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordRequest.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordRequest.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordRequest.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordRequest.NotContains(HttpClientLoggingTagNames.StatusCode);
        logRecordRequest.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordRequest.NotContains(testEnricher.KvpRequest.Key);
        EnsureLogRecordContainsOriginalFormat(logRecords[0]);

        var logRecordFull = logRecords[1].GetStructuredState();
        logRecordFull.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordFull.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordFull.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordFull.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        logRecordFull.Contains(HttpClientLoggingTagNames.StatusCode, expectedLogRecord.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
        logRecordFull.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordFull.Contains(HttpClientLoggingTagNames.ResponseBody, expectedLogRecord.ResponseBody);
        logRecordFull.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordFull.Contains(TestExpectedResponseHeaderKey, expectedLogRecord.ResponseHeaders.FirstOrDefault().Value);
        logRecordFull.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        logRecordFull.Contains(testEnricher.KvpResponse.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpResponse.Key));
        EnsureLogRecordContainsOriginalFormat(logRecords[1]);
    }

    [Fact]
    public async Task HttpLoggingHandler_AllOptionsSendAsyncFailed_LogsRequestInformation()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var requestHeaderValue = _fixture.Create<string>();
        var responseHeaderValue = _fixture.Create<string>();
        var testEnricher = new TestEnricher();

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            StatusCode = 200,
            ResponseHeaders = [new(TestResponseHeader, Redacted)],
            RequestHeaders = [new(TestRequestHeader, Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            EnrichmentTags = testEnricher.EnrichmentBag
        };

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = new StringContent(requestContent, Encoding.UTF8, TextPlain)
        };

        httpRequestMessage.Headers.Add(TestRequestHeader, requestHeaderValue);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8, TextPlain),
        };

        httpResponseMessage.Headers.Add(TestResponseHeader, responseHeaderValue);

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = false,
            LogBody = true
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>(new FakeLogCollector(Options.Options.Create(new FakeLogCollectorOptions())));

        var exception = new TaskCanceledException();

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    GetHttpRouteFormatter(),
                    Mock.Of<IHttpRouteParser>(),
                    headersReader, RequestMetadataContext),
                new List<IHttpClientLogEnricher> { testEnricher },
                options),
            new TestingHandlerStub((_, _) => throw exception));

        using var client = new HttpClient(handler);
        var act = () => client.SendAsync(httpRequestMessage, CancellationToken.None);
        await Assert.ThrowsAsync<TaskCanceledException>(act);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal($"{httpRequestMessage.Method} {httpRequestMessage.RequestUri.Host}/{TelemetryConstants.Redacted}", logRecord.Message);
        Assert.Same(exception, logRecord.Exception);

        var logRecordState = logRecord.GetStructuredState();
        logRecordState.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordState.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordState.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordState.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordState.NotContains(HttpClientLoggingTagNames.ResponseBody);
        logRecordState.NotContains(HttpClientLoggingTagNames.StatusCode);
        logRecordState.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordState.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        logRecordState.NotContains(testEnricher.KvpResponse.Key);
        logRecordState.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        Assert.DoesNotContain(logRecordState, kvp => kvp.Key.StartsWith(HttpClientLoggingTagNames.ResponseHeaderPrefix));
        EnsureLogRecordContainsOriginalFormat(logRecord);
    }

    [Fact(Skip = "Flaky test, see https://github.com/dotnet/extensions/issues/4530")]
    public async Task HttpLoggingHandler_ReadResponseThrows_LogsException()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var requestHeaderValue = _fixture.Create<string>();
        var responseHeaderValue = _fixture.Create<string>();
        var testEnricher = new TestEnricher();

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            StatusCode = 200,
            ResponseHeaders = [new(TestResponseHeader, Redacted)],
            RequestHeaders = [new(TestRequestHeader, Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            EnrichmentTags = testEnricher.EnrichmentBag
        };

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = new StringContent(requestContent, Encoding.UTF8, TextPlain)
        };

        httpRequestMessage.Headers.Add(TestRequestHeader, requestHeaderValue);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8, TextPlain),
        };

        httpResponseMessage.Headers.Add(TestResponseHeader, responseHeaderValue);

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = false,
            LogBody = true
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>(
            new FakeLogCollector(
                Options.Options.Create(new FakeLogCollectorOptions())));

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        var exception = new InvalidOperationException("test");

        var actualRequestReader = new HttpRequestReader(options, GetHttpRouteFormatter(), Mock.Of<IHttpRouteParser>(), headersReader, RequestMetadataContext);
        var mockedRequestReader = new Mock<IHttpRequestReader>();
        mockedRequestReader
            .Setup(m =>
                m.ReadRequestAsync(// so this method is not mocked
                    It.IsAny<LogRecord>(),
                    It.IsAny<HttpRequestMessage>(),
                    It.IsAny<List<KeyValuePair<string, string>>>(),
                    It.IsAny<CancellationToken>()))
            .Returns((LogRecord a, HttpRequestMessage b, List<KeyValuePair<string, string>> c, CancellationToken d) =>
                actualRequestReader.ReadRequestAsync(a, b, c, d));
        mockedRequestReader
            .Setup(m =>
                m.ReadResponseAsync(// but this method is setup to throw an exception
                    It.IsAny<LogRecord>(),
                    It.IsAny<HttpResponseMessage>(),
                    It.IsAny<List<KeyValuePair<string, string>>>(),
                    It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                mockedRequestReader.Object,
                new List<IHttpClientLogEnricher> { testEnricher },
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(httpResponseMessage)));

        using var client = new HttpClient(handler);
        var act = async () => await client
            .SendAsync(httpRequestMessage, It.IsAny<CancellationToken>())
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);

        var logRecordState = logRecord.GetStructuredState();
        logRecordState.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordState.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordState.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordState.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordState.NotContains(HttpClientLoggingTagNames.ResponseBody);
        logRecordState.NotContains(HttpClientLoggingTagNames.StatusCode);
        logRecordState.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordState.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        logRecordState.Contains(testEnricher.KvpResponse.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpResponse.Key));
        logRecordState.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        Assert.DoesNotContain(logRecordState, kvp => kvp.Key.StartsWith(HttpClientLoggingTagNames.ResponseHeaderPrefix));
        EnsureLogRecordContainsOriginalFormat(logRecord);
    }

    [Fact]
    public async Task HttpLoggingHandler_AllOptionsTransferEncodingIsNotChunked_LogsOutgoingRequest()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var requestHeaderValue = _fixture.Create<string>();
        var responseHeaderValue = _fixture.Create<string>();
        var testEnricher = new TestEnricher();

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            StatusCode = 200,
            ResponseHeaders = [new(TestResponseHeader, Redacted)],
            RequestHeaders = [new(TestRequestHeader, Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            EnrichmentTags = testEnricher.EnrichmentBag,
        };

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = new StringContent(requestContent, Encoding.UTF8, TextPlain)
        };
        httpRequestMessage.Headers.Add(TestRequestHeader, requestHeaderValue);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8, TextPlain),
        };
        httpResponseMessage.Headers.Add(TestResponseHeader, responseHeaderValue);
        httpResponseMessage.Headers.TransferEncoding.Add(new("compress"));

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = false,
            LogBody = true
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>(new FakeLogCollector(Options.Options.Create(new FakeLogCollectorOptions())));

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    GetHttpRouteFormatter(),
                    Mock.Of<IHttpRouteParser>(),
                    headersReader, RequestMetadataContext),
                new List<IHttpClientLogEnricher> { testEnricher },
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(httpResponseMessage)));

        using var client = new HttpClient(handler);
        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);

        var logRecordState = logRecord.GetStructuredState();
        logRecordState.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordState.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordState.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordState.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        logRecordState.Contains(HttpClientLoggingTagNames.StatusCode, expectedLogRecord.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
        logRecordState.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordState.Contains(HttpClientLoggingTagNames.ResponseBody, expectedLogRecord.ResponseBody);
        logRecordState.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordState.Contains(TestExpectedResponseHeaderKey, expectedLogRecord.ResponseHeaders.FirstOrDefault().Value);
        logRecordState.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        EnsureLogRecordContainsOriginalFormat(logRecord);
    }

    [Fact]
    public async Task HttpLoggingHandler_WithEnrichers_CallsEnrichMethodExactlyOnce()
    {
        var enricher1 = new Mock<IHttpClientLogEnricher>();
        var enricher2 = new Mock<IHttpClientLogEnricher>();
        var options = new LoggingOptions();
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    GetHttpRouteFormatter(),
                    Mock.Of<IHttpRouteParser>(),
                    new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object),
                    RequestMetadataContext),
                new List<IHttpClientLogEnricher> { enricher1.Object, enricher2.Object },
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://default-uri.com/foo/bar"),
            Content = new StringContent(_fixture.Create<string>(), Encoding.UTF8, TextPlain)
        };

        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        _ = Assert.Single(logRecords);

        enricher1.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
        enricher2.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
    }

    [Fact]
    public async Task HttpLoggingHandler_WithEnrichersAndLogRequestStart_CallsEnrichMethodExactlyOnce()
    {
        var enricher1 = new Mock<IHttpClientLogEnricher>();
        var enricher2 = new Mock<IHttpClientLogEnricher>();
        var options = new LoggingOptions
        {
            LogRequestStart = true
        };
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var fakeLogger = new FakeLogger<HttpClientLogger>();
        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    GetHttpRouteFormatter(),
                    Mock.Of<IHttpRouteParser>(),
                    new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object),
                    RequestMetadataContext),
                new List<IHttpClientLogEnricher> { enricher1.Object, enricher2.Object },
                options),
            new TestingHandlerStub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://default-uri.com/foo/bar"),
            Content = new StringContent(_fixture.Create<string>(), Encoding.UTF8, TextPlain)
        };

        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        Assert.Equal(2, logRecords.Count);

        enricher1.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
        enricher2.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
    }

    [Fact]
    public async Task HttpLoggingHandler_WithEnrichers_OneEnricherThrows_LogsEnrichmentErrorAndRequest()
    {
        var exception = new ArgumentNullException();
        var enricher1 = new Mock<IHttpClientLogEnricher>();
        enricher1
            .Setup(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()))
            .Throws(exception);

        var enricher2 = new Mock<IHttpClientLogEnricher>();
        var fakeLogger = new FakeLogger<HttpClientLogger>();
        var options = new LoggingOptions();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), Mock.Of<IHttpHeadersRedactor>());
        var requestReader = new HttpRequestReader(options, GetHttpRouteFormatter(), Mock.Of<IHttpRouteParser>(), headersReader, RequestMetadataContext);
        var enrichers = new List<IHttpClientLogEnricher> { enricher1.Object, enricher2.Object };

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(fakeLogger, requestReader, enrichers, options),
            new TestingHandlerStub(
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://default-uri.com/foo/bar"),
            Content = new StringContent(_fixture.Create<string>(), Encoding.UTF8, TextPlain)
        };

        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        Assert.Equal(2, logRecords.Count);

        Assert.Equal(nameof(Log.EnrichmentError), logRecords[0].Id.Name);
        Assert.Equal(exception, logRecords[0].Exception);

        Assert.Equal(nameof(Log.OutgoingRequest), logRecords[1].Id.Name);

        enricher1.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
        enricher2.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
    }

    [Fact]
    public async Task HttpLoggingHandler_WithEnrichers_SendAsyncAndOneEnricher_LogsEnrichmentErrorAndRequestError()
    {
        var enrichmentException = new ArgumentNullException();
        var enricher1 = new Mock<IHttpClientLogEnricher>();
        enricher1
            .Setup(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()))
            .Throws(enrichmentException)
            .Verifiable();

        var enricher2 = new Mock<IHttpClientLogEnricher>();
        var fakeLogger = new FakeLogger<HttpClientLogger>();

        var sendAsyncException = new TaskCanceledException();
        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                Mock.Of<IHttpRequestReader>(),
                new List<IHttpClientLogEnricher> { enricher1.Object, enricher2.Object },
                new LoggingOptions()),
            new TestingHandlerStub((_, _) => throw sendAsyncException));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://default-uri.com/foo/bar"),
            Content = new StringContent(_fixture.Create<string>(), Encoding.UTF8, TextPlain)
        };

        var act = () => client.SendAsync(httpRequestMessage, CancellationToken.None);
        await Assert.ThrowsAsync<TaskCanceledException>(act);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        Assert.Equal(2, logRecords.Count);

        Assert.Equal(nameof(Log.EnrichmentError), logRecords[0].Id.Name);
        Assert.Equal(enrichmentException, logRecords[0].Exception);

        Assert.Equal(nameof(Log.OutgoingRequestError), logRecords[1].Id.Name);
        Assert.Equal(sendAsyncException, logRecords[1].Exception);

        enricher1.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
        enricher2.Verify(e => e.Enrich(It.IsAny<IEnrichmentTagCollector>(), It.IsAny<HttpRequestMessage>(), It.IsAny<HttpResponseMessage>(), It.IsAny<Exception>()), Times.Exactly(1));
    }

    [Fact]
    public async Task HttpLoggingHandler_AllOptionsTransferEncodingChunked_LogsOutgoingRequest()
    {
        var requestInput = _fixture.Create<string>();
        var responseInput = _fixture.Create<string>();
        var requestHeaderValue = _fixture.Create<string>();
        var responseHeaderValue = _fixture.Create<string>();
        var fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var testEnricher = new TestEnricher();

        var expectedLogRecord = new LogRecord
        {
            Host = "default-uri.com",
            Method = HttpMethod.Post,
            Path = "foo/bar",
            Duration = 1000,
            StatusCode = 200,
            ResponseHeaders = [new(TestExpectedResponseHeaderKey, Redacted)],
            RequestHeaders = [new(TestExpectedRequestHeaderKey, Redacted)],
            RequestBody = requestInput,
            ResponseBody = responseInput,
            EnrichmentTags = testEnricher.EnrichmentBag
        };

        using var requestContent = new StreamContent(new NotSeekableStream(new(Encoding.UTF8.GetBytes(requestInput))));
        requestContent.Headers.Add("Content-Type", TextPlain);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://{expectedLogRecord.Host}/{expectedLogRecord.Path}"),
            Content = requestContent,
        };
        httpRequestMessage.Headers.Add(TestRequestHeader, requestHeaderValue);

        using var responseContent = new StreamContent(new NotSeekableStream(new(Encoding.UTF8.GetBytes(responseInput))));
        responseContent.Headers.Add("Content-Type", TextPlain);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = responseContent,
        };
        httpResponseMessage.Headers.Add(TestResponseHeader, responseHeaderValue);
        httpResponseMessage.Headers.TransferEncoding.Add(new("chunked"));

        var options = new LoggingOptions
        {
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { TestResponseHeader, FakeTaxonomy.PrivateData } },
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { TestRequestHeader, FakeTaxonomy.PrivateData } },
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            RequestBodyContentTypes = new HashSet<string> { TextPlain },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured,
            LogRequestStart = false,
            LogBody = true
        };

        var fakeLogger = new FakeLogger<HttpClientLogger>(new FakeLogCollector(Options.Options.Create(new FakeLogCollectorOptions())));

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(
                fakeLogger,
                new HttpRequestReader(
                    options,
                    GetHttpRouteFormatter(),
                    Mock.Of<IHttpRouteParser>(),
                    headersReader, RequestMetadataContext),
                new List<IHttpClientLogEnricher> { testEnricher },
                options),
            new TestingHandlerStub((_, _) =>
            {
                fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(1000));
                return Task.FromResult(httpResponseMessage);
            }));

        using var client = new HttpClient(handler);
        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        var logRecordState = logRecord.GetStructuredState();

        logRecordState.Contains(HttpClientLoggingTagNames.Host, expectedLogRecord.Host);
        logRecordState.Contains(HttpClientLoggingTagNames.Method, expectedLogRecord.Method.ToString());
        logRecordState.Contains(HttpClientLoggingTagNames.Path, TelemetryConstants.Redacted);
        logRecordState.Contains(HttpClientLoggingTagNames.Duration, EnsureLogRecordDuration);
        logRecordState.Contains(HttpClientLoggingTagNames.StatusCode, expectedLogRecord.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
        logRecordState.Contains(HttpClientLoggingTagNames.RequestBody, expectedLogRecord.RequestBody);
        logRecordState.Contains(HttpClientLoggingTagNames.ResponseBody, expectedLogRecord.ResponseBody);
        logRecordState.Contains(TestExpectedRequestHeaderKey, expectedLogRecord.RequestHeaders.FirstOrDefault().Value);
        logRecordState.Contains(TestExpectedResponseHeaderKey, expectedLogRecord.ResponseHeaders.FirstOrDefault().Value);
        logRecordState.Contains(testEnricher.KvpRequest.Key, expectedLogRecord.GetEnrichmentProperty(testEnricher.KvpRequest.Key));
        EnsureLogRecordContainsOriginalFormat(logRecord);
    }

    [Theory]
    [InlineData(399, LogLevel.Information)]
    [InlineData(400, LogLevel.Error)]
    [InlineData(499, LogLevel.Error)]
    [InlineData(500, LogLevel.Error)]
    [InlineData(599, LogLevel.Error)]
    [InlineData(600, LogLevel.Information)]
    public async Task HttpLoggingHandler_OnDifferentHttpStatusCodes_LogsOutgoingRequestWithAppropriateLogLevel(
        int httpStatusCode, LogLevel expectedLogLevel)
    {
        var fakeLogger = new FakeLogger<HttpClientLogger>();
        var options = new LoggingOptions();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), new Mock<IHttpHeadersRedactor>().Object);
        var requestReader = new HttpRequestReader(
            options, GetHttpRouteFormatter(), Mock.Of<IHttpRouteParser>(), headersReader, RequestMetadataContext);

        using var handler = new TestLoggingHandler(
            new HttpClientLogger(fakeLogger, requestReader, Array.Empty<IHttpClientLogEnricher>(), options),
            new TestingHandlerStub((_, _) =>
                Task.FromResult(new HttpResponseMessage((HttpStatusCode)httpStatusCode))));

        using var client = new HttpClient(handler);
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new($"http://default-uri.com/foo/bar"),
            Content = new StringContent("request_content", Encoding.UTF8, TextPlain)
        };

        await client.SendAsync(httpRequestMessage, It.IsAny<CancellationToken>());

        var logRecord = fakeLogger.Collector.GetSnapshot().Single();
        Assert.Equal(expectedLogLevel, logRecord.Level);
    }

    [Fact]
    public async Task HttpClientLogger_LogsAnError_WhenResponseReaderThrows()
    {
        var exception = new InvalidOperationException("Test exception");
        var requestReaderMock = new Mock<IHttpRequestReader>();
        requestReaderMock
            .Setup(r => r.ReadResponseAsync(It.IsAny<LogRecord>(), It.IsAny<HttpResponseMessage>(), It.IsAny<List<KeyValuePair<string, string>>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var fakeLogger = new FakeLogger<HttpClientLogger>();
        var logger = new HttpClientLogger(fakeLogger, requestReaderMock.Object, Array.Empty<IHttpClientLogEnricher>(), new());

        using var httpRequestMessage = new HttpRequestMessage();
        using var httpResponseMessage = new HttpResponseMessage();
        await logger.LogRequestStopAsync(new LogRecord(), httpRequestMessage, httpResponseMessage, TimeSpan.Zero);

        var logRecords = fakeLogger.Collector.GetSnapshot();
        var logRecord = Assert.Single(logRecords);
        Assert.Equal(LogLevel.Error, logRecord.Level);
        Assert.Same(exception, logRecord.Exception);
    }

    [Fact]
    public void SyncMethods_ShouldThrow()
    {
        var options = new LoggingOptions();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), Mock.Of<IHttpHeadersRedactor>());
        var requestReader = new HttpRequestReader(
            options, GetHttpRouteFormatter(), Mock.Of<IHttpRouteParser>(), headersReader, RequestMetadataContext);

        var logger = new HttpClientLogger(new FakeLogger<HttpClientLogger>(), requestReader, Array.Empty<IHttpClientLogEnricher>(), options);
        using var httpRequestMessage = new HttpRequestMessage();
        using var httpResponseMessage = new HttpResponseMessage();

        Assert.Throws<NotSupportedException>(() => logger.LogRequestStart(httpRequestMessage));
        Assert.Throws<NotSupportedException>(() => logger.LogRequestStop(null, httpRequestMessage, httpResponseMessage, TimeSpan.Zero));
        Assert.Throws<NotSupportedException>(() => logger.LogRequestFailed(null, httpRequestMessage, null, new InvalidOperationException(), TimeSpan.Zero));
    }

    private static IHttpRouteFormatter GetHttpRouteFormatter()
    {
        var builder = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpRouteProcessor()
            .BuildServiceProvider();

        return builder.GetService<IHttpRouteFormatter>()!;
    }

    private static void EnsureLogRecordDuration(string? actualValue)
    {
        Assert.NotNull(actualValue);
        Assert.InRange(int.Parse(actualValue), 0, int.MaxValue);
    }

    private static IOutgoingRequestContext RequestMetadataContext
        => new Mock<IOutgoingRequestContext>().Object;

    private static void EnsureLogRecordContainsOriginalFormat(FakeLogRecord logRecord)
    {
        var pair = logRecord.StructuredState!.Last();
        Assert.Equal("{OriginalFormat}", pair.Key);
        Assert.Equal("{http.request.method} {server.address}/{url.path}", pair.Value);
    }
}
