// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Shared.Instruments;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Network;

internal sealed class WindowsNetworkMetrics
{
    private readonly ITcpStateInfoProvider _tcpStateInfoProvider;

    public WindowsNetworkMetrics(IMeterFactory meterFactory, ITcpStateInfoProvider tcpStateInfoProvider)
    {
        _tcpStateInfoProvider = tcpStateInfoProvider;

#pragma warning disable CA2000 // Dispose objects before losing scope
        // We don't dispose the meter because IMeterFactory handles that
        // It's a false-positive, see: https://github.com/dotnet/roslyn-analyzers/issues/6912.
        // Related documentation: https://github.com/dotnet/docs/pull/37170
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

    private IEnumerable<Measurement<long>> GetMeasurements()
    {
        const string NetworkTypeKey = "network.type";
        const string NetworkStateKey = "system.network.state";

        // These are covered in https://github.com/open-telemetry/semantic-conventions/blob/main/docs/rpc/rpc-metrics.md#attributes:
        KeyValuePair<string, object?> tcpVersionFourTag = new(NetworkTypeKey, "ipv4");
        KeyValuePair<string, object?> tcpVersionSixTag = new(NetworkTypeKey, "ipv6");

        List<Measurement<long>> measurements = new(24);

        // IPv4:
        TcpStateInfo stateV4 = _tcpStateInfoProvider.GetIpV4TcpStateInfo();
        measurements.Add(new Measurement<long>(stateV4.ClosedCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "close") }));
        measurements.Add(new Measurement<long>(stateV4.ListenCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "listen") }));
        measurements.Add(new Measurement<long>(stateV4.SynSentCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "syn_sent") }));
        measurements.Add(new Measurement<long>(stateV4.SynRcvdCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "syn_recv") }));
        measurements.Add(new Measurement<long>(stateV4.EstabCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "established") }));
        measurements.Add(new Measurement<long>(stateV4.FinWait1Count, new TagList { tcpVersionFourTag, new(NetworkStateKey, "fin_wait_1") }));
        measurements.Add(new Measurement<long>(stateV4.FinWait2Count, new TagList { tcpVersionFourTag, new(NetworkStateKey, "fin_wait_2") }));
        measurements.Add(new Measurement<long>(stateV4.CloseWaitCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "close_wait") }));
        measurements.Add(new Measurement<long>(stateV4.ClosingCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "closing") }));
        measurements.Add(new Measurement<long>(stateV4.LastAckCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "last_ack") }));
        measurements.Add(new Measurement<long>(stateV4.TimeWaitCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "time_wait") }));
        measurements.Add(new Measurement<long>(stateV4.DeleteTcbCount, new TagList { tcpVersionFourTag, new(NetworkStateKey, "delete") }));

        // IPv6:
        TcpStateInfo stateV6 = _tcpStateInfoProvider.GetIpV6TcpStateInfo();
        measurements.Add(new Measurement<long>(stateV6.ClosedCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "close") }));
        measurements.Add(new Measurement<long>(stateV6.ListenCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "listen") }));
        measurements.Add(new Measurement<long>(stateV6.SynSentCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "syn_sent") }));
        measurements.Add(new Measurement<long>(stateV6.SynRcvdCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "syn_recv") }));
        measurements.Add(new Measurement<long>(stateV6.EstabCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "established") }));
        measurements.Add(new Measurement<long>(stateV6.FinWait1Count, new TagList { tcpVersionSixTag, new(NetworkStateKey, "fin_wait_1") }));
        measurements.Add(new Measurement<long>(stateV6.FinWait2Count, new TagList { tcpVersionSixTag, new(NetworkStateKey, "fin_wait_2") }));
        measurements.Add(new Measurement<long>(stateV6.CloseWaitCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "close_wait") }));
        measurements.Add(new Measurement<long>(stateV6.ClosingCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "closing") }));
        measurements.Add(new Measurement<long>(stateV6.LastAckCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "last_ack") }));
        measurements.Add(new Measurement<long>(stateV6.TimeWaitCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "time_wait") }));
        measurements.Add(new Measurement<long>(stateV6.DeleteTcbCount, new TagList { tcpVersionSixTag, new(NetworkStateKey, "delete") }));

        return measurements;
    }
}
