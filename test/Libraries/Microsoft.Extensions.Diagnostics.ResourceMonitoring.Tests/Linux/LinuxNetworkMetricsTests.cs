// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Network;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Helpers;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

public class LinuxNetworkMetricsTests
{
    private readonly Mock<ITcpStateInfoProvider> _tcpStateInfoProvider = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private FakeTimeProvider _timeProvider;

    public LinuxNetworkMetricsTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");

        _timeProvider = new FakeTimeProvider(_startTime);

        _tcpStateInfoProvider.Setup(p => p.GetIpV4TcpStateInfo()).Returns(new TcpStateInfo());
        _tcpStateInfoProvider.Setup(p => p.GetIpV6TcpStateInfo()).Returns(new TcpStateInfo());
    }

    [Fact]
    public void CreatesMeter_WithCorrectName()
    {
        using var meterFactory = new TestMeterFactory();
        _ = new LinuxNetworkMetrics(
            meterFactory,
            _tcpStateInfoProvider.Object,
            _timeProvider);

        Meter meter = meterFactory.Meters.Single();
        Assert.Equal(ResourceUtilizationInstruments.MeterName, meter.Name);
    }

    [Fact]
    public void GetTcpStateInfoWithRetry_SuccessfulCall_ReturnsState()
    {
        var expectedV4 = new TcpStateInfo { ClosedCount = 42 };
        var expectedV6 = new TcpStateInfo { EstabCount = 24 };
        _tcpStateInfoProvider.Setup(p => p.GetIpV4TcpStateInfo()).Returns(expectedV4);
        _tcpStateInfoProvider.Setup(p => p.GetIpV6TcpStateInfo()).Returns(expectedV6);

        LinuxNetworkMetrics metrics = CreateMetrics();
        List<Measurement<long>> measurements = metrics.GetMeasurements().ToList();

        Assert.Contains(measurements, m => HasTagWithValue(m, "network.type", "ipv4", 42));
        Assert.Contains(measurements, m => HasTagWithValue(m, "system.network.state", "close", 42));
        Assert.Contains(measurements, m => HasTagWithValue(m, "network.type", "ipv6", 24));
        Assert.Contains(measurements, m => HasTagWithValue(m, "system.network.state", "established", 24));
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void GetTcpStateInfoWithRetry_Failure_SetsUnavailableAndReturnsDefault(Type exceptionType)
    {
        _tcpStateInfoProvider.Setup(p => p.GetIpV4TcpStateInfo()).Throws((Exception)Activator.CreateInstance(exceptionType)!);

        LinuxNetworkMetrics metrics = CreateMetrics();
        List<Measurement<long>> measurements = metrics.GetMeasurements().ToList();

        Assert.All(measurements.Take(11), m => Assert.Equal(0, m.Value));
    }

    [Fact]
    public void GetTcpStateInfoWithRetry_DuringRetryInterval_ReturnsDefault()
    {
        _tcpStateInfoProvider.SetupSequence(p => p.GetIpV4TcpStateInfo())
            .Throws(new FileNotFoundException())
            .Returns(new TcpStateInfo { ClosedCount = 123 });

        LinuxNetworkMetrics metrics = CreateMetrics();
        List<Measurement<long>> first = metrics.GetMeasurements().ToList();

        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        List<Measurement<long>> second = metrics.GetMeasurements().ToList();

        Assert.All(first.Take(11), m => Assert.Equal(0, m.Value));
        Assert.All(second.Take(11), m => Assert.Equal(0, m.Value));
        _tcpStateInfoProvider.Verify(p => p.GetIpV4TcpStateInfo(), Times.Once);
    }

    [Fact]
    public void GetTcpStateInfoWithRetry_AfterRetryInterval_ResetsUnavailableOnSuccess()
    {
        _tcpStateInfoProvider.SetupSequence(p => p.GetIpV4TcpStateInfo())
            .Throws(new FileNotFoundException())
            .Returns(new TcpStateInfo { ClosedCount = 99 });

        LinuxNetworkMetrics metrics = CreateMetrics();
        List<Measurement<long>> first = metrics.GetMeasurements().ToList();

        _timeProvider.Advance(TimeSpan.FromMinutes(6));
        List<Measurement<long>> second = metrics.GetMeasurements().ToList();

        Assert.All(first.Take(11), m => Assert.Equal(0, m.Value));
        Assert.Equal(99, second[0].Value);
        Assert.Contains(second, m => HasTagWithValue(m, "network.type", "ipv4", 99));
        Assert.Contains(second, m => HasTagWithValue(m, "system.network.state", "close", 99));

        _tcpStateInfoProvider.Verify(p => p.GetIpV4TcpStateInfo(), Times.Exactly(2));
    }

    private static bool HasTagWithValue(Measurement<long> measurement, string tagKey, string tagValue, long expectedValue)
    {
        foreach (KeyValuePair<string, object?> tag in measurement.Tags)
        {
            if (tag.Key == tagKey && string.Equals(tag.Value as string, tagValue, StringComparison.Ordinal))
            {
                return measurement.Value == expectedValue;
            }
        }

        return false;
    }

    private LinuxNetworkMetrics CreateMetrics()
    {
        using var meterFactory = new TestMeterFactory();
        return new LinuxNetworkMetrics(
            meterFactory,
            _tcpStateInfoProvider.Object,
            _timeProvider);
    }
}
