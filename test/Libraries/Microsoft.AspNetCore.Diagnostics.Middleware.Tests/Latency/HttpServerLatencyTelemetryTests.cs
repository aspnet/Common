// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Latency.Test;

public class HttpServerLatencyTelemetryTests
{
    [Fact]
    public void AddHttpServerLatencyTelemetry_NullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HttpLoggingServiceCollectionExtensions.AddHttpServerLatencyTelemetry(null!));
    }

    [Fact]
    public void AddHttpServerLatencyTelemetry_RegistersEnricher()
    {
        using var serviceProvider = new ServiceCollection()
            .AddHttpServerLatencyTelemetry()
            .BuildServiceProvider();

        var enricher = serviceProvider.GetRequiredService<IHttpLogEnricher>();

        Assert.NotNull(enricher);
        Assert.IsType<HttpLatencyLogEnricher>(enricher);
    }

    [Fact]
    public void AddHttpServerLatencyTelemetry_RedactionNotRegistered_RegistersErasingRedactor()
    {
        using var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddHttpServerLatencyTelemetry()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<ErasingRedactor>(serviceProvider.GetRequiredService<IRedactorProvider>().GetRedactor(FakeTaxonomy.PrivateData));
    }
}
