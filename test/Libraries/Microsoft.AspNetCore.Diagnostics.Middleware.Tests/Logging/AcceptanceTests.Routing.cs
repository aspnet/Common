// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.Logging.Test.Controllers;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Text;
using Xunit;
using static Microsoft.Extensions.Http.Diagnostics.HttpRouteParameterRedactionMode;

namespace Microsoft.AspNetCore.Diagnostics.Logging.Test;

public partial class AcceptanceTests
{
    [SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable", Justification = "Needed for reflection")]
    private class TestStartupWithRouting
    {
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Used through reflection")]
        public static void ConfigureServices(IServiceCollection services)
            => services
                .AddFakeRedaction(x => x.RedactionFormat = RedactedFormat)
                .AddRouting()
                .AddControllers();

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Used through reflection")]
        public static void Configure(IApplicationBuilder app)
            => app
                .UseRouting()
                .UseHttpLogging()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();

                    endpoints.MapControllerRoute(
                        name: "default",
                        pattern: ConventionalRoutingController.Route);

                    endpoints.MapControllerRoute(
                        name: "mixed-routing",
                        pattern: "mixed/conventional-routing",
                        defaults: new { controller = "MixedRouting", action = "ConventionalRouting" });
                });
    }

    private static Task RunRoutingTestAsync<TStartup>(
        string httpPath,
        Action<IServiceCollection> configureHttpLogging,
        Action<Dictionary<string, string?>> validateRequestState)
        where TStartup : class
    {
        return RunAsync<TStartup>(
            LogLevel.Information,
            configureHttpLogging,
            async (logCollector, client, _) =>
            {
                using var response = await client.GetAsync(httpPath).ConfigureAwait(false);
                Assert.True(response.IsSuccessStatusCode);

                await WaitForLogRecordsAsync(logCollector, TimeSpan.FromSeconds(30));

                Assert.Equal(1, logCollector.Count);

                var logRecord = logCollector.LatestRecord;
                Assert.Null(logRecord.Exception);
                Assert.Equal(LoggingCategory, logRecord.Category);
                Assert.Equal(LogLevel.Information, logRecord.Level);

                var responseStatus = ((int)response.StatusCode).ToInvariantString();
                var state = logRecord.StructuredState!;
                validateRequestState(new Dictionary<string, string?>(state));
            });
    }

    [Theory]

    // Conventional routing.
    [InlineData(Strict, "", "")]
    [InlineData(Loose, "", "")]
    [InlineData(Strict, "ConventionalRouting", "ConventionalRouting")]
    [InlineData(Loose, "ConventionalRouting", "ConventionalRouting")]
    [InlineData(Strict, "ConventionalRouting/GetEntity/12345", "ConventionalRouting/GetEntity/<redacted:12345>")]
    [InlineData(Loose, "ConventionalRouting/GetEntity/12345", "ConventionalRouting/GetEntity/<redacted:12345>")]
    [InlineData(Strict, "ConventionalRouting/GetData/12345", $"ConventionalRouting/GetData/{TelemetryConstants.Redacted}")]
    [InlineData(Loose, "ConventionalRouting/GetData/12345", "ConventionalRouting/GetData/12345")]

    // Attribute routing.
    [InlineData(Strict, "AttributeRouting", "AttributeRouting")]
    [InlineData(Loose, "AttributeRouting", "AttributeRouting")]
    [InlineData(Strict, "AttributeRouting/all", "AttributeRouting/all")]
    [InlineData(Loose, "AttributeRouting/all", "AttributeRouting/all")]
    [InlineData(Strict, "AttributeRouting/get-1/12345", "AttributeRouting/get-1/<redacted:12345>")]
    [InlineData(Loose, "AttributeRouting/get-1/12345", "AttributeRouting/get-1/<redacted:12345>")]
    [InlineData(Strict, "AttributeRouting/get-2", "AttributeRouting/get-2")]
    [InlineData(Loose, "AttributeRouting/get-2", "AttributeRouting/get-2")]
    [InlineData(Strict, "AttributeRouting/get-2/12345", "AttributeRouting/get-2/<redacted:12345>")]
    [InlineData(Loose, "AttributeRouting/get-2/12345", "AttributeRouting/get-2/<redacted:12345>")]
    [InlineData(Strict, "AttributeRouting/get-3", "AttributeRouting/get-3")]
    [InlineData(Loose, "AttributeRouting/get-3", "AttributeRouting/get-3")]
    [InlineData(Strict, "AttributeRouting/get-3/top10", "AttributeRouting/get-3/top10")]
    [InlineData(Loose, "AttributeRouting/get-3/top10", "AttributeRouting/get-3/top10")]
    [InlineData(Strict, "AttributeRouting/get-4/top10", $"AttributeRouting/get-4/{TelemetryConstants.Redacted}")]
    [InlineData(Loose, "AttributeRouting/get-4/top10", "AttributeRouting/get-4/top10")]
    [InlineData(Strict, "AttributeRouting/get-5/top10{x}12345", "AttributeRouting/get-5/top10%7Bx%7D<redacted:12345>")]
    [InlineData(Loose, "AttributeRouting/get-5/top10{x}12345", "AttributeRouting/get-5/top10%7Bx%7D<redacted:12345>")]
    [InlineData(Strict, "AttributeRouting/get-6/{c}/12345/top10", "AttributeRouting/get-6/%7Bc%7D/<redacted:12345>/top10")]
    [InlineData(Loose, "AttributeRouting/get-6/{c}/12345/top10", "AttributeRouting/get-6/%7Bc%7D/<redacted:12345>/top10")]

    // Mixed routing.
    [InlineData(Strict, "mixed/conventional-routing", "mixed/conventional-routing")]
    [InlineData(Loose, "mixed/conventional-routing", "mixed/conventional-routing")]
    [InlineData(Strict, "mixed/attribute-routing-1/12345", "mixed/attribute-routing-1/<redacted:12345>")]
    [InlineData(Loose, "mixed/attribute-routing-1/12345", "mixed/attribute-routing-1/<redacted:12345>")]
    [InlineData(Strict, "mixed/attribute-routing-2", "mixed/attribute-routing-2")]
    [InlineData(Loose, "mixed/attribute-routing-2", "mixed/attribute-routing-2")]
    [InlineData(Strict, "mixed/attribute-routing-2/12345", "mixed/attribute-routing-2/<redacted:12345>")]
    [InlineData(Loose, "mixed/attribute-routing-2/12345", "mixed/attribute-routing-2/<redacted:12345>")]
    [InlineData(Strict, "mixed/attribute-routing-3", "mixed/attribute-routing-3")]
    [InlineData(Loose, "mixed/attribute-routing-3", "mixed/attribute-routing-3")]
    [InlineData(Strict, "mixed/attribute-routing-3/top10", "mixed/attribute-routing-3/top10")]
    [InlineData(Loose, "mixed/attribute-routing-3/top10", "mixed/attribute-routing-3/top10")]
    [InlineData(Strict, "mixed/attribute-routing-4/test1234", $"mixed/attribute-routing-4/{TelemetryConstants.Redacted}")]
    [InlineData(Loose, "mixed/attribute-routing-4/test1234", "mixed/attribute-routing-4/test1234")]
    public async Task Routing_WithFormattedPath_RedactPath(
        HttpRouteParameterRedactionMode mode, string httpPath, string expectedHttpPath)
    {
        await RunRoutingTestAsync<TestStartupWithRouting>(
            httpPath,
            configureHttpLogging: services =>
            {
                services.AddHttpLoggingRedaction(o => o.RequestPathParameterRedactionMode = mode);
            },
            validateRequestState: state =>
            {
                Assert.Equal(expectedHttpPath, state[HttpLoggingTagNames.Path]);
            });
    }

    [Theory]

    // Conventional routing.
    [InlineData("", ConventionalRoutingController.Route, "ConventionalRouting", "Index", "")]
    [InlineData("ConventionalRouting", ConventionalRoutingController.Route, "ConventionalRouting", "Index", "")]
    [InlineData("ConventionalRouting/GetEntity/12345", ConventionalRoutingController.Route, "ConventionalRouting", "GetEntity", "<redacted:12345>")]

    // Attribute routing.
    [InlineData("AttributeRouting", "AttributeRouting", null, null, null)]
    [InlineData("AttributeRouting/all", "AttributeRouting/all", null, null, null)]
    [InlineData("AttributeRouting/get-1/12345", "AttributeRouting/get-1/{param:int:min(1)}", null, null, "<redacted:12345>")]
    [InlineData("AttributeRouting/get-2", "AttributeRouting/get-2/{param?}", null, null, "")]
    [InlineData("AttributeRouting/get-2/12345", "AttributeRouting/get-2/{param?}", null, null, "<redacted:12345>")]
    [InlineData("AttributeRouting/get-3", "AttributeRouting/get-3/{param=all}", null, null, "all")]
    [InlineData("AttributeRouting/get-3/top10", "AttributeRouting/get-3/{param=all}", null, null, "top10")]
    [InlineData("AttributeRouting/get-5/top10{x}12345", "AttributeRouting/get-5/{id}{{x}}{param}", null, null, "<redacted:12345>")]
    [InlineData("AttributeRouting/get-6/{c}/12345/top10", "AttributeRouting/get-6/{{c}}/{param}/{id}", null, null, "<redacted:12345>")]

    // Mixed routing.
    [InlineData("mixed/conventional-routing", "mixed/conventional-routing", null, null, null)]
    [InlineData("mixed/attribute-routing-1/12345", "mixed/attribute-routing-1/{param:int:min(1)}", null, null, "<redacted:12345>")]
    [InlineData("mixed/attribute-routing-2", "mixed/attribute-routing-2/{param?}", null, null, "")]
    [InlineData("mixed/attribute-routing-2/12345", "mixed/attribute-routing-2/{param?}", null, null, "<redacted:12345>")]
    [InlineData("mixed/attribute-routing-3", "mixed/attribute-routing-3/{param=all}", null, null, "all")]
    [InlineData("mixed/attribute-routing-3/top10", "mixed/attribute-routing-3/{param=all}", null, null, "top10")]
    public async Task Routing_WithStructuredPath_RedactParameters(
        string httpPath, string httpRoute, string? controller, string? action, string? param)
    {
        await RunRoutingTestAsync<TestStartupWithRouting>(
            httpPath,
            configureHttpLogging: services => services.AddHttpLoggingRedaction(options =>
            {
                options.RequestPathLoggingMode = IncomingPathLoggingMode.Structured;
            }),
            validateRequestState: state =>
            {
                Assert.Equal(httpRoute, state[HttpLoggingTagNames.Path]);

                if (controller != null)
                {
                    Assert.Equal(controller, state["controller"]);
                }

                if (action != null)
                {
                    Assert.Equal(action, state["action"]);
                }

                if (param == null)
                {
                    Assert.DoesNotContain("param", state.Keys);
                }
                else
                {
                    Assert.Equal(param, state["param"]);
                }
            });
    }
}
#endif
