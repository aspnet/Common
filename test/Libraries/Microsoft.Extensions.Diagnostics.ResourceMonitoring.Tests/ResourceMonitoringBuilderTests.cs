// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Publishers;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test;

public sealed class ResourceMonitoringBuilderTests
{
    public ResourceMonitoringBuilderTests()
    {
        Assert.SkipUnless(!RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "Skipped on macOS");
    }

    [Fact]
    public void AddPublisher_CalledOnce_AddsSinglePublisherToServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddResourceMonitoring(builder =>
            {
                builder.AddPublisher<EmptyPublisher>();
            })
            .BuildServiceProvider();

        var publisher = provider.GetRequiredService<IResourceUtilizationPublisher>();
        var publishersArray = provider.GetServices<IResourceUtilizationPublisher>();

        Assert.NotNull(publisher);
        Assert.IsType<EmptyPublisher>(publisher);
        Assert.NotNull(publishersArray);
        Assert.Single(publishersArray);
        Assert.IsAssignableFrom<EmptyPublisher>(publishersArray.First());
    }

    [Fact]
    public void AddPublisher_CalledMultipleTimes_AddsMultiplePublishersToServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddResourceMonitoring(builder =>
            {
                builder
                    .AddPublisher<EmptyPublisher>()
                    .AddPublisher<AnotherPublisher>();
            })
            .BuildServiceProvider();

        var publishersArray = provider.GetServices<IResourceUtilizationPublisher>();

        Assert.NotNull(publishersArray);
        Assert.Equal(2, publishersArray.Count());
        Assert.IsAssignableFrom<EmptyPublisher>(publishersArray.First());
        Assert.IsAssignableFrom<AnotherPublisher>(publishersArray.Last());
    }
}
