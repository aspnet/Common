// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Xunit;

namespace Microsoft.Extensions.Http.Diagnostics.Test;

public class HttpParserTests
{
    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_NullRouteParametersArray_ReturnsFalse(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "chatId", FakeTaxonomy.PrivateData } };

        string httpPath = "api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";

        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = null!;
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.False(success);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_RouteParametersArraySmallerThanActualParamCount_ReturnsFalse(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "chatId", FakeTaxonomy.PrivateData } };

        string httpPath = "api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";

        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[1];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.False(success);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    public void TryExtractParameters_InvalidHttpRouteParameterRedactionMode_Throws(int redactionMode)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };

        string httpPath = "routeId123/chats/chatId123/";
        string httpRoute = "{routeId}/chats/{chatId}/";
        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[2];
        var ex = Assert.Throws<InvalidOperationException>(
            () => httpParser.TryExtractParameters(httpPath, routeSegments, (HttpRouteParameterRedactionMode)redactionMode, parametersToRedact, ref httpRouteParameters));
        Assert.Equal(TelemetryCommonExtensions.UnsupportedEnumValueExceptionMessage, ex.Message);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_RouteHasFirstParameterToBeRedacted_ReturnsCorrectParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };

        string httpPath = "routeId123/chats/chatId123/";
        string httpRoute = "{routeId}/chats/{chatId}/";
        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[2];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", "chatId123", false);
        }

        // route begins with forward slash
        httpPath = "routeId123/chats/chatId123/";
        httpRoute = "/{routeId}/chats/{chatId}/";
        routeSegments = httpParser.ParseRoute(httpRoute);

        httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", "chatId123", false);
        }
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_NoParameters_ReturnEmptyArray(HttpRouteParameterRedactionMode redactionMode)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "routeId", FakeTaxonomy.PrivateData } };

        string httpPath = "users/chats/messages";
        string httpRoute = "users/chats/messages";
        var routeSegments = httpParser.ParseRoute(httpRoute);
        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[0];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.Empty(httpRouteParameters);

        httpPath = "/";
        httpRoute = "/";
        routeSegments = httpParser.ParseRoute(httpRoute);
        httpRouteParameters = new HttpRouteParameter[0];
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.Empty(httpRouteParameters);

        httpPath = "";
        httpRoute = "/";
        routeSegments = httpParser.ParseRoute(httpRoute);
        httpRouteParameters = new HttpRouteParameter[0];
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.Empty(httpRouteParameters);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_ReturnsExpectedParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "chatId", FakeTaxonomy.PrivateData } };

        // Route ends with text
        string httpPath = "api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";

        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[2];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", "routeId123", false);
        }

        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);

        // Route ends with parameter that needs to be redacted
        httpPath = "api/routes/routeId123/chats/chatId123/";
        httpRoute = "/api/routes/{routeId}/chats/{chatId}/";
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", "routeId123", false);
        }

        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);

        // Route ends with parameter that doesn't need to be redacted
        parametersToRedact.Add("routeId", FakeTaxonomy.PrivateData);
        parametersToRedact.Remove("chatId");
        httpPath = "api/routes/routeId123/chats/chatId123";
        httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[1], "chatId", "chatId123", false);
        }
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_WithSomeParametersWithPublicNonPersonalData_ReturnsExpectedParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "chatId", FakeTaxonomy.PrivateData } };

        // Route ends with text
        string httpPath = "api/routes/routeId123/chats/chatId123";
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";

        var routeSegments = httpParser.ParseRoute(httpRoute);

        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[2];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        if (redactionMode == HttpRouteParameterRedactionMode.Strict)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", TelemetryConstants.Redacted, true);
        }

        if (redactionMode == HttpRouteParameterRedactionMode.Loose)
        {
            ValidateRouteParameter(httpRouteParameters[0], "routeId", "routeId123", false);
        }

        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);

        // Route ends with parameter that needs to be redacted
        parametersToRedact.Add("routeId", DataClassification.None);
        httpPath = "api/routes/routeId123/chats/chatId123/";
        httpRoute = "/api/routes/{routeId}/chats/{chatId}/";
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", "routeId123", false);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);

        // Route ends with parameter that doesn't need to be redacted
        parametersToRedact.Remove("chatId");
        parametersToRedact.Add("chatId", DataClassification.None);
        httpPath = "api/routes/routeId123/chats/chatId123";
        httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", "routeId123", false);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", "chatId123", false);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_WhenRouteHasDefaultParameters_ReturnsExpectedParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new() { { "filter", FakeTaxonomy.PrivateData } };
        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[3];

        const string HttpRoute = "{controller=home}/{action=index}/{filter=all}";
        ParsedRouteSegments routeSegments = httpParser.ParseRoute(HttpRoute);

        // An http path includes well known "controller" and "action" parameters, and a parameter "filter".
        // Well known parameters are not redacted, a parameter with default value is redacted.
        string httpPath = "users/list/top10";

        bool success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "controller", "users", false);
        ValidateRouteParameter(httpRouteParameters[1], "action", "list", false);
        ValidateRouteParameter(httpRouteParameters[2], "filter", $"{redactedPrefix}top10", isRedacted);

        // An http path doesn't include some of the optional parameters.
        // Well known parameters are not redacted, a missing parameter with default value is not redacted.
        httpPath = "users";

        success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "controller", "users", false);
        ValidateRouteParameter(httpRouteParameters[1], "action", "index", false);
        ValidateRouteParameter(httpRouteParameters[2], "filter", "all", false);

        // An http path doesn't include all optional parameters.
        // Well known parameters are not redacted,
        // a missing parameter with default value is not redacted.
        httpPath = "";

        success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "controller", "home", false);
        ValidateRouteParameter(httpRouteParameters[1], "action", "index", false);
        ValidateRouteParameter(httpRouteParameters[2], "filter", "all", false);

        // A well known parameter is redacted when it is explicitly specified in an http path,
        // and is not redacted when it is omitted.
        parametersToRedact.Add("controller", FakeTaxonomy.PrivateData);
        parametersToRedact.Add("action", FakeTaxonomy.PrivateData);

        httpPath = "users";

        success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "controller", $"{redactedPrefix}users", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "action", "index", false);
        ValidateRouteParameter(httpRouteParameters[2], "filter", "all", false);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_WhenRouteHasOptionalsAndConstraints_ReturnsExpectedParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "routeId", FakeTaxonomy.PrivateData },
            { "chatId", FakeTaxonomy.PrivateData },
        };
        HttpRouteParameter[] httpRouteParameters = new HttpRouteParameter[2];

        const string HttpRoute = "api/routes/{routeId:int:min(1)}/chats/{chatId?}";
        ParsedRouteSegments routeSegments = httpParser.ParseRoute(HttpRoute);

        // An http path includes a parameter with a constraint and an optional parameter.
        // Both parameters are redacted.
        string httpPath = "api/routes/routeId123/chats/chatId123";

        bool success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);

        // An http path includes a parameter with a constraint, an optional parameter is not provided.
        // The parameter with constraint is redacted, the optional parameter isn't.
        httpPath = "api/routes/routeId123/chats";

        success = httpParser.TryExtractParameters(
            httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);
        Assert.True(success);

        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", "", false);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_WhenRouteHasCatchAllParameter_ReturnsCorrectParameters(
        bool routeHasMessageSegment,
        bool roundTripSyntax,
        HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "routeId", FakeTaxonomy.PrivateData },
            { "chatId", FakeTaxonomy.PrivateData },
            { "catchAll", FakeTaxonomy.PrivateData },
        };

        string httpPath = "api/routes/routeId123/chats/chatId123/messages/1/2/3/";

        var paramName = "*catchAll";
        if (roundTripSyntax)
        {
            paramName = "**catchAll";
        }

        var expectedValue = "messages/1/2/3/";
        var segment = string.Empty;
        if (routeHasMessageSegment)
        {
            segment = "/messages";
            expectedValue = "1/2/3/";
        }

        string httpRoute = $"api/routes/{{routeId}}/chats/{{chatId}}{segment}/{{{paramName}}}/";

        var routeSegments = httpParser.ParseRoute(httpRoute);
        var httpRouteParameters = new HttpRouteParameter[3];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);

        Assert.True(success);
        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[2], "catchAll", $"{redactedPrefix}{expectedValue}", isRedacted);
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_WhenRouteHasCatchAllParameter_Optional_ReturnsCorrectParameters(
        bool routeHasDefaultValue,
        bool useRoundTripSyntax,
        HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "routeId", FakeTaxonomy.PrivateData },
            { "chatId", FakeTaxonomy.PrivateData },
            { "catchAll", FakeTaxonomy.PrivateData },
        };

        var httpPath = "api/routes/routeId123/chats/chatId123";

        var paramName = "*catchAll";
        if (useRoundTripSyntax)
        {
            paramName = "**catchAll";
        }

        var expectedValue = string.Empty;
        if (routeHasDefaultValue)
        {
            expectedValue = nameof(routeHasDefaultValue);
            paramName += $"={expectedValue}";
        }

        var httpRoute = $"api/routes/{{routeId}}/chats/{{chatId}}/{{{paramName}}}";

        var routeSegments = httpParser.ParseRoute(httpRoute);
        var httpRouteParameters = new HttpRouteParameter[3];
        var success = httpParser.TryExtractParameters(httpPath, routeSegments, redactionMode, parametersToRedact, ref httpRouteParameters);

        Assert.True(success);
        ValidateRouteParameter(httpRouteParameters[0], "routeId", $"{redactedPrefix}routeId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "chatId", $"{redactedPrefix}chatId123", isRedacted);
        ValidateRouteParameter(httpRouteParameters[2], "catchAll", expectedValue, false);
    }

    [Fact]
    public void ParseRoute_WithRouteParameter_ReturnsRouteSegments()
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        // An http route has parameters and ends with a parameter.
        string httpRoute = "/api/routes/{routeId}/chats/{chatId}";
        var routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(4, routeSegments.Segments.Length);
        Assert.Equal("api/routes/{routeId}/chats/{chatId}", routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/routes/", false, "", "", 0, 11, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("routeId", true, "routeId", "", 11, 20, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/chats/", false, "", "", 20, 27, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("chatId", true, "chatId", "", 27, 35, false));

        // An http route has parameters and ends with text.
        httpRoute = "/api/routes/{routeId}/chats/{chatId}/messages";
        routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(5, routeSegments.Segments.Length);
        Assert.Equal("api/routes/{routeId}/chats/{chatId}/messages", routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/routes/", false, "", "", 0, 11, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("routeId", true, "routeId", "", 11, 20, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/chats/", false, "", "", 20, 27, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("chatId", true, "chatId", "", 27, 35, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/messages", false, "", "", 35, 44, false));
    }

    [Fact]
    public void ParseRoute_WithQueryParameter_ReturnRouteSegmentExcludingQueryParams()
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        string httpRoute = "/api/routes/{routeId}/chats/{chatId}/messages?from=7";
        var routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(5, routeSegments.Segments.Length);
        Assert.Equal("api/routes/{routeId}/chats/{chatId}/messages", routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/routes/", false, "", "", 0, 11, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("routeId", true, "routeId", "", 11, 20, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/chats/", false, "", "", 20, 27, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("chatId", true, "chatId", "", 27, 35, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/messages", false, "", "", 35, 44, false));

        // Route doesn't start with forward slash, the final result should begin with forward slash.
        httpRoute = "api/routes/{routeId}/chats/{chatId}/messages?from=7";
        routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(5, routeSegments.Segments.Length);
        Assert.Equal("api/routes/{routeId}/chats/{chatId}/messages", routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/routes/", false, "", "", 0, 11, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("routeId", true, "routeId", "", 11, 20, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/chats/", false, "", "", 20, 27, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("chatId", true, "chatId", "", 27, 35, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/messages", false, "", "", 35, 44, false));
    }

    [Fact]
    public void ParseRoute_WhenRouteHasDefaultsOptionalsConstraints_ReturnsRouteSegments()
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        string httpRoute = "api/{controller=home}/{action=index}/{routeId:int:min(1)}/{chatId?}";
        ParsedRouteSegments routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(8, routeSegments.Segments.Length);
        Assert.Equal(httpRoute, routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/", false, "", "", 0, 4, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("controller=home", true, "controller", "home", 4, 21, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/", false, "", "", 21, 22, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("action=index", true, "action", "index", 22, 36, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/", false, "", "", 36, 37, false));
        ValidateRouteSegment(routeSegments.Segments[5], ("routeId:int:min(1)", true, "routeId", "", 37, 57, false));
        ValidateRouteSegment(routeSegments.Segments[6], ("/", false, "", "", 57, 58, false));
        ValidateRouteSegment(routeSegments.Segments[7], ("chatId?", true, "chatId", "", 58, 67, false));
    }

    [Theory]
    [InlineData("api/{controller=home}/{action=index}/{*url}/{invalid}")]
    [InlineData("api/{controller=home}/{action=index}/{**url}/{invalid}")]
    public void ParseRoute_WhenRouteHasCatchAllParameter_OutOfOrder(string httpRoute)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        var exception = Assert.Throws<ArgumentException>(() => httpParser.ParseRoute(httpRoute));

        Assert.StartsWith("A catch-all parameter must be the last segment in the route.", exception.Message);
    }

    [Theory]
    [InlineData("api/{controller=home}/{action=index}/{*url}")]
    [InlineData("api/{controller=home}/{action=index}/{*url}/")]
    [InlineData("api/{controller=home}/{action=index}/{**url}")]
    [InlineData("api/{controller=home}/{action=index}/{**url}/")]
    public void ParseRoute_WhenRouteHasCatchAllParameter_InCorrectPosition(string httpRoute)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        ParsedRouteSegments routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(3, routeSegments.ParameterCount);
        Assert.Equal(httpRoute, routeSegments.RouteTemplate);
    }

    [Theory]
    [InlineData("api/{controller=home}/{action=index}/{*url}", 37, 43)]
    [InlineData("api/{controller=home}/{action=index}/{**url}", 37, 44)]
    public void ParseRoute_WhenRouteHasCatchAllParameter_ReturnsRouteSegments(string httpRoute, int start, int end)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        ParsedRouteSegments routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(6, routeSegments.Segments.Length);
        Assert.Equal(httpRoute, routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/", false, "", "", 0, 4, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("controller=home", true, "controller", "home", 4, 21, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/", false, "", "", 21, 22, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("action=index", true, "action", "index", 22, 36, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/", false, "", "", 36, 37, false));
        ValidateRouteSegment(routeSegments.Segments[5], ("url", true, "url", "", start, end, true));
    }

    [Theory]
    [InlineData("api/{controller=home}/{action=index}/{*url:int:min(1)}", 37, 54)]
    [InlineData("api/{controller=home}/{action=index}/{**url:int:min(1)}", 37, 55)]
    public void ParseRoute_WhenRouteHasCatchAllParameterWithRouteConstraint_ReturnsRouteSegments(string httpRoute, int start, int end)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        ParsedRouteSegments routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(6, routeSegments.Segments.Length);
        Assert.Equal(httpRoute, routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/", false, "", "", 0, 4, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("controller=home", true, "controller", "home", 4, 21, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/", false, "", "", 21, 22, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("action=index", true, "action", "index", 22, 36, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/", false, "", "", 36, 37, false));
        ValidateRouteSegment(routeSegments.Segments[5], ("url:int:min(1)", true, "url", "", start, end, true));
    }

    [Theory]
    [InlineData("api/{controller=home}/{action=index}/{*url:regex(^(web|shared*)$)}", 37, 66)]
    [InlineData("api/{controller=home}/{action=index}/{**url:regex(^(web|shared*)$)}", 37, 67)]
    public void ParseRoute_WhenRouteHasCatchAllParameterWithRouteConstraintContainingRegexWithStar_ReturnsRouteSegments(string httpRoute, int start, int end)
    {
        HttpRouteParser httpParser = CreateHttpRouteParser();

        ParsedRouteSegments routeSegments = httpParser.ParseRoute(httpRoute);

        Assert.Equal(6, routeSegments.Segments.Length);
        Assert.Equal(httpRoute, routeSegments.RouteTemplate);

        ValidateRouteSegment(routeSegments.Segments[0], ("api/", false, "", "", 0, 4, false));
        ValidateRouteSegment(routeSegments.Segments[1], ("controller=home", true, "controller", "home", 4, 21, false));
        ValidateRouteSegment(routeSegments.Segments[2], ("/", false, "", "", 21, 22, false));
        ValidateRouteSegment(routeSegments.Segments[3], ("action=index", true, "action", "index", 22, 36, false));
        ValidateRouteSegment(routeSegments.Segments[4], ("/", false, "", "", 36, 37, false));
        ValidateRouteSegment(routeSegments.Segments[5], ("url:regex(^(web|shared*)$)", true, "url", "", start, end, true));
    }

    [Theory]
    [CombinatorialData]
    public void TryExtractParameters_ParameterValueContainsFollowingLiteral_ReturnsCorrectParameters(HttpRouteParameterRedactionMode redactionMode)
    {
        bool isRedacted = redactionMode != HttpRouteParameterRedactionMode.None;
        string redactedPrefix = isRedacted ? "Redacted:" : string.Empty;

        HttpRouteParser httpParser = CreateHttpRouteParser();
        Dictionary<string, DataClassification> parametersToRedact = new()
        {
            { "token", FakeTaxonomy.PrivateData },
            { "format", DataClassification.None },
        };

        var httpRouteParameters = new HttpRouteParameter[2];

        Assert.True(httpParser.TryExtractParameters(
            "/b/json.secret", httpParser.ParseRoute("/b/{format}.{token}"), redactionMode, parametersToRedact, ref httpRouteParameters));
        ValidateRouteParameter(httpRouteParameters[0], "format", "json", false);
        ValidateRouteParameter(httpRouteParameters[1], "token", $"{redactedPrefix}secret", isRedacted);

        Assert.True(httpParser.TryExtractParameters(
            "/a/part1.part2.part3.json", httpParser.ParseRoute("/a/{token}.{format}"), redactionMode, parametersToRedact, ref httpRouteParameters));
        ValidateRouteParameter(httpRouteParameters[0], "token", $"{redactedPrefix}part1.part2.part3", isRedacted);
        ValidateRouteParameter(httpRouteParameters[1], "format", "json", false);

        Assert.True(httpParser.TryExtractParameters(
            "/c/json.secret.ZIP", httpParser.ParseRoute("/c/{format}.{token}.zip"), redactionMode, parametersToRedact, ref httpRouteParameters));
        ValidateRouteParameter(httpRouteParameters[0], "format", "json", false);
        ValidateRouteParameter(httpRouteParameters[1], "token", $"{redactedPrefix}secret", isRedacted);
    }

    [Theory]
    [InlineData("{token}.{format}", "part1.part2.part3.json", "token", "part1.part2.part3")]
    [InlineData("{format}.{token}", "json.secret", "format", "json")]
    [InlineData("{token}.json", "part1.part2.json", "token", "part1.part2")]
    [InlineData("{token}.json", "part1.part2.JSON", "token", "part1.part2")]
    [InlineData("{token}.{format}.zip", "part1.part2.json.zip", "token", "part1.part2")]
    [InlineData("{token}.json/{format}", "part1.part2.json/xml", "token", "part1.part2")]
    [InlineData("{token}.{format}/rest", "part1.part2.json/rest", "token", "part1.part2")]
    [InlineData("{a}.{b}.{c}", "p.q.r.s", "a", "p.q")]
    [InlineData("{a}.{b}.{c}", "r.s", "b", "r")]
    public void GetParameterLength_ParameterFollowedByLiteral_ReturnsLengthOfValue(string httpRoute, string httpPath, string parameterName, string expectedValue)
    {
        var segments = CreateHttpRouteParser().ParseRoute(httpRoute).Segments;
        var index = Array.FindIndex(segments, segment => segment.ParamName == parameterName);

        var length = HttpRouteParser.GetParameterLength(httpPath.AsSpan(), segments, index);

        Assert.Equal(expectedValue, length >= 0 ? httpPath.Substring(0, length) : null);
    }

    // -1 means the helper doesn't apply or the literals don't match, and the callers use their existing logic,
    // e.g. "{id}/rest" with "5/rest" still gives id = "5".
    [Theory]
    [InlineData("{token}.{format}", "json", "format")] // Last segment.
    [InlineData("{a}{b}", "xy", "a")] // Followed by a parameter.
    [InlineData("{id}/rest", "5/rest", "id")] // The parameter is the whole path segment.
    [InlineData("{token}.{format}", "abc", "token")] // Literal not found.
    [InlineData("{id}.{format?}", "5", "id")] // No room for the literal and the following value.
    public void GetParameterLength_NotApplicableOrNotMatched_ReturnsMinusOne(string httpRoute, string httpPath, string parameterName)
    {
        var segments = CreateHttpRouteParser().ParseRoute(httpRoute).Segments;
        var index = Array.FindIndex(segments, segment => segment.ParamName == parameterName);

        Assert.Equal(-1, HttpRouteParser.GetParameterLength(httpPath.AsSpan(), segments, index));
    }

    [Fact]
    public void AddHttpRouteProcessor_ParserAndFormatterInstanceAdded()
    {
        var sp = new ServiceCollection().AddHttpRouteProcessor().AddFakeRedaction().BuildServiceProvider();

        var httpRouteParser = sp.GetRequiredService<IHttpRouteParser>();
        var httpRouteFormatter = sp.GetRequiredService<IHttpRouteFormatter>();

        Assert.NotNull(httpRouteParser);
        Assert.NotNull(httpRouteFormatter);
    }

    private static HttpRouteParser CreateHttpRouteParser()
    {
        var redactorProvider = new FakeRedactorProvider(
            new FakeRedactorOptions { RedactionFormat = "Redacted:{0}" });
        return new HttpRouteParser(redactorProvider);
    }

    private static void ValidateRouteParameter(
        HttpRouteParameter parameter, string name, string value, bool isRedacted)
    {
        Assert.Equal(name, parameter.Name);
        Assert.Equal(value, parameter.Value);
        Assert.Equal(isRedacted, parameter.IsRedacted);
    }

    private static void ValidateRouteSegment(
        Segment segment, (string content, bool isParam, string paramName, string defaultValue, int start, int end, bool isCatchAll) values)
    {
        var (content, isParam, paramName, defaultValue, start, end, isCatchAll) = values;

        Assert.Equal(content, segment.Content);
        Assert.Equal(isParam, segment.IsParam);
        Assert.Equal(paramName, segment.ParamName);
        Assert.Equal(defaultValue, segment.DefaultValue);
        Assert.Equal(start, segment.Start);
        Assert.Equal(end, segment.End);
        Assert.Equal(isCatchAll, segment.IsCatchAll);
    }
}
