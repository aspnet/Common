// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Shared.Pools;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring.Linux.Test;

public sealed class OSFileSystemTests
{
    public OSFileSystemTests()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Windows/macOS");
    }

    [Fact]
    public void GetDirectoryNames_ReturnsDirectoryNames()
    {
        var fileSystem = new OSFileSystem();
        var directoryNames = fileSystem.GetDirectoryNames(
            Path.Combine(Directory.GetCurrentDirectory(), "fixtures"), "*.slice");

        Assert.Single(directoryNames);
    }

    [Fact]
    public void Reading_First_File_Line_Works()
    {
        const string Content = "Name:   cat";
        var fileSystem = new OSFileSystem();
        var file = new FileInfo("fixtures/status");
        var bw = new BufferWriter<char>();
        fileSystem.ReadFirstLine(file, bw);
        var s = new string(bw.WrittenSpan).Replace("\r", ""); // Git is overwriting LF to CRLF all the time for windows, I am so tired of it I am hacking it!!

        Assert.Equal(Content, s);
    }

    [Fact]
    public void Reading_The_Whole_File_Works()
    {
        const string Content = "user 1399428\nsystem 1124053\n";
        var fileSystem = new OSFileSystem();
        var file = new FileInfo("fixtures/cpuacct.stat");
        var bw = new BufferWriter<char>();
        fileSystem.ReadAll(file, bw);

        var s = new string(bw.WrittenSpan).Replace("\r", ""); // Git is overwriting LF to CRLF all the time for windows, I am so tired of it I am hacking it!!

        Assert.Equal(Content, s);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Reading_Small_Portion_Of_Big_File_Works(int length)
    {
        const char Content = 'R';
        var b = new char[length];
        var fileSystem = new OSFileSystem();
        var file = new FileInfo("fixtures/FileWithRChars");
        var written = fileSystem.Read(file, length, b);

        Assert.True(length >= written);
        Assert.True(b.AsSpan(0, written).SequenceEqual(new string(Content, written).AsSpan()));
    }

    [Fact]
    public void Reading_Line_By_Line_From_File_Works()
    {
        var fileSystem = new OSFileSystem();
        var file = new FileInfo("fixtures/tcpacct.stat");
        var bw = new BufferWriter<char>();
        var index = 1;
        foreach (var line in fileSystem.ReadAllByLines(file, bw))
        {
            var expected = string.Format("line {0}", index);
            var actual = line.ToString().Replace("\r", "");
            Assert.Equal(expected, actual);
            index++;
        }

        Assert.Equal(15, index);
    }

    [Fact]
    public void ReadAllByLines_Returns_Correct_Lines()
    {
        // Arrange
        var fileContent = "Line 1\nLine 2\nLine 3\n";
        var fileBytes = Encoding.ASCII.GetBytes(fileContent);
        var fileStream = new MemoryStream(fileBytes);
        var fileInfo = new FileInfo("test.txt");
        var osFileSystem = new OSFileSystem();

        // Write the file content to the file
        using (var file = fileInfo.Create())
        {
            file.Write(fileBytes, 0, fileBytes.Length);
        }

        // Act
        var lines = osFileSystem.ReadAllByLines(fileInfo, new BufferWriter<char>());

        // Assert
        var expectedLines = new[] { "Line 1", "Line 2", "Line 3" };
        var i = 0;
        foreach (var line in lines)
        {
            Assert.Equal(expectedLines[i], line.ToString());
            i++;
        }

        Assert.Equal(expectedLines.Length, i);
    }

    [Fact]
    public void ReadAllByLines_Returns_Empty_Sequence_For_Empty_File()
    {
        // Arrange
        var fileInfo = new FileInfo("test.txt");
        var osFileSystem = new OSFileSystem();

        // Write an empty file
        using (var file = fileInfo.Create())
        {
            // Do nothing
        }

        // Act
        var lines = osFileSystem.ReadAllByLines(fileInfo, new BufferWriter<char>());

        // Assert
        Assert.Empty(lines);
    }

    [Fact]
    public void ReadAllByLines_Returns_Single_Line_For_Single_Line_File()
    {
        // Arrange
        var fileContent = "Line 1";
        var fileBytes = Encoding.ASCII.GetBytes(fileContent);
        var fileInfo = new FileInfo("test.txt");
        var osFileSystem = new OSFileSystem();

        // Write the file content to the file
        using (var file = fileInfo.Create())
        {
            file.Write(fileBytes, 0, fileBytes.Length);
        }

        // Act
        var lines = osFileSystem.ReadAllByLines(fileInfo, new BufferWriter<char>());

        // Assert
        var expectedLines = new[] { "Line 1" };
        var i = 0;
        foreach (var line in lines)
        {
            Assert.Equal(expectedLines[i], line.ToString());
            i++;
        }

        Assert.Equal(expectedLines.Length, i);
    }

    [Fact]
    public void ReadAllByLines_Returns_Empty_Sequence_For_Nonexistent_File()
    {
        // Arrange
        var fileInfo = new FileInfo("nonexistent.txt");
        var osFileSystem = new OSFileSystem();

        // Act
        // Assert
        Assert.Throws<FileNotFoundException>(() => osFileSystem.ReadAllByLines(fileInfo, new BufferWriter<char>()).ToList());
    }

    [Fact]
    public void ReadAllByLines_Returns_Correct_Lines_For_Large_File()
    {
        // Arrange
        // Large line counts of file (Should always be more than 420)
        var count = 900000;
        var fileContent = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            fileContent.AppendLine($"Line {i}");
        }

        var fileBytes = Encoding.ASCII.GetBytes(fileContent.ToString());
        var fileStream = new MemoryStream(fileBytes);
        var fileInfo = new FileInfo("test.txt");
        var osFileSystem = new OSFileSystem();

        // Write the file content to the file
        using (var file = fileInfo.Create())
        {
            file.Write(fileBytes, 0, fileBytes.Length);
        }

        // Act
        var lines = osFileSystem.ReadAllByLines(fileInfo, new BufferWriter<char>());

        // Assert
        var expectedLines = Enumerable.Range(0, count).Select(i => $"Line {i}");
        var cnt = 0;
        foreach (var line in lines)
        {
            Assert.Equal(expectedLines.ElementAt(cnt), line.ToString().Replace("\r", ""));
            cnt++;
        }
    }
}
