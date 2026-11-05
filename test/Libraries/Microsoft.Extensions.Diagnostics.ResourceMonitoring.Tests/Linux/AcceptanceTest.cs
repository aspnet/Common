// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.IO;
#if !NET10_0_OR_GREATER
using System.Linq;
#endif
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Shared.Instruments;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

public sealed class AcceptanceTest
{
    [Fact]
    public void Adding_Linux_Resource_Utilization_Allows_To_Query_Snapshot_Provider()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        using var services = new ServiceCollection()
            .AddResourceMonitoring()
            .BuildServiceProvider();

        var provider = services.GetRequiredService<ISnapshotProvider>();

        Assert.NotEqual(default, provider.Resources);
        Assert.NotEqual(default, provider.GetSnapshot());
    }

    [Fact]
    [SuppressMessage("Minor Code Smell", "S3257:Declarations and initializations should be as concise as possible", Justification = "Broken analyzer.")]
    public void Adding_Linux_Resource_Utilization_Can_Be_Configured_With_Section()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);

        var config = new KeyValuePair<string, string?>[]
            {
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.CpuConsumptionRefreshInterval)}", cpuRefresh.ToString()),
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.MemoryConsumptionRefreshInterval)}", memoryRefresh.ToString()),
            };

        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build()
            .GetSection(nameof(ResourceMonitoringOptions));

        using var services = new ServiceCollection()
            .AddResourceMonitoring(x => x.ConfigureMonitor(section))
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<ResourceMonitoringOptions>>();

        Assert.NotNull(options.Value);
        Assert.Equal(cpuRefresh, options.Value.CpuConsumptionRefreshInterval);
        Assert.Equal(memoryRefresh, options.Value.MemoryConsumptionRefreshInterval);
    }

    [Fact]
    public void Adding_Linux_Resource_Utilization_Can_Be_Configured_With_Action()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);

        using var services = new ServiceCollection()
            .AddResourceMonitoring(x => x.ConfigureMonitor(options =>
            {
                options.CpuConsumptionRefreshInterval = cpuRefresh;
                options.MemoryConsumptionRefreshInterval = memoryRefresh;
            }))
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<ResourceMonitoringOptions>>();

        Assert.NotNull(options.Value);
        Assert.Equal(cpuRefresh, options.Value.CpuConsumptionRefreshInterval);
        Assert.Equal(memoryRefresh, options.Value.MemoryConsumptionRefreshInterval);
    }

    [Fact]
    [SuppressMessage("Minor Code Smell", "S3257:Declarations and initializations should be as concise as possible", Justification = "Broken analyzer.")]
    public void Adding_Linux_Resource_Utilization_With_Section_Registers_SnapshotProvider_Cgroupv1()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);

        var config = new KeyValuePair<string, string?>[]
            {
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.CpuConsumptionRefreshInterval)}", cpuRefresh.ToString()),
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.MemoryConsumptionRefreshInterval)}", memoryRefresh.ToString()),
            };

        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build()
            .GetSection(nameof(ResourceMonitoringOptions));

        using var services = new ServiceCollection()
            .AddSingleton<IUserHz>(new FakeUserHz(100))
            .AddSingleton<IFileSystem>(new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
            {
                { new FileInfo("/sys/fs/cgroup/memory/memory.limit_in_bytes"), "100000" },
                { new FileInfo("/proc/stat"), "cpu  10 10 10 10 10 10 10 10 10 10"},
                { new FileInfo("/sys/fs/cgroup/cpuacct/cpuacct.usage"), "102312"},
                { new FileInfo("/proc/meminfo"), "MemTotal: 102312 kB"},
                { new FileInfo("/sys/fs/cgroup/cpuset/cpuset.cpus"), "0-19"},
                { new FileInfo("/sys/fs/cgroup/cpu/cpu.cfs_quota_us"), "12"},
                { new FileInfo("/sys/fs/cgroup/cpu/cpu.cfs_period_us"), "6"},
                { new FileInfo("/sys/fs/cgroup/cpu/cpu.shares"), "1024"}
            }))
            .AddResourceMonitoring(x => x.ConfigureMonitor(section))

            // Ingesting LinuxUtilizationParser with cgroup v1 support.
            .Replace(ServiceDescriptor.Singleton<ILinuxUtilizationParser, LinuxUtilizationParserCgroupV1>())
            .BuildServiceProvider();

        var provider = services.GetService<ISnapshotProvider>();
        Assert.NotNull(provider);
        Assert.Equal(1, provider.Resources.GuaranteedCpuUnits); // hack to make hardcoded calculation in resource utilization main package work.
        Assert.Equal(2.0d, provider.Resources.MaximumCpuUnits); // read from cpuset.cpus
        Assert.Equal(100_000UL, provider.Resources.GuaranteedMemoryInBytes); // read from memory.limit_in_bytes

        // The main usage of this library is containers.
        // To not break the contaract with the main package, we need to set the maximum memory to the same value as the guaranteed memory.
        Assert.Equal(100_000UL, provider.Resources.MaximumMemoryInBytes);
    }

    [Fact]
    [SuppressMessage("Minor Code Smell", "S3257:Declarations and initializations should be as concise as possible", Justification = "Broken analyzer.")]
    public void Adding_Linux_Resource_Utilization_With_Section_Registers_SnapshotProvider_Cgroupv2()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);

        var config = new KeyValuePair<string, string?>[]
            {
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.CpuConsumptionRefreshInterval)}", cpuRefresh.ToString()),
                new($"{nameof(ResourceMonitoringOptions)}:{nameof(ResourceMonitoringOptions.MemoryConsumptionRefreshInterval)}", memoryRefresh.ToString()),
            };

        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build()
            .GetSection(nameof(ResourceMonitoringOptions));

        using var services = new ServiceCollection()
            .AddSingleton<IUserHz>(new FakeUserHz(100))
            .AddSingleton<IFileSystem>(new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
            {
                { new FileInfo("/proc/self/cgroup"), "0::/" },
                { new FileInfo("/proc/stat"), "cpu  10 10 10 10 10 10 10 10 10 10"},
                { new FileInfo("/sys/fs/cgroup/cpu.stat"), "usage_usec 102312\nnr_periods 50"},
                { new FileInfo("/proc/meminfo"), "MemTotal: 102312 kB"},
                { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), "0-1"},
                { new FileInfo("/sys/fs/cgroup/cpu.max"), "20000 100000"},
                { new FileInfo("/sys/fs/cgroup/cpu.weight"), "17"},
                { new FileInfo("/sys/fs/cgroup/memory.max"), "100000" }
            }))
            .AddResourceMonitoring(x => x.ConfigureMonitor(section))

            // Ingesting LinuxUtilizationParser with cgroup v2 support.
            .Replace(ServiceDescriptor.Singleton<ILinuxUtilizationParser, LinuxUtilizationParserCgroupV2>())
            .BuildServiceProvider();

        var provider = services.GetService<ISnapshotProvider>();
        Assert.NotNull(provider);
        Assert.Equal(0.1, Math.Round(provider.Resources.GuaranteedCpuUnits, 1)); // hack to make hardcoded calculation in resource utilization main package work.
        Assert.Equal(0.2d, Math.Round(provider.Resources.MaximumCpuUnits, 1)); // read from cpuset.cpus
        Assert.Equal(100_000UL, provider.Resources.GuaranteedMemoryInBytes); // read from memory.max

        // The main usage of this library is containers.
        // To not break the contract with the main package, we need to set the maximum memory value to the guaranteed memory.
        Assert.Equal(100_000UL, provider.Resources.MaximumMemoryInBytes);
    }

    [Fact]
    public Task ResourceUtilizationTracker_And_Metrics_Report_Same_Values_With_Cgroupsv1()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);
        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory/memory.limit_in_bytes"), "100000" },
            { new FileInfo("/sys/fs/cgroup/memory/memory.usage_in_bytes"), "450000" },
            { new FileInfo("/proc/stat"), "cpu  10 10 10 10 10 10 10 10 10 10"},
            { new FileInfo("/sys/fs/cgroup/cpuacct/cpuacct.usage"), "102312"},
            { new FileInfo("/proc/meminfo"), "MemTotal: 102312 kB"},
            { new FileInfo("/sys/fs/cgroup/cpuset/cpuset.cpus"), "0-19"},
            { new FileInfo("/sys/fs/cgroup/cpu/cpu.cfs_quota_us"), "24"},
            { new FileInfo("/sys/fs/cgroup/cpu/cpu.cfs_period_us"), "6"},
            { new FileInfo("/sys/fs/cgroup/cpu/cpu.shares"), "2048"},
            { new FileInfo("/sys/fs/cgroup/memory/memory.stat"), "total_inactive_file 100"},
        });

        using var listener = new MeterListener();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cpuUserTime = 0.0d;
        var cpuKernelTime = 0.0d;
        var cpuFromGauge = 0.0d;
        var cpuLimitFromGauge = 0.0d;
        var cpuRequestFromGauge = 0.0d;
        var memoryFromGauge = 0.0d;
        var memoryLimitFromGauge = 0.0d;
        using var e = new ManualResetEventSlim();

        object? meterScope = null;
        listener.InstrumentPublished = (Instrument instrument, MeterListener meterListener) =>
            OnInstrumentPublished(instrument, meterListener, meterScope);
        listener.SetMeasurementEventCallback<double>((m, f, tags, _) =>
            OnMeasurementReceived(
                m,
                f,
                tags,
                ref cpuUserTime,
                ref cpuKernelTime,
                ref cpuFromGauge,
                ref cpuLimitFromGauge,
                ref cpuRequestFromGauge,
                ref memoryFromGauge,
                ref memoryLimitFromGauge));
        listener.Start();

        using var host = FakeHost.CreateBuilder()
            .ConfigureServices(x =>
                x.AddLogging()
                .AddSingleton<TimeProvider>(clock)
                .AddSingleton<IUserHz>(new FakeUserHz(100))
                .AddSingleton<IFileSystem>(fileSystem)
                .AddSingleton<IResourceUtilizationPublisher>(new GenericPublisher(_ => e.Set()))
                .AddResourceMonitoring()
                .Replace(ServiceDescriptor.Singleton<ILinuxUtilizationParser, LinuxUtilizationParserCgroupV1>()))
            .Build();

        meterScope = host.Services.GetRequiredService<IMeterFactory>();
        var tracker = host.Services.GetService<IResourceMonitor>();
        Assert.NotNull(tracker);

        _ = host.RunAsync();

        listener.RecordObservableInstruments();

        var utilization = tracker.GetUtilization(TimeSpan.FromSeconds(5));

        Assert.Equal(0, utilization.CpuUsedPercentage);
        Assert.Equal(100, utilization.MemoryUsedPercentage);
        Assert.True(double.IsNaN(cpuFromGauge));

        // gauge multiplied by 100 because gauges are in range [0, 1], and utilization is in range [0, 100]
        Assert.Equal(utilization.MemoryUsedPercentage, memoryFromGauge * 100);

        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/memory/memory.usage_in_bytes"), "50100");
        fileSystem.ReplaceFileContent(new FileInfo("/proc/stat"), "cpu  11 10 10 10 10 10 10 10 10 10");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/cpuacct/cpuacct.usage"), "112312");

        clock.Advance(TimeSpan.FromSeconds(6));
        listener.RecordObservableInstruments();

        e.Wait();

        utilization = tracker.GetUtilization(TimeSpan.FromSeconds(5));

        Assert.Equal(1, utilization.CpuUsedPercentage);
        Assert.Equal(50, utilization.MemoryUsedPercentage);
        Assert.Equal(0.5, cpuLimitFromGauge * 100);
        Assert.Equal(utilization.CpuUsedPercentage, cpuRequestFromGauge * 100);
        Assert.Equal(utilization.MemoryUsedPercentage, memoryLimitFromGauge * 100);
        Assert.Equal(utilization.CpuUsedPercentage, cpuFromGauge * 100);
        Assert.Equal(utilization.MemoryUsedPercentage, memoryFromGauge * 100);

        return Task.CompletedTask;
    }

    [Fact]
    public Task ResourceUtilizationTracker_And_Metrics_Report_Same_Values_With_Cgroupsv2()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var cpuRefresh = TimeSpan.FromMinutes(13);
        var memoryRefresh = TimeSpan.FromMinutes(14);
        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/self/cgroup"), "0::/" },
            { new FileInfo("/proc/stat"), "cpu  10 10 10 10 10 10 10 10 10 10"},
            { new FileInfo("/sys/fs/cgroup/cpu.stat"), "usage_usec 102\nnr_periods 50"},
            { new FileInfo("/sys/fs/cgroup/memory.max"), "1048576" },
            { new FileInfo("/proc/meminfo"), "MemTotal: 1024 kB"},
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), "0-19"},
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "40000 10000"},
            { new FileInfo("/sys/fs/cgroup/cpu.weight"), "173"}, // equals to ~2048 CPU shares (cgroups v1) which is ~2 CPU units (2 * 1024), so have to use Math.Round() in Assertions down below.
        });

        using var listener = new MeterListener();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cpuUserTime = 0.0d;
        var cpuKernelTime = 0.0d;
        var cpuFromGauge = 0.0d;
        var cpuLimitFromGauge = 0.0d;
        var cpuRequestFromGauge = 0.0d;
        var memoryFromGauge = 0.0d;
        var memoryLimitFromGauge = 0.0d;
        long memoryUsageFromGauge = 0;
        using var e = new ManualResetEventSlim();

        object? meterScope = null;
        listener.InstrumentPublished = (Instrument instrument, MeterListener meterListener) =>
            OnInstrumentPublished(instrument, meterListener, meterScope);
        listener.SetMeasurementEventCallback<double>((m, f, tags, _) =>
            OnMeasurementReceived(
                m,
                f,
                tags,
                ref cpuUserTime,
                ref cpuKernelTime,
                ref cpuFromGauge,
                ref cpuLimitFromGauge,
                ref cpuRequestFromGauge,
                ref memoryFromGauge,
                ref memoryLimitFromGauge));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == ResourceUtilizationInstruments.ContainerMemoryUsage)
            {
                memoryUsageFromGauge = value;
            }
        });
        listener.Start();

        using var host = FakeHost.CreateBuilder()
            .ConfigureServices(x =>
                x.AddLogging()
                .AddSingleton<TimeProvider>(clock)
                .AddSingleton<IUserHz>(new FakeUserHz(100))
                .AddSingleton<IFileSystem>(fileSystem)
                .AddSingleton<IResourceUtilizationPublisher>(new GenericPublisher(_ => e.Set()))
                .AddResourceMonitoring()
                .Replace(ServiceDescriptor.Singleton<ILinuxUtilizationParser, LinuxUtilizationParserCgroupV2>()))
            .Build();

        meterScope = host.Services.GetRequiredService<IMeterFactory>();
        var tracker = host.Services.GetService<IResourceMonitor>();
        Assert.NotNull(tracker);

        _ = host.RunAsync();

        listener.RecordObservableInstruments();

        var utilization = tracker.GetUtilization(TimeSpan.FromSeconds(5));

        Assert.Equal(0, utilization.CpuUsedPercentage);
        Assert.Equal(100, utilization.MemoryUsedPercentage);
        Assert.True(double.IsNaN(cpuFromGauge));

        // gauge multiplied by 100 because gauges are in range [0, 1], and utilization is in range [0, 100]
        Assert.Equal(utilization.MemoryUsedPercentage, memoryFromGauge * 100);

        fileSystem.ReplaceFileContent(new FileInfo("/proc/stat"), "cpu  11 10 10 10 10 10 10 10 10 10");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/cpu.stat"), "usage_usec 112\nnr_periods 56");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/memory.current"), "524298");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 10");

        clock.Advance(TimeSpan.FromSeconds(6));
        listener.RecordObservableInstruments();

        e.Wait();

        utilization = tracker.GetUtilization(TimeSpan.FromSeconds(5));

        var roundedCpuUsedPercentage = Math.Round(utilization.CpuUsedPercentage, 1);

        Assert.Equal(1, roundedCpuUsedPercentage);
        Assert.Equal(50, utilization.MemoryUsedPercentage);
        Assert.Equal(524288, memoryUsageFromGauge);
        Assert.Equal(0.5, cpuLimitFromGauge * 100);
        Assert.Equal(roundedCpuUsedPercentage, Math.Round(cpuRequestFromGauge * 100));
        Assert.Equal(utilization.MemoryUsedPercentage, memoryLimitFromGauge * 100);
        Assert.Equal(roundedCpuUsedPercentage, Math.Round(cpuFromGauge * 100));
        Assert.Equal(utilization.MemoryUsedPercentage, memoryFromGauge * 100);

        return Task.CompletedTask;
    }

    [Fact]
    public Task ResourceUtilizationTracker_And_Metrics_Report_Same_Values_With_Cgroupsv2_Using_LinuxCalculationV2()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux specific tests");

        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/self/cgroup"), "0::/fakeslice"},
            { new FileInfo("/proc/stat"), "cpu  10 10 10 10 10 10 10 10 10 10"},
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.stat"), "usage_usec 1020000\nnr_periods 50"},
            { new FileInfo("/sys/fs/cgroup/memory.max"), "1048576" },
            { new FileInfo("/proc/meminfo"), "MemTotal: 1024 kB"},
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), "0-19"},
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.max"), "40000 10000"},
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.weight"), "173"},
        });

        using var listener = new MeterListener();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cpuFromGauge = 0.0d;
        var cpuLimitFromGauge = 0.0d;
        var cpuUserTime = 0.0d;
        var cpuKernelTime = 0.0d;
        var cpuRequestFromGauge = 0.0d;
        var memoryFromGauge = 0.0d;
        var memoryLimitFromGauge = 0.0d;

        object? meterScope = null;
        listener.InstrumentPublished = (Instrument instrument, MeterListener meterListener) =>
            OnInstrumentPublished(instrument, meterListener, meterScope);
        listener.SetMeasurementEventCallback<double>((m, f, tags, _) =>
            OnMeasurementReceived(
                m,
                f,
                tags,
                ref cpuUserTime,
                ref cpuKernelTime,
                ref cpuFromGauge,
                ref cpuLimitFromGauge,
                ref cpuRequestFromGauge,
                ref memoryFromGauge,
                ref memoryLimitFromGauge));
        listener.Start();

        using IHost host = FakeHost.CreateBuilder()
            .ConfigureServices(x =>
                x.AddLogging()
                .AddSingleton<TimeProvider>(clock)
                .AddSingleton<IUserHz>(new FakeUserHz(100))
                .AddSingleton<IFileSystem>(fileSystem)
                .AddResourceMonitoring(x => x.ConfigureMonitor(options =>
                    {
                        options.UseLinuxCalculationV2 = true;
                    }))
                .Replace(ServiceDescriptor.Singleton<ILinuxUtilizationParser, LinuxUtilizationParserCgroupV2>()))
            .Build();

        meterScope = host.Services.GetRequiredService<IMeterFactory>();

        _ = host.RunAsync();

        listener.RecordObservableInstruments();

        fileSystem.ReplaceFileContent(new FileInfo("/proc/stat"), "cpu  11 10 10 10 10 10 10 10 10 10");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/fakeslice/cpu.stat"), "usage_usec 1120800\nnr_periods 56");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/memory.current"), "524298");
        fileSystem.ReplaceFileContent(new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 10");

        clock.Advance(TimeSpan.FromSeconds(6));
        listener.RecordObservableInstruments();

        Assert.Equal(42, Math.Round(cpuLimitFromGauge * 100));
        Assert.Equal(84, Math.Round(cpuRequestFromGauge * 100));
        Assert.Equal(168, Math.Round(cpuUserTime * 100));
        Assert.Equal(81, Math.Round(cpuKernelTime * 100));

        return Task.CompletedTask;
    }

    private static void OnInstrumentPublished(Instrument instrument, MeterListener meterListener, object? meterScope)
    {
        if (!ReferenceEquals(instrument.Meter.Scope, meterScope))
        {
            return;
        }

#pragma warning disable S1067 // Expressions should not be too complex
        if (instrument.Name == ResourceUtilizationInstruments.ProcessCpuUtilization ||
            instrument.Name == ResourceUtilizationInstruments.ProcessMemoryUtilization ||
            instrument.Name == ResourceUtilizationInstruments.ContainerCpuTime ||
            instrument.Name == ResourceUtilizationInstruments.ContainerCpuRequestUtilization ||
            instrument.Name == ResourceUtilizationInstruments.ContainerCpuLimitUtilization ||
            instrument.Name == ResourceUtilizationInstruments.ContainerMemoryLimitUtilization ||
            instrument.Name == ResourceUtilizationInstruments.ContainerMemoryUsage)
        {
            meterListener.EnableMeasurementEvents(instrument);
        }
#pragma warning restore S1067 // Expressions should not be too complex
    }

