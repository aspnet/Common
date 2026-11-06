// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Shared.Pools;
using Moq;
using VerifyXunit;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

public sealed class LinuxUtilizationParserCgroupV2Tests
{
    public LinuxUtilizationParserCgroupV2Tests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");
    }

    private const string VerifiedDataDirectory = "Verified";

    [Theory]
    [InlineData("DFIJEUWGHFWGBWEFWOMDOWKSLA")]
    [InlineData("")]
    [InlineData("________________________Asdasdasdas          dd")]
    [InlineData(" ")]
    [InlineData("!@#!$%!@")]
    public void Throws_When_Data_Is_Invalid(string line)
    {
        var parser = new LinuxUtilizationParserCgroupV2(new HardcodedValueFileSystem(line), new FakeUserHz(100));

        Assert.Throws<InvalidOperationException>(() => parser.GetHostAvailableMemory());
        Assert.Throws<InvalidOperationException>(() => parser.GetAvailableMemoryInBytes());
        Assert.Throws<InvalidOperationException>(() => parser.GetMemoryUsageInBytes());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupLimitedCpus());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupLimitV2());
        Assert.Throws<InvalidOperationException>(() => parser.GetHostCpuUsageInNanoseconds());
        Assert.Throws<InvalidOperationException>(() => parser.GetHostCpuCount());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupCpuUsageInNanoseconds());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupCpuUsageInNanosecondsAndCpuPeriodsV2());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupRequestCpu());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupRequestCpuV2());
        Assert.Throws<InvalidOperationException>(() => parser.GetCgroupPeriodsIntervalInMicroSecondsV2());
    }

    [Fact]
    public void Can_Read_Host_And_Cgroup_Available_Cpu_Count()
    {
        var parser = new LinuxUtilizationParserCgroupV2(new FileNamesOnlyFileSystem(TestResources.TestFilesLocation), new FakeUserHz(100));
        var hostCpuCount = parser.GetHostCpuCount();
        var cgroupCpuCount = parser.GetCgroupLimitedCpus();

        Assert.Equal(2.0, hostCpuCount);
        Assert.Equal(2.0, cgroupCpuCount);
    }

    [Fact]
    public void Provides_Total_Available_Memory_In_Bytes()
    {
        var fs = new FileNamesOnlyFileSystem(TestResources.TestFilesLocation);
        var parser = new LinuxUtilizationParserCgroupV2(fs, new FakeUserHz(100));

        var totalMem = parser.GetHostAvailableMemory();

        Assert.Equal(16_233_760UL * 1024, totalMem);
    }

    [Theory]
    [InlineData("----------------------")]
    [InlineData("@ @#dddada")]
    [InlineData("1231234124124")]
    [InlineData("1024 KB")]
    [InlineData("1024 KB  d \n\r 1024")]
    [InlineData("\n\r")]
    [InlineData("")]
    [InlineData("Suspicious")]
    [InlineData("string@")]
    [InlineData("string12312")]
    [InlineData("total-inactive-file")]
    [InlineData("total_inactive-file")]
    [InlineData("total_active_file")]
    [InlineData("Total_Inactive_File 2")]
    [InlineData("total_inactive_file:_ 21391")]
    [InlineData("string@ -1")]
    public Task Throws_When_TotalInactiveFile_Is_Invalid(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), content }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetMemoryUsageInBytes());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("----------------------")]
    [InlineData("@ @#dddada")]
    [InlineData("_1231234124124")]
    [InlineData("eee 1024 KB")]
    [InlineData("\n\r")]
    [InlineData("")]
    [InlineData("Suspicious")]
    [InlineData("Suspicious12312312")]
    [InlineData("string@")]
    [InlineData("string12312")]
    public Task Throws_When_UsageInBytes_Is_Invalid(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 14340" },
            { new FileInfo("/sys/fs/cgroup/memory.current"), content }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetMemoryUsageInBytes());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("max\n", 134_796_910_592ul)]
    [InlineData("1000000\n", 1_000_000ul)]
    public void Returns_Available_Memory_When_AvailableMemoryInBytes_Is_Valid(string content, ulong expectedResult)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.max"), content },
            { new FileInfo("/proc/meminfo"), "MemTotal:       131637608 kB" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var result = p.GetAvailableMemoryInBytes();

        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("Suspicious12312312")]
    [InlineData("string@")]
    [InlineData("string12312")]
    public Task Throws_When_AvailableMemoryInBytes_Doesnt_Contain_Just_A_Number(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.max"), content },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetAvailableMemoryInBytes());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Fact]
    public Task Throws_When_UsageInBytes_Doesnt_Contain_A_Number()
    {
        var globPatternForSlices = "*.slice";
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "dasda"},
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetMemoryUsageInBytesFromSlices(globPatternForSlices));

        Assert.NotNull(r);
        return Verifier.Verify(r).UseDirectory(VerifiedDataDirectory);
    }

    [Fact]
    public void Returns_Memory_Usage_When_Memory_Usage_Is_Valid()
    {
        // When memory usage is a positive number
        var globPatternForSlices = "*.slice";
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "5342342"},
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = p.GetMemoryUsageInBytesFromSlices(globPatternForSlices);

        Assert.Equal(5_342_342, r);

        // When memory usage is zero
        f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "0"},
        });

        p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        r = p.GetMemoryUsageInBytesFromSlices(globPatternForSlices);
        Assert.Equal(0, r);
    }

    [Theory]
    [InlineData(104343, 1)]
    [InlineData(23423, 22)]
    [InlineData(10000, 100)]
    public Task Throws_When_Inactive_Memory_Is_Bigger_Than_Total_Memory(int inactive, int total)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), $"inactive_file {inactive}" },
            { new FileInfo("/sys/fs/cgroup/memory.current"), total.ToString(CultureInfo.CurrentCulture) }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetMemoryUsageInBytes());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(inactive, total).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("Mem")]
    [InlineData("MemTotal:")]
    [InlineData("MemTotal: 120")]
    [InlineData("MemTotal: kb")]
    [InlineData("MemTotal: MB")]
    [InlineData("MemTotal: PB")]
    [InlineData("MemTotal: 1024 PB")]
    [InlineData("MemTotal: 1024   ")]
    [InlineData("MemTotal: 1024 @@  ")]
    [InlineData("MemoryTotal: 1024 MB ")]
    [InlineData("MemoryTotal: 123123123123123123")]
    public Task Throws_When_MemInfo_Does_Not_Contain_TotalMemory(string totalMemory)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/meminfo"), totalMemory },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetHostAvailableMemory());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(totalMemory).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("kB", 231, 236_544)]
    [InlineData("MB", 287, 300_941_312)]
    [InlineData("GB", 372, 399_431_958_528)]
    [InlineData("TB", 2, 2_199_023_255_552)]
    public void Transforms_Supported_Units_To_Bytes(string unit, int value, ulong bytes)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/meminfo"), $"MemTotal: {value} {unit}" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var memory = p.GetHostAvailableMemory();

        Assert.Equal(bytes, memory);
    }

    [Theory]
    [InlineData("0-11", 12)]
    [InlineData("0", 1)]
    [InlineData("1000", 1)]
    [InlineData("0,1", 2)]
    [InlineData("0,1,2", 3)]
    [InlineData("0,1,2,4", 4)]
    [InlineData("0,1-2,3", 4)]
    [InlineData("0,1,2-3,4", 5)]
    [InlineData("0-1,2-3", 4)]
    [InlineData("0-1,2-3,4-5", 6)]
    [InlineData("0-2,3-5,6-8", 9)]
    public void Gets_Available_Cpus_From_CpuSetCpus_When_Cpu_Limits_Not_Set(string content, int result)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), content },
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "-1" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var cpus = p.GetCgroupLimitedCpus();

        Assert.Equal(result, cpus);
    }

    [Theory]
    [InlineData("0::/")]
    [InlineData("0::/fakeslice")]
    public void Gets_Available_Cpus_From_CpuSetCpusFromSlices_When_Cpu_Limits_Not_Set(string slicepath)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "200000 100000" },
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.max"), "200000 100000" },
            { new FileInfo("/proc/self/cgroup"), slicepath }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var cpus = p.GetCgroupLimitV2();

        Assert.Equal(2, cpus);
    }

    [Theory]
    [InlineData("100", 1)]
    [InlineData("1", 0.001953125)]
    [InlineData("10000", 256)]
    [InlineData("102", 1.02539062)]
    public void Calculates_Cpu_Request_From_Cpu_WeightInSlices(string content, float result)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.weight"), content },
            { new FileInfo("/proc/self/cgroup"), "0::/fakeslice" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = p.GetCgroupRequestCpuV2();

        Assert.Equal(result, r);
    }

    // Based on https://github.com/kubernetes/website/blob/main/content/en/blog/_posts/2026-01-30-new-cgroup-v1-to-v2-conversion-formula/index.md#new-conversion-formula
    [Theory]
    [InlineData("100", 1)]
    [InlineData("1", 0.001953125)]
    [InlineData("10000", 256)]
    [InlineData("102", 1.02539062)]
    public void Calculates_Cpu_Request_From_Cpu_Weight(string content, float result)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.weight"), content },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = p.GetCgroupRequestCpu();

        Assert.Equal(result, r);
    }

    [Fact]
    public void Gets_Available_Cpus_From_CpuSetCpus_When_Cpu_Max_Set_To_Max_()
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), "0,1,2" },
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "max 100000" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var cpus = p.GetCgroupLimitedCpus();

        Assert.Equal(3, cpus);
    }

    [Theory]
    [InlineData("-11")]
    [InlineData("0-")]
    [InlineData("d-22")]
    [InlineData("22-d")]
    [InlineData("22-18")]
    [InlineData("aaaa")]
    [InlineData("    d  182-1923")]
    [InlineData("")]
    [InlineData("1-18-22")]
    [InlineData("1-18                   \r\n")]
    [InlineData("\r\n")]
    public Task Throws_When_CpuSet_Has_Invalid_Content(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), content },
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "-1" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetHostCpuCount());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Fact]
    public Task Fallsback_To_Cpuset_When_Quota_And_Period_Are_Minus_One_()
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpuset.cpus.effective"), "@" },
            { new FileInfo("/sys/fs/cgroup/cpu.max"), "-1" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetCgroupLimitedCpus());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("dd1d", "18")]
    [InlineData("-18", "18")]
    [InlineData("\r\r\r\r\r", "18")]
    [InlineData("123", "\r\r\r\r\r")]
    [InlineData("-", "d'")]
    [InlineData("-", "d/:")]
    [InlineData("2", "d/:")]
    [InlineData("2d2d2d", "e3")]
    [InlineData("3d", "d3")]
    [InlineData("           12", "eeeee 12")]
    [InlineData("12       ", "")]
    public Task Throws_When_Cgroup_Cpu_Files_Contain_Invalid_Data(string quota, string period)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.max"), $"{quota} {period}"},
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetCgroupLimitedCpus());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(quota, period).UseDirectory(VerifiedDataDirectory);
    }

    [Fact]
    public void Reads_CpuUsage_When_Valid_Input()
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/stat"), "cpu  2569530 36700 245693 4860924 82283 0 4360 0 0 0" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = p.GetHostCpuUsageInNanoseconds();

        Assert.Equal(77_994_900_000_000, r);
    }

    [Theory]
    [InlineData("0::/", "usage_usec 222222\nnr_periods 50", "222222000", "50")]
    [InlineData("0::/fakeslice", "usage_usec 222222\nnr_periods 75", "222222000", "75")]
    public void Reads_CpuUsageFromSlices_When_Valid_Input(string slicepath, string content, string expectedUsage, string expectedPeriods)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.stat"), content },
            { new FileInfo("/sys/fs/cgroup/fakeslice/cpu.stat"), content },
            { new FileInfo("/proc/self/cgroup"), slicepath }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var (usage, periods) = p.GetCgroupCpuUsageInNanosecondsAndCpuPeriodsV2();

        Assert.IsType<long>(usage);
        Assert.IsType<long>(periods);
        Assert.Equal(expectedUsage, usage.ToString());
        Assert.Equal(expectedPeriods, periods.ToString());
    }

    [Fact]
    public void Reads_TotalMemory_When_Valid_Input()
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.current"), "32493514752" },
            { new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 100" }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetMemoryUsageInBytes());

        Assert.Null(r);
    }

    [Theory]
    [InlineData("2569530367000")]
    [InlineData("  2569530 36700 245693 4860924 82283 0 4360 0dsa")]
    [InlineData("asdasd  2569530 36700 245693 4860924 82283 0 4360 0 0 0")]
    [InlineData("  2569530 36700 245693")]
    [InlineData("cpu  2569530 36700 245693")]
    [InlineData("  2")]
    public Task Throws_When_CpuUsage_Invalid(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/stat"), content }
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetHostCpuUsageInNanoseconds());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("usage_", 12222)]
    [InlineData("dasd", -1)]
    [InlineData("@#dddada", 342322)]
    public Task Throws_When_CpuAcctUsage_Has_Invalid_Content_Both_Parts(string content, int value)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.stat"), $"{content} {value}"},
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetCgroupCpuUsageInNanoseconds());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content, value).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData(-32131)]
    [InlineData(-1)]
    [InlineData(-15.323)]
    public Task Throws_When_Usage_Usec_Has_Negative_Value(int value)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.stat"), $"usage_usec {value}"},
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetCgroupCpuUsageInNanoseconds());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(value).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("dasrz3424")]
    [InlineData("0")]
    [InlineData("10001")]
    public Task Throws_When_Cgroup_Cpu_Weight_Files_Contain_Invalid_Data(string content)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/cpu.weight"), content },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = Record.Exception(() => p.GetCgroupRequestCpu());

        Assert.NotNull(r);
        return Verifier.Verify(r).UseParameters(content).UseDirectory(VerifiedDataDirectory);
    }

    [Theory]
    [InlineData("0::/", "filename", "/sys/fs/cgroup/filename")]
    [InlineData("0::/filesystem.slice", "filename", "/sys/fs/cgroup/filesystem.slice/filename")]
    [InlineData("0::/filesystem.slice/", "filename", "/sys/fs/cgroup/filesystem.slice/filename")]
    public void Create_Path_From_Proc_Self_Cgroup(string content, string filename, string result)
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/self/cgroup"), content },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));
        var r = p.GetCgroupPath(filename);

        Assert.Equal(result, r);
    }

    [Fact]
    public async Task Is_Thread_Safe_Async()
    {
        var f1 = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/stat"), "cpu  6163 0 3853 4222848 614 0 1155 0 0 0\r\ncpu0 240 0 279 210987 59 0 927 0 0 0" },
        });
        var f2 = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/stat"), "cpu  9137 0 9296 13972503 1148 0 2786 0 0 0\r\ncpu0 297 0 431 698663 59 0 2513 0 0 0" },
        });

        int callCount = 0;
        Mock<IFileSystem> fs = new();
        fs.Setup(x => x.ReadFirstLine(It.IsAny<FileInfo>(), It.IsAny<BufferWriter<char>>()))
             .Callback<FileInfo, BufferWriter<char>>((fileInfo, buffer) =>
             {
                 callCount++;
                 if (callCount % 2 == 0)
                 {
                     f1.ReadFirstLine(fileInfo, buffer);
                 }
                 else
                 {
                     f2.ReadFirstLine(fileInfo, buffer);
                 }
             })
             .Verifiable();

        var p = new LinuxUtilizationParserCgroupV2(fs.Object, new FakeUserHz(100));

        Task[] tasks = new Task[1_000];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(p.GetHostCpuUsageInNanoseconds);
        }

        await Task.WhenAll(tasks);

        Assert.True(true);
    }

    [Fact]
    public void Reads_Memory_Usage_From_Slices_When_Root_MemoryCurrent_Does_Not_Exist()
    {
        // Regression test for https://github.com/dotnet/extensions/issues/7748.
        // On hosts and WSL, the root cgroup of a cgroup v2 hierarchy has a memory.stat file but no
        // memory.current file (memory.current only exists on non-root cgroups). The memory usage is
        // then read from the top-level *.slice directories, and the inactive file memory of each
        // slice has to be subtracted from the slice's usage. The inactive_file of the root
        // memory.stat covers the entire system and can't be used here: it can be bigger than the
        // total memory usage of all the slices, which produced a negative result and an
        // InvalidOperationException on startup.
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 492773376" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "100000000" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.stat"), "inactive_file 40000000" },
            { new FileInfo("/sys/fs/cgroup/user.slice/memory.current"), "40439552" },
            { new FileInfo("/sys/fs/cgroup/user.slice/memory.stat"), "inactive_file 439552" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));

        Assert.Equal(100_000_000UL, p.GetMemoryUsageInBytes());
    }

    [Fact]
    public void Reads_Memory_Usage_From_Slices_When_Slices_Have_No_MemoryStat()
    {
        // The WSL scenario from https://github.com/dotnet/extensions/issues/7748: the inactive_file
        // of the root memory.stat (492773376) is bigger than the total memory usage of all the
        // slices (140439652). It previously produced a negative result and threw.
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 492773376" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "140439552" },
            { new FileInfo("/sys/fs/cgroup/user.slice/memory.current"), "100" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));

        Assert.Equal(140_439_652UL, p.GetMemoryUsageInBytes());
    }

    [Fact]
    public void Clamps_Slice_Memory_Usage_When_Inactive_File_Exceeds_Slice_Usage()
    {
        // Within a cgroup, memory.current is always greater than or equal to inactive_file, but the
        // two files are read at different points in time so the difference can be slightly negative.
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/memory.stat"), "inactive_file 100" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "100" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.stat"), "inactive_file 500" },
            { new FileInfo("/sys/fs/cgroup/user.slice/memory.current"), "200" },
            { new FileInfo("/sys/fs/cgroup/user.slice/memory.stat"), "inactive_file 50" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));

        Assert.Equal(150UL, p.GetMemoryUsageInBytes());
    }

    [Fact]
    public void Throws_With_Slice_MemoryStat_Path_When_Slice_MemoryStat_Is_Invalid()
    {
        var f = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.current"), "1000" },
            { new FileInfo("/sys/fs/cgroup/system.slice/memory.stat"), "garbage content" },
        });

        var p = new LinuxUtilizationParserCgroupV2(f, new FakeUserHz(100));

        var e = Assert.Throws<InvalidOperationException>(() => p.GetMemoryUsageInBytesFromSlices("*.slice"));
        Assert.Contains("/sys/fs/cgroup/system.slice/memory.stat", e.Message);
    }
}
