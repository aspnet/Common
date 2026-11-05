// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Network;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

public class LinuxCountersTests
{
    [Fact]
    public void LinuxNetworkCounters_Registers_Instruments()
    {
        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            {
                new FileInfo("/proc/net/tcp"),
                "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
                "   0: 030011AC:8AF2 C1B17822:01BB 01 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAC 856CC7B9:01BB 02 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF3 C1B17823:01BB 03 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAD 856CC7BA:01BB 04 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF4 C1B17824:01BB 05 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAE 856CC7BB:01BB 06 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF5 C1B17825:01BB 07 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAF 856CC7BC:01BB 08 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF6 C1B17826:01BB 09 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEA1 856CC7BD:01BB 0A 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF7 C1B17827:01BB 0B 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   0: 030011AC:8AF2 C1B17822:01BB 01 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAC 856CC7B9:01BB 02 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF3 C1B17823:01BB 03 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAD 856CC7BA:01BB 04 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF4 C1B17824:01BB 05 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAE 856CC7BB:01BB 06 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF5 C1B17825:01BB 07 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEAF 856CC7BC:01BB 08 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF6 C1B17826:01BB 09 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   1: 030011AC:DEA1 856CC7BD:01BB 0A 00000000:00000000 02:000000D1 00000000   472        0 2483767 2 0000000014121fd6 28 4 30 10 -1\r\n" +
                "   0: 030011AC:8AF7 C1B17827:01BB 0B 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n"
            },
            {
#pragma warning disable S103 // Lines should not be too long - disabled for better readability
                new FileInfo("/proc/net/tcp6"),
                "  sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
                "   0: 00000000000000000000000000000000:0BB8 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB9 00000000000000000000000000000000:0001 0B 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBB 00000000000000000000000000000000:0003 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBC 00000000000000000000000000000000:0004 02 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBD 00000000000000000000000000000000:0005 03 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBE 00000000000000000000000000000000:0006 04 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB1 00000000000000000000000000000000:0007 05 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB2 00000000000000000000000000000000:0008 06 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB3 00000000000000000000000000000000:0009 07 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB4 00000000000000000000000000000000:000A 08 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB5 00000000000000000000000000000000:000B 09 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB8 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB9 00000000000000000000000000000000:0001 0B 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBB 00000000000000000000000000000000:0003 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBC 00000000000000000000000000000000:0004 02 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBD 00000000000000000000000000000000:0005 03 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BBE 00000000000000000000000000000000:0006 04 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB1 00000000000000000000000000000000:0007 05 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB2 00000000000000000000000000000000:0008 06 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB3 00000000000000000000000000000000:0009 07 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB4 00000000000000000000000000000000:000A 08 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB5 00000000000000000000000000000000:000B 09 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n"
            },
        });

        var parser = new LinuxNetworkUtilizationParser(fileSystem);
        var options = Microsoft.Extensions.Options.Options.Create<ResourceMonitoringOptions>(new());

        using var meter = new Meter(nameof(LinuxNetworkMetrics));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock
            .Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        var tcpStateInfo = new LinuxTcpStateInfo(options, parser);
        var lnm = new LinuxNetworkMetrics(meterFactoryMock.Object, tcpStateInfo, new FakeTimeProvider(DateTimeOffset.UtcNow));

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
        samples.Count.Should().Be(22);
        samples.Should().AllSatisfy(x => x.instrument.Name.Should().Be(ResourceUtilizationInstruments.SystemNetworkConnections));
        samples.Should().AllSatisfy(x => x.value.Should().Be(2));
        samples.Should().AllSatisfy(x => x.instrument.Description.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Tcp_State_Info_Changes_After_Time()
    {
        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            {
                new FileInfo("/proc/net/tcp"),
                "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
                "   0: 030011AC:8AF2 C1B17822:01BB 01 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
                "   0: 030011AC:8AF8 C1B17828:01BB 01 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n"
            },
            {
                new FileInfo("/proc/net/tcp6"),
                "  sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
                "   0: 00000000000000000000000000000000:0BBB 00000000000000000000000000000000:0003 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB6 0000000000000000FFFF000000000000:000C 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
                "   0: 00000000000000000000000000000000:0BB7 0000000000000000FFFF000000000000:000D 01 00000000:00000000 00:00000000 00000000   472        0 0 3 0000000000000000\r\n"
            },
        });

        var parser = new LinuxNetworkUtilizationParser(fileSystem);

        var tcpIPv4StateInfo = parser.GetTcpIPv4StateInfo();
        var tcpIPv6StateInfo = parser.GetTcpIPv6StateInfo();
        Assert.Equal(2, tcpIPv4StateInfo.EstabCount);
        Assert.Equal(0, tcpIPv4StateInfo.SynSentCount);
        Assert.Equal(3, tcpIPv6StateInfo.EstabCount);
        Assert.Equal(0, tcpIPv6StateInfo.SynSentCount);

        Thread.Sleep(3000);
        var tcpFile =
            "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
            "   0: 030011AC:8AF2 C1B17822:01BB 01 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n" +
            "   0: 030011AC:8AF8 C1B17828:01BB 02 00000000:00000000 02:000000D1 00000000   472        0 2481276 2 00000000c62511cb 28 4 26 10 -1\r\n";
        var tcp6File =
            "  sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\r\n" +
            "   0: 00000000000000000000000000000000:0BBB 00000000000000000000000000000000:0003 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
            "   0: 00000000000000000000000000000000:0BB6 0000000000000000FFFF000000000000:000C 01 00000000:00000000 00:00000000 00000000   472        0 2455375 1 00000000f4cb7621 100 0 0 10 0\r\n" +
            "   0: 00000000000000000000000000000000:0BB7 0000000000000000FFFF000000000000:000D 02 00000000:00000000 00:00000000 00000000   472        0 0 3 0000000000000000\r\n";
        fileSystem.ReplaceFileContent(new FileInfo("/proc/net/tcp"), tcpFile);
        fileSystem.ReplaceFileContent(new FileInfo("/proc/net/tcp6"), tcp6File);
        Thread.Sleep(3000);

        tcpIPv4StateInfo = parser.GetTcpIPv4StateInfo();
        tcpIPv6StateInfo = parser.GetTcpIPv6StateInfo();
        Assert.Equal(1, tcpIPv4StateInfo.EstabCount);
        Assert.Equal(1, tcpIPv4StateInfo.SynSentCount);
        Assert.Equal(2, tcpIPv6StateInfo.EstabCount);
        Assert.Equal(1, tcpIPv6StateInfo.SynSentCount);
    }
}
