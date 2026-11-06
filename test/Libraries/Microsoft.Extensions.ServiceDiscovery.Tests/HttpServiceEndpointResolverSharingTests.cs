// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery.Http;
using Xunit;

namespace Microsoft.Extensions.ServiceDiscovery.Tests;

public class HttpServiceEndpointResolverSharingTests
{
    [Fact]
    public void AddServiceDiscovery_HttpClient_SynchronousProviderDisposalDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("test").AddServiceDiscovery();

        using var provider = services.BuildServiceProvider();
        using var handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler("test");

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task AddServiceDiscoveryCore_RegistersResolverAsSingleton()
    {
        await using var services = new ServiceCollection()
            .AddServiceDiscoveryCore()
            .BuildServiceProvider();

        var first = services.GetRequiredService<HttpServiceEndpointResolver>();
        var second = services.GetRequiredService<HttpServiceEndpointResolver>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddServiceDiscovery_HttpClient_HandlerUsesSharedResolverSingleton()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("test").AddServiceDiscovery();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("test");
        var resolvingHandler = FindHandler<ResolvingHttpDelegatingHandler>(handler);
        Assert.NotNull(resolvingHandler);

        var resolverField = typeof(ResolvingHttpDelegatingHandler)
            .GetField("_resolver", BindingFlags.Instance | BindingFlags.NonPublic);
        var usedResolver = resolverField!.GetValue(resolvingHandler);

        Assert.Same(provider.GetRequiredService<HttpServiceEndpointResolver>(), usedResolver);
    }

    private static T? FindHandler<T>(HttpMessageHandler handler)
        where T : HttpMessageHandler
    {
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
