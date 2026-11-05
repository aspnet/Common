// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Interop;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;
using static Microsoft.Extensions.Diagnostics.ResourceMonitoring.Windows.Interop.JobObjectInfo;

namespace Microsoft.Extensions.Diagnostics.HealthChecks.Test;

public class ResourceHealthCheckExtensionsTests
{
    public ResourceHealthCheckExtensionsTests()
    {
        Assert.SkipUnless(!RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "Skipped on macOS");
    }

    [Fact]
    public async Task AddResourceHealthCheck()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow);

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithCustomResourceMonitorAddedAfterInternalResourceMonitor_OverridesIt()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow).Services
            .AddSingleton(dataTracker.Object);

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_RegistersInternalResourceMonitoring()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow);

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);

        // check Resource Monitoring Options:
        var resourceMonitoringOptions = serviceProvider.GetRequiredService<IOptions<ResourceMonitoringOptions>>().Value;
        Assert.NotNull(resourceMonitoringOptions);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithTags()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow, "test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithTags_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck("test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithTags_WithCustomResourceMonitorAddedAfterInternalResourceMonitor_OverridesIt()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow, "test").Services
            .AddSingleton(dataTracker.Object);

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithTagsEnumerable()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(options =>
                options.SamplingWindow = samplingWindow, new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithTagsEnumerable_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithAction()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
                o.SamplingWindow = samplingWindow;
            });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithAction_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
            });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithActionAndTags()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
                o.SamplingWindow = samplingWindow;
            },
            "test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithActionAndTags_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
            },
            "test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithActionAndTagsEnumerable()
    {
        var dataTracker = new Mock<IResourceMonitor>();
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
                o.SamplingWindow = samplingWindow;
            },
            new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithActionAndTagsEnumerable_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
            },
            new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithConfigurationSection()
    {
        var dataTracker = new Mock<IResourceMonitor>();

        TimeSpan samplingWindow = TimeSpan.FromSeconds(5);
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)));

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithConfigurationSection_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)));

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithConfigurationSectionAndTags()
    {
        var dataTracker = new Mock<IResourceMonitor>();

        TimeSpan samplingWindow = TimeSpan.FromSeconds(5);
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(
                SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)),
                "test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithConfigurationSectionAndTags_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)),
                "test");

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public async Task AddResourceHealthCheck_WithConfigurationSectionAndTagsEnumerable()
    {
        var dataTracker = new Mock<IResourceMonitor>();

        TimeSpan samplingWindow = TimeSpan.FromSeconds(5);
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddSingleton(dataTracker.Object)
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(
                SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)),
                new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        HealthCheckService service = serviceProvider.GetRequiredService<HealthCheckService>();
        _ = await service.CheckHealthAsync();
        dataTracker.Verify(tracker => tracker.GetUtilization(samplingWindow), Times.Once);
    }

    [Fact]
    public void AddResourceHealthCheck_WithConfigurationSectionAndTagsEnumerable_RegistersInternalResourceMonitoring()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddLogging()
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(
                SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)),
                new List<string> { "test" });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

        IResourceMonitor? resourceMonitor = serviceProvider.GetService<IResourceMonitor>();
        Assert.NotNull(resourceMonitor);
    }

    [Fact]
    public void ConfigureResourceUtilizationHealthCheck_WithAction()
    {
        TimeSpan samplingWindow = TimeSpan.FromSeconds(1);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(o =>
            {
                o.CpuThresholds = new ResourceUsageThresholds { DegradedUtilizationPercentage = 0.2, UnhealthyUtilizationPercentage = 0.4 };
                o.SamplingWindow = samplingWindow;
            });

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        ResourceUtilizationHealthCheckOptions options = serviceProvider.GetRequiredService<IOptions<ResourceUtilizationHealthCheckOptions>>().Value;

        Assert.Equal(samplingWindow, options.SamplingWindow);
        Assert.Equal(0.2, options.CpuThresholds.DegradedUtilizationPercentage);
        Assert.Equal(0.4, options.CpuThresholds.UnhealthyUtilizationPercentage);
    }

    [Fact]
    public void ConfigureResourceUtilizationHealthCheck_WithConfigurationSection()
    {
        TimeSpan samplingWindow = TimeSpan.FromSeconds(5);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddHealthChecks()
            .AddResourceUtilizationHealthCheck(SetupResourceHealthCheckConfiguration("0.5", "0.7", "0.5", "0.7", "00:00:05").GetSection(nameof(ResourceUtilizationHealthCheckOptions)));

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        ResourceUtilizationHealthCheckOptions options = serviceProvider.GetRequiredService<IOptions<ResourceUtilizationHealthCheckOptions>>().Value;

        Assert.Equal(samplingWindow, options.SamplingWindow);
        Assert.Equal(0.5, options.CpuThresholds.DegradedUtilizationPercentage);
        Assert.Equal(0.7, options.CpuThresholds.UnhealthyUtilizationPercentage);
        Assert.Equal(0.5, options.MemoryThresholds.DegradedUtilizationPercentage);
        Assert.Equal(0.7, options.MemoryThresholds.UnhealthyUtilizationPercentage);
    }

    [Fact]
    public void TestNullChecks()
    {
        Assert.Throws<ArgumentNullException>(() => ResourceUtilizationHealthCheckExtensions.AddResourceUtilizationHealthCheck(null!));
        Assert.Throws<ArgumentNullException>(() => ((IHealthChecksBuilder)null!).AddResourceUtilizationHealthCheck((IEnumerable<string>)null!));
        Assert.Throws<ArgumentNullException>(() => ((IHealthChecksBuilder)null!).AddResourceUtilizationHealthCheck((Action<ResourceUtilizationHealthCheckOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => ((IHealthChecksBuilder)null!).AddResourceUtilizationHealthCheck((IConfigurationSection)null!));
    }

    [Theory]
    [ClassData(typeof(HealthCheckTestData))]
    public async Task TestCpuAndMemoryChecks_WithMetrics(
        HealthStatus expected, double utilization, ulong memoryUsed, ulong totalMemory,
        ResourceUsageThresholds cpuThresholds, ResourceUsageThresholds memoryThresholds,
        string expectedDescription)
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-specific test.");

        var logger = new FakeLogger<WindowsContainerSnapshotProvider>();
        var fakeClock = new FakeTimeProvider();
        var dataTracker = new Mock<IResourceMonitor>();
        SYSTEM_INFO sysInfo = default;
        sysInfo.NumberOfProcessors = 1;
        Mock<ISystemInfo> systemInfoMock = new();
        systemInfoMock.Setup(s => s.GetSystemInfo())
            .Returns(() => sysInfo);
        Mock<IMemoryInfo> memoryInfoMock = new();

        JOBOBJECT_CPU_RATE_CONTROL_INFORMATION cpuLimit = default;
        cpuLimit.CpuRate = 7_000;
        Mock<IJobHandle> jobHandleMock = new();
        jobHandleMock.Setup(j => j.GetJobCpuLimitInfo()).Returns(() => cpuLimit);

        Mock<IProcessInfo> processInfoMock = new();
        var appMemoryUsage = memoryUsed;
        processInfoMock.Setup(p => p.GetMemoryUsage()).Returns(() => appMemoryUsage);
        processInfoMock.Setup(p => p.GetCurrentProcessMemoryUsage()).Returns(() => appMemoryUsage);

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limitInfo = default;
        limitInfo.JobMemoryLimit = new UIntPtr(totalMemory);
        jobHandleMock.Setup(j => j.GetExtendedLimitInfo()).Returns(() => limitInfo);

        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION initialAccountingInfo = default;
        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accountingInfoAfter1Ms = default;
        accountingInfoAfter1Ms.TotalUserTime = (long)(utilization * 100);
        jobHandleMock.SetupSequence(j => j.GetBasicAccountingInfo())
            .Returns(() => initialAccountingInfo) // this is called from the WindowsContainerSnapshotProvider's constructor
            .Returns(() => initialAccountingInfo) // this is called from the WindowsContainerSnapshotProvider's GetCpuTime method
            .Returns(() => accountingInfoAfter1Ms); // this is called from the WindowsContainerSnapshotProvider's CpuPercentage method

        using var meter = new Meter("Microsoft.Extensions.Diagnostics.ResourceMonitoring");
        var meterFactoryMock = new Mock<IMeterFactory>();
        meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);
        var resourceQuotaProvider = new WindowsContainerResourceQuotaProvider(
            memoryInfoMock.Object,
            systemInfoMock.Object,
            () => jobHandleMock.Object);

        var snapshotProvider = new WindowsContainerSnapshotProvider(
            processInfoMock.Object,
            logger,
            meterFactoryMock.Object,
            () => jobHandleMock.Object,
            fakeClock,
            new ResourceMonitoringOptions { CpuConsumptionRefreshInterval = TimeSpan.FromMilliseconds(1) },
            resourceQuotaProvider);

        var checkContext = new HealthCheckContext();
        var checkOptions = new ResourceUtilizationHealthCheckOptions
        {
            CpuThresholds = cpuThresholds,
            MemoryThresholds = memoryThresholds,
            UseObservableResourceMonitoringInstruments = true
        };

        var options = Microsoft.Extensions.Options.Options.Create(checkOptions);
#if !NETFRAMEWORK
        using var healthCheck = new ResourceUtilizationHealthCheck(
            options,
            dataTracker.Object,
            Microsoft.Extensions.Options.Options.Create(new ResourceMonitoringOptions()));
#else
        using var healthCheck = new ResourceUtilizationHealthCheck(
            options,
            dataTracker.Object);
#endif

        // Act
        fakeClock.Advance(TimeSpan.FromMilliseconds(1));
        var healthCheckResult = await healthCheck.CheckHealthAsync(checkContext);

        // Assert
        Assert.Equal(expected, healthCheckResult.Status);
        Assert.NotEmpty(healthCheckResult.Data);
        if (healthCheckResult.Status != HealthStatus.Healthy)
        {
            Assert.Equal(expectedDescription, healthCheckResult.Description);
        }
    }

    private static IConfiguration SetupResourceHealthCheckConfiguration(
        string cpuDegradedThreshold,
        string cpuUnhealthyThreshold,
        string memoryDegradedThreshold,
        string memoryUnhealthyThreshold,
        string samplingWindow)
    {
        ResourceUtilizationHealthCheckOptions resourceHealthCheckOptions;

        var configurationDict = new Dictionary<string, string?>
        {
            {
                $"{nameof(ResourceUtilizationHealthCheckOptions)}:{nameof(resourceHealthCheckOptions.CpuThresholds)}:"
                + $"{nameof(resourceHealthCheckOptions.CpuThresholds.DegradedUtilizationPercentage)}",
                cpuDegradedThreshold
            },
            {
                $"{nameof(ResourceUtilizationHealthCheckOptions)}:{nameof(resourceHealthCheckOptions.CpuThresholds)}:"
                + $"{nameof(resourceHealthCheckOptions.CpuThresholds.UnhealthyUtilizationPercentage)}",
                cpuUnhealthyThreshold
            },
            {
                $"{nameof(ResourceUtilizationHealthCheckOptions)}:{nameof(resourceHealthCheckOptions.MemoryThresholds)}:"
                + $"{nameof(resourceHealthCheckOptions.MemoryThresholds.DegradedUtilizationPercentage)}",
                memoryDegradedThreshold
            },
            {
                $"{nameof(ResourceUtilizationHealthCheckOptions)}:{nameof(resourceHealthCheckOptions.MemoryThresholds)}:"
                +$"{nameof(resourceHealthCheckOptions.MemoryThresholds.UnhealthyUtilizationPercentage)}",
                memoryUnhealthyThreshold
            },
            {
                $"{nameof(ResourceUtilizationHealthCheckOptions)}:{nameof(resourceHealthCheckOptions.SamplingWindow)}", samplingWindow
            }
        };

        return new ConfigurationBuilder().AddInMemoryCollection(configurationDict).Build();
    }
}
