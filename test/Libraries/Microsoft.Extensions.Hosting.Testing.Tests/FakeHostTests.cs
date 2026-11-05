// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Hosting.Testing.Test;

public class FakeHostTests
{
    [Fact]
    public async Task CreateBuilder_AddsFakeLogging()
    {
        using var host = await FakeHost.CreateBuilder().StartAsync();
        Assert.Contains(host.Services.GetServices<ILoggerProvider>(), x => x is FakeLoggerProvider);
    }

    [Fact]
    public async Task Host_ShutsDownAfterTimeout()
    {
        using var host = await FakeHost
            .CreateBuilder(x =>
            {
                x.FakeRedaction = false;
                x.TimeToLive = TimeSpan.Zero;
            })
            .StartAsync();

        // poll up to 50 times (5 seconds total) until the host has shut down and its service provider has been disposed
        ObjectDisposedException? captured = null;
        for (var i = 0; i < 50; i++)
        {
            try
            {
                host.Services.GetService<IHost>();
            }
            catch (ObjectDisposedException ex)
            {
                captured = ex;
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(captured);
    }

    [Fact]
    public async Task StartAsync_NoTokenProvided_UsesDefaultTimeout()
    {
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StartAsync(It.Is<CancellationToken>(y => y != default)))
            .Returns(Task.CompletedTask);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { StartUpTimeout = TimeSpan.Zero });
#pragma warning restore CA2000
        await sut.StartAsync();

        // poll up to 50 times (5 seconds total) until the inner IHost.StartAsync invocation has been recorded
        for (var i = 0; i < 50 && hostMock.Invocations.Count == 0; i++)
        {
            await Task.Delay(100);
        }

        hostMock.VerifyAll();
    }

    [Fact]
    public async Task StartAsync_TokenProvided_Starts()
    {
        using var tokenSource = new CancellationTokenSource();
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StartAsync(It.Is<CancellationToken>(y => y != tokenSource.Token)))
            .Returns(Task.CompletedTask);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { StartUpTimeout = TimeSpan.Zero });
#pragma warning restore CA2000
        await sut.StartAsync(tokenSource.Token);
        hostMock.VerifyAll();
    }

    [Fact]
    public void StartAsync_TokenProvided_LinksTheToken()
    {
        var timeProvider = new FakeTimeProvider();
        var task = new Task(() => { });
        var cancellationTokenSource = timeProvider.CreateCancellationTokenSource(TimeSpan.FromMilliseconds(1));
        CancellationToken receivedToken = default;
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StartAsync(It.Is<CancellationToken>(y => y != cancellationTokenSource.Token)))
            .Callback<CancellationToken>(x => receivedToken = x)
            .Returns(task);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { StartUpTimeout = TimeSpan.FromMilliseconds(-1) });
#pragma warning restore CA2000
        _ = sut.StartAsync(cancellationTokenSource.Token);

        Assert.False(receivedToken.IsCancellationRequested);
        cancellationTokenSource.Cancel();
        Assert.True(receivedToken.IsCancellationRequested);

        hostMock.VerifyAll();

        cancellationTokenSource.Dispose();
    }

    [Fact]
    public async Task StopAsync_NoTokenProvided_UsesDefaultTimeout()
    {
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StopAsync(It.Is<CancellationToken>(y => y != default)))
            .Returns(Task.CompletedTask);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { ShutDownTimeout = TimeSpan.Zero });
#pragma warning restore CA2000
        await sut.StopAsync();

        hostMock.VerifyAll();
    }

    [Fact]
    public async Task StopAsync_TokenProvided_Stops()
    {
        using var tokenSource = new CancellationTokenSource();
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StopAsync(It.Is<CancellationToken>(y => y != tokenSource.Token)))
            .Returns(Task.CompletedTask);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { StartUpTimeout = TimeSpan.Zero });
#pragma warning restore CA2000
        await sut.StopAsync(tokenSource.Token);
        hostMock.VerifyAll();
    }

    [Fact]
    public void StopAsync_TokenProvided_LinksTheToken()
    {
        var timeProvider = new FakeTimeProvider();
        var task = new Task(() => { });

        var cancellationTokenSource = timeProvider.CreateCancellationTokenSource(TimeSpan.FromMilliseconds(1));
        CancellationToken receivedToken = default;
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock
            .Setup(x => x.StopAsync(It.Is<CancellationToken>(y => y != cancellationTokenSource.Token)))
            .Callback<CancellationToken>(x => receivedToken = x)
            .Returns(task);

#pragma warning disable CA2000
        var sut = new FakeHost(hostMock.Object, new FakeHostOptions { StartUpTimeout = TimeSpan.FromMilliseconds(-1) });
#pragma warning restore CA2000
        _ = sut.StopAsync(cancellationTokenSource.Token);

        Assert.False(receivedToken.IsCancellationRequested);
        cancellationTokenSource.Cancel();
        Assert.True(receivedToken.IsCancellationRequested);

        hostMock.VerifyAll();

        cancellationTokenSource.Dispose();
    }

    [Fact]
    public void Dispose_ShutsDownHost()
    {
        var hostMock = new Mock<IHost>(MockBehavior.Strict);
        hostMock.Setup(x => x.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        hostMock.Setup(x => x.Dispose());

        var sut = new FakeHost(hostMock.Object, new FakeHostOptions()) { TimeProvider = new FakeTimeProvider() };
        sut.Dispose();

        hostMock.VerifyAll();
    }

    [Fact]
    public void Dispose_RunsOnlyOnce()
    {
        var hostMock = new Mock<IHost>();
        hostMock.Setup(x => x.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        hostMock.Setup(x => x.Dispose());

        var sut = new FakeHost(hostMock.Object, new FakeHostOptions()) { TimeProvider = new FakeTimeProvider() };
        sut.Dispose();
#pragma warning disable S3966
        sut.Dispose();
#pragma warning restore S3966

        hostMock.Verify(x => x.Dispose(), Times.Once);
    }
}
