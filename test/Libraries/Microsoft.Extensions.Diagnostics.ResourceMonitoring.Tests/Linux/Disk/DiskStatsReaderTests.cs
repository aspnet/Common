// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Disk.Test;

public class DiskStatsReaderTests
{
    public DiskStatsReaderTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");
    }

    private static readonly string[] _skipDevicePrefixes = new[] { "ram", "loop", "dm-" };

    [Fact]
    public void Test_ReadAll_Valid_DiskStats()
    {
        string diskStatsFileContent =
            "   7       0 loop0 269334 0 12751202 147117 11604772 0 97447664 1402945 0 12193892 2255752 0 0 0 0 1206808 705690\n" +
            "   7       1 loop1 965348 0 28605866 474103 73636257 0 1211288288 14086242 0 60580032 24777643 0 0 0 0 18723136 10217297\n" +
            "   7       2 loop2 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 259       1 nvme1n1 4180498 5551 247430002 746099 96474435 12677267 2160066791 23514624 0 68786140 29777259 0 0 0 0 22111407 5516535\n" +
            " 259       2 nvme1n1p1 4180387 5551 247422458 746080 96474435 12677267 2160066791 23514624 0 68786108 24260705 0 0 0 0 0 0\n" +
            " 259       0 nvme0n1 6090587 689465 1120208521 1810566 19069165 8947684 406356430 3897150 0 38134844 6246643 69106 0 271818368 23139 1659742 515787\n" +
            " 259       3 nvme0n1p1 378 0 26406 96 0 0 0 0 0 760 96 0 0 0 0 0 0\n" +
            " 259       4 nvme0n1p2 7301 26408 116617 3628 600 47 59970 98 0 1196 3767 48 0 33106424 40 0 0\n" +
            " 259       5 nvme0n1p3 6079544 663057 1119819306 1806337 19068535 8947637 406296460 3897045 0 38130316 5726482 69058 0 238711944 23098 0 0\n" +
            " 252       0 dm-0 1303410 0 10434296 166616 1812455 0 14879824 1213588 0 397256 1380204 0 0 0 0 0 0\n" +
            " 252       1 dm-1 712122 0 38299466 140852 18159197 0 286348832 1552768 0 14182384 1716692 69058 0 238711944 23072 0 0\n" +
            " 252       5 dm-5 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 252       7 dm-7 6828 0 360325 2100 14438 0 1149672 1508 0 7524 3608 0 0 0 0 0 0\n" +
            "   8       0 sda 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 252       8 dm-8 100601 0 2990980 23940 3097278 0 32037680 1410540 0 5488608 1434496 513 0 67108872 16 0 0\n";

        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/diskstats"), diskStatsFileContent }
        });

        var reader = new DiskStatsReader(fileSystem);
        var dictionary = reader.ReadAll(_skipDevicePrefixes).ToDictionary(x => x.DeviceName);

        var expectedDevices = new[]
        {
            "nvme1n1", "nvme1n1p1", "nvme0n1", "nvme0n1p1", "nvme0n1p2", "nvme0n1p3", "sda"
        };
        Assert.Equal(expectedDevices.Length, dictionary.Count);
        foreach (var device in expectedDevices)
        {
            Assert.True(dictionary.ContainsKey(device), $"Expected device {device} to be present.");
        }

        var disk1 = dictionary["nvme0n1"];
        Assert.Equal(6_090_587u, disk1.ReadsCompleted);
        Assert.Equal(689_465u, disk1.ReadsMerged);
        Assert.Equal(1_120_208_521u, disk1.SectorsRead);
        Assert.Equal(1_810_566u, disk1.TimeReadingMs);
        Assert.Equal(19_069_165u, disk1.WritesCompleted);
        Assert.Equal(8_947_684u, disk1.WritesMerged);
        Assert.Equal(406_356_430u, disk1.SectorsWritten);
        Assert.Equal(3_897_150u, disk1.TimeWritingMs);
        Assert.Equal(0u, disk1.IoInProgress);
        Assert.Equal(38_134_844u, disk1.TimeIoMs);
        Assert.Equal(6_246_643u, disk1.WeightedTimeIoMs);
        Assert.Equal(69_106u, disk1.DiscardsCompleted);
        Assert.Equal(0u, disk1.DiscardsMerged);
        Assert.Equal(271_818_368u, disk1.SectorsDiscarded);
        Assert.Equal(23_139u, disk1.TimeDiscardingMs);
        Assert.Equal(1_659_742u, disk1.FlushRequestsCompleted);
        Assert.Equal(515_787u, disk1.TimeFlushingMs);

        var disk2 = dictionary["sda"];
        Assert.Equal(0u, disk2.ReadsCompleted);
        Assert.Equal(0u, disk2.ReadsMerged);
        Assert.Equal(0u, disk2.SectorsRead);
        Assert.Equal(0u, disk2.TimeReadingMs);
        Assert.Equal(0u, disk2.WritesCompleted);
        Assert.Equal(0u, disk2.WritesMerged);
        Assert.Equal(0u, disk2.SectorsWritten);
        Assert.Equal(0u, disk2.TimeWritingMs);
        Assert.Equal(0u, disk2.IoInProgress);
        Assert.Equal(0u, disk2.TimeIoMs);
        Assert.Equal(0u, disk2.WeightedTimeIoMs);
    }

    [Fact]
    public void Test_ReadAll_With_Invalid_Lines()
    {
        string diskStatsFileContent =
            " 259       1 nvme1n1 4180498 5551 247430002 746099 96474435 12677267 2160066791 23514624 0 68786140 29777259 0 0 0 0 22111407 5516535\n" +
            " 259       2 nvme1n1p1 4180387 5551 247422458\n" +
            " 259       2 nvme1n1p1 4180387 5551 247422458 746080 96474435 12677267 2160066791 23514624 0 68786108 24260705 0 0 0 0 0 0\n" +
            " 259       0 nvme0n1 6090587 689465 1120208521 1810566 19069165 8947684 406356430 3897150 0 38134844 6246643 69106 0 271818368 23139 1659742 515787\n" +
            " 259       nvme0n1p1 378 0 26406 96 0 0 0 0 0 760 96 0 0 0 0 0 0\n";

        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/diskstats"), diskStatsFileContent }
        });

        var reader = new DiskStatsReader(fileSystem);
        var dictionary = reader.ReadAll(_skipDevicePrefixes).ToDictionary(x => x.DeviceName);
        Assert.Equal(3, dictionary.Count);

        var disk1 = dictionary["nvme1n1"];
        Assert.Equal(4_180_498u, disk1.ReadsCompleted);
        Assert.Equal(5_551u, disk1.ReadsMerged);
        Assert.Equal(247_430_002u, disk1.SectorsRead);
        Assert.Equal(746_099u, disk1.TimeReadingMs);
        Assert.Equal(96_474_435u, disk1.WritesCompleted);
        Assert.Equal(12_677_267u, disk1.WritesMerged);
        Assert.Equal(2_160_066_791u, disk1.SectorsWritten);
        Assert.Equal(23_514_624u, disk1.TimeWritingMs);
        Assert.Equal(0u, disk1.IoInProgress);
        Assert.Equal(68_786_140u, disk1.TimeIoMs);
        Assert.Equal(29_777_259u, disk1.WeightedTimeIoMs);
        Assert.Equal(0u, disk1.DiscardsCompleted);
        Assert.Equal(0u, disk1.DiscardsMerged);
        Assert.Equal(0u, disk1.SectorsDiscarded);
        Assert.Equal(0u, disk1.TimeDiscardingMs);
        Assert.Equal(22_111_407u, disk1.FlushRequestsCompleted);
        Assert.Equal(5_516_535u, disk1.TimeFlushingMs);

        var disk2 = dictionary["nvme1n1p1"];
        Assert.NotNull(disk2);

        var disk3 = dictionary["nvme0n1"];
        Assert.NotNull(disk3);
    }

    [Fact]
    public void Test_ReadAll_Skips_Prefixes()
    {
        string diskStatsFileContent =
            "   7       0 loop0 100 0 1000 10 1000 0 10000 100 0 1000 100 0 0 0 0 100 100\n" +
            "   1       0 ram0 200 0 2000 20 2000 0 20000 200 0 2000 200 0 0 0 0 200 200\n" +
            " 259       0 nvme0n1 300 0 3000 30 3000 0 30000 300 0 3000 300 0 0 0 0 300 300\n" +
            " 252       0 dm-0 400 0 4000 40 4000 0 40000 400 0 4000 400 0 0 0 0 400 400\n" +
            "   8       0 sda 500 0 5000 50 5000 0 50000 500 0 5000 500 0 0 0 0 500 500\n";

        var fileSystem = new HardcodedValueFileSystem(new Dictionary<FileInfo, string>
        {
            { new FileInfo("/proc/diskstats"), diskStatsFileContent }
        });

        var reader = new DiskStatsReader(fileSystem);
        var dictionary = reader.ReadAll(_skipDevicePrefixes).ToDictionary(x => x.DeviceName);

        Assert.DoesNotContain("loop0", dictionary.Keys);
        Assert.DoesNotContain("ram0", dictionary.Keys);
        Assert.DoesNotContain("dm-0", dictionary.Keys);
        Assert.Contains("nvme0n1", dictionary.Keys);
        Assert.Contains("sda", dictionary.Keys);
    }
}
