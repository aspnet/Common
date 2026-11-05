// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Network;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

[Collection("Tcp Connection Tests")]
public sealed class WindowsCountersTests
{
    [Fact]
    public void WindowsCounters_Registers_Instruments()
    {
        TcpTableInfoTests.StartTimestamp = DateTimeOffset.UtcNow;
        TcpTableInfoTests.NextTimestamp = TcpTableInfoTests.StartTimestamp.Add(TcpTableInfoTests.DefaultTimeSpan);
        Tcp6TableInfoTests.StartTimestamp = DateTimeOffset.UtcNow;
        Tcp6TableInfoTests.NextTimestamp = TcpTableInfoTests.StartTimestamp.Add(TcpTableInfoTests.DefaultTimeSpan);
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "127.0.0.1", "[::1]" },
            SamplingInterval = TimeSpan.FromSeconds(5)
        };

        using var meter = new Meter(nameof(WindowsCounters_Registers_Instruments));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        var tcpTableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcpTableInfo.SetGetTcpTableDelegate(TcpTableInfoTests.FakeGetTcpTableWithFakeInformation);
        tcpTableInfo.SetGetTcp6TableDelegate(Tcp6TableInfoTests.FakeGetTcp6TableWithFakeInformation);
        var windowsCounters = new WindowsNetworkMetrics(meterFactoryMock.Object, tcpTableInfo);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(meter, instrument.Meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        var samples = new List<(Instrument instrument, long value)>();
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            samples.Add((instrument, value));
        });

        listener.Start();
        listener.RecordObservableInstruments();
        samples.Count.Should().Be(24);
        samples.Should().AllSatisfy(x => x.instrument.Name.Should().Be("system.network.connections"));
        samples.Should().AllSatisfy(x => x.value.Should().Be(1));
        samples.Should().AllSatisfy(x => x.instrument.Description.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void WindowsCounters_Got_Unsuccessful()
    {
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "127.0.0.1", "[::1]" },
            SamplingInterval = TimeSpan.FromSeconds(5)
        };

        using var meter = new Meter(nameof(WindowsCounters_Got_Unsuccessful));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        var tcpTableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcpTableInfo.SetGetTcpTableDelegate(TcpTableInfoTests.FakeGetTcpTableWithUnsuccessfulStatusAllTheTime);
        tcpTableInfo.SetGetTcp6TableDelegate(Tcp6TableInfoTests.FakeGetTcp6TableWithUnsuccessfulStatusAllTheTime);
        var windowsCounters = new WindowsNetworkMetrics(meterFactoryMock.Object, tcpTableInfo);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(meter, instrument.Meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        var samples = new List<(Instrument instrument, long value)>();
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            samples.Add((instrument, value));
        });

        listener.Start();
        Assert.Throws<AggregateException>(() =>
        {
            listener.RecordObservableInstruments();
        });
    }
}
