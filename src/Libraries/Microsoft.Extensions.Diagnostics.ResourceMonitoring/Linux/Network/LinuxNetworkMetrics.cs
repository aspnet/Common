// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using Microsoft.Shared.Instruments;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Network;

internal sealed class LinuxNetworkMetrics
{
    private readonly ITcpStateInfoProvider _tcpStateInfoProvider;
    private readonly TimeProvider _timeProvider;

    private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastV4Failure = DateTimeOffset.MinValue;
    private DateTimeOffset _lastV6Failure = DateTimeOffset.MinValue;
    private int _v4Unavailable;
    private int _v6Unavailable;

    public LinuxNetworkMetrics(IMeterFactory meterFactory, ITcpStateInfoProvider tcpStateInfoProvider, TimeProvider timeProvider)
    {
        _tcpStateInfoProvider = tcpStateInfoProvider;
        _timeProvider = timeProvider;

#pragma warning disable CA2000 // Dispose objects before losing scope
        // We don't dispose the meter because IMeterFactory handles that
        // Is's a false-positive, see: https://github.com/dotnet/roslyn-analyzers/issues/6912
        // Related documentation: https://github.com/dotnet/docs/pull/37170.
        var meter = meterFactory.Create(ResourceUtilizationInstruments.MeterName);
#pragma warning restore CA2000 // Dispose objects before losing scope

        KeyValuePair<string, object?> tcpTag = new("network.transport", "tcp");
        TagList commonTags = new() { tcpTag };

        // The metric is aligned with
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/system/system-metrics.md#metric-systemnetworkconnections
        _ = meter.CreateObservableUpDownCounter(
            ResourceUtilizationInstruments.SystemNetworkConnections,
            GetMeasurements,
            unit: "{connection}",
            description: "Number of network connections by state.",
            tags: commonTags);
    }

    public IEnumerable<Measurement<long>> GetMeasurements()
    {
        const string NetworkTypeKey = "network.type";

        // These are covered in https://github.com/open-telemetry/semantic-conventions/blob/main/docs/rpc/rpc-metrics.md#attributes:
        KeyValuePair<string, object?> tcpVersionFourTag = new(NetworkTypeKey, "ipv4");
        KeyValuePair<string, object?> tcpVersionSixTag = new(NetworkTypeKey, "ipv6");

        List<Measurement<long>> measurements = new(24);

        // IPv4:
        TcpStateInfo stateV4 = GetTcpStateInfoWithRetry(_tcpStateInfoProvider.GetIpV4TcpStateInfo, ref _v4Unavailable, ref _lastV4Failure);
        CreateMeasurements(tcpVersionFourTag, measurements, stateV4);

        // IPv6:
        TcpStateInfo stateV6 = GetTcpStateInfoWithRetry(_tcpStateInfoProvider.GetIpV6TcpStateInfo, ref _v6Unavailable, ref _lastV6Failure);
        CreateMeasurements(tcpVersionSixTag, measurements, stateV6);

        return measurements;
    }

    private static void CreateMeasurements(KeyValuePair<string, object?> tcpVersionTag, List<Measurement<long>> measurements, TcpStateInfo state)
    {
        const string NetworkStateKey = "system.network.state";

        measurements.Add(new Measurement<long>(state.ClosedCount, new TagList { tcpVersionTag, new(NetworkStateKey, "close") }));
        measurements.Add(new Measurement<long>(state.ListenCount, new TagList { tcpVersionTag, new(NetworkStateKey, "listen") }));
        measurements.Add(new Measurement<long>(state.SynSentCount, new TagList { tcpVersionTag, new(NetworkStateKey, "syn_sent") }));
        measurements.Add(new Measurement<long>(state.SynRcvdCount, new TagList { tcpVersionTag, new(NetworkStateKey, "syn_recv") }));
        measurements.Add(new Measurement<long>(state.EstabCount, new TagList { tcpVersionTag, new(NetworkStateKey, "established") }));
        measurements.Add(new Measurement<long>(state.FinWait1Count, new TagList { tcpVersionTag, new(NetworkStateKey, "fin_wait_1") }));
        measurements.Add(new Measurement<long>(state.FinWait2Count, new TagList { tcpVersionTag, new(NetworkStateKey, "fin_wait_2") }));
        measurements.Add(new Measurement<long>(state.CloseWaitCount, new TagList { tcpVersionTag, new(NetworkStateKey, "close_wait") }));
        measurements.Add(new Measurement<long>(state.ClosingCount, new TagList { tcpVersionTag, new(NetworkStateKey, "closing") }));
        measurements.Add(new Measurement<long>(state.LastAckCount, new TagList { tcpVersionTag, new(NetworkStateKey, "last_ack") }));
        measurements.Add(new Measurement<long>(state.TimeWaitCount, new TagList { tcpVersionTag, new(NetworkStateKey, "time_wait") }));
    }

    private TcpStateInfo GetTcpStateInfoWithRetry(
        Func<TcpStateInfo> getStateInfoFunc,
        ref int unavailableFlag,
        ref DateTimeOffset lastFailureTime)
    {
        if (Volatile.Read(ref unavailableFlag) == 0 || _timeProvider.GetUtcNow() - lastFailureTime > _retryInterval)
        {
            try
            {
                TcpStateInfo state = getStateInfoFunc();
                _ = Interlocked.Exchange(ref unavailableFlag, 0);
                return state;
            }
            catch (Exception ex) when (
                ex is FileNotFoundException ||
                ex is DirectoryNotFoundException ||
                ex is UnauthorizedAccessException)
            {
                lastFailureTime = _timeProvider.GetUtcNow();
                _ = Interlocked.Exchange(ref unavailableFlag, 1);
                return new TcpStateInfo();
            }
        }
        else
        {
            return new TcpStateInfo();
        }
    }
}
