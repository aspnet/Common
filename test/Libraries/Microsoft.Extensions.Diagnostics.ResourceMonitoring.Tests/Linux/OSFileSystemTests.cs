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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(2049)]
    [InlineData(5000)]
    public void ReadAll_Returns_Whole_File_Across_Multiple_Reads(int length)
    {
        var content = CreateContent(length);
        var file = CreateTempFile(content);

        try
        {
            var bw = new BufferWriter<char>();
            new OSFileSystem().ReadAll(file, bw);

            Assert.Equal(content, new string(bw.WrittenSpan));
        }
        finally
        {
            file.Delete();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(1000)]
    public void ReadFirstLine_Stops_At_Newline_Across_Multiple_Reads(int firstLineLength)
    {
        var firstLine = CreateContent(firstLineLength);
        var file = CreateTempFile(firstLine + "\n" + CreateContent(3000));

        try
        {
            var bw = new BufferWriter<char>();
            new OSFileSystem().ReadFirstLine(file, bw);

            Assert.Equal(firstLine, new string(bw.WrittenSpan));
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public void ReadFirstLine_Returns_Whole_File_If_There_Is_No_Newline()
    {
        var content = CreateContent(600);
        var file = CreateTempFile(content);

        try
        {
            var bw = new BufferWriter<char>();
            new OSFileSystem().ReadFirstLine(file, bw);

            Assert.Equal(content, new string(bw.WrittenSpan));
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public void Read_Returns_At_Most_The_Requested_Length()
    {
        var content = CreateContent(1000);
        var file = CreateTempFile(content);

        try
        {
            var destination = new char[400];
            var written = new OSFileSystem().Read(file, 300, destination);

            Assert.Equal(300, written);
            Assert.Equal(content.Substring(0, 300), new string(destination, 0, written));
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public void Reading_Nonexistent_File_Throws_FileNotFoundException()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var fileSystem = new OSFileSystem();

        Assert.Throws<FileNotFoundException>(() => fileSystem.ReadAll(file, new BufferWriter<char>()));
        Assert.Throws<FileNotFoundException>(() => fileSystem.ReadFirstLine(file, new BufferWriter<char>()));
        Assert.Throws<FileNotFoundException>(() => fileSystem.Read(file, 10, new char[10]));
    }

    [Fact]
    public void Reading_Can_Be_Done_While_File_Is_Open_For_Reading()
    {
        var file = CreateTempFile("line 1\nline 2\n");

        try
        {
            using var other = file.OpenRead();

            var bw = new BufferWriter<char>();
            new OSFileSystem().ReadAll(file, bw);

            Assert.Equal("line 1\nline 2\n", new string(bw.WrittenSpan));
        }
        finally
        {
            file.Delete();
        }
    }

    private static string CreateContent(int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append((char)('a' + (i % 26)));
        }

        return builder.ToString();
    }

    private static FileInfo CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content, Encoding.ASCII);
        return new FileInfo(path);
    }
}
