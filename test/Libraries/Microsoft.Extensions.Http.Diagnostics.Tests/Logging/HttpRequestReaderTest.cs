// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Test.Internal;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Telemetry.Internal;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpRequestReaderTest
{
    private const string Redacted = "REDACTED";
    private const string RequestedHost = "default-uri.com";

    private readonly Fixture _fixture;

    public HttpRequestReaderTest()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public async Task ReadAsync_AllData_ReturnsLogRecord()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");
        var header3 = new KeyValuePair<string, string>("Header3", "Value3");
        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = TelemetryConstants.Redacted,
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted), new("Header3", Redacted)],
            ResponseHeaders = [new("Header2", Redacted), new("Header3", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty,
        };

        var options = new LoggingOptions
        {
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData }, { header3.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData }, { header3.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(100000),
            LogBody = true,
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var serviceKey = "my-key";
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(serviceKey), mockHeadersRedactor.Object, serviceKey);
        using var serviceProvider = GetServiceProvider(headersReader, serviceKey);

        var reader = new HttpRequestReader(serviceProvider, options.ToOptionsMonitor(serviceKey), serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext, serviceKey: serviceKey);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo"),
            Content = new StringContent(requestContent, Encoding.UTF8)
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);
        httpRequestMessage.Headers.Add(header3.Key, header3.Value);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);
        httpResponseMessage.Headers.Add(header3.Key, header3.Value);

        var logRecord = new LogRecord();
        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(logRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        logRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_NoHost_ReturnsLogRecordWithoutHost()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        const string PlainTextMedia = "text/plain";

        var expectedRecord = new LogRecord
        {
            Host = TelemetryConstants.Unknown,
            Method = HttpMethod.Post,
            Path = TelemetryConstants.Unknown,
            StatusCode = 200,
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty
        };

        var options = new LoggingOptions
        {
            RequestBodyContentTypes = new HashSet<string> { PlainTextMedia },
            ResponseBodyContentTypes = new HashSet<string> { PlainTextMedia },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            LogBody = true,
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, options.ToOptionsMonitor(), serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = null,
            Content = new StringContent(requestContent, Encoding.UTF8)
        };

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        var actualRecord = new LogRecord();
        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_AllDataWithRequestMetadataSet_ReturnsLogRecord()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = "foo/bar/123",
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted)],
            ResponseHeaders = [new("Header2", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            LogBody = true,
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);
        httpRequestMessage.SetRequestMetadata(new RequestMetadata
        {
            RequestRoute = "/foo/bar/{userId}"
        });

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_FormatRequestPathDisabled_ReturnsLogRecordWithRoute()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = "foo/bar/{userId}",
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted)],
            ResponseHeaders = [new("Header2", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,
            PathParametersCount = 1,
            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            LogRequestStart = true,
            LogBody = true,
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader, configureRedaction: x => x.RedactionFormat = Redacted);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"https://{RequestedHost}/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);
        httpRequestMessage.SetRequestMetadata(new RequestMetadata
        {
            RequestRoute = "foo/bar/{userId}"
        });

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord, o => o.Excluding(x => x.PathParameters));

        HttpRouteParameter[] expectedParameters = [new("userId", Redacted, true)];
        actualRecord.PathParameters.Should().NotBeNull().And.Subject.Take(actualRecord.PathParametersCount).Should().BeEquivalentTo(expectedParameters);
    }

    [Fact]
    public async Task ReadAsync_RouteParameterRedactionModeNone_ReturnsLogRecordWithUnredactedRoute()
    {
        var requestContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = "/foo/bar/123",
            RequestHeaders = [new("Header1", Redacted)],
            RequestBody = requestContent,
            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            LogRequestStart = true,
            LogBody = true,
            RequestPathParameterRedactionMode = HttpRouteParameterRedactionMode.None,
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            RequestPathLoggingMode = OutgoingPathLoggingMode.Structured
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_RequestMetadataRequestNameSetAndRouteMissing_ReturnsLogRecord()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = "TestRequest",
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted)],
            ResponseHeaders = [new("Header2", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            LogBody = true,
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);
        httpRequestMessage.SetRequestMetadata(new RequestMetadata
        {
            RequestName = "TestRequest"
        });

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_NoMetadataUsesRedactedString_ReturnsLogRecord()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = TelemetryConstants.Redacted,
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted)],
            ResponseHeaders = [new("Header2", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            LogBody = true,
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_MetadataWithoutRequestRouteOrName_UsesRedactedPath_ReturnsLogRecord()
    {
        var requestContent = _fixture.Create<string>();
        var responseContent = _fixture.Create<string>();
        var header1 = new KeyValuePair<string, string>("Header1", "Value1");
        var header2 = new KeyValuePair<string, string>("Header2", "Value2");

        var expectedRecord = new LogRecord
        {
            Host = RequestedHost,
            Method = HttpMethod.Post,
            Path = TelemetryConstants.Redacted,
            StatusCode = 200,
            RequestHeaders = [new("Header1", Redacted)],
            ResponseHeaders = [new("Header2", Redacted)],
            RequestBody = requestContent,
            ResponseBody = responseContent,

            QueryString = string.Empty
        };

        var opts = new LoggingOptions
        {
            RequestHeadersDataClasses = new Dictionary<string, DataClassification> { { header1.Key, FakeTaxonomy.PrivateData } },
            ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { header2.Key, FakeTaxonomy.PrivateData } },
            RequestBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            ResponseBodyContentTypes = new HashSet<string> { MediaTypeNames.Text.Plain },
            BodyReadTimeout = TimeSpan.FromSeconds(10),
            LogBody = true,
        };

        opts.RouteParameterDataClasses.Add("userId", FakeTaxonomy.PrivateData);
        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(serviceProvider, opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(), serviceProvider.GetRequiredService<IHttpRouteParser>(), RequestMetadataContext);

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://default-uri.com/foo/bar/123"),
            Content = new StringContent(requestContent, Encoding.UTF8),
        };

        httpRequestMessage.Headers.Add(header1.Key, header1.Value);
        httpRequestMessage.SetRequestMetadata(new RequestMetadata());

        using var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, Encoding.UTF8)
        };

        httpResponseMessage.Headers.Add(header2.Key, header2.Value);

        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        var responseHeadersBuffer = new List<KeyValuePair<string, string>>();
        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        await reader.ReadResponseAsync(actualRecord, httpResponseMessage, responseHeadersBuffer, CancellationToken.None);

        actualRecord.Should().BeEquivalentTo(expectedRecord);
    }

    [Fact]
    public async Task ReadAsync_DependencyMetadataResolverReturnsHostDefaults_UsesRedactedPath()
    {
        var opts = new LoggingOptions();

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor.Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(opts.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(
            headersReader,
            configureServices: services => services.AddDownstreamDependencyMetadata(new TestDependencyMetadata()));

        var reader = new HttpRequestReader(
            serviceProvider,
            opts.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext,
            serviceProvider.GetRequiredService<HttpDependencyMetadataResolver>());

        // The host is registered, but no route matches, so the resolver hands back the host's default metadata.
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"https://{RequestedHost}/foo/bar/123")
        };

        var actualRecord = new LogRecord();
        await reader.ReadRequestAsync(actualRecord, httpRequestMessage, null, CancellationToken.None);

        actualRecord.Path.Should().Be(TelemetryConstants.Redacted);
    }

    [Fact]
    public async Task ReadAsync_SetsQueryParameters_WhenClassificationPresent()
    {
        var requestContent = _fixture.Create<string>();
        var queryParamName = "userId";
        var queryParamValue = "12345";

        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { queryParamName, FakeTaxonomy.PrivateData }
            },
            BodySizeLimit = 32000,
            BodyReadTimeout = TimeSpan.FromMinutes(5),
            LogBody = true
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<string>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        await using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var uri = new Uri($"https://{RequestedHost}/api/resource?{queryParamName}={queryParamValue}");

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = uri,
            Content = new StringContent(requestContent, Encoding.UTF8, "text/plain")
        };

        var logRecord = new LogRecord();
        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);
        logRecord.QueryString.Should().NotBeNullOrEmpty();
        logRecord.QueryString.Should().Contain("userId=REDACTED");
    }

    [Fact]
    public async Task ReadAsync_SkipsQueryString_WhenClassificationEmpty()
    {
        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>() // No data classification
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var uri = new Uri($"https://{RequestedHost}/api/resource?userId=12345");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        var logRecord = new LogRecord();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, new List<KeyValuePair<string, string>>(), CancellationToken.None);
        logRecord.QueryString.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ReadAsync_SetsEmptyQueryParameters_WhenNoMatchingClassification()
    {
        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { "otherParam", FakeTaxonomy.PrivateData }
            }
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var uri = new Uri($"https://{RequestedHost}/api/resource?userId=12345");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        var logRecord = new LogRecord();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, new List<KeyValuePair<string, string>>(), CancellationToken.None);

        logRecord.QueryString.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ReadAsync_SetsMultipleQueryParameters_WhenMultipleClassifications()
    {
        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { "userId", FakeTaxonomy.PrivateData },
                { "token", FakeTaxonomy.PrivateData }
            }
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<string>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var uri = new Uri($"https://{RequestedHost}/api/resource?userId=12345&token=abc&other=not_logged");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        var logRecord = new LogRecord();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, new List<KeyValuePair<string, string>>(), CancellationToken.None);
        logRecord.QueryString.Should().NotBeNullOrEmpty();
        logRecord.QueryString.Should().Be("userId=REDACTED&token=REDACTED");
    }

    [Fact]
    public async Task LogRequestStartAsync_LogsQueryParameters_TagArray()
    {
        // Arrange
        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { "userId", FakeTaxonomy.PrivateData }
            },
            LogRequestStart = true
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<string>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);

        var fakeLogger = new FakeLogger<HttpClientLogger>(
            new FakeLogCollector(
                Options.Options.Create(
                    new FakeLogCollectorOptions())));
        using var serviceProvider = GetServiceProvider(headersReader);
        var enrichers = Enumerable.Empty<IHttpClientLogEnricher>();
        var httpRequestReader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var clientLogger = new HttpClientLogger(
            fakeLogger,
            httpRequestReader,
            enrichers,
            options);

        var uri = new Uri($"https://{RequestedHost}/api/resource?userId=12345");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        // Act
        await clientLogger.LogRequestStartAsync(httpRequestMessage);

        // Assert
        var logRecord = fakeLogger.Collector.GetSnapshot().First();
        var state = logRecord.GetStructuredState();

        Assert.Contains(
            state,
            tag => tag.Key == "url.query" && (tag.Value!).Contains("userId=REDACTED"));
    }

    [Fact]
    public async Task ReadAsync_DoesntSetQueryString_WhenQueryValueEmpty()
    {
        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { "userId", FakeTaxonomy.PrivateData }
            }
        };

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<IEnumerable<string>>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        var uri = new Uri($"https://{RequestedHost}/api/resource?userId=");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        var logRecord = new LogRecord();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, new List<KeyValuePair<string, string>>(), CancellationToken.None);
        logRecord.QueryString.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ReadAsync_RedactsPathAndQueryParameters()
    {
        // Arrange
        var requestContent = _fixture.Create<string>();
        var queryParamName = "userId";
        var queryParamValue = "12345";
        var pathParamName = "orderId";
        var pathParamValue = "789";

        var options = new LoggingOptions
        {
            RequestQueryParametersDataClasses = new Dictionary<string, DataClassification>
            {
                { queryParamName, FakeTaxonomy.PrivateData }
            },
            LogBody = true,
            RequestPathLoggingMode = OutgoingPathLoggingMode.Formatted
        };
        options.RouteParameterDataClasses.Add("routeId", FakeTaxonomy.PrivateData);

        var mockHeadersRedactor = new Mock<IHttpHeadersRedactor>();
        mockHeadersRedactor
            .Setup(r => r.Redact(It.IsAny<string>(), It.IsAny<DataClassification>()))
            .Returns(Redacted);

        var headersReader = new HttpHeadersReader(options.ToOptionsMonitor(), mockHeadersRedactor.Object);
        using var serviceProvider = GetServiceProvider(headersReader);

        var reader = new HttpRequestReader(
            serviceProvider,
            options.ToOptionsMonitor(),
            serviceProvider.GetRequiredService<IHttpRouteFormatter>(),
            serviceProvider.GetRequiredService<IHttpRouteParser>(),
            RequestMetadataContext);

        // The route template includes a path parameter
        var routeTemplate = $"/api/orders/{{{pathParamName}}}/details";
        var uri = new Uri($"https://{RequestedHost}/api/orders/{pathParamValue}/details?{queryParamName}={queryParamValue}");

        using var httpRequestMessage = new HttpRequestMessage();
        httpRequestMessage.Method = HttpMethod.Get;
        httpRequestMessage.RequestUri = uri;
        httpRequestMessage.Content = new StringContent(requestContent, Encoding.UTF8, "text/plain");

        // Attach request metadata for the route template
        httpRequestMessage.SetRequestMetadata(new RequestMetadata
        {
            RequestRoute = routeTemplate
        });

        var logRecord = new LogRecord();
        var requestHeadersBuffer = new List<KeyValuePair<string, string>>();
        await reader.ReadRequestAsync(logRecord, httpRequestMessage, requestHeadersBuffer, CancellationToken.None);

        // Assert: path parameter is redacted in the path
        logRecord.Path.Should().NotContain(pathParamValue);
        logRecord.Path.Should().Contain(Redacted);

        logRecord.QueryString.Should().NotBeNullOrEmpty();
        logRecord.QueryString.Should().Contain($"{queryParamName}={Redacted}");
        logRecord.QueryString.Should().NotContain(queryParamValue);
    }

    private static ServiceProvider GetServiceProvider(
        HttpHeadersReader headersReader,
        string? serviceKey = null,
        Action<FakeRedactorOptions>? configureRedaction = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        if (serviceKey is null)
        {
            _ = services.AddSingleton<IHttpHeadersReader>(headersReader);
        }
        else
        {
            _ = services.AddKeyedSingleton<IHttpHeadersReader>(serviceKey, headersReader);
        }

        configureServices?.Invoke(services);

        return services
            .AddFakeRedaction(configureRedaction ?? (_ => { }))
            .AddHttpRouteProcessor()
            .BuildServiceProvider();
    }

    private static IOutgoingRequestContext RequestMetadataContext => Mock.Of<IOutgoingRequestContext>();

    private sealed class TestDependencyMetadata : IDownstreamDependencyMetadata
    {
        public string DependencyName => "TestDependency";

        public ISet<string> UniqueHostNameSuffixes { get; } = new HashSet<string> { RequestedHost };

        public ISet<RequestMetadata> RequestMetadata { get; } = new HashSet<RequestMetadata>
        {
            new("GET", "/v1/orders/{orderId}", "GetOrder")
        };
    }
}
