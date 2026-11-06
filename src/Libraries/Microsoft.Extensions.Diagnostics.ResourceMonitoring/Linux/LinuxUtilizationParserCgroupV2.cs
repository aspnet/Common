// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Shared.Diagnostics;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux;

/// <remarks>
/// Parses Linux cgroups v2 files to retrieve resource utilization data.
/// This class is not thread safe.
/// When the same instance is called by multiple threads it may return corrupted data.
/// </remarks>
internal sealed class LinuxUtilizationParserCgroupV2 : ILinuxUtilizationParser
{
    private const int Thousand = 1000;
    private const int CpuShares = 1024;
    private const string CpuStat = "cpu.stat"; // File containing CPU usage in nanoseconds.
    private const string CpuLimit = "cpu.max"; // File with amount of CPU time available to the group along with the accounting period in microseconds.
    private const string CpuRequest = "cpu.weight"; // CPU weights, also known as shares in cgroup v1, is used for resource allocation.
    private const string MemoryMin = "memory.min"; // File contains min memory, set for QoS by K8s
    private const string MemoryLow = "memory.low"; // File contains min memory, set if there was memory reservation for container
    private static readonly ObjectPool<BufferWriter<char>> _sharedBufferWriterPool = BufferWriterPool.CreateBufferWriterPool<char>();

    /// <remarks>
    /// File contains the amount of CPU time (in microseconds) available to the group during each accounting period.
    /// and the length of the accounting period in microseconds.
    /// In Cgroup V1 : /sys/fs/cgroup/cpu/cpu.cfs_quota_us and /sys/fs/cgroup/cpu/cpu.cfs_period_us.
    /// </remarks>
    private static readonly FileInfo _cpuCfsQuaotaPeriodUs = new("/sys/fs/cgroup/cpu.max");

    /// <remarks>
    /// Stat file contains information about all CPUs and their time.
    /// </remarks>
    /// <remarks>
    /// The file has format of whitespace separated values. Each value has its own meaning and unit.
    /// To know which value we read, why and what it means refer to proc (5) man page (its POSIX).
    /// </remarks>
    private static readonly FileInfo _procStat = new("/proc/stat");

    /// <remarks>
    /// File that contains information about available memory.
    /// </remarks>
    private static readonly FileInfo _memInfo = new("/proc/meminfo");

    /// <remarks>
    /// List of available CPUs for host.
    /// In Cgroup v1 : /sys/fs/cgroup/cpuset/cpuset.cpus.
    /// </remarks>
    private static readonly FileInfo _cpuSetCpus = new("/sys/fs/cgroup/cpuset.cpus.effective");

    /// <remarks>
    /// Cgroup memory limit.
    /// </remarks>
    private static readonly FileInfo _memoryLimitInBytes = new("/sys/fs/cgroup/memory.max");

    /// <summary>
    /// Cgroup memory stats.
    /// </summary>
    /// <remarks>
    /// Single line representing used memory by cgroup in bytes.
    /// </remarks>
    private static readonly FileInfo _memoryUsageInBytes = new("/sys/fs/cgroup/memory.current");

    /// <summary>
    /// Cgroup memory stats.
    /// </summary>
    /// <remarks>
    /// This file contains the details about memory usage.
    /// The format is (type of memory spent) (value) (unit of measure).
    /// </remarks>
    private static readonly FileInfo _memoryStat = new("/sys/fs/cgroup/memory.stat");

    /// <summary>
    /// File containing usage in nanoseconds.
    /// </summary>
    /// <remarks>
    /// This value refers to the container/cgroup utilization.
    /// The format is single line with one number value.
    /// </remarks>
    private static readonly FileInfo _cpuacctUsage = new("/sys/fs/cgroup/cpu.stat");

    /// <summary>
    /// CPU weights, also known as shares in cgroup v1, is used for resource allocation.
    /// </summary>
    private static readonly FileInfo _cpuPodWeight = new("/sys/fs/cgroup/cpu.weight");

    private static readonly FileInfo _cpuCgroupInfoFile = new("/proc/self/cgroup");

    private readonly IFileSystem _fileSystem;
    private readonly long _userHz;

    // Cache for the trimmed path string to avoid repeated file reads and processing
    private string? _cachedCgroupPath;

    public LinuxUtilizationParserCgroupV2(IFileSystem fileSystem, IUserHz userHz)
    {
        _fileSystem = fileSystem;
        _userHz = userHz.Value;
    }

