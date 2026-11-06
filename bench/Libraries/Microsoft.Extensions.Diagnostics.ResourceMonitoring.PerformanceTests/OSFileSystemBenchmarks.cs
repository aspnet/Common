// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Bench;

/// <summary>
/// Benchmarks for the ways that <see cref="OSFileSystem"/> reads files, which the Linux resource monitoring
/// code does every sampling interval to read files like <c>/proc/stat</c> and the cgroup files.
/// </summary>
/// <remarks>
/// On Linux the real procfs files are read. On other operating systems, files with similar content are generated instead.
/// </remarks>
[MemoryDiagnoser]
public class OSFileSystemBenchmarks
{
    private static readonly Microsoft.Extensions.ObjectPool.ObjectPool<BufferWriter<char>> _bufferWriterPool = BufferWriterPool.CreateBufferWriterPool<char>();

    private readonly OSFileSystem _fileSystem = new();
    private readonly char[] _destination = new char[512];

    private string? _tempDirectory;
    private FileInfo _firstLineFile = null!;
    private FileInfo _readFile = null!;
    private FileInfo _readAllFile = null!;
    private FileInfo _linesFile = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/stat") && File.Exists("/proc/self/status") && File.Exists("/proc/net/tcp"))
        {
            _firstLineFile = new FileInfo("/proc/stat");
            _readFile = new FileInfo("/proc/self/stat");
            _readAllFile = new FileInfo("/proc/self/status");
            _linesFile = new FileInfo("/proc/net/tcp");
            return;
        }

        _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDirectory);

        _firstLineFile = CreateFile("stat", "cpu  1234 5 678 91011 12 0 3 0 0 0", 40);
        _readFile = CreateFile("self-stat", "1234 (dotnet) S 1 1234 1234 0 -1 4194560 12345 0 0 0 10 5 0 0 20 0 10 0 12345", 1);
        _readAllFile = CreateFile("self-status", "Name:   dotnet", 30);
        _linesFile = CreateFile("tcp", "   0: 0100007F:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 12345 1 0000000000000000 100 0 0 10 0", 100);

        FileInfo CreateFile(string name, string line, int lines)
        {
            var path = Path.Combine(_tempDirectory, name);
            var builder = new StringBuilder();

            for (int i = 0; i < lines; i++)
            {
                _ = builder.Append(line).Append('\n');
            }

            File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
            return new FileInfo(path);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_tempDirectory is not null)
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Benchmark]
    public int Read()
        => _fileSystem.Read(_readFile, 300, _destination);

    [Benchmark]
    public int ReadFirstLine()
    {
        using ReturnableBufferWriter<char> writer = new(_bufferWriterPool);
        _fileSystem.ReadFirstLine(_firstLineFile, writer.Buffer);
        return writer.Buffer.WrittenCount;
    }

    [Benchmark]
    public int ReadAll()
    {
        using ReturnableBufferWriter<char> writer = new(_bufferWriterPool);
        _fileSystem.ReadAll(_readAllFile, writer.Buffer);
        return writer.Buffer.WrittenCount;
    }

    [Benchmark]
    public int ReadAllByLines()
    {
        int count = 0;

        using ReturnableBufferWriter<char> writer = new(_bufferWriterPool);
        foreach (var line in _fileSystem.ReadAllByLines(_linesFile, writer.Buffer))
        {
            count += line.Length;
        }

        return count;
    }
}
