// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Logging.Test;

public class HttpLoggingServiceExtensionsTests
{
    [Fact]
    public void ShouldThrow_WhenArgsNull()
    {
        var services = Mock.Of<IServiceCollection>();

        Assert.Throws<ArgumentNullException>(static () => HttpLoggingServiceCollectionExtensions.AddHttpLogEnricher<TestHttpLogEnricher>(null!));

        Assert.Throws<ArgumentNullException>(
            () => HttpLoggingServiceCollectionExtensions.AddHttpLoggingRedaction(services, (IConfigurationSection)null!));
    }

    [Fact]
    public void AddHttpLogging_WhenConfiguredUsingConfigurationSection_IsCorrect()
    {
        var services = new ServiceCollection();
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("HttpLogging:RequestPathLoggingMode", "Structured"),
            new KeyValuePair<string, string?>("HttpLogging:RequestPathParameterRedactionMode","None"),
            new KeyValuePair<string, string?>("HttpLogging:ExcludePathStartsWith:[0]","/path0toexclude"),
            new KeyValuePair<string, string?>("HttpLogging:ExcludePathStartsWith:[1]","/path1toexclude"),
        });
        var configuration = builder.Build();
        services.AddHttpLoggingRedaction(configuration.GetSection("HttpLogging"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;

        Assert.Equal(IncomingPathLoggingMode.Structured, options.RequestPathLoggingMode);
        Assert.Equal(HttpRouteParameterRedactionMode.None, options.RequestPathParameterRedactionMode);

        Assert.Contains("/path0toexclude", options.ExcludePathStartsWith);
        Assert.Contains("/path1toexclude", options.ExcludePathStartsWith);
    }

    [Fact]
    public void AddHttpLogging_WhenDataClassesConfiguredUsingConfigurationSection_IsCorrect()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("HttpLogging:IncludeUnmatchedRoutes", "true"),
            new KeyValuePair<string, string?>("HttpLogging:ExcludePathStartsWith:[0]", "/probe/live"),
            new KeyValuePair<string, string?>("HttpLogging:ExcludePathStartsWith:[1]", null),
            new KeyValuePair<string, string?>("HttpLogging:RequestPathLoggingMode", "Structured"),
            new KeyValuePair<string, string?>("HttpLogging:RequestPathParameterRedactionMode", "None"),
            new KeyValuePair<string, string?>("HttpLogging:RequestHeadersDataClasses:User-Agent", "None"),
            new KeyValuePair<string, string?>("HttpLogging:ResponseHeadersDataClasses:Content-Type", "Unknown"),
            new KeyValuePair<string, string?>("HttpLogging:RouteParameterDataClasses:userId", "MyTaxonomy:EUII"),
            new KeyValuePair<string, string?>("HttpLogging:RouteParameterDataClasses:userContent:TaxonomyName", "MyTaxonomy"),
            new KeyValuePair<string, string?>("HttpLogging:RouteParameterDataClasses:userContent:Value", "CustomerContent"),
        }).Build();
        var configurationSection = configuration.GetSection("HttpLogging");

        services.AddHttpLoggingRedaction(configurationSection);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;

        Assert.Equal(
            typeof(LoggingRedactionOptions).GetProperties()
                .Where(property => property.SetMethod?.IsPublic is true)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
            configurationSection.GetChildren()
                .Select(child => child.Key)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(options.IncludeUnmatchedRoutes);
        Assert.Contains("/probe/live", options.ExcludePathStartsWith);
        Assert.Equal(IncomingPathLoggingMode.Structured, options.RequestPathLoggingMode);
        Assert.Equal(HttpRouteParameterRedactionMode.None, options.RequestPathParameterRedactionMode);
        Assert.Equal(DataClassification.None, options.RequestHeadersDataClasses["User-Agent"]);
        Assert.Equal(DataClassification.Unknown, options.ResponseHeadersDataClasses["Content-Type"]);
        Assert.Equal(new DataClassification("MyTaxonomy", "EUII"), options.RouteParameterDataClasses["userId"]);
        Assert.Equal(new DataClassification("MyTaxonomy", "CustomerContent"), options.RouteParameterDataClasses["userContent"]);
    }

    [Fact]
    public void AddHttpLogging_WhenDataClassIsInvalid_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("HttpLogging:RequestHeadersDataClasses:User-Agent", "invalid"),
        }).Build();

        services.AddHttpLoggingRedaction(configuration.GetSection("HttpLogging"));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value);

        Assert.Contains("HttpLogging:RequestHeadersDataClasses:User-Agent", exception.Message);
    }

    [Fact]
    public void AddHttpLogging_WhenScalarValueIsInvalid_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("HttpLogging:RequestPathLoggingMode", "invalid"),
        }).Build();

        services.AddHttpLoggingRedaction(configuration.GetSection("HttpLogging"));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value);

        Assert.Contains("HttpLogging:RequestPathLoggingMode", exception.Message);
    }

    [Fact]
    public void AddHttpLogging_WhenConfigurationSectionIsMissing_UsesDefaults()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddHttpLoggingRedaction(configuration.GetSection("Missing"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;
        var defaults = new LoggingRedactionOptions();

        Assert.Equal(defaults.RequestPathLoggingMode, options.RequestPathLoggingMode);
        Assert.Equal(defaults.RequestPathParameterRedactionMode, options.RequestPathParameterRedactionMode);
        Assert.Empty(options.RouteParameterDataClasses);
        Assert.Empty(options.RequestHeadersDataClasses);
        Assert.Empty(options.ResponseHeadersDataClasses);
        Assert.Empty(options.ExcludePathStartsWith);
        Assert.Equal(defaults.IncludeUnmatchedRoutes, options.IncludeUnmatchedRoutes);
    }

    [Fact]
    public void AddHttpLogging_CanConfigureDataClasses()
    {
        var services = new ServiceCollection();
        services.AddHttpLoggingRedaction(o =>
        {
            o.RouteParameterDataClasses = new Dictionary<string, DataClassification>
            {
                { "one", new DataClassification("Taxonomy1", "Value1") },
            };

            o.RequestHeadersDataClasses = new Dictionary<string, DataClassification>
            {
                { "two", new DataClassification("Taxonomy2", "Value2") },
            };

            o.ResponseHeadersDataClasses = new Dictionary<string, DataClassification>
            {
                { "three", new DataClassification("Taxonomy3", "Value3") },
            };
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;

        Assert.Single(options.RouteParameterDataClasses);
        Assert.Equal("Taxonomy1", options.RouteParameterDataClasses["one"].TaxonomyName);
        Assert.Equal("Value1", options.RouteParameterDataClasses["one"].Value);

        Assert.Single(options.RequestHeadersDataClasses);
        Assert.Equal("Taxonomy2", options.RequestHeadersDataClasses["two"].TaxonomyName);
        Assert.Equal("Value2", options.RequestHeadersDataClasses["two"].Value);

        Assert.Single(options.ResponseHeadersDataClasses);
        Assert.Equal("Taxonomy3", options.ResponseHeadersDataClasses["three"].TaxonomyName);
        Assert.Equal("Value3", options.ResponseHeadersDataClasses["three"].Value);
    }

    [Fact]
    public void AddHttpLoggingRedaction_RedactionNotRegistered_RegistersErasingRedactor()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddHttpLoggingRedaction()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<ErasingRedactor>(provider.GetRequiredService<IRedactorProvider>().GetRedactor(FakeTaxonomy.PrivateData));
    }

    [Fact]
    public void AddHttpLoggingRedaction_WithConfigurationSection_RedactionNotRegistered_RegistersErasingRedactor()
    {
        var section = new ConfigurationBuilder().Build().GetSection("HttpLogging");
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddHttpLoggingRedaction(section)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<ErasingRedactor>(provider.GetRequiredService<IRedactorProvider>().GetRedactor(FakeTaxonomy.PrivateData));
    }

    [Fact]
    public void AddHttpLogEnricher_RedactionNotRegistered_RegistersErasingRedactor()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddHttpLogEnricher<TestHttpLogEnricher>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<ErasingRedactor>(provider.GetRequiredService<IRedactorProvider>().GetRedactor(FakeTaxonomy.PrivateData));
    }

    [Fact]
    public void AddHttpLoggingRedaction_RedactionRegisteredAfter_UsesRegisteredRedactor()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddHttpLoggingRedaction()
            .AddRedaction(redaction => redaction.SetFakeRedactor(FakeTaxonomy.PrivateData))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<FakeRedactor>(provider.GetRequiredService<IRedactorProvider>().GetRedactor(FakeTaxonomy.PrivateData));
    }
}
#endif
