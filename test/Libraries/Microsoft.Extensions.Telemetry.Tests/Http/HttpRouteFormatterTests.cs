// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.Http.Diagnostics;
using Xunit;

namespace Microsoft.Extensions.Http.Diagnostics.Test;

public class HttpRouteFormatterTests
{
    [Theory]
    [CombinatorialData]
    public void Format_WithEmptyParametersToRedact_ReturnsOriginalPath(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        var parametersToRedact = new Dictionary<string, DataClassification>();

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/{TelemetryConstants.Redacted}/chats/{TelemetryConstants.Redacted}", formattedPath);
        }
        else
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }

        httpPath = "api/routes/routeId123/chats/chatId123";
        httpRoute = "api/routes/{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/{TelemetryConstants.Redacted}/chats/{TelemetryConstants.Redacted}", formattedPath);
        }
        else
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }

        httpPath = "api/routes/routeId123/chats/chatId123";
        httpRoute = "api/routes/{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/{TelemetryConstants.Redacted}/chats/{TelemetryConstants.Redacted}", formattedPath);
        }
        else
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }

        httpPath = "/api/chats:chatId123@routeId123";
        httpRoute = "/api/chats:{chatId}@{routeId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/chats:{TelemetryConstants.Redacted}@{TelemetryConstants.Redacted}", formattedPath);
        }
        else
        {
            Assert.Equal($"api/chats:chatId123@routeId123", formattedPath);
        }

        httpPath = "/api/chats:chatId123@routeId123/messages";
        httpRoute = "/api/chats:{chatId}@{routeId}/messages";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/chats:{TelemetryConstants.Redacted}@{TelemetryConstants.Redacted}/messages", formattedPath);
        }
        else
        {
            Assert.Equal($"api/chats:chatId123@routeId123/messages", formattedPath);
        }
    }

    [Theory]
    [CombinatorialData]
    public void Format_NoParameterRoute_WithParametersToRedact_ReturnsOriginalpath(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "v1", FakeTaxonomy.PrivateData }
        };

        string httpPath = "/api/v1/chats";
        string httpRoute = "/api/v1/chats";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal(httpPath.TrimStart('/'), formattedPath);

        // http path doesn't begin with / while route begins with /
        httpPath = "api/v1/chats";
        httpRoute = "/api/v1/chats";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal(httpPath.TrimStart('/'), formattedPath);

        // route doesn't begin with / while http path begins with /
        httpPath = "/api/v1/chats";
        httpRoute = "api/v1/chats";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal(httpPath.TrimStart('/'), formattedPath);

        // empty route
        httpPath = "/";
        httpRoute = "/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal(httpPath.TrimStart('/'), formattedPath);

        // empty route
        httpPath = "";
        httpRoute = "/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal(httpPath.TrimStart('/'), formattedPath);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Format_WithParametersToRedact_GivenInvalidHttpRouteParameterRedactionMode_Throws(int redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "routeId", FakeTaxonomy.PrivateData }
        };

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => httpFormatter.Format(httpRoute, httpPath, (HttpRouteParameterRedactionMode)redactionMode, parametersToRedact));
        Assert.Equal(TelemetryCommonExtensions.UnsupportedEnumValueExceptionMessage, ex.Message);
    }

    [Theory]
    [CombinatorialData]
    public void Format_WithParametersToRedact_ReturnsPathWithSensitiveParamsRedacted(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "routeId", FakeTaxonomy.PrivateData }
        };

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/Redacted:routeId123/chats/{TelemetryConstants.Redacted}", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            Assert.Equal($"api/routes/Redacted:routeId123/chats/chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }

        parametersToRedact.Add("chatId", FakeTaxonomy.PrivateData);
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123/chats/{redactedPrefix}chatId123", formattedPath);

        // path doesn't begin with forward slash, route does
        httpPath = "api/routes/routeId123/chats/chatId123";
        httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123/chats/{redactedPrefix}chatId123", formattedPath);

        // route doesn't begin with forward slash, path does
        httpPath = "/api/routes/routeId123/chats/chatId123";
        httpRoute = "api/routes/{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123/chats/{redactedPrefix}chatId123", formattedPath);

        // route has no parameters that needs redaction
        parametersToRedact.Remove("routeId");
        parametersToRedact.Remove("chatId");
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/{TelemetryConstants.Redacted}/chats/{TelemetryConstants.Redacted}", formattedPath);
        }
        else
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }
    }

    [Theory]
    [CombinatorialData]
    public void Format_WithParametersToRedact_DataClassPublicNonPersonalData_DoesNotRedactParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", DataClassification.None },
            { "routeId", DataClassification.None },
            { "chatId", DataClassification.None },
        };

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_WithParametersToRedact_PublicNonPersonalData_NotRedacted(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "routeId", DataClassification.None },
            { "chatId", FakeTaxonomy.PrivateData },
        };

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/routeId123/chats/{redactedPrefix}chatId123", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasFirstParameterToBeRedacted_ReturnsCorrectlyRedactedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };

        string httpPath = "routeId123/chats/chatId123";
        string httpRoute = "{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"Redacted:routeId123/chats/{TelemetryConstants.Redacted}", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            Assert.Equal($"Redacted:routeId123/chats/chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal($"routeId123/chats/chatId123", formattedPath);
        }

        // path begins with forward slash, route doesn't
        httpPath = "/routeId123/chats/chatId123";
        httpRoute = "{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"Redacted:routeId123/chats/{TelemetryConstants.Redacted}", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            Assert.Equal($"Redacted:routeId123/chats/chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal($"routeId123/chats/chatId123", formattedPath);
        }

        // route begins with forward slash, path doesn't
        httpPath = "routeId123/chats/chatId123";
        httpRoute = "/{routeId}/chats/{chatId}";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"Redacted:routeId123/chats/{TelemetryConstants.Redacted}", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            Assert.Equal($"Redacted:routeId123/chats/chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal($"routeId123/chats/chatId123", formattedPath);
        }
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasLastParameterToBeRedacted_ReturnsCorrectlyRedactedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "chatId", FakeTaxonomy.PrivateData } };

        string httpPath = "/api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            Assert.Equal($"api/routes/{TelemetryConstants.Redacted}/chats/Redacted:chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            Assert.Equal($"api/routes/routeId123/chats/Redacted:chatId123", formattedPath);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal($"api/routes/routeId123/chats/chatId123", formattedPath);
        }
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasDefaultParametersToBeRedacted_ReturnsCorrectlyRedactedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        string redactedPrefix = redactionMode == HttpRouteParameterRedactionMode.None ? string.Empty : "Redacted:";

        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };
        string httpRoute = "/api/routes/{routeId=defaultRoute}";

        // A default parameter is redacted when it is explicitly specified.
        string httpPath = "/api/routes/routeId123";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123", formattedPath);

        // An http path is correctly formatted if a default parameter is omitted.
        httpPath = "/api/routes";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("api/routes", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasWellKnownParameters_ReturnsCorrectlyFormattedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        string redactedPrefix = redactionMode == HttpRouteParameterRedactionMode.None ? string.Empty : "Redacted:";
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "filter", FakeTaxonomy.PrivateData } };
        string httpRoute = "{controller=home}/{action=index}/{filter=all}";

        // An http path includes well known "controller" and "action" parameters, and a parameter "filter".
        // Well known parameters are not redacted, a parameter with default value is redacted.
        string httpPath = "users/list/top10";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"users/list/{redactedPrefix}top10", formattedPath);

        // An http path doesn't include an optional parameter with a default value.
        // Well known parameters are not redacted.
        httpPath = "users/list";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("users/list", formattedPath);

        // An http path includes only one optional well known parameter.
        // Well known parameters are not redacted.
        httpPath = "users";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("users", formattedPath);

        // An http path doesn't include all optional parameters.
        httpPath = "";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("", formattedPath);

        // A well known parameter is redacted when it is explicitly specified in an http path,
        // and is not redacted when it is omitted.
        parametersToRedact.Add("controller", FakeTaxonomy.PrivateData);
        parametersToRedact.Add("action", FakeTaxonomy.PrivateData);

        httpPath = "users";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"{redactedPrefix}users", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasOptionalParametersToBeRedacted_ReturnsCorrectlyRedactedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        string redactedPrefix = redactionMode == HttpRouteParameterRedactionMode.None ? string.Empty : "Redacted:";
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };
        string httpRoute = "/api/routes/{routeId?}";

        // An optional parameter is redacted when it is explicitly specified.
        string httpPath = "/api/routes/routeId123";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123", formattedPath);

        // An http path is correctly formatted if an optional parameter is omitted.
        httpPath = "/api/routes";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("api/routes", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasParametersWithConstraintsToBeRedacted_ReturnsCorrectlyRedactedPath(HttpRouteParameterRedactionMode redactionMode)
    {
        string redactedPrefix = redactionMode == HttpRouteParameterRedactionMode.None ? string.Empty : "Redacted:";
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };

        string httpRoute = "/api/routes/{routeId:int:min(1)}";
        string httpPath = "/api/routes/routeId123";

        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123", formattedPath);
    }

    [Theory]
    [CombinatorialData]
    public void Format_HttpPathMayHaveTrailingSlash_FormattedHttpPathDoNotHaveTrailingSlash(HttpRouteParameterRedactionMode redactionMode)
    {
        string redactedPrefix = redactionMode == HttpRouteParameterRedactionMode.None ? string.Empty : "Redacted:";

        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };
        string httpRoute = "/api/routes/static_route";

        // An http route is static.
        string httpPath = "/api/routes/static_route";
        string formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/static_route", formattedPath);

        httpPath = "/api/routes/static_route/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/static_route", formattedPath);

        // An http route ends with a slash and has an optional parameter which may be omitted.
        httpRoute = "/api/routes/{routeId?}/";

        httpPath = "/api/routes/routeId123";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123", formattedPath);

        httpPath = "/api/routes/routeId123/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal($"api/routes/{redactedPrefix}routeId123", formattedPath);

        httpPath = "/api/routes";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("api/routes", formattedPath);

        httpPath = "/api/routes/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("api/routes", formattedPath);

        // All segments of an http route are omitted.
        // The formatted http path is always an empty string.
        httpRoute = "{controller=home}/{action=index}";

        httpPath = "/";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("", formattedPath);

        httpPath = "";
        formattedPath = httpFormatter.Format(httpRoute, httpPath, redactionMode, parametersToRedact);
        Assert.Equal("", formattedPath);
    }

    [Theory]
    [InlineData("/a/{token}.{format}", "/a/part1.part2.part3.json", "a/[REDACTED].json")]
    [InlineData("/a/{token}.{format}.zip", "/a/part1.part2.json.zip", "a/[REDACTED].json.zip")]
    [InlineData("/a/{token}.json/{format}", "/a/part1.part2.json/xml", "a/[REDACTED].json/xml")]
    [InlineData("/a/{token}.{format}.zip", "/a/part1.part2.json.ZIP", "a/[REDACTED].json.zip")]
    public void Format_ParameterValueContainsFollowingLiteral_RedactsWholeValue(string httpRoute, string httpPath, string expectedPath)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter("[REDACTED]");
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "token", FakeTaxonomy.PrivateData },
            { "format", DataClassification.None },
        };

        Assert.Equal(expectedPath, httpFormatter.Format(httpRoute, httpPath, HttpRouteParameterRedactionMode.Strict, parametersToRedact));
        Assert.Equal(expectedPath, httpFormatter.Format(httpRoute, httpPath, HttpRouteParameterRedactionMode.Loose, parametersToRedact));
    }

    [Theory]
    [InlineData("api/files/{*path}", "api/files/a/b/c", "api/files/Redacted:a/b/c")]
    [InlineData("api/files/{**path}", "api/files/a/b/c", "api/files/Redacted:a/b/c")]
    [InlineData("api/files/{*path}/", "api/files/a/b/c", "api/files/Redacted:a/b/c")]
    [InlineData("api/{userId}/files/{*path}", "api/user1/files/a/b/c", "api/Redacted:user1/files/Redacted:a/b/c")]
    public void Format_RouteHasCatchAllParameter_RedactsWholeValue(string httpRoute, string httpPath, string expectedPath)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "path", FakeTaxonomy.PrivateData },
        };

        Assert.Equal(expectedPath, httpFormatter.Format(httpRoute, httpPath, HttpRouteParameterRedactionMode.Strict, parametersToRedact));
        Assert.Equal(expectedPath, httpFormatter.Format(httpRoute, httpPath, HttpRouteParameterRedactionMode.Loose, parametersToRedact));
    }

    [Theory]
    [CombinatorialData]
    public void Format_RouteHasCatchAllParameter_KeepsWholeValue(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteFormatter httpFormatter = CreateHttpRouteFormatter("[REDACTED]");
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "userId", FakeTaxonomy.PrivateData },
            { "path", DataClassification.None },
        };

        string formattedPath = httpFormatter.Format("api/{userId}/files/{*path}", "api/user1/files/a/b/c", redactionMode, parametersToRedact);

        if (redactionMode == HttpRouteParameterRedactionMode.None)
        {
            Assert.Equal("api/user1/files/a/b/c", formattedPath);
        }
        else
        {
            Assert.Equal("api/[REDACTED]/files/a/b/c", formattedPath);
        }
    }

    private static HttpRouteFormatter CreateHttpRouteFormatter(string redactionFormat = "Redacted:{0}")
    {
        var redactorProvider = new FakeRedactorProvider(
            new FakeRedactorOptions { RedactionFormat = redactionFormat });
        var httpParser = new HttpRouteParser(redactorProvider);
        return new HttpRouteFormatter(httpParser, redactorProvider);
    }
}
