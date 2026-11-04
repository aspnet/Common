// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Test.Internal;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpClientLoggingExtensionsTest
{
    private readonly Fixture _fixture;

    public HttpClientLoggingExtensionsTest()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void AddHttpClientLogging_AnyArgumentIsNull_Throws()
    {
        var act = () => ((IHttpClientBuilder)null!).AddExtendedHttpClientLogging();
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IHttpClientBuilder)null!).AddExtendedHttpClientLogging(_ => { });
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IHttpClientBuilder)null!).AddExtendedHttpClientLogging(Mock.Of<IConfigurationSection>());
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IHttpClientBuilder>().AddExtendedHttpClientLogging((Action<LoggingOptions>)null!);
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IHttpClientBuilder>().AddExtendedHttpClientLogging((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddHttpClientLogging_ServiceCollection_AnyArgumentIsNull_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddExtendedHttpClientLogging();
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IServiceCollection)null!).AddExtendedHttpClientLogging(_ => { });
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IServiceCollection)null!).AddExtendedHttpClientLogging(Mock.Of<IConfigurationSection>());
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IServiceCollection>().AddExtendedHttpClientLogging((Action<LoggingOptions>)null!);
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IServiceCollection>().AddExtendedHttpClientLogging((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddHttpClientLogEnricher_AnyArgumentIsNull_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddHttpClientLogEnricher<EmptyEnricher>();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddHttpClientLogging_ConfiguredOptionsWithNamedClient_ShouldNotBeSame()
    {
        var services = new ServiceCollection();

        using var provider = services
            .AddHttpClient("test1")
            .AddExtendedHttpClientLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(1))
            .Services
            .AddHttpClient("test2")
            .AddExtendedHttpClientLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(2))
            .Services
            .BuildServiceProvider();

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test1");
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test2");
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
        optionsFirst.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(1));
        optionsSecond.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddHttpClientLogging_ConfiguredOptionsWithTypedClient_ShouldNotBeSame()
    {
        var services = new ServiceCollection();

        using var provider = services
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>()
            .AddExtendedHttpClientLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(1))
            .Services
            .AddHttpClient<ITestHttpClient2, TestHttpClient2>()
            .AddExtendedHttpClientLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(2))
            .Services
            .BuildServiceProvider();

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient1));
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient2));
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
        optionsFirst.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(1));
        optionsSecond.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddHttpClientLogging_DefaultOptions_CreatesOptionsCorrectly()
    {
        var services = new ServiceCollection();

        using var provider = services
            .AddHttpClient("")
            .AddExtendedHttpClientLogging(o => o.RequestHeadersDataClasses.Add("test1", FakeTaxonomy.PrivateData))
            .Services
            .AddHttpClient("")
            .AddExtendedHttpClientLogging(o => o.RequestHeadersDataClasses.Add("test2", FakeTaxonomy.PrivateData))
            .Services
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;
        options.RequestHeadersDataClasses.Should().HaveCount(2);
        options.RequestHeadersDataClasses.Should().ContainKeys(new List<string> { "test1", "test2" });
        options.RequestHeadersDataClasses.Should().ContainValues(new List<DataClassification> { FakeTaxonomy.PrivateData });
    }

    [Fact]
    public void AddHttpClientLogging_GivenActionDelegate_RegistersInDi()
    {
        var requestBodyContentType = "application/json";
        var responseBodyContentType = "application/json";
        var requestHeader = _fixture.Create<string>();
        var responseHeader = _fixture.Create<string>();
        var bodyReadTimeout = TimeSpan.FromSeconds(1);
        var bodySizeLimit = 100;
        var formatRequestPath = _fixture.Create<OutgoingPathLoggingMode>();
        var formatRequestPathParameters = _fixture.Create<HttpRouteParameterRedactionMode>();
        var logStart = _fixture.Create<bool>();
        var paramToRedact = new KeyValuePair<string, DataClassification>("userId", FakeTaxonomy.PrivateData);

        var services = new ServiceCollection();

        services
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(options =>
            {
                options.RequestBodyContentTypes.Add(requestBodyContentType);
                options.ResponseBodyContentTypes.Add(responseBodyContentType);
                options.BodyReadTimeout = bodyReadTimeout;
                options.BodySizeLimit = bodySizeLimit;
                options.RequestPathLoggingMode = formatRequestPath;
                options.RequestPathParameterRedactionMode = formatRequestPathParameters;
                options.RequestHeadersDataClasses.Add(requestHeader, FakeTaxonomy.PrivateData);
                options.ResponseHeadersDataClasses.Add(responseHeader, FakeTaxonomy.PrivateData);
                options.RouteParameterDataClasses.Add(paramToRedact);
                options.LogRequestStart = logStart;
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        options.Should().NotBeNull();
        options.RequestBodyContentTypes.Should().ContainSingle();
        options.RequestBodyContentTypes.Should().Contain(requestBodyContentType);
        options.ResponseBodyContentTypes.Should().ContainSingle();
        options.ResponseBodyContentTypes.Should().Contain(responseBodyContentType);
        options.BodyReadTimeout.Should().Be(bodyReadTimeout);
        options.BodySizeLimit.Should().Be(bodySizeLimit);
        options.RequestPathLoggingMode.Should().Be(formatRequestPath);
        options.RequestPathParameterRedactionMode.Should().Be(formatRequestPathParameters);
        options.RequestHeadersDataClasses.Should().ContainSingle();
        options.RequestHeadersDataClasses.Should().Contain(requestHeader, FakeTaxonomy.PrivateData);
        options.ResponseHeadersDataClasses.Should().ContainSingle();
        options.ResponseHeadersDataClasses.Should().Contain(responseHeader, FakeTaxonomy.PrivateData);
        options.RouteParameterDataClasses.Should().ContainSingle();
        options.RouteParameterDataClasses.Should().Contain(paramToRedact);
        options.LogRequestStart.Should().Be(logStart);
    }

    [Fact]
    public async Task AddHttpClientLogging_GivenInvalidOptions_Throws()
    {
        using var host = FakeHost.CreateBuilder()
            .ConfigureServices(services =>
            {
                services
                    .AddFakeRedaction()
                    .AddHttpClient("test")
                    .AddExtendedHttpClientLogging(options =>
                    {
                        options.BodyReadTimeout = TimeSpan.Zero;
                        options.BodySizeLimit = -1;
                    });
            })
            .Build();

        var act = async () => await host.StartAsync().ConfigureAwait(false);
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(17)]
    public void AddHttpClientLogging_GivenConfigurationSection_SetsTimeoutCorrectly(int seconds)
    {
        var timeoutValue = TimeSpan.FromSeconds(seconds);

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(TestConfiguration.GetHttpClientLoggingConfigurationSection(timeoutValue))
            .Services
            .BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        options.Should().NotBeNull();
        options.BodyReadTimeout.Should().Be(timeoutValue);
    }

    [Fact]
    public void AddHttpClientLogging_GivenConfigurationSection_BindsDataClasses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.LogRequestStart)}"] = "true",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestQueryParametersDataClasses)}:search:{nameof(DataClassification.TaxonomyName)}"] = "MyTaxonomy",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestQueryParametersDataClasses)}:search:{nameof(DataClassification.Value)}"] = "PrivateData",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.LogBody)}"] = "true",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.BodySizeLimit)}"] = "1024",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.BodyReadTimeout)}"] = "00:00:02",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestBodyContentTypes)}:0"] = "application/json",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestBodyContentTypes)}:1"] = null,
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.ResponseBodyContentTypes)}:0"] = "text/plain",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestHeadersDataClasses)}:User-Agent"] = "None",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.ResponseHeadersDataClasses)}:Content-Type"] = "Unknown",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestPathLoggingMode)}"] = "Structured",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestPathParameterRedactionMode)}"] = "None",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RouteParameterDataClasses)}:userId"] = "MyTaxonomy:EUII",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.LogContentHeaders)}"] = "true",
            })
            .Build();
        var configurationSection = configuration.GetSection(nameof(LoggingOptions));

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(configurationSection)
            .Services
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        configurationSection.GetChildren().Select(child => child.Key).Should().BeEquivalentTo(
            typeof(LoggingOptions).GetProperties()
                .Where(property => property.SetMethod?.IsPublic is true)
                .Select(property => property.Name));
        options.LogRequestStart.Should().BeTrue();
        options.RequestQueryParametersDataClasses.Should().Contain("search", new DataClassification("MyTaxonomy", "PrivateData"));
        options.LogBody.Should().BeTrue();
        options.BodySizeLimit.Should().Be(1024);
        options.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(2));
        options.RequestBodyContentTypes.Should().Equal("application/json");
        options.ResponseBodyContentTypes.Should().Equal("text/plain");
        options.RequestHeadersDataClasses.Should().Contain("User-Agent", DataClassification.None);
        options.ResponseHeadersDataClasses.Should().Contain("Content-Type", DataClassification.Unknown);
        options.RequestPathLoggingMode.Should().Be(OutgoingPathLoggingMode.Structured);
        options.RequestPathParameterRedactionMode.Should().Be(HttpRouteParameterRedactionMode.None);
        options.RouteParameterDataClasses.Should().Contain("userId", new DataClassification("MyTaxonomy", "EUII"));
        options.LogContentHeaders.Should().BeTrue();
    }

    [Fact]
    public void LoggingOptionsConfigureOptions_HandlesOptionNamesAndMissingSections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.BodySizeLimit)}"] = "1024",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.LogBody)}"] = "false",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.LogContentHeaders)}"] = "false",
            })
            .Build();
        var section = configuration.GetSection(nameof(LoggingOptions));
        int defaultBodySizeLimit = new LoggingOptions().BodySizeLimit;

        var defaultOptions = new LoggingOptions
        {
            LogBody = true,
            LogContentHeaders = true,
        };
        var defaultConfigurator = new LoggingOptionsConfigureOptions(Microsoft.Extensions.Options.Options.DefaultName, section);
        ((IConfigureOptions<LoggingOptions>)defaultConfigurator).Configure(defaultOptions);

        defaultOptions.BodySizeLimit.Should().Be(1024);
        defaultOptions.LogBody.Should().BeFalse();
        defaultOptions.LogContentHeaders.Should().BeFalse();

        var mismatchedOptions = new LoggingOptions();
        new LoggingOptionsConfigureOptions("test", section).Configure("other", mismatchedOptions);

        mismatchedOptions.BodySizeLimit.Should().Be(defaultBodySizeLimit);

        var missingSectionOptions = new LoggingOptions();
        new LoggingOptionsConfigureOptions("test", configuration.GetSection("Missing")).Configure("test", missingSectionOptions);

        missingSectionOptions.BodySizeLimit.Should().Be(defaultBodySizeLimit);
    }

    [Fact]
    public void AddHttpClientLogging_GivenDocumentedConfigurationSample_CreatesClient()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "HttpClientLogging": {
            "LogRequestStart": false,
            "LogBody": false,
            "BodySizeLimit": 32768,
            "BodyReadTimeout": "00:00:01",
            "RequestHeadersDataClasses": {
              "User-Agent": "None",
              "Content-Type": "None"
            },
            "ResponseHeadersDataClasses": {
              "Content-Type": "None"
            },
            "RequestPathLoggingMode": "Formatted",
            "RequestPathParameterRedactionMode": "Strict"
          }
        }
        """));

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedaction();
        services.AddHttpClient("test")
            .AddExtendedHttpClientLogging(configuration.GetSection("HttpClientLogging"));

        using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        client.Should().NotBeNull();

        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");
        options.LogRequestStart.Should().BeFalse();
        options.LogBody.Should().BeFalse();
        options.BodySizeLimit.Should().Be(32768);
        options.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(1));
        options.RequestHeadersDataClasses.Should().Contain("User-Agent", DataClassification.None);
        options.RequestHeadersDataClasses.Should().Contain("Content-Type", DataClassification.None);
        options.ResponseHeadersDataClasses.Should().Contain("Content-Type", DataClassification.None);
        options.RequestPathLoggingMode.Should().Be(OutgoingPathLoggingMode.Formatted);
        options.RequestPathParameterRedactionMode.Should().Be(HttpRouteParameterRedactionMode.Strict);
    }

    [Fact]
    public void AddHttpClientLogging_GivenInvalidDataClassification_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestHeadersDataClasses)}:User-Agent"] = "invalid",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(configuration.GetSection(nameof(LoggingOptions)))
            .Services
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LoggingOptions:RequestHeadersDataClasses:User-Agent*");
    }

    [Fact]
    public void AddHttpClientLogging_GivenInvalidScalarValue_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestPathLoggingMode)}"] = "invalid",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(configuration.GetSection(nameof(LoggingOptions)))
            .Services
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LoggingOptions:RequestPathLoggingMode*");
    }

    [Fact]
    public void AddHttpClientLogging_GivenConfigurationReload_UpdatesOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.BodySizeLimit)}"] = "1024",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestHeadersDataClasses)}:User-Agent"] = "None",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddExtendedHttpClientLogging(configuration.GetSection(nameof(LoggingOptions)))
            .Services
            .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>();

        options.Get("test").BodySizeLimit.Should().Be(1024);
        options.Get("test").RequestHeadersDataClasses.Should().Contain("User-Agent", DataClassification.None);

        configuration[$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.BodySizeLimit)}"] = "2048";
        configuration[$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestHeadersDataClasses)}:User-Agent"] = "Unknown";
        configuration.Reload();

        options.Get("test").BodySizeLimit.Should().Be(2048);
        options.Get("test").RequestHeadersDataClasses.Should().Contain("User-Agent", DataClassification.Unknown);
    }

    [Fact]
    public void AddHttpClientLogEnricher_RegistersEnricherInDI()
    {
        using var provider = new ServiceCollection()
            .AddHttpClientLogEnricher<EmptyEnricher>()
            .BuildServiceProvider();

        var enricherRegistered = provider.GetService<IHttpClientLogEnricher>();

        enricherRegistered.Should().NotBeNull();
        enricherRegistered.Should().BeOfType<EmptyEnricher>();
    }

    [Fact]
    public void AddHttpClientLogging_ServiceCollection_GivenActionDelegate_RegistersInDi()
    {
        var requestBodyContentType = "application/json";
        var responseBodyContentType = "application/json";
        var requestHeader = _fixture.Create<string>();
        var responseHeader = _fixture.Create<string>();
        var bodyReadTimeout = TimeSpan.FromSeconds(1);
        var bodySizeLimit = 100;
        var formatRequestPath = _fixture.Create<OutgoingPathLoggingMode>();
        var formatRequestPathParameters = _fixture.Create<HttpRouteParameterRedactionMode>();
        var logStart = _fixture.Create<bool>();
        var paramToRedact = new KeyValuePair<string, DataClassification>("userId", FakeTaxonomy.PrivateData);

        var services = new ServiceCollection();

        services
            .AddFakeRedaction()
            .AddHttpClient()
            .AddExtendedHttpClientLogging(options =>
            {
                options.RequestBodyContentTypes.Add(requestBodyContentType);
                options.ResponseBodyContentTypes.Add(responseBodyContentType);
                options.BodyReadTimeout = bodyReadTimeout;
                options.BodySizeLimit = bodySizeLimit;
                options.RequestPathLoggingMode = formatRequestPath;
                options.RequestPathParameterRedactionMode = formatRequestPathParameters;
                options.RequestHeadersDataClasses.Add(requestHeader, FakeTaxonomy.PrivateData);
                options.ResponseHeadersDataClasses.Add(responseHeader, FakeTaxonomy.PrivateData);
                options.RouteParameterDataClasses.Add(paramToRedact);
                options.LogRequestStart = logStart;
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;

        options.Should().NotBeNull();
        options.RequestBodyContentTypes.Should().ContainSingle();
        options.RequestBodyContentTypes.Should().Contain(requestBodyContentType);
        options.ResponseBodyContentTypes.Should().ContainSingle();
        options.ResponseBodyContentTypes.Should().Contain(responseBodyContentType);
        options.BodyReadTimeout.Should().Be(bodyReadTimeout);
        options.BodySizeLimit.Should().Be(bodySizeLimit);
        options.RequestPathLoggingMode.Should().Be(formatRequestPath);
        options.RequestPathParameterRedactionMode.Should().Be(formatRequestPathParameters);
        options.RequestHeadersDataClasses.Should().ContainSingle();
        options.RequestHeadersDataClasses.Should().Contain(requestHeader, FakeTaxonomy.PrivateData);
        options.ResponseHeadersDataClasses.Should().ContainSingle();
        options.ResponseHeadersDataClasses.Should().Contain(responseHeader, FakeTaxonomy.PrivateData);
        options.RouteParameterDataClasses.Should().ContainSingle();
        options.RouteParameterDataClasses.Should().Contain(paramToRedact);
        options.LogRequestStart.Should().Be(logStart);

        using var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public async Task AddHttpClientLogging_ServiceCollection_GivenInvalidOptions_Throws()
    {
        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .AddExtendedHttpClientLogging(options =>
            {
                options.BodyReadTimeout = TimeSpan.Zero;
                options.BodySizeLimit = -1;
            })
            .BuildServiceProvider();

        var act = () =>
            provider
                .GetRequiredService<IHostedService>()
                .StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void AddHttpClientLogging_ServiceCollectionAndHttpClientBuilder_DoesNotDuplicate()
    {
        const string ClientName = "test";

        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient(ClientName)
            .AddExtendedHttpClientLogging(x =>
            {
                x.BodySizeLimit = 100500;
                x.RequestHeadersDataClasses.Add(ClientName, FakeTaxonomy.PublicData);
            }).Services
            .AddExtendedHttpClientLogging(x =>
            {
                x.BodySizeLimit = 347;
                x.RequestHeadersDataClasses.Add("default", FakeTaxonomy.PrivateData);
            })
            .BuildServiceProvider();

        EnsureSingleLogger<HttpClientLogger>(provider, ClientName);
    }

    [Fact]
    public void AddHttpClientLogging_HttpClientBuilderAndServiceCollection_DoesNotDuplicate()
    {
        const string ClientName = "test";

        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddExtendedHttpClientLogging()
            .AddHttpClient(ClientName)
            .AddExtendedHttpClientLogging()
            .Services.BuildServiceProvider();

        EnsureSingleLogger<HttpClientLogger>(provider, ClientName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(17)]
    public void AddHttpClientLogging_ServiceCollection_GivenConfigurationSection_SetsTimeoutCorrectly(int seconds)
    {
        var timeoutValue = TimeSpan.FromSeconds(seconds);

        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .AddExtendedHttpClientLogging(TestConfiguration.GetHttpClientLoggingConfigurationSection(timeoutValue))
            .BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<LoggingOptions>>().Value;

        options.Should().NotBeNull();
        options.BodyReadTimeout.Should().Be(timeoutValue);

        using var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public void AddHttpClientLogging_ServiceCollection_GivenConfigurationSection_BindsDataClasses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestQueryParametersDataClasses)}:search"] = "MyTaxonomy:PrivateData",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RequestHeadersDataClasses)}:User-Agent"] = "None",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.ResponseHeadersDataClasses)}:Content-Type"] = "Unknown",
                [$"{nameof(LoggingOptions)}:{nameof(LoggingOptions.RouteParameterDataClasses)}:userId"] = "MyTaxonomy:EUII",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddHttpClient()
            .AddExtendedHttpClientLogging(configuration.GetSection(nameof(LoggingOptions)))
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;

        options.RequestQueryParametersDataClasses.Should().Contain("search", new DataClassification("MyTaxonomy", "PrivateData"));
        options.RequestHeadersDataClasses.Should().Contain("User-Agent", DataClassification.None);
        options.ResponseHeadersDataClasses.Should().Contain("Content-Type", DataClassification.Unknown);
        options.RouteParameterDataClasses.Should().Contain("userId", new DataClassification("MyTaxonomy", "EUII"));
    }

    [Fact]
    public void AddHttpClientLogging_ServiceCollection_CreatesClientSuccessfully()
    {
        using var sp = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .AddExtendedHttpClientLogging()
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public async Task LatencyInfo_IsPopulated_WhenLoggerWrapsHandlersPipeline()
    {
        await using var sp = new ServiceCollection()
            .AddLatencyContext()
            .AddRedaction()
            .AddHttpClientLatencyTelemetry()
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => new TestMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation(TelemetryConstants.ServerApplicationNameHeader, "TestServer");
                return response;
            }))
            .AddExtendedHttpClientLogging(wrapHandlersPipeline: true)
            .Services
            .AddFakeLogging()
            .BuildServiceProvider();

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://localhost/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var collector = sp.GetFakeLogCollector();
        var record = collector.LatestRecord;
        Assert.NotNull(record);

        var latencyInfo = record.GetStructuredStateValue("LatencyInfo");
        Assert.False(string.IsNullOrEmpty(latencyInfo));
        Assert.StartsWith("v1.0,", latencyInfo);
        Assert.Contains("TestServer", latencyInfo);
    }

    [Fact]
    public async Task AddExtendedHttpClientLogging_WrapHandlersPipelineParameter_LogsCorrectNumberOfAttempts()
    {
        int attemptCount = 0;
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddTransient(_ =>
            new TestMessageHandler(_ =>
            {
                attemptCount++;
                return attemptCount % 3 == 0 // every 3rd attempt succeeds
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : throw new HttpRequestException("Simulated failure");
            }));

        serviceCollection.AddTransient<TestRetryHandler>();

        serviceCollection
            .AddFakeRedaction()
            .AddFakeLogging()
            .AddHttpClient("outer")
                .ConfigurePrimaryHttpMessageHandler<TestMessageHandler>()
                .AddHttpMessageHandler<TestRetryHandler>()
                .AddExtendedHttpClientLogging(o => o.LogRequestStart = true, wrapHandlersPipeline: true);

        serviceCollection
            .AddHttpClient("inner")
                .ConfigurePrimaryHttpMessageHandler<TestMessageHandler>()
                .AddHttpMessageHandler<TestRetryHandler>()
                .AddExtendedHttpClientLogging(o => o.LogRequestStart = true, wrapHandlersPipeline: false);

        using var services = serviceCollection.BuildServiceProvider();
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var collector = services.GetFakeLogCollector();

        var outerClient = factory.CreateClient("outer");
        attemptCount = 0;
        collector.Clear();

        _ = await outerClient.GetAsync("http://localhost/api");

        var outerLogs = collector.GetSnapshot();
        Assert.Equal(2, outerLogs.Count); // 1 request start + 1 request stop

        var innerClient = factory.CreateClient("inner");
        attemptCount = 0;
        collector.Clear();

        _ = await innerClient.GetAsync("http://localhost/api");

        var innerLogs = collector.GetSnapshot();
        Assert.Equal(6, innerLogs.Count); // 3 attempts × (1 start + 1 stop/failed)
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddExtendedHttpClientLogging_WithoutRedactionRegistration_LogsRequest(bool useHttpClientBuilder)
    {
        await using var sp = BuildServiceProviderWithoutRedactionRegistration(useHttpClientBuilder);

        using var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.GetAsync("http://localhost/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("200", GetHttpClientLoggerRecord(sp).GetStructuredStateValue(HttpClientLoggingTagNames.StatusCode));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddExtendedHttpClientLogging_WithoutRedactionRegistration_ErasesClassifiedData(bool useHttpClientBuilder)
    {
        await using var sp = BuildServiceProviderWithoutRedactionRegistration(useHttpClientBuilder, options =>
        {
            options.RequestHeadersDataClasses.Add("X-Private", FakeTaxonomy.PrivateData);
            options.RequestHeadersDataClasses.Add("X-Public", DataClassification.None);
        });

        using var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api");
        request.Headers.Add("X-Private", "private-value");
        request.Headers.Add("X-Public", "public-value");
        using var response = await client.SendAsync(request);

        var record = GetHttpClientLoggerRecord(sp);
        Assert.Equal(string.Empty, record.GetStructuredStateValue($"{HttpClientLoggingTagNames.RequestHeaderPrefix}x-private"));
        Assert.Equal("public-value", record.GetStructuredStateValue($"{HttpClientLoggingTagNames.RequestHeaderPrefix}x-public"));
    }

    [Fact]
    public async Task AddExtendedHttpClientLogging_RedactionConfiguredAfterLogging_UsesConfiguredRedactor()
    {
        await using var sp = new ServiceCollection()
            .AddFakeLogging()
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => new TestMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            .AddExtendedHttpClientLogging(options => options.RequestHeadersDataClasses.Add("X-Test", FakeTaxonomy.PrivateData))
            .Services
            .AddRedaction(redaction => redaction.SetFakeRedactor(options => options.RedactionFormat = "REDACTED:{0}", FakeTaxonomy.PrivateData))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api");
        request.Headers.Add("X-Test", "header-value");
        using var response = await client.SendAsync(request);

        Assert.Equal("REDACTED:header-value", GetHttpClientLoggerRecord(sp).GetStructuredStateValue($"{HttpClientLoggingTagNames.RequestHeaderPrefix}x-test"));
    }

    [Fact]
    public async Task LatencyInfo_IsPopulated_WithoutLatencyContextAndRedactionRegistration()
    {
        await using var sp = new ServiceCollection()
            .AddHttpClientLatencyTelemetry()
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => new TestMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation(TelemetryConstants.ServerApplicationNameHeader, "TestServer");
                return response;
            }))
            .AddExtendedHttpClientLogging(wrapHandlersPipeline: true)
            .Services
            .AddFakeLogging()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync("http://localhost/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var latencyInfo = GetHttpClientLoggerRecord(sp).GetStructuredStateValue("LatencyInfo");
        Assert.False(string.IsNullOrEmpty(latencyInfo));
        Assert.StartsWith("v1.0,", latencyInfo);
        Assert.Contains("TestServer", latencyInfo);
    }

    private static ServiceProvider BuildServiceProviderWithoutRedactionRegistration(bool useHttpClientBuilder, Action<LoggingOptions>? configure = null)
    {
        var services = new ServiceCollection().AddFakeLogging();
        var builder = services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => new TestMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        if (useHttpClientBuilder)
        {
            _ = configure is null ? builder.AddExtendedHttpClientLogging() : builder.AddExtendedHttpClientLogging(configure);
        }
        else
        {
            _ = configure is null ? services.AddExtendedHttpClientLogging() : services.AddExtendedHttpClientLogging(configure);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static FakeLogRecord GetHttpClientLoggerRecord(IServiceProvider serviceProvider)
        => serviceProvider.GetFakeLogCollector().GetSnapshot().Single(r => r.Category == typeof(HttpClientLogger).FullName);

    private sealed class TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestRetryHandler : DelegatingHandler
    {
        private const int MaxRetries = 2;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpRequestException? lastException = null;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt == MaxRetries)
                    {
                        throw;
                    }
                }
            }

            throw lastException!;
        }
    }

    private static void EnsureSingleLogger<T>(IServiceProvider serviceProvider, string serviceKey)
        where T : IHttpClientLogger
    {
        var loggers = serviceProvider.GetServices<T>();
        loggers.Should().ContainSingle();

        var keyedLoggers = serviceProvider.GetKeyedServices<T>(serviceKey);
        keyedLoggers.Should().ContainSingle();
    }
}
