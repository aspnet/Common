// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Microsoft.Extensions.Logging.Testing.Test.Logging;

public partial class FakeLogCollectorTests
{
    private class Output : ITestOutputHelper
    {
        private readonly System.Text.StringBuilder _sb = new();

        public string Last { get; private set; } = string.Empty;
        string ITestOutputHelper.Output => _sb.ToString();

        public void Write(string message)
        {
            _sb.Append(message);
            Last += message;
        }

        public void Write(string format, params object[] args) => Write(string.Format(CultureInfo.InvariantCulture, format, args));

        public void WriteLine(string message)
        {
            _sb.AppendLine(message);
            Last = message;
        }

        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    [Fact]
    public void Basic()
    {
        var output = new Output();

        var timeProvider = new FakeTimeProvider();
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        var options = new FakeLogCollectorOptions
        {
            OutputSink = output.WriteLine,
            TimeProvider = timeProvider,
        };

        var collector = FakeLogCollector.Create(options);
        var logger = new FakeLogger(collector);

        logger.LogTrace("Hello world!");
        Assert.Equal("[00:00.001, trace] Hello world!", output.Last);

        logger.LogDebug("Hello world!");
        Assert.Equal("[00:00.001, debug] Hello world!", output.Last);

        logger.LogInformation("Hello world!");
        Assert.Equal("[00:00.001,  info] Hello world!", output.Last);

        logger.LogWarning("Hello world!");
        Assert.Equal("[00:00.001,  warn] Hello world!", output.Last);

        logger.LogError("Hello world!");
        Assert.Equal("[00:00.001, error] Hello world!", output.Last);

        logger.LogCritical("Hello world!");
        Assert.Equal("[00:00.001,  crit] Hello world!", output.Last);

        logger.Log(LogLevel.None, "Hello world!");
        Assert.Equal("[00:00.001,  none] Hello world!", output.Last);

        logger.Log((LogLevel)42, "Hello world!");
        Assert.Equal("[00:00.001, invld] Hello world!", output.Last);
    }

    [Fact]
    public void DIEntryPoint()
    {
        var output = new Output();

        var timeProvider = new FakeTimeProvider();
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        var options = new FakeLogCollectorOptions
        {
            OutputSink = output.WriteLine,
            TimeProvider = timeProvider,
        };

        var collector = new FakeLogCollector(Microsoft.Extensions.Options.Options.Create(options));
        var logger = new FakeLogger(collector);

        logger.LogTrace("Hello world!");
        Assert.Equal("[00:00.001, trace] Hello world!", output.Last);

        logger.LogDebug("Hello world!");
        Assert.Equal("[00:00.001, debug] Hello world!", output.Last);

        logger.LogInformation("Hello world!");
        Assert.Equal("[00:00.001,  info] Hello world!", output.Last);

        logger.LogWarning("Hello world!");
        Assert.Equal("[00:00.001,  warn] Hello world!", output.Last);

        logger.LogError("Hello world!");
        Assert.Equal("[00:00.001, error] Hello world!", output.Last);

        logger.LogCritical("Hello world!");
        Assert.Equal("[00:00.001,  crit] Hello world!", output.Last);

        logger.Log(LogLevel.None, "Hello world!");
        Assert.Equal("[00:00.001,  none] Hello world!", output.Last);

        logger.Log((LogLevel)42, "Hello world!");
        Assert.Equal("[00:00.001, invld] Hello world!", output.Last);
    }

    [Fact]
    public void DIEntryPoint_NullChecks()
    {
        Assert.Throws<ArgumentNullException>(() => new FakeLogCollector(null!));
        Assert.Throws<ArgumentException>(() => new FakeLogCollector(Microsoft.Extensions.Options.Options.Create((FakeLogCollectorOptions)null!)));
    }

    [Fact]
    public void TestOutputHelperExtensionsNonGeneric()
    {
        var output = new Output();

        var logger = new FakeLogger(output.WriteLine, "Storage");

        logger.LogTrace("Hello world!");
        Assert.Contains("trace] Hello world!", output.Last);

        logger.LogDebug("Hello world!");
        Assert.Contains("debug] Hello world!", output.Last);

        logger.LogInformation("Hello world!");
        Assert.Contains("info] Hello world!", output.Last);

        logger.LogWarning("Hello world!");
        Assert.Contains("warn] Hello world!", output.Last);

        logger.LogError("Hello world!");
        Assert.Contains("error] Hello world!", output.Last);

        logger.LogCritical("Hello world!");
        Assert.Contains("crit] Hello world!", output.Last);

        logger.Log(LogLevel.None, "Hello world!");
        Assert.Contains("none] Hello world!", output.Last);

        logger.Log((LogLevel)42, "Hello world!");
        Assert.Contains("invld] Hello world!", output.Last);
    }

    [Fact]
    public void TestOutputHelperExtensionsGeneric()
    {
        var output = new Output();

        var logger = new FakeLogger<FakeLogCollectorTests>(output.WriteLine);

        logger.LogTrace("Hello world!");
        Assert.Contains("trace] Hello world!", output.Last);

        logger.LogDebug("Hello world!");
        Assert.Contains("debug] Hello world!", output.Last);

        logger.LogInformation("Hello world!");
        Assert.Contains("info] Hello world!", output.Last);

        logger.LogWarning("Hello world!");
        Assert.Contains("warn] Hello world!", output.Last);

        logger.LogError("Hello world!");
        Assert.Contains("error] Hello world!", output.Last);

        logger.LogCritical("Hello world!");
        Assert.Contains("crit] Hello world!", output.Last);

        logger.Log(LogLevel.None, "Hello world!");
        Assert.Contains("none] Hello world!", output.Last);

        logger.Log((LogLevel)42, "Hello world!");
        Assert.Contains("invld] Hello world!", output.Last);
    }
}
