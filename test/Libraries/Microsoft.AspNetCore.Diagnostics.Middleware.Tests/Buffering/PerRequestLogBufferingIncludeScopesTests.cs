// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Buffering.Test;

public class PerRequestLogBufferingIncludeScopesTests
{
    private const string RequestPath = "/scoped";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncludeScopes_ControlsWhetherScopesAreAddedToBufferedLogRecords(bool includeScopes)
    {
        using IHost host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureLogging(logging => logging
                    .AddPerIncomingRequestBuffer(options =>
                    {
                        options.IncludeScopes = includeScopes;
                        options.Rules.Add(new LogBufferingFilterRule(categoryName: "test", logLevel: LogLevel.Information));
                    }))
                .Configure(app => app.Run(static context =>
                {
                    ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("test");
                    using (logger.BeginScope("Order {OrderId}", 42))
                    {
                        logger.LogInformation("buffered");
                    }

                    context.RequestServices.GetRequiredService<PerRequestLogBuffer>().Flush();
                    return Task.CompletedTask;
                })))
            .StartAsync();

        using HttpClient client = host.GetTestClient();
        using HttpResponseMessage response = await client.GetAsync(RequestPath);

        FakeLogRecord record = Assert.Single(host.GetFakeLogCollector().GetSnapshot(), r => r.Message == "buffered");
        Assert.Equal(includeScopes ? "42" : null, record.GetStructuredStateValue("OrderId"));

        // The request scope is begun by ASP.NET Core with a different logger than the one which logged the record.
        Assert.Equal(includeScopes ? RequestPath : null, record.GetStructuredStateValue("RequestPath"));
    }
}
#endif
