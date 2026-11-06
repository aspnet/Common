// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;
using System.Net;

namespace Microsoft.Extensions.ServiceDiscovery.Dns.Resolver.Tests;

public class ResolvConfTests
{
    [Fact]
    public void GetServers()
    {
        var contents = @"
nameserver 10.96.0.10
search default.svc.cluster.local svc.cluster.local cluster.local
options ndots:5
@";

        var reader = new StringReader(contents);
        var servers = ResolvConf.GetServers(reader);

        IPEndPoint ipAddress = Assert.Single(servers);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("10.96.0.10"), 53), ipAddress);
    }

    [Fact]
    public void GetServers_TabSeparated()
    {
        var contents = "nameserver\t10.96.0.10\nnameserver \t 10.96.0.11\nnameserver 10.96.0.12\n";

        var reader = new StringReader(contents);
        var servers = ResolvConf.GetServers(reader);

        IPEndPoint[] expected =
        [
            new IPEndPoint(IPAddress.Parse("10.96.0.10"), 53),
            new IPEndPoint(IPAddress.Parse("10.96.0.11"), 53),
            new IPEndPoint(IPAddress.Parse("10.96.0.12"), 53),
        ];
        Assert.Equal(expected, servers);
    }
}