    public string GetCgroupPath(string filename)
    {
        // If we've already parsed the path, use the cached value
        if (_cachedCgroupPath != null)
        {
            return $"{_cachedCgroupPath}{filename}";
        }

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);

        // Read the content of the file
        _fileSystem.ReadFirstLine(_cpuCgroupInfoFile, bufferWriter.Buffer);
        ReadOnlySpan<char> fileContent = bufferWriter.Buffer.WrittenSpan;

        // Ensure the file content is not empty
        if (fileContent.IsEmpty)
        {
            Throw.InvalidOperationException($"The file '{_cpuCgroupInfoFile}' is empty or could not be read.");
        }

        // Find the index of the first colon (:)
        int colonIndex = fileContent.LastIndexOf(':');
        if (colonIndex == -1 || colonIndex + 1 >= fileContent.Length)
        {
            Throw.InvalidOperationException($"Invalid format in file '{_cpuCgroupInfoFile}'. Expected content with ':' separator.");
        }

        // Extract the part after the last colon and cache it for future use
        ReadOnlySpan<char> trimmedPath = fileContent[(colonIndex + 1)..];
        _cachedCgroupPath = $"/sys/fs/cgroup{trimmedPath.TrimEnd('/')}/";

        return $"{_cachedCgroupPath}{filename}";
    }

    public long GetCgroupCpuUsageInNanoseconds()
    {
        // If the file doesn't exist, we assume that the system is a Host and we read the CPU usage from /proc/stat.
        if (!_fileSystem.Exists(_cpuacctUsage))
        {
            return GetHostCpuUsageInNanoseconds();
        }

        return ParseCpuUsageFromFile(_fileSystem, _cpuacctUsage).cpuUsageNanoseconds;
    }

    public (long cpuUsageNanoseconds, long elapsedPeriods) GetCgroupCpuUsageInNanosecondsAndCpuPeriodsV2()
    {
        FileInfo cpuUsageFile = new(GetCgroupPath(CpuStat));

        // If the file doesn't exist, we assume that the system is a Host and we read the CPU usage from /proc/stat.
        if (!_fileSystem.Exists(cpuUsageFile))
        {
            return (GetHostCpuUsageInNanoseconds(), 1);
        }

        return ParseCpuUsageFromFile(_fileSystem, cpuUsageFile);
    }

    public long GetHostCpuUsageInNanoseconds()
    {
        const string StartingTokens = "cpu ";
        const int NumberOfColumnsRepresentingCpuUsage = 8;
        const int NanosecondsInSecond = 1_000_000_000;

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadFirstLine(_procStat, bufferWriter.Buffer);

        ReadOnlySpan<char> stat = bufferWriter.Buffer.WrittenSpan;
        long total = 0L;

        if (!bufferWriter.Buffer.WrittenSpan.StartsWith(StartingTokens))
        {
            Throw.InvalidOperationException($"Expected proc/stat to start with '{StartingTokens}' but it was '{new string(bufferWriter.Buffer.WrittenSpan)}'.");
        }

        stat = stat.Slice(StartingTokens.Length, stat.Length - StartingTokens.Length);

        for (int i = 0; i < NumberOfColumnsRepresentingCpuUsage; i++)
        {
            int next = GetNextNumber(stat, out long number);

            if (number != -1)
            {
                total += number;
            }

            if (next == -1)
            {
                Throw.InvalidOperationException(
                    $"'{_procStat}' should contain whitespace separated values according to POSIX. We've failed trying to get {i}th value. File content: '{new string(stat)}'.");
            }

            stat = stat.Slice(next);
        }

        return (long)(total / (double)_userHz * NanosecondsInSecond);
    }

    /// <remarks>
    /// When CGroup limits are set, we can calculate number of cores based on the file settings.
    /// It should be 99% of the cases when app is hosted in the container environment.
    /// Otherwise, we assume that all host's CPUs are available, which we read from proc/stat file.
    /// </remarks>
    public float GetCgroupLimitedCpus()
    {
        if (LinuxUtilizationParserCgroupV2.TryGetCpuUnitsFromCgroups(_fileSystem, out float cpus))
        {
            return cpus;
        }

        return GetHostCpuCount();
    }

    /// <remarks>
    /// When CGroup limits are set, we can calculate number of cores based on the file settings.
    /// It should be 99% of the cases when app is hosted in the container environment.
    /// Otherwise, we assume that all host's CPUs are available, which we read from proc/stat file.
    /// </remarks>
    public float GetCgroupLimitV2()
    {
        FileInfo cpuLimitsFile = new(GetCgroupPath(CpuLimit));
        if (LinuxUtilizationParserCgroupV2.TryGetCpuLimitFromCgroupsV2(_fileSystem, cpuLimitsFile, out float cpus))
        {
            return cpus;
        }

        return GetHostCpuCount();
    }

    public long GetCgroupPeriodsIntervalInMicroSecondsV2()
    {
        FileInfo cpuLimitsFile = new(GetCgroupPath(CpuLimit));
        return LinuxUtilizationParserCgroupV2.GetCpuPeriodsIntervalFromCgroupsV2(_fileSystem, cpuLimitsFile);
    }

    /// <remarks>
    /// If we are able to read the CPU share, we calculate the CPU request based on the weight by dividing it by 1024.
    /// If we can't read the CPU weight, we assume that the pod/vm cpu request is 1 core by default.
    /// </remarks>
    public float GetCgroupRequestCpu()
    {
        if (TryGetCgroupRequestCpu(_fileSystem, out float cpuPodRequest))
        {
            return cpuPodRequest / CpuShares;
        }

        return GetHostCpuCount();
    }

    /// <remarks>
    /// If we are able to read the CPU share, we calculate the CPU request based on the weight by dividing it by 1024.
    /// If we can't read the CPU weight, we assume that the pod/vm cpu request is 1 core by default.
    /// </remarks>
    public float GetCgroupRequestCpuV2()
    {
        FileInfo cpuRequestsFile = new(GetCgroupPath(CpuRequest));
        if (TryGetCgroupRequestCpuV2(_fileSystem, cpuRequestsFile, out float cpuPodRequest))
        {
            return cpuPodRequest / CpuShares;
        }

        return GetHostCpuCount();
    }

    /// <remarks>
    /// If the file doesn't exist, we assume that the system is a Host and we read the memory from /proc/meminfo.
    /// </remarks>
    public ulong GetAvailableMemoryInBytes()
    {
        if (!_fileSystem.Exists(_memoryLimitInBytes))
        {
            return GetHostAvailableMemory();
        }

        const long UnsetCgroupMemoryLimit = 9_223_372_036_854_771_712;
        long maybeMemory = UnsetCgroupMemoryLimit;

        // Constrain the scope of the buffer because GetHostAvailableMemory is allocating its own buffer.
        using (ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool))
        {
            _fileSystem.ReadAll(_memoryLimitInBytes, bufferWriter.Buffer);

            ReadOnlySpan<char> memoryBuffer = bufferWriter.Buffer.WrittenSpan;

            if (memoryBuffer.Equals("max\n", StringComparison.InvariantCulture))
            {
                return GetHostAvailableMemory();
            }

            _ = GetNextNumber(memoryBuffer, out maybeMemory);

            if (maybeMemory == -1)
            {
                Throw.InvalidOperationException($"Could not parse '{_memoryLimitInBytes}' content. Expected to find available memory in bytes but got '{new string(memoryBuffer)}' instead.");
            }
        }

        return maybeMemory == UnsetCgroupMemoryLimit
            ? GetHostAvailableMemory()
            : (ulong)maybeMemory;
    }

    /// <remarks>
    /// In cgroup v2, the memory usage of a system without a container cgroup has to be read from all
    /// the slices, which are directories in /sys/fs/cgroup matching <paramref name="pattern"/>.
    /// The inactive file memory of each slice is subtracted from the slice's usage. It has to be read
    /// from the slice's own memory.stat file: the one of the root cgroup accounts for the entire
    /// system, and subtracting it would produce a negative result.
    /// </remarks>
    public long GetMemoryUsageInBytesFromSlices(string pattern)
    {
        IReadOnlyCollection<string> memoryUsageInBytesSlicesPath = _fileSystem.GetDirectoryNames("/sys/fs/cgroup/", pattern);

        long memoryUsageInBytesTotal = 0;

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        foreach (string path in memoryUsageInBytesSlicesPath)
        {
            FileInfo memoryUsageInBytesFile = new(Path.Combine(path, "memory.current"));
            if (!_fileSystem.Exists(memoryUsageInBytesFile))
            {
                continue;
            }

            _fileSystem.ReadAll(memoryUsageInBytesFile, bufferWriter.Buffer);

            ReadOnlySpan<char> memoryUsageFile = bufferWriter.Buffer.WrittenSpan;
            int next = GetNextNumber(memoryUsageFile, out long containerMemoryUsage);

            if (containerMemoryUsage == -1)
            {
                memoryUsageInBytesTotal = 0;
                Throw.InvalidOperationException(
                    $"We tried to read '{memoryUsageInBytesFile}', and we expected to get a non-negative number but instead it was: '{memoryUsageFile}'.");
            }

            bufferWriter.Buffer.Reset();

            FileInfo memoryStatFile = new(Path.Combine(path, "memory.stat"));
            if (_fileSystem.Exists(memoryStatFile))
            {
                containerMemoryUsage -= GetInactiveFileMemoryInBytes(memoryStatFile);
            }

            // A slice's memory.current is always greater than or equal to its inactive file memory,
            // but the two files are read at different points in time, so the difference can be
            // slightly negative.
            if (containerMemoryUsage > 0)
            {
                memoryUsageInBytesTotal += containerMemoryUsage;
            }
        }

        return memoryUsageInBytesTotal;
    }

    /// <remarks>
    /// If the file doesn't exist, we assume that the system is a Host and we read the memory from /proc/meminfo.
    /// </remarks>
    public ulong GetMemoryUsageInBytes()
    {
        const string InactiveFile = "inactive_file";

        // Glob pattern for the slice directory paths in the real file system.
        const string Pattern = "*.slice";

        if (!_fileSystem.Exists(_memoryStat))
        {
            return GetHostAvailableMemory();
        }

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadAll(_memoryStat, bufferWriter.Buffer);
        ReadOnlySpan<char> memoryFile = bufferWriter.Buffer.WrittenSpan;

        int index = memoryFile.IndexOf(InactiveFile.AsSpan());

        if (index == -1)
        {
            Throw.InvalidOperationException($"Unable to find inactive_file from '{_memoryStat}'.");
        }

        ReadOnlySpan<char> inactiveMemorySlice = memoryFile.Slice(index + InactiveFile.Length, memoryFile.Length - index - InactiveFile.Length);

        _ = GetNextNumber(inactiveMemorySlice, out long inactiveMemory);

        if (inactiveMemory == -1)
        {
            Throw.InvalidOperationException($"The value of inactive_file found in '{_memoryStat}' is not a positive number: '{new string(inactiveMemorySlice)}'.");
        }

        if (!_fileSystem.Exists(_memoryUsageInBytes))
        {
            // The root cgroup of a cgroup v2 hierarchy has no memory.current file (it only exists on
            // non-root cgroups), which is the case when running directly on a host or on WSL. The
            // memory usage is then summed across the top-level slices, and each slice's own inactive
            // file memory is subtracted inside GetMemoryUsageInBytesFromSlices. The root
            // memory.stat can't be used for that subtraction because it accounts for the entire
            // system, which produces a negative result and throws an InvalidOperationException.
            // See https://github.com/dotnet/extensions/issues/7748.
            return (ulong)GetMemoryUsageInBytesFromSlices(Pattern);
        }

        long memoryUsage = GetMemoryUsageInBytesPod();

        long memoryUsageTotal = memoryUsage - inactiveMemory;

        if (memoryUsageTotal < 0)
        {
            Throw.InvalidOperationException($"The total memory usage read from '{_memoryUsageInBytes}' is lesser than inactive memory read from '{_memoryStat}'.");
        }

        return (ulong)memoryUsageTotal;
    }

    public ulong GetHostAvailableMemory()
    {
        // The value we are interested in starts with this. We just want to make sure it is true.
        const string MemTotal = "MemTotal:";

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadFirstLine(_memInfo, bufferWriter.Buffer);
        ReadOnlySpan<char> firstLine = bufferWriter.Buffer.WrittenSpan;

        if (!firstLine.StartsWith(MemTotal))
        {
            Throw.InvalidOperationException($"Could not parse '{_memInfo}'. We expected first line of the file to start with '{MemTotal}' but it was '{new string(firstLine)}' instead.");
        }

        ReadOnlySpan<char> totalMemory = firstLine.Slice(MemTotal.Length, firstLine.Length - MemTotal.Length);

        int next = GetNextNumber(totalMemory, out long totalMemoryAvailable);

        if (totalMemoryAvailable == -1)
        {
            Throw.InvalidOperationException($"Could not parse '{_memInfo}'. We expected to get total memory usage on first line but we've got: '{new string(firstLine)}'.");
        }

        if (next == -1 || totalMemory.Length - next < 2)
        {
            Throw.InvalidOperationException($"Could not parse '{_memInfo}'. We expected to get memory usage followed by the unit (kB, MB, GB) but found no unit: '{new string(firstLine)}'.");
        }

        ReadOnlySpan<char> unit = totalMemory.Slice(totalMemory.Length - 2, 2);
        ulong memory = (ulong)totalMemoryAvailable;

        ulong u = unit switch
        {
            "kB" => memory << 10,
            "MB" => memory << 20,
            "GB" => memory << 30,
            "TB" => memory << 40,
            _ => throw new InvalidOperationException(
                $"We tried to convert total memory usage value from '{_memInfo}' to bytes, but we've got a unit that we don't recognize: '{new string(unit)}'.")
        };

        return u;
    }

    /// <remarks>
    /// Comma-separated list of integers, with dashes ("-") to represent ranges. For example "0-1,5", or "0", or "1,2,3".
    /// Each value represents the zero-based index of a CPU.
    /// </remarks>
    public float GetHostCpuCount()
    {
        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadFirstLine(_cpuSetCpus, bufferWriter.Buffer);
        ReadOnlySpan<char> stats = bufferWriter.Buffer.WrittenSpan;

        if (stats.IsEmpty)
        {
            ThrowException(stats);
        }

        long cpuCount = 0L;

        // Iterate over groups (comma-separated)
        while (true)
        {
            int groupIndex = stats.IndexOf(',');

            ReadOnlySpan<char> group = groupIndex == -1 ? stats : stats.Slice(0, groupIndex);

            int rangeIndex = group.IndexOf('-');

            if (rangeIndex == -1)
            {
                // Single number
                _ = GetNextNumber(group, out long singleCpu);

                if (singleCpu == -1)
                {
                    ThrowException(stats);
                }

                cpuCount += 1;
            }
            else
            {
                // Range
                ReadOnlySpan<char> first = group.Slice(0, rangeIndex);
                _ = GetNextNumber(first, out long startCpu);

                ReadOnlySpan<char> second = group.Slice(rangeIndex + 1);
                int next = GetNextNumber(second, out long endCpu);

                if (endCpu == -1 || startCpu == -1 || endCpu < startCpu || next != -1)
                {
                    ThrowException(stats);
                }

                cpuCount += endCpu - startCpu + 1;
            }

            if (groupIndex == -1)
            {
                break;
            }

            stats = stats.Slice(groupIndex + 1);
        }

        return cpuCount;

        static void ThrowException(ReadOnlySpan<char> content) =>
            Throw.InvalidOperationException(
                $"Could not parse '{_cpuSetCpus}'. Expected comma-separated list of integers, with dashes (\"-\") based ranges (\"0\", \"2-6,12\") but got '{new string(content)}'.");
    }

    public ulong GetMinMemoryInBytes()
    {
        FileInfo memoryMinFile = new(GetCgroupPath(MemoryMin));
        if (_fileSystem.Exists(memoryMinFile))
        {
            using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
            _fileSystem.ReadAll(memoryMinFile, bufferWriter.Buffer);

            ReadOnlySpan<char> memoryMinBuffer = bufferWriter.Buffer.WrittenSpan;

            _ = GetNextNumber(memoryMinBuffer, out long memoryMin);

            if (memoryMin == -1)
            {
                Throw.InvalidOperationException($"Could not parse '{memoryMinFile}' content. Expected to find memory minimum in bytes but got '{new string(memoryMinBuffer)}' instead.");
            }

            if (memoryMin != 0)
            {
                return (ulong)memoryMin;
            }
        }

        FileInfo memoryLowFile = new(GetCgroupPath(MemoryLow));
        if (_fileSystem.Exists(memoryLowFile))
        {
            using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
            _fileSystem.ReadAll(memoryLowFile, bufferWriter.Buffer);

            ReadOnlySpan<char> memoryLowBuffer = bufferWriter.Buffer.WrittenSpan;

            _ = GetNextNumber(memoryLowBuffer, out long memoryLow);

            if (memoryLow == -1)
            {
                Throw.InvalidOperationException($"Could not parse '{memoryLowFile}' content. Expected to find memory low in bytes but got '{new string(memoryLowBuffer)}' instead.");
            }

            return (ulong)memoryLow;
        }

        return 0;
    }

    private static (long cpuUsageNanoseconds, long nrPeriods) ParseCpuUsageFromFile(IFileSystem fileSystem, FileInfo cpuUsageFile)
    {
        // The values we are interested in start with these prefixes
        const string UsageUsec = "usage_usec";
        const string NrPeriods = "nr_periods";

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        fileSystem.ReadAll(cpuUsageFile, bufferWriter.Buffer);
        ReadOnlySpan<char> content = bufferWriter.Buffer.WrittenSpan;

        // Parse usage_usec
        int usageIndex = content.IndexOf(UsageUsec);
        if (usageIndex == -1)
        {
            Throw.InvalidOperationException($"Could not parse '{cpuUsageFile}'. Expected to find 'usage_usec' but it was not present in the file content.");
        }

        ReadOnlySpan<char> usageSlice = content.Slice(usageIndex + UsageUsec.Length);
        int usageNext = GetNextNumber(usageSlice, out long microseconds);

        if (microseconds == -1)
        {
            Throw.InvalidOperationException($"Could not get usage_usec from '{cpuUsageFile}'. Expected positive number, but got '{new string(content.Slice(usageIndex))}'.");
        }

        // Parse nr_periods
        int periodsIndex = content.IndexOf(NrPeriods);
        if (periodsIndex == -1)
        {
            Throw.InvalidOperationException($"Could not parse '{cpuUsageFile}'. Expected to find 'nr_periods' but it was not present in the file content.");
        }

        ReadOnlySpan<char> periodsSlice = content.Slice(periodsIndex + NrPeriods.Length);
        int periodsNext = GetNextNumber(periodsSlice, out long periods);

        if (periods == -1)
        {
            Throw.InvalidOperationException($"Could not get nr_periods from '{cpuUsageFile}'. Expected positive number, but got '{new string(content.Slice(periodsIndex))}'.");
        }

        // In cgroup v2, the Units are microseconds for usage_usec.
        // We multiply by 1000 to convert to nanoseconds to keep the common calculation logic.
        return (microseconds * Thousand, periods);
    }

    /// <remarks>
    /// The input must contain only number. If there is something more than whitespace before the number, it will return failure (-1).
    /// </remarks>
    private static int GetNextNumber(ReadOnlySpan<char> buffer, out long number)
    {
        int numberStart = 0;

        while (numberStart < buffer.Length && char.IsWhiteSpace(buffer[numberStart]))
        {
            numberStart++;
        }

        if (numberStart == buffer.Length || !char.IsDigit(buffer[numberStart]))
        {
            number = -1;
            return -1;
        }

        int numberEnd = numberStart;
        number = 0;

        while (numberEnd < buffer.Length && char.IsDigit(buffer[numberEnd]))
        {
            int current = buffer[numberEnd] - '0';
            number *= 10;
            number += current;
            numberEnd++;
        }

        return numberEnd < buffer.Length ? numberEnd : -1;
    }

    /// <remarks>
    /// If the file doesn't exist, we assume that the system is a Host and we read the CPU usage from /proc/stat.
    /// </remarks>
    private static bool TryGetCpuUnitsFromCgroups(IFileSystem fileSystem, out float cpuUnits)
    {
        if (!fileSystem.Exists(_cpuCfsQuaotaPeriodUs))
        {
            cpuUnits = 0;
            return false;
        }

        return TryParseCpuQuotaAndPeriodFromFile(fileSystem, _cpuCfsQuaotaPeriodUs, out cpuUnits);
    }

    /// <remarks>
    /// If the file doesn't exist, we assume that the system is a Host and we read the CPU usage from /proc/stat.
    /// </remarks>
    private static bool TryGetCpuLimitFromCgroupsV2(IFileSystem fileSystem, FileInfo cpuLimitsFile, out float cpuUnits)
    {
        if (!fileSystem.Exists(cpuLimitsFile))
        {
            cpuUnits = 0;
            return false;
        }

        return TryParseCpuQuotaAndPeriodFromFile(fileSystem, cpuLimitsFile, out cpuUnits);
    }

    private static bool TryParseCpuQuotaAndPeriodFromFile(IFileSystem fileSystem, FileInfo cpuLimitsFile, out float cpuUnits)
    {
        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        fileSystem.ReadFirstLine(cpuLimitsFile, bufferWriter.Buffer);

        ReadOnlySpan<char> quotaBuffer = bufferWriter.Buffer.WrittenSpan;

        if (quotaBuffer.IsEmpty || (quotaBuffer.Length == 2 && quotaBuffer[0] == '-' && quotaBuffer[1] == '1'))
        {
            cpuUnits = -1;
            return false;
        }

        if (quotaBuffer.StartsWith("max", StringComparison.InvariantCulture))
        {
            cpuUnits = 0;
            return false;
        }

        _ = GetNextNumber(quotaBuffer, out long quota);

        if (quota == -1)
        {
            Throw.InvalidOperationException($"Could not parse '{cpuLimitsFile}'. Expected an integer but got: '{new string(quotaBuffer)}'.");
        }

        string quotaString = quota.ToString(CultureInfo.CurrentCulture);
        int index = quotaBuffer.IndexOf(quotaString.AsSpan());
        ReadOnlySpan<char> cpuPeriodSlice = quotaBuffer.Slice(index + quotaString.Length, quotaBuffer.Length - index - quotaString.Length);
        _ = GetNextNumber(cpuPeriodSlice, out long period);

        if (period == -1)
        {
            Throw.InvalidOperationException($"Could not parse '{cpuLimitsFile}'. Expected to get an integer but got: '{new string(cpuPeriodSlice)}'.");
        }

        cpuUnits = (float)quota / period;

        return true;
    }

    private static long GetCpuPeriodsIntervalFromCgroupsV2(IFileSystem fileSystem, FileInfo cpuLimitsFile)
    {
        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        fileSystem.ReadFirstLine(cpuLimitsFile, bufferWriter.Buffer);

        ReadOnlySpan<char> content = bufferWriter.Buffer.WrittenSpan;

        if (content.IsEmpty)
        {
            Throw.InvalidOperationException($"Could not read content from '{cpuLimitsFile}'. The file was empty.");
        }

        // Check if the first value is "max" (unlimited quota)
        if (content.StartsWith("max", StringComparison.InvariantCulture))
        {
            // Skip "max" and any whitespace
            ReadOnlySpan<char> periodSlice = content.Slice(3);
            _ = GetNextNumber(periodSlice, out long period);

            if (period == -1)
            {
                Throw.InvalidOperationException($"Could not parse period value from '{cpuLimitsFile}'. Expected an integer after 'max' but got: '{new string(content)}'.");
            }

            return period;
        }

        // Check if the first value is "-1" (also unlimited quota)
        else if (content.Length >= 2 && content[0] == '-' && content[1] == '1')
        {
            // Skip "-1" and any whitespace
            ReadOnlySpan<char> periodSlice = content.Slice(2);
            _ = GetNextNumber(periodSlice, out long period);

            if (period == -1)
            {
                Throw.InvalidOperationException($"Could not parse period value from '{cpuLimitsFile}'. Expected an integer after '-1' but got: '{new string(content)}'.");
            }

            return period;
        }
        else
        {
            // First get the first number (quota)
            _ = GetNextNumber(content, out long quota);

            if (quota == -1)
            {
                Throw.InvalidOperationException($"Could not parse quota value from '{cpuLimitsFile}'. Expected an integer but got: '{new string(content)}'.");
            }

            // Convert quota to string to find its position in the content
            string quotaString = quota.ToString(CultureInfo.CurrentCulture);
            int index = content.IndexOf(quotaString.AsSpan());

            // Get the content after the first number
            ReadOnlySpan<char> periodSlice = content.Slice(index + quotaString.Length);
            _ = GetNextNumber(periodSlice, out long period);

            if (period == -1)
            {
                Throw.InvalidOperationException($"Could not parse period value from '{cpuLimitsFile}'. Expected an integer after quota but got: '{new string(content)}'.");
            }

            return period;
        }
    }

    private static bool TryGetCgroupRequestCpu(IFileSystem fileSystem, out float cpuUnits)
    {
        if (!fileSystem.Exists(_cpuPodWeight))
        {
            cpuUnits = 0;
            return false;
        }

        return TryParseCpuWeightFromFile(fileSystem, _cpuPodWeight, out cpuUnits);
    }

    private static bool TryGetCgroupRequestCpuV2(IFileSystem fileSystem, FileInfo cpuRequestsFile, out float cpuUnits)
    {
        if (!fileSystem.Exists(cpuRequestsFile))
        {
            cpuUnits = 0;
            return false;
        }

        return TryParseCpuWeightFromFile(fileSystem, cpuRequestsFile, out cpuUnits);
    }

    private static bool TryParseCpuWeightFromFile(IFileSystem fileSystem, FileInfo cpuWeightFile, out float cpuUnits)
    {
        const long CpuPodWeightPossibleMax = 10_000;
        const long CpuPodWeightPossibleMin = 1;

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        fileSystem.ReadFirstLine(cpuWeightFile, bufferWriter.Buffer);
        ReadOnlySpan<char> cpuPodWeightBuffer = bufferWriter.Buffer.WrittenSpan;

        if (cpuPodWeightBuffer.IsEmpty || (cpuPodWeightBuffer.Length == 2 && cpuPodWeightBuffer[0] == '-' && cpuPodWeightBuffer[1] == '1'))
        {
            Throw.InvalidOperationException(
                $"Could not parse '{cpuWeightFile}' content. Expected to find CPU weight but got '{new string(cpuPodWeightBuffer)}' instead.");
        }

        _ = GetNextNumber(cpuPodWeightBuffer, out long cpuPodWeight);

        if (cpuPodWeight == -1)
        {
            Throw.InvalidOperationException(
                $"Could not parse '{cpuWeightFile}' content. Expected to get an integer but got: '{cpuPodWeightBuffer}'.");
        }

        if (cpuPodWeight < CpuPodWeightPossibleMin || cpuPodWeight > CpuPodWeightPossibleMax)
        {
            Throw.ArgumentOutOfRangeException("CPU weight",
                $"Expected to find CPU weight in range [{CpuPodWeightPossibleMin}-{CpuPodWeightPossibleMax}] in '{cpuWeightFile}', but got '{cpuPodWeight}' instead.");
        }

        cpuUnits = ConvertCpuWeightToShares(cpuPodWeight);
        return true;
    }

    private static float ConvertCpuWeightToShares(long cpuWeight)
    {
        const long MinValueInRange = 2;
        const long MaxValueInRange = 262144;

        // Inversion of https://github.com/kubernetes/website/blob/main/content/en/blog/_posts/2026-01-30-new-cgroup-v1-to-v2-conversion-formula/index.md#new-conversion-formula
        // cpu.shares = 2^L
        // L = (sqrt(16129 + 2448 * log10(cpu.weight)) - 125) / 2
        double a = Math.Log10(cpuWeight);
        double b = 2448 * a;
        double c = 16129 + b;
        double d = Math.Sqrt(c);
        double l = (d - 125) / 2;

        double shares = Math.Round(Math.Pow(2, l));
        shares = Math.Clamp(shares, MinValueInRange, MaxValueInRange);

        return (float)shares;
    }

    /// <remarks>
    /// Reads the inactive_file value from a cgroup's memory.stat file.
    /// </remarks>
    private long GetInactiveFileMemoryInBytes(FileInfo memoryStatFile)
    {
        const string InactiveFile = "inactive_file";

        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadAll(memoryStatFile, bufferWriter.Buffer);
        ReadOnlySpan<char> memoryFile = bufferWriter.Buffer.WrittenSpan;

        int index = memoryFile.IndexOf(InactiveFile.AsSpan());

        if (index == -1)
        {
            Throw.InvalidOperationException($"Unable to find inactive_file from '{memoryStatFile}'.");
        }

        ReadOnlySpan<char> inactiveMemorySlice = memoryFile.Slice(index + InactiveFile.Length, memoryFile.Length - index - InactiveFile.Length);

        _ = GetNextNumber(inactiveMemorySlice, out long inactiveMemory);

        if (inactiveMemory == -1)
        {
            Throw.InvalidOperationException($"The value of inactive_file found in '{memoryStatFile}' is not a positive number: '{new string(inactiveMemorySlice)}'.");
        }

        return inactiveMemory;
    }

    private long GetMemoryUsageInBytesPod()
    {
        using ReturnableBufferWriter<char> bufferWriter = new(_sharedBufferWriterPool);
        _fileSystem.ReadAll(_memoryUsageInBytes, bufferWriter.Buffer);

        ReadOnlySpan<char> memoryUsageFile = bufferWriter.Buffer.WrittenSpan;
        int next = GetNextNumber(memoryUsageFile, out long memoryUsage);

        // this file format doesn't expect to contain anything after the number.
        if (memoryUsage == -1)
        {
            Throw.InvalidOperationException(
                $"We tried to read '{_memoryUsageInBytes}', and we expected to get a positive number but instead it was: '{memoryUsageFile}'.");
        }

        return memoryUsage;
    }
}
