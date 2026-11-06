// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

internal sealed class HardcodedValueFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _fileContent;
    private readonly string _fallback;

    public HardcodedValueFileSystem(string fallback)
    {
        _fallback = fallback;
        _fileContent = [];
    }

    public HardcodedValueFileSystem(Dictionary<FileInfo, string> fileContent, string fallback = "")
    {
        _fileContent = fileContent.ToDictionary(static x => x.Key.FullName, static y => y.Value, StringComparer.OrdinalIgnoreCase);
        _fallback = fallback;
    }

    public bool Exists(FileInfo fileInfo)
    {
        return _fileContent.ContainsKey(fileInfo.FullName);
    }

    public IReadOnlyCollection<string> GetDirectoryNames(string directory, string pattern)
    {
        // Emulates Directory.GetDirectories(directory, pattern): returns the distinct names of the
        // directories that are direct children of 'directory' and match the glob pattern
        // ('*' matches any sequence of characters, '?' matches a single character).
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";

        return _fileContent.Keys
                .Where(x => x.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Substring(directory.Length))
                .Where(x => x.Contains(Path.DirectorySeparatorChar))
                .Select(x => x.Substring(0, x.IndexOf(Path.DirectorySeparatorChar)))
                .Where(x => Regex.IsMatch(x, regexPattern, RegexOptions.IgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => Path.Combine(directory, x))
                .ToArray();
    }

    public void ReadFirstLine(FileInfo file, BufferWriter<char> destination)
    {
        if (_fileContent.Count == 0 || !_fileContent.TryGetValue(file.FullName, out var content))
        {
            destination.Write(_fallback);

            return;
        }

        var newLineIndex = content.IndexOf('\n');

        destination.Write(newLineIndex != -1 ? content.Substring(0, newLineIndex) : content);
    }

    public void ReadAll(FileInfo file, BufferWriter<char> destination)
    {
        if (_fileContent.Count == 0 || !_fileContent.TryGetValue(file.FullName, out var content))
        {
            destination.Write(_fallback);

            return;
        }

        destination.Write(content);
    }

    public int Read(FileInfo file, int length, Span<char> destination)
    {
        var toRead = _fallback;

        if (_fileContent.Count != 0 && _fileContent.TryGetValue(file.FullName, out var content))
        {
            toRead = content;
        }

        var min = Math.Min(toRead.Length, length);

        for (var i = 0; i < min; i++)
        {
            destination[i] = toRead[i];
        }

        return min;
    }

    public void ReplaceFileContent(FileInfo file, string value)
    {
        _fileContent[file.FullName] = value;
    }

    public IEnumerable<ReadOnlyMemory<char>> ReadAllByLines(FileInfo file, BufferWriter<char> destination)
    {
        bool flag = !_fileContent.TryGetValue(file.FullName, out var content);
        if (_fileContent.Count == 0 || flag)
        {
            destination.Reset();
            destination.Write(_fallback);
            yield return destination.WrittenMemory;
        }
        else
        {
            if (content != null)
            {
                var start = 0;
                for (var newLineIndex = content.IndexOf('\n'); newLineIndex >= 0; newLineIndex = content.IndexOf('\n', newLineIndex + 1))
                {
                    destination.Reset();
                    destination.Write(newLineIndex != -1 ? content.Substring(start, newLineIndex - start) : content);
                    start = newLineIndex + 1;
                    yield return destination.WrittenMemory;
                }
            }
        }
    }
}
