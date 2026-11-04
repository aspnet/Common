// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.HealthChecks.Test;

public class TelemetryHealthChecksPublisherTests
{
    private const string HealthReportMetricName = "dotnet.health_check.reports";
    private const string UnhealthyHealthCheckMetricName = "dotnet.health_check.unhealthy_checks";

    public static TheoryData<List<HealthStatus>, bool, int, string, LogLevel, string> PublishAsyncArgs => new()
    {
        {
            new List<HealthStatus> { HealthStatus.Healthy },
            false,
            1,
            "Process reporting healthy: Healthy.",
            LogLevel.Debug,
            HealthStatus.Healthy.ToString()
        },
        {
            new List<HealthStatus> { HealthStatus.Degraded },
            true,
            1,
            "Process reporting unhealthy: Degraded. Health check entries are id0: {status: Degraded, description: desc0}",
            LogLevel.Warning,
            HealthStatus.Degraded.ToString()
        },
        {
            new List<HealthStatus> { HealthStatus.Unhealthy },
            false,
            1,
            "Process reporting unhealthy: Unhealthy. Health check entries are id0: {status: Unhealthy, description: desc0}",
            LogLevel.Warning,
            HealthStatus.Unhealthy.ToString()
        },
        {
            new List<HealthStatus> { HealthStatus.Healthy, HealthStatus.Healthy },
            true,
            0,
            "Process reporting healthy: Healthy.",
            LogLevel.Debug,
            HealthStatus.Healthy.ToString()
        },
        {
            new List<HealthStatus> { HealthStatus.Healthy, HealthStatus.Unhealthy },
            true,
            1,
            "Process reporting unhealthy: Unhealthy. Health check entries are id0: {status: Healthy, description: desc0}, id1: {status: Unhealthy, description: desc1}",
            LogLevel.Warning,
            HealthStatus.Unhealthy.ToString()
        },
        {
            new List<HealthStatus> { HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy },
            false,
            1,
            "Process reporting unhealthy: Unhealthy. Health check entries are " +
            "id0: {status: Healthy, description: desc0}, id1: {status: Degraded, description: desc1}, id2: {status: Unhealthy, description: desc2}",
            LogLevel.Warning,
            HealthStatus.Unhealthy.ToString()
        },
    };

    [Theory]
    [MemberData(nameof(PublishAsyncArgs))]
    public async Task PublishAsync(
        IList<HealthStatus> healthStatuses,
        bool logOnlyUnhealthy,
        int expectedLogCount,
        string expectedLogMessage,
        LogLevel expectedLogLevel,
        string expectedMetricStatus)
    {
        using var meter = new Meter(nameof(PublishAsync));
        var metrics = GetMockedMetrics(meter);
        using var healthyMetricCollector = new MetricCollector<long>(meter, HealthReportMetricName);
        using var unhealthyMetricCollector = new MetricCollector<long>(meter, UnhealthyHealthCheckMetricName);

        var logger = new FakeLogger<TelemetryHealthCheckPublisher>();
        var collector = logger.Collector;

        var options = Options.Options.Create(new TelemetryHealthCheckPublisherOptions
        {
            LogOnlyUnhealthy = logOnlyUnhealthy,
        });

        var publisher = new TelemetryHealthCheckPublisher(metrics, logger, options);

        await publisher.PublishAsync(CreateHealthReport(healthStatuses), CancellationToken.None);
        Assert.Equal(expectedLogCount, collector.Count);
        if (expectedLogCount > 0)
        {
            Assert.Equal(expectedLogMessage, collector.LatestRecord.Message);
            Assert.Equal(expectedLogLevel, collector.LatestRecord.Level);
        }

        var latest = healthyMetricCollector.LastMeasurement;

        Assert.NotNull(latest);

        latest.Value.Should().Be(1);
        latest.Tags.Should().ContainKey("dotnet.health_check.status").WhoseValue.Should().Be(expectedMetricStatus);

        var unhealthyCounters = unhealthyMetricCollector.GetMeasurementSnapshot();

        for (int i = 0; i < healthStatuses.Count; i++)
        {
            var healthStatus = healthStatuses[i];
            if (healthStatus != HealthStatus.Healthy)
            {
                Assert.Equal(1, GetValue(unhealthyCounters, GetKey(i), healthStatuses[i].ToString()));
            }
        }
    }

    [Fact]
    public void Metrics_HaveDescriptions()
    {
        using var meter = new Meter(nameof(Metrics_HaveDescriptions));
        _ = GetMockedMetrics(meter);
        using var healthReportMetricCollector = new MetricCollector<long>(meter, HealthReportMetricName);
        using var unhealthyHealthCheckMetricCollector = new MetricCollector<long>(meter, UnhealthyHealthCheckMetricName);

        Assert.Equal(
            "Number of times a health report reported the health status of an application.",
            healthReportMetricCollector.Instrument?.Description);
        Assert.Equal(
            "Number of times a health check reported the health status of an application as Degraded or Unhealthy.",
            unhealthyHealthCheckMetricCollector.Instrument?.Description);
    }

    [Fact]
    public void Ctor_ThrowsWhenOptionsValueNull()
    {
        using var meter = new Meter(nameof(Ctor_ThrowsWhenOptionsValueNull));
        var metrics = GetMockedMetrics(meter);
        var logger = new FakeLogger<TelemetryHealthCheckPublisher>();

        Assert.Throws<ArgumentException>(() => new TelemetryHealthCheckPublisher(metrics, logger, Options.Options.Create<TelemetryHealthCheckPublisherOptions>(null!)));
    }

    [Fact]
    public void Counters_HaveUnits()
    {
        using var meter = new Meter(nameof(Counters_HaveUnits));
        _ = GetMockedMetrics(meter);
        using var healthReportCollector = new MetricCollector<long>(meter, HealthReportMetricName);
        using var unhealthyHealthCheckCollector = new MetricCollector<long>(meter, UnhealthyHealthCheckMetricName);

        Assert.Equal("{report}", healthReportCollector.Instrument?.Unit);
        Assert.Equal("{unhealthy_check}", unhealthyHealthCheckCollector.Instrument?.Unit);
    }

    private static long GetValue(IReadOnlyCollection<CollectedMeasurement<long>> counters, string healthy, string status)
    {
        foreach (var counter in counters)
        {
            if (counter!.Tags["dotnet.health_check.name"]?.ToString() == healthy &&
                counter!.Tags["dotnet.health_check.status"]?.ToString() == status)
            {
                return counter.Value;
            }
        }

        return 0;
    }

    private static HealthReport CreateHealthReport(IEnumerable<HealthStatus> healthStatuses)
    {
        var healthStatusRecords = new Dictionary<string, HealthReportEntry>();

        int index = 0;
        foreach (var status in healthStatuses)
        {
            var entry = new HealthReportEntry(status, $"desc{index}", TimeSpan.Zero, null, null);
            healthStatusRecords.Add(GetKey(index), entry);
            index++;
        }

        return new HealthReport(healthStatusRecords, TimeSpan.Zero);
    }

    private static string GetKey(int index) => $"id{index}";

    private static HealthCheckMetrics GetMockedMetrics(Meter meter)
    {
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        return new HealthCheckMetrics(meterFactoryMock.Object);
    }
}
