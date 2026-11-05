// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Network;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Test;

[Collection("Tcp Connection Tests")]
public sealed class TcpTableInfoTests
{
    public TcpTableInfoTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");
    }

    public static readonly TimeSpan DefaultTimeSpan = TimeSpan.FromSeconds(5);
    public static DateTimeOffset StartTimestamp = DateTimeOffset.UtcNow;
    public static DateTimeOffset NextTimestamp = StartTimestamp.Add(DefaultTimeSpan);

    // Each MIB_TCPROW needs 20. In experiments, the size should be 20 * MIB_TCPROW_count + 12.
    private const uint FakeSize = 272;

    // Add 13 rows more.
    private const uint FakeSize2 = FakeSize + (20 * 13);
    private const uint FakeNumberOfEntries = 13;
    private const uint FakeNumberOfEntries2 = FakeNumberOfEntries * 2;

    public static uint FakeGetTcpTableWithUnsuccessfulStatusAllTheTime(IntPtr pTcpTable, ref uint pdwSize, bool bOrder)
    {
        return (uint)NTSTATUS.UnsuccessfulStatus;
    }

    public static uint FakeGetTcpTableWithInsufficientBufferAndInvalidParameter(IntPtr pTcpTable, ref uint pdwSize, bool bOrder)
    {
        if (pdwSize < FakeSize)
        {
            pdwSize = FakeSize;
            return (uint)NTSTATUS.InsufficientBuffer;
        }

        return (uint)NTSTATUS.InvalidParameter;
    }

    public static unsafe uint FakeGetTcpTableWithFakeInformation(IntPtr pTcpTable, ref uint pdwSize, bool bOrder)
    {
        if (DateTimeOffset.UtcNow < NextTimestamp)
        {
            if (pdwSize < FakeSize)
            {
                pdwSize = FakeSize;
                return (uint)NTSTATUS.InsufficientBuffer;
            }

            MIB_TCPTABLE fakeTcpTable = new()
            {
                NumberOfEntries = FakeNumberOfEntries
            };
            MIB_TCPROW[] fakeRows = new MIB_TCPROW[FakeNumberOfEntries];
            for (int i = 0; i < 12; ++i)
            {
                fakeRows[i] = new MIB_TCPROW
                {
                    State = (MIB_TCP_STATE)(i + 1),

                    // 16_777_343 means 127.0.0.1.
                    LocalAddr = 16_777_343
                };
            }

            fakeRows[12] = new MIB_TCPROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };

            // True means the result should be sorted.
            if (bOrder)
            {
                fakeRows[12].LocalAddr = 16_777_343 + 1;
            }
            else
            {
                fakeRows[11].LocalAddr = 16_777_343 + 1;
                fakeRows[12].LocalAddr = 16_777_343;
            }

            fakeTcpTable.Table = fakeRows[0];
            Marshal.StructureToPtr(fakeTcpTable, pTcpTable, false);
            var offset = Marshal.OffsetOf<MIB_TCPTABLE>(nameof(MIB_TCPTABLE.Table)).ToInt32();
            var fakePtr = IntPtr.Add(pTcpTable, offset);

            for (int i = 0; i < fakeTcpTable.NumberOfEntries; ++i)
            {
                Marshal.StructureToPtr(fakeRows[i], fakePtr, false);
                fakePtr = IntPtr.Add(fakePtr, sizeof(MIB_TCPROW));
            }
        }
        else
        {
            if (pdwSize < FakeSize2)
            {
                pdwSize = FakeSize2;
                return (uint)NTSTATUS.InsufficientBuffer;
            }

            MIB_TCPTABLE fakeTcpTable = new()
            {
                NumberOfEntries = FakeNumberOfEntries2
            };
            MIB_TCPROW[] fakeRows = new MIB_TCPROW[FakeNumberOfEntries2];
            for (int i = 0; i < 12; ++i)
            {
                fakeRows[i] = new MIB_TCPROW
                {
                    State = (MIB_TCP_STATE)(i + 1),

                    // 16_777_343 means 127.0.0.1.
                    LocalAddr = 16_777_343
                };
            }

            for (int i = 13; i < 25; ++i)
            {
                fakeRows[i] = new MIB_TCPROW
                {
                    State = (MIB_TCP_STATE)(i + 1 - 13),

                    // 16_777_343 means 127.0.0.1.
                    LocalAddr = 16_777_343
                };
            }

            fakeRows[12] = new MIB_TCPROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };
            fakeRows[25] = new MIB_TCPROW
            {
                State = MIB_TCP_STATE.DELETE_TCB,
            };

            // True means the result should be sorted.
            if (bOrder)
            {
                fakeRows[12].LocalAddr = 16_777_343 + 1;
                fakeRows[25].LocalAddr = 16_777_343 + 1;
            }
            else
            {
                fakeRows[11].LocalAddr = 16_777_343 + 1;
                fakeRows[12].LocalAddr = 16_777_343;
                fakeRows[24].LocalAddr = 16_777_343 + 1;
                fakeRows[25].LocalAddr = 16_777_343;
            }

            fakeTcpTable.Table = fakeRows[0];
            Marshal.StructureToPtr(fakeTcpTable, pTcpTable, false);
            var offset = Marshal.OffsetOf<MIB_TCPTABLE>(nameof(MIB_TCPTABLE.Table)).ToInt32();
            var fakePtr = IntPtr.Add(pTcpTable, offset);

            for (int i = 0; i < fakeTcpTable.NumberOfEntries; ++i)
            {
                Marshal.StructureToPtr(fakeRows[i], fakePtr, false);
                fakePtr = IntPtr.Add(fakePtr, sizeof(MIB_TCPROW));
            }
        }

        return (uint)NTSTATUS.Success;
    }

    [Fact]
    public void Test_TcpTableInfo_Get_UnsuccessfulStatus_All_The_Time()
    {
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "127.0.0.1" },
            SamplingInterval = DefaultTimeSpan
        };
        WindowsTcpStateInfo tcpTableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcpTableInfo.SetGetTcpTableDelegate(FakeGetTcpTableWithUnsuccessfulStatusAllTheTime);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var tcpStateInfo = tcpTableInfo.GetIpV4TcpStateInfo();
        });
    }

    [Fact]
    public void Test_TcpTableInfo_Get_InsufficientBuffer_Then_Get_InvalidParameter()
    {
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "127.0.0.1" },
            SamplingInterval = DefaultTimeSpan
        };
        WindowsTcpStateInfo tcpTableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcpTableInfo.SetGetTcpTableDelegate(FakeGetTcpTableWithInsufficientBufferAndInvalidParameter);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var tcpStateInfo = tcpTableInfo.GetIpV4TcpStateInfo();
        });
    }

    [Fact]
    public void Test_TcpTableInfo_Get_Correct_Information()
    {
        StartTimestamp = DateTimeOffset.UtcNow;
        NextTimestamp = StartTimestamp.Add(DefaultTimeSpan);
        var options = new ResourceMonitoringOptions
        {
            SourceIpAddresses = new HashSet<string> { "127.0.0.1" },
            SamplingInterval = DefaultTimeSpan
        };
        WindowsTcpStateInfo tcpTableInfo = new WindowsTcpStateInfo(Options.Options.Create(options));
        tcpTableInfo.SetGetTcpTableDelegate(FakeGetTcpTableWithFakeInformation);
        var tcpStateInfo = tcpTableInfo.GetIpV4TcpStateInfo();
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
        tcpStateInfo = tcpTableInfo.GetIpV4TcpStateInfo();
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
        tcpStateInfo = tcpTableInfo.GetIpV4TcpStateInfo();
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

    [Fact]
    public void Test_TcpTableInfo_CalculateCount_default_branch()
    {
        TcpStateInfo tcpStateInfo = new();

        // Add this case to increase coverage, but 0 will not happen in actual case.
        WindowsTcpStateInfo.CalculateCount(tcpStateInfo, 0);
        Assert.NotNull(tcpStateInfo);
        Assert.Equal(0, tcpStateInfo.ClosedCount);
        Assert.Equal(0, tcpStateInfo.ListenCount);
        Assert.Equal(0, tcpStateInfo.SynSentCount);
        Assert.Equal(0, tcpStateInfo.SynRcvdCount);
        Assert.Equal(0, tcpStateInfo.EstabCount);
        Assert.Equal(0, tcpStateInfo.FinWait1Count);
        Assert.Equal(0, tcpStateInfo.FinWait2Count);
        Assert.Equal(0, tcpStateInfo.CloseWaitCount);
        Assert.Equal(0, tcpStateInfo.ClosingCount);
        Assert.Equal(0, tcpStateInfo.LastAckCount);
        Assert.Equal(0, tcpStateInfo.TimeWaitCount);
        Assert.Equal(0, tcpStateInfo.DeleteTcbCount);
    }
}
