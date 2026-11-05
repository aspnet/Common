// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Network;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

/// <summary>
/// Keep this Test to distinguish different tests for IPv6.
/// </summary>
[Collection("Tcp Connection Tests")]
public sealed class Tcp6TableInfoTests
{
    public Tcp6TableInfoTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    public static readonly TimeSpan DefaultTimeSpan = TimeSpan.FromSeconds(5);
    public static DateTimeOffset StartTimestamp = DateTimeOffset.UtcNow;
    public static DateTimeOffset NextTimestamp = StartTimestamp.Add(DefaultTimeSpan);

    // Each MIB_TCP6ROW needs 52. In experiments, the size should be 52 * MIB_TCP6ROW_count + 12.
    private const uint FakeSize = 688;

    // Add 13 rows more.
    private const uint FakeSize2 = FakeSize + (52 * 13);
    private const uint FakeNumberOfEntries = 13;
    private const uint FakeNumberOfEntries2 = FakeNumberOfEntries * 2;

    // Fake Local IPv6 Address
    private static readonly byte[] _fakeAddress = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 };
    private static readonly byte[] _fakeAddress2 = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 };

    public static uint FakeGetTcp6TableWithUnsuccessfulStatusAllTheTime(IntPtr pTcp6Table, ref uint pdwSize, bool bOrder)
    {
        return (uint)NTSTATUS.UnsuccessfulStatus;
    }

    public static uint FakeGetTcp6TableWithInsufficientBufferAndInvalidParameter(IntPtr pTcp6Table, ref uint pdwSize, bool bOrder)
    {
        if (pdwSize < FakeSize)
        {
            pdwSize = FakeSize;
            return (uint)NTSTATUS.InsufficientBuffer;
        }

        return (uint)NTSTATUS.InvalidParameter;
    }

    public static uint FakeGetTcp6TableWithFakeInformation(IntPtr pTcp6Table, ref uint pdwSize, bool bOrder)
    {
        if (DateTimeOffset.UtcNow < NextTimestamp)
        {
            if (pdwSize < FakeSize)
            {
                pdwSize = FakeSize;
                return (uint)NTSTATUS.InsufficientBuffer;
            }

            MIB_TCP6TABLE fakeTcp6Table = new()
            {
                NumberOfEntries = FakeNumberOfEntries
            };
            MIB_TCP6ROW[] fakeRows = new MIB_TCP6ROW[FakeNumberOfEntries];
            for (int i = 0; i < 12; ++i)
            {
                fakeRows[i] = new MIB_TCP6ROW
                {
                    State = (MIB_TCP_STATE)(i + 1),

                    LocalAddr = new IN6_ADDR
                    {
                        Byte = _fakeAddress
                    },
                    RemoteAddr = new IN6_ADDR
                    {
                        Byte = new byte[16]
                    }
                };
            }

            fakeRows[12] = new MIB_TCP6ROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };

            // True means the result should be sorted.
            if (bOrder)
            {
                fakeRows[12].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[12].RemoteAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
            }
            else
            {
                fakeRows[11].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[12].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress
                };
                fakeRows[11].RemoteAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[12].RemoteAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress
                };
            }

            fakeTcp6Table.Table = fakeRows[0];
            Marshal.StructureToPtr(fakeTcp6Table, pTcp6Table, false);
            var offset = Marshal.OffsetOf<MIB_TCP6TABLE>(nameof(MIB_TCP6TABLE.Table)).ToInt32();
            var fakePtr = IntPtr.Add(pTcp6Table, offset);

            for (int i = 0; i < fakeTcp6Table.NumberOfEntries; ++i)
            {
                Marshal.StructureToPtr(fakeRows[i], fakePtr, false);
                fakePtr = IntPtr.Add(fakePtr, Marshal.SizeOf<MIB_TCP6ROW>());
            }
        }
        else
        {
            if (pdwSize < FakeSize2)
            {
                pdwSize = FakeSize2;
                return (uint)NTSTATUS.InsufficientBuffer;
            }

            MIB_TCP6TABLE fakeTcp6Table = new()
            {
                NumberOfEntries = FakeNumberOfEntries2
            };
            var test = _fakeAddress;
            MIB_TCP6ROW[] fakeRows = new MIB_TCP6ROW[FakeNumberOfEntries2];
            for (int i = 0; i < 12; ++i)
            {
                fakeRows[i] = new MIB_TCP6ROW
                {
                    State = (MIB_TCP_STATE)(i + 1),

                    LocalAddr = new IN6_ADDR
                    {
                        Byte = _fakeAddress
                    }
                };
            }

            for (int i = 13; i < 25; ++i)
            {
                fakeRows[i] = new MIB_TCP6ROW
                {
                    State = (MIB_TCP_STATE)(i + 1 - 13),

                    LocalAddr = new IN6_ADDR
                    {
                        Byte = _fakeAddress
                    }
                };
            }

            fakeRows[12] = new MIB_TCP6ROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };
            fakeRows[25] = new MIB_TCP6ROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };

            // True means the result should be sorted.
            if (bOrder)
            {
                fakeRows[12].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[25].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
            }
            else
            {
                fakeRows[11].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[12].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress
                };
                fakeRows[24].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress2
                };
                fakeRows[25].LocalAddr = new IN6_ADDR
                {
                    Byte = _fakeAddress
                };
            }

            fakeTcp6Table.Table = fakeRows[0];
            Marshal.StructureToPtr(fakeTcp6Table, pTcp6Table, false);
            var offset = Marshal.OffsetOf<MIB_TCP6TABLE>(nameof(MIB_TCP6TABLE.Table)).ToInt32();
            var fakePtr = IntPtr.Add(pTcp6Table, offset);

            for (int i = 0; i < fakeTcp6Table.NumberOfEntries; ++i)
            {
                Marshal.StructureToPtr(fakeRows[i], fakePtr, false);
                fakePtr = IntPtr.Add(fakePtr, Marshal.SizeOf<MIB_TCP6ROW>());
            }
        }

        return (uint)NTSTATUS.Success;
    }

    [Fact]
    public void Test_Tcp6TableInfo_Get_UnsuccessfulStatus_All_The_Time()
    {
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "[::1]" },
            SamplingInterval = DefaultTimeSpan
        };

        var tcp6TableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcp6TableInfo.SetGetTcp6TableDelegate(FakeGetTcp6TableWithUnsuccessfulStatusAllTheTime);
        Assert.Throws<InvalidOperationException>(() =>
        {
            TcpStateInfo tcpStateInfo = tcp6TableInfo.GetIpV6TcpStateInfo();
        });
    }

    [Fact]
    public void Test_Tcp6TableInfo_Get_InsufficientBuffer_Then_Get_InvalidParameter()
    {
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "[::1]" },
            SamplingInterval = DefaultTimeSpan
        };
        WindowsTcpStateInfo tcp6TableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcp6TableInfo.SetGetTcp6TableDelegate(FakeGetTcp6TableWithInsufficientBufferAndInvalidParameter);
        Assert.Throws<InvalidOperationException>(() =>
        {
            TcpStateInfo tcpStateInfo = tcp6TableInfo.GetIpV6TcpStateInfo();
        });
    }

    [Fact]
    public void Test_Tcp6TableInfo_Get_Correct_Information()
    {
        StartTimestamp = DateTimeOffset.UtcNow;
        NextTimestamp = StartTimestamp.Add(DefaultTimeSpan);
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "[::1]" },
            SamplingInterval = DefaultTimeSpan
        };
        WindowsTcpStateInfo tcp6TableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcp6TableInfo.SetGetTcp6TableDelegate(FakeGetTcp6TableWithFakeInformation);
        TcpStateInfo tcpStateInfo = tcp6TableInfo.GetIpV6TcpStateInfo();
        Assert.NotNull(tcpStateInfo);
        Assert.Equal(1, tcpStateInfo.ClosedCount);
        Assert.Equal(1, tcpStateInfo.ListenCount);
        Assert.Equal(1, tcpStateInfo.SynSentCount);
        Assert.Equal(1, tcpStateInfo.SynRcvdCount);
        Assert.Equal(1, tcpStateInfo.EstabCount);
        Assert.Equal(1, tcpStateInfo.FinWait1Count);
        Assert.Equal(1, tcpStateInfo.FinWait2Count);
        Assert.Equal(1, tcpStateInfo.CloseWaitCount);
        Assert.Equal(1, tcpStateInfo.ClosingCount);
        Assert.Equal(1, tcpStateInfo.LastAckCount);
        Assert.Equal(1, tcpStateInfo.TimeWaitCount);
        Assert.Equal(1, tcpStateInfo.DeleteTcbCount);

        // Second calling in a small interval.
        tcpStateInfo = tcp6TableInfo.GetIpV6TcpStateInfo();
        Assert.NotNull(tcpStateInfo);
        Assert.Equal(1, tcpStateInfo.ClosedCount);
        Assert.Equal(1, tcpStateInfo.ListenCount);
        Assert.Equal(1, tcpStateInfo.SynSentCount);
        Assert.Equal(1, tcpStateInfo.SynRcvdCount);
        Assert.Equal(1, tcpStateInfo.EstabCount);
        Assert.Equal(1, tcpStateInfo.FinWait1Count);
        Assert.Equal(1, tcpStateInfo.FinWait2Count);
        Assert.Equal(1, tcpStateInfo.CloseWaitCount);
        Assert.Equal(1, tcpStateInfo.ClosingCount);
        Assert.Equal(1, tcpStateInfo.LastAckCount);
        Assert.Equal(1, tcpStateInfo.TimeWaitCount);
        Assert.Equal(1, tcpStateInfo.DeleteTcbCount);

        // Third calling in a long interval.
        Thread.Sleep(6000);
        tcpStateInfo = tcp6TableInfo.GetIpV6TcpStateInfo();
        Assert.NotNull(tcpStateInfo);
        Assert.Equal(2, tcpStateInfo.ClosedCount);
        Assert.Equal(2, tcpStateInfo.ListenCount);
        Assert.Equal(2, tcpStateInfo.SynSentCount);
        Assert.Equal(2, tcpStateInfo.SynRcvdCount);
        Assert.Equal(2, tcpStateInfo.EstabCount);
        Assert.Equal(2, tcpStateInfo.FinWait1Count);
        Assert.Equal(2, tcpStateInfo.FinWait2Count);
        Assert.Equal(2, tcpStateInfo.CloseWaitCount);
        Assert.Equal(2, tcpStateInfo.ClosingCount);
        Assert.Equal(2, tcpStateInfo.LastAckCount);
        Assert.Equal(2, tcpStateInfo.TimeWaitCount);
        Assert.Equal(2, tcpStateInfo.DeleteTcbCount);
    }
}

