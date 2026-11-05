// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Testing.Test.TestResources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Testing.Test;

[SuppressMessage("Reliability", "CA2000", Justification = "HttpClient shouldn't be deliberately disposed.")]
public class FakesExtensionsTests
{
    private static readonly string[] _urlAddresses = { "https://first.com/", "https://second.com/", "https://third.com/" };

    [Fact]
    public async Task UseTestStartup_UsesFakeStartup()
    {
        using var host = FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.UseFakeStartup().ListenHttpOnAnyPort())
            .Build();

        Assert.Null(await Record.ExceptionAsync(() => host.StartAsync()));
    }

    [Fact]
    public async Task ListenHttpOnAnyPort_AddsListener()
    {
        using var host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.ListenHttpOnAnyPort().UseFakeStartup())
            .StartAsync();

        Assert.Null(Record.Exception(() => host.GetListenUris()));
    }

    [Fact]
    public async Task ListenHttpsOnAnyPort_WithoutCertificate_CertificateProvided()
    {
        using var host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.UseStartup<Startup>().ListenHttpsOnAnyPort())
            .StartAsync();

        var certificate = host.Services.GetRequiredService<IOptions<FakeCertificateOptions>>().Value.Certificate;

        Assert.NotNull(certificate);

        var client = new HttpClient(new FakeCertificateHttpClientHandler(certificate))
        {
            BaseAddress = host.GetListenUris().First()
        };

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task ListenHttpsOnAnyPort_WithCertificate_UsesTheCertificate()
    {
        var certificate = FakeSslCertificateFactory.CreateSslCertificate();

        using var host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.UseStartup<Startup>().ListenHttpsOnAnyPort(certificate))
            .StartAsync();

        var client = host.CreateClient(new FakeCertificateHttpClientHandler(certificate));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task CreateClient_HandlerGiven_UsesTheHandler()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        using var handler = new ReturningHttpClientHandler();
        using var client = hostMock.Object.CreateClient(handler);
        using var response = await client.SendAsync(new HttpRequestMessage());

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public void CreateClient_NoAddressFilter_UseFirstAddress()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        using var client = hostMock.Object.CreateClient();

        Assert.Equal(_urlAddresses[0], client.BaseAddress?.AbsoluteUri);
    }

    [Fact]
    public void CreateClient_NoAddress_Throws()
    {
        var hostMock = CreateHostMock();

        var exception = Record.Exception(() => hostMock.Object.CreateClient(null, _ => false));

        Assert.NotNull(exception);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("No suitable address found to call the server.", exception.Message);
    }

    [Fact]
    public void CreateClient_NoSuitableAddress_Throws()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        var exception = Record.Exception(() => hostMock.Object.CreateClient(null, _ => false));

        Assert.NotNull(exception);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("No suitable address found to call the server.", exception.Message);
    }

    [Fact]
    public void CreateClient_NoHost_Throws()
    {
        var exception = Record.Exception(() => ((IHost)null!).CreateClient(new TestHandler(), _ => true));
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void CreateClient_NoServer_Throws()
    {
        var hostMock = CreateHostMock(_urlAddresses);
        var services = Mock.Get(hostMock.Object.Services);
        services.Setup(x => x.GetService(typeof(IServer))).Returns(null);

        var exception = Record.Exception(() => hostMock.Object.CreateClient(new TestHandler(), _ => true));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void CreateClient_AddressFilterGiven_UseFirstAddressPassingFilter()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        using var client = hostMock.Object.CreateClient(null, x => x.AbsoluteUri == _urlAddresses[1]);

        Assert.Equal(_urlAddresses[1], client.BaseAddress?.AbsoluteUri);
    }

    [Fact]
    public async Task CreateClient_UsingHttpsWithoutCertificate_CreatesCertificateAndMakesItWorking()
    {
        using var host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.UseStartup<Startup>().ListenHttpsOnAnyPort())
            .StartAsync();

        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task CreateClient_UsingHttp_CreatesClient()
    {
        using var host = await FakeHost.CreateBuilder()
            .ConfigureWebHost(webHost => webHost.UseStartup<Startup>().ListenHttpOnAnyPort())
            .StartAsync();

        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public void GetListenUris_NoServer_Throws()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        var services = Mock.Get(hostMock.Object.Services);
        services.Setup(x => x.GetService(typeof(IServer))).Returns(null);

        var exception = Record.Exception(() => hostMock.Object.GetListenUris());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void GetListenUris_NoAddressesFeatureInServer_Throws()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        hostMock.Object.Services.GetRequiredService<IServer>().Features[typeof(IServerAddressesFeature)] = null;

        var services = Mock.Get(hostMock.Object.Services);
        services.Setup(x => x.GetService(typeof(IServer))).Returns(null);

        var exception = Record.Exception(() => hostMock.Object.GetListenUris());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void GetUri_NoHost_Throws()
    {
        var exception = Record.Exception(() => ((IHost)null!).GetListenUris());
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void GetListenUris_PassesAddresses()
    {
        var hostMock = CreateHostMock(_urlAddresses);

        Assert.Collection(hostMock.Object.GetListenUris(),
            address => Assert.Equal(address.AbsoluteUri, _urlAddresses[0]),
            address => Assert.Equal(address.AbsoluteUri, _urlAddresses[1]),
            address => Assert.Equal(address.AbsoluteUri, _urlAddresses[2]));
    }

    [Fact]
    public void GetListenUris_ReplacesEmptyAddressWithLocalhost()
    {
        var hostMock = CreateHostMock("https://[::]");
        Assert.StartsWith("https://localhost", hostMock.Object.GetListenUris().Single().AbsoluteUri);
    }

    private static Mock<IHost> CreateHostMock(params string[] addresses)
    {
        var addressesFeature = new ServerAddressesFeature();

        foreach (var address in addresses)
        {
            addressesFeature.Addresses.Add(address);
        }

        var features = new FeatureCollection { [typeof(IServerAddressesFeature)] = addressesFeature };

        var mockServer = new Mock<IServer>();
        mockServer.Setup(x => x.Features).Returns(features);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(x => x.GetService(typeof(IServer))).Returns(mockServer.Object);

        var hostMock = new Mock<IHost>();
        hostMock.SetupGet(x => x.Services).Returns(serviceProviderMock.Object);

        return hostMock;
    }
}