#pragma warning disable S107 // Methods should not have too many parameters
    private static void OnMeasurementReceived(Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        ref double cpuUserTime,
        ref double cpuKernelTime,
        ref double cpuFromGauge,
        ref double cpuLimitFromGauge,
        ref double cpuRequestFromGauge,
        ref double memoryFromGauge,
        ref double memoryLimitFromGauge)
#pragma warning restore S107 // Methods should not have too many parameters
    {
        if (instrument.Name == ResourceUtilizationInstruments.ProcessCpuUtilization)
        {
            cpuFromGauge = value;
        }
        else if (instrument.Name == ResourceUtilizationInstruments.ProcessMemoryUtilization)
        {
            memoryFromGauge = value;
        }
        else if (instrument.Name == ResourceUtilizationInstruments.ContainerCpuTime)
        {
            var tagsArray = tags.ToArray();
            if (tagsArray.Contains(new KeyValuePair<string, object?>("cpu.mode", "user")))
            {
                cpuUserTime = value;
            }
            else if (tagsArray.Contains(new KeyValuePair<string, object?>("cpu.mode", "system")))
            {
                cpuKernelTime = value;
            }
        }
        else if (instrument.Name == ResourceUtilizationInstruments.ContainerCpuLimitUtilization)
        {
            cpuLimitFromGauge = value;
        }
        else if (instrument.Name == ResourceUtilizationInstruments.ContainerCpuRequestUtilization)
        {
            cpuRequestFromGauge = value;
        }
        else if (instrument.Name == ResourceUtilizationInstruments.ContainerMemoryLimitUtilization)
        {
            memoryLimitFromGauge = value;
        }
    }
}
