// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Kubernetes;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Test.Linux;

public class AcceptanceTest
{
    public AcceptanceTest()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");
    }

    [Fact]
    public async Task LinuxUtilizationProvider_MeasuredWithKubernetesMetadata()
    {
        const ulong LimitsMemory = 2_000;
        const ulong LimitsCpu = 2_000; // 2 cores in millicores
        const ulong RequestsMemory = 1_000;
        const ulong RequestsCpu = 1_000; // 1 core in millicores

        using var environmentSetup = new TestKubernetesEnvironmentSetup();

        environmentSetup.SetupKubernetesEnvironment(
            "ACCEPTANCE_TEST_LINUX_",
            LimitsMemory,
            LimitsCpu,
            RequestsMemory,
            RequestsCpu);

        var logger = new FakeLogger<LinuxUtilizationProvider>();
        var parserMock = new Mock<ILinuxUtilizationParser>();

        ulong initialMemoryUsage = 400UL;
        ulong updatedMemoryUsage = 1200UL;

        var memoryCallCount = 0;
        parserMock.Setup(p => p.GetMemoryUsageInBytes())
            .Returns(() =>
            {
                memoryCallCount++;
                return memoryCallCount <= 1 ? initialMemoryUsage : updatedMemoryUsage;
            });

        long initialHostCpuTime = 1000L;
        long initialCgroupCpuTime = 100L;
        long updatedHostCpuTime = 2000L;
        long updatedCgroupCpuTime = 300L;

        var hostCpuCallCount = 0;
        parserMock.Setup(p => p.GetHostCpuUsageInNanoseconds())
            .Returns(() =>
            {
                hostCpuCallCount++;
                return hostCpuCallCount <= 1 ? initialHostCpuTime : updatedHostCpuTime;
            });

        var cgroupCpuCallCount = 0;
        parserMock.Setup(p => p.GetCgroupCpuUsageInNanoseconds())
            .Returns(() =>
            {
                cgroupCpuCallCount++;
                return cgroupCpuCallCount <= 1 ? initialCgroupCpuTime : updatedCgroupCpuTime;
            });

        parserMock.Setup(p => p.GetHostCpuCount()).Returns(2.0f);
        parserMock.Setup(p => p.GetAvailableMemoryInBytes()).Returns(LimitsMemory);

        var fakeClock = new FakeTimeProvider();
        using var meter = new Meter(nameof(LinuxUtilizationProvider_MeasuredWithKubernetesMetadata));
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>())).Returns(meter);

        using var containerCpuLimitMetricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ContainerCpuLimitUtilization, fakeClock);
        using var containerMemoryLimitMetricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ContainerMemoryLimitUtilization, fakeClock);
        using var containerCpuRequestMetricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ContainerCpuRequestUtilization, fakeClock);
        using var containerMemoryRequestMetricCollector = new MetricCollector<double>(meter, ResourceUtilizationInstruments.ContainerMemoryRequestUtilization, fakeClock);

        var options = Microsoft.Extensions.Options.Options.Create(new ResourceMonitoringOptions
        {
            MemoryConsumptionRefreshInterval = TimeSpan.FromMilliseconds(2),
            CpuConsumptionRefreshInterval = TimeSpan.FromMilliseconds(2),
            UseZeroToOneRangeForMetrics = false
        });

        var kubernetesMetadata = new KubernetesMetadata
        {
            LimitsMemory = LimitsMemory,
            LimitsCpu = LimitsCpu,
            RequestsMemory = RequestsMemory,
            RequestsCpu = RequestsCpu
        };
        var kubernetesResourceQuotaProvider = new KubernetesResourceQuotaProvider(kubernetesMetadata);

        var utilizationProvider = new LinuxUtilizationProvider(
            options,
            parserMock.Object,
            meterFactoryMock.Object,
            kubernetesResourceQuotaProvider,
            logger,
            fakeClock);

        containerCpuLimitMetricCollector.RecordObservableInstruments();
        containerMemoryLimitMetricCollector.RecordObservableInstruments();
        containerCpuRequestMetricCollector.RecordObservableInstruments();
        containerMemoryRequestMetricCollector.RecordObservableInstruments();

        Assert.NotNull(containerCpuLimitMetricCollector.LastMeasurement?.Value);
        Assert.NotNull(containerMemoryLimitMetricCollector.LastMeasurement?.Value);
        Assert.NotNull(containerCpuRequestMetricCollector.LastMeasurement?.Value);
        Assert.NotNull(containerMemoryRequestMetricCollector.LastMeasurement?.Value);

        Assert.True(double.IsNaN(containerCpuLimitMetricCollector.LastMeasurement.Value));
        Assert.True(double.IsNaN(containerCpuRequestMetricCollector.LastMeasurement.Value));

        // Container memory: 400 bytes / 2000 bytes limit = 20%
        Assert.Equal(0.2, containerMemoryLimitMetricCollector.LastMeasurement.Value);

        // Container memory: 400 bytes / 1000 bytes request = 40%
        Assert.Equal(0.4, containerMemoryRequestMetricCollector.LastMeasurement.Value);

        fakeClock.Advance(options.Value.MemoryConsumptionRefreshInterval);
        containerCpuLimitMetricCollector.RecordObservableInstruments();
        containerMemoryLimitMetricCollector.RecordObservableInstruments();
        containerCpuRequestMetricCollector.RecordObservableInstruments();
        containerMemoryRequestMetricCollector.RecordObservableInstruments();

        // CPU calculation: (300-100)/(2000-1000) = 200/1000 = 0.2 = 20%
        // With 2 host CPUs and 2 core limit: 20% * 2/2 = 20%
        Assert.Equal(0.2, containerCpuLimitMetricCollector.LastMeasurement.Value);

        // CPU against 1 core request: 20% * 2/1 = 40%
        Assert.Equal(0.4, containerCpuRequestMetricCollector.LastMeasurement.Value);

        // Container memory: 1200 bytes / 2000 bytes limit = 60%
        Assert.Equal(0.6, containerMemoryLimitMetricCollector.LastMeasurement.Value);

        // Container memory: 1200 bytes / 1000 bytes request = 120%
        Assert.Equal(1, containerMemoryRequestMetricCollector.LastMeasurement.Value);

        await Task.CompletedTask;
    }
}
