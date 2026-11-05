// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.AI;

public class HostedToolSearchToolTests
{
    [Fact]
    public void Constructor_Roundtrips()
    {
        var tool = new HostedToolSearchTool();
        Assert.Equal("tool_search", tool.Name);
        Assert.Empty(tool.Description);
        Assert.Empty(tool.AdditionalProperties);
        Assert.Equal(tool.Name, tool.ToString());
        Assert.Null(tool.DeferredTools);
        Assert.Null(tool.Namespace);
        Assert.Null(tool.NamespaceDescription);
    }

    [Fact]
    public void Constructor_AdditionalProperties_Roundtrips()
    {
        var props = new Dictionary<string, object?> { ["key"] = "value" };
        var tool = new HostedToolSearchTool(props);

        Assert.Equal("tool_search", tool.Name);
        Assert.Same(props, tool.AdditionalProperties);
    }

    [Fact]
    public void Constructor_NullAdditionalProperties_UsesEmpty()
    {
        var tool = new HostedToolSearchTool(null);

        Assert.Empty(tool.AdditionalProperties);
    }

    [Fact]
    public void DeferredTools_Roundtrips()
    {
        var tool = new HostedToolSearchTool
        {
            DeferredTools = ["GetWeather", "GetTime"]
        };

        Assert.NotNull(tool.DeferredTools);
        Assert.Equal(2, tool.DeferredTools.Count);
        Assert.Contains("GetWeather", tool.DeferredTools);
        Assert.Contains("GetTime", tool.DeferredTools);
    }

    [Fact]
    public void Namespace_Roundtrips()
    {
        var tool = new HostedToolSearchTool
        {
            Namespace = "my_tools"
        };

        Assert.Equal("my_tools", tool.Namespace);
    }

    [Fact]
    public void Namespace_DefaultsToNull()
    {
        var tool = new HostedToolSearchTool();
        Assert.Null(tool.Namespace);
    }

    [Fact]
    public void NamespaceDescription_Roundtrips()
    {
        var tool = new HostedToolSearchTool
        {
            NamespaceDescription = "Tools for managing my data."
        };

        Assert.Equal("Tools for managing my data.", tool.NamespaceDescription);
    }

    [Fact]
    public void NamespaceDescription_DefaultsToNull()
    {
        var tool = new HostedToolSearchTool();
        Assert.Null(tool.NamespaceDescription);
    }
}
