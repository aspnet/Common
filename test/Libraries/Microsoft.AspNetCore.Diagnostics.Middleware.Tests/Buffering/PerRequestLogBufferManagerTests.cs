// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET9_0_OR_GREATER
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Diagnostics.Buffering.Test;

public class PerRequestLogBufferManagerTests
{
    [Fact]
    public void WhenRequestServicesIsNull_GlobalBufferIsUsed()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(builder => builder
                .AddFakeLogging()
                .AddPerIncomingRequestBuffer(LogLevel.Information))
            .BuildServiceProvider();
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        ILogger logger = services.GetRequiredService<ILogger<PerRequestLogBufferManagerTests>>();
        PerRequestLogBuffer buffer = services.GetRequiredService<PerRequestLogBuffer>();
        FakeLogCollector logCollector = services.GetFakeLogCollector();

        Assert.Null(Record.Exception(() => logger.LogInformation("test")));
        Assert.Empty(logCollector.GetSnapshot());

        Assert.Null(Record.Exception(buffer.Flush));
        Assert.Equal("test", Assert.Single(logCollector.GetSnapshot()).Message);
    }
}
#endif
