// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable MEAI001

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class HostedFileClientExtensionsTests
{
    [Fact]
    public async Task UploadAsync_DataContent_PassesCorrectArgs()
    {
        var data = new byte[] { 10, 20, 30 };
        var content = new DataContent(data, "application/pdf") { Name = "doc.pdf" };
        Stream? capturedStream = null;
        string? capturedMediaType = null;
        string? capturedName = null;

        using var client = new TestHostedFileClient
        {
            UploadAsyncCallback = (stream, mediaType, fileName, options, ct) =>
            {
                capturedStream = stream;
                capturedMediaType = mediaType;
                capturedName = fileName;
                return Task.FromResult(new HostedFileContent("file-1"));
            }
        };
        var result = await client.UploadAsync(content);
        Assert.NotNull(capturedStream);
        capturedStream!.Position = 0;
        var buffer = new byte[capturedStream.Length];
        _ = await capturedStream.ReadAsync(buffer, 0, buffer.Length);
        Assert.Equal(data, buffer);
        Assert.Equal("application/pdf", capturedMediaType);
        Assert.Equal("doc.pdf", capturedName);
        Assert.Equal("file-1", result.FileId);
    }

    [Fact]
    public async Task UploadAsync_DataContent_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        var content = new DataContent(new byte[] { 1 }, "text/plain");
        await Assert.ThrowsAsync<ArgumentNullException>("client", () => client.UploadAsync(content));
    }

    [Fact]
    public async Task UploadAsync_DataContent_NullContent_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("content", () => client.UploadAsync((DataContent)null!));
    }

    [Fact]
    public async Task UploadAsync_FilePath_CreatesStreamAndInfersMediaType()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.txt");
        var fileContent = new byte[] { 65, 66, 67 }; // ABC
        await File.WriteAllBytesAsync(tempFile, fileContent);

        try
        {
            Stream? capturedStream = null;
            string? capturedMediaType = null;
            string? capturedFileName = null;

            using var client = new TestHostedFileClient
            {
                UploadAsyncCallback = async (stream, mediaType, fileName, options, ct) =>
                {
                    var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, 81920, ct);
                    capturedStream = ms;
                    capturedMediaType = mediaType;
                    capturedFileName = fileName;
                    return new HostedFileContent("file-2");
                }
            };
            var result = await client.UploadAsync(tempFile);
            Assert.NotNull(capturedStream);
            Assert.Equal(fileContent, ((MemoryStream)capturedStream!).ToArray());
            Assert.Equal("text/plain", capturedMediaType);
            Assert.Equal(Path.GetFileName(tempFile), capturedFileName);
            Assert.Equal("file-2", result.FileId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadAsync_FilePath_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        await Assert.ThrowsAsync<ArgumentNullException>("client", () => client.UploadAsync("somefile.txt"));
    }

    [Fact]
    public async Task UploadAsync_FilePath_NullPath_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("filePath", () => client.UploadAsync((string)null!));
    }

    [Fact]
    public async Task UploadAsync_FilePath_EmptyPath_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentException>("filePath", () => client.UploadAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadToAsync_SavesStreamToFilePath()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var tempFile = Path.Combine(Path.GetTempPath(), $"download-{Guid.NewGuid()}.bin");

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
            {
                Assert.Equal("file-dl", fileId);
                return Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream(data));
            }
        };

        try
        {
            var savedPath = await client.DownloadToAsync("file-dl", tempFile);
            Assert.Equal(tempFile, savedPath);
            Assert.Equal(data, await File.ReadAllBytesAsync(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadToAsync_DirectoryPath_UsesFileName()
    {
        var data = new byte[] { 10, 20 };
        var tempDir = Path.Combine(Path.GetTempPath(), $"dldir-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
                Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream(data, fileName: "result.bin"))
        };

        try
        {
            var savedPath = await client.DownloadToAsync("file-dl2", tempDir);
            Assert.Equal(Path.Combine(tempDir, "result.bin"), savedPath);
            Assert.Equal(data, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("application/unknown", "")]
    [InlineData("text/plain", ".txt")]
    [InlineData("text/plain; charset=utf-8", ".txt")]
    [InlineData("TEXT/PLAIN", ".txt")]
    public async Task DownloadToAsync_DirectoryPath_NoFileName_UsesFileId(string? mediaType, string extension)
    {
        var data = new byte[] { 99 };
        var tempDir = Path.Combine(Path.GetTempPath(), $"dldir2-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
                Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream(data, mediaType: mediaType))
        };

        try
        {
            var savedPath = await client.DownloadToAsync("file-id-fallback", tempDir);
            Assert.Equal(Path.Combine(tempDir, $"file-id-fallback{extension}"), savedPath);
            Assert.Equal(data, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadToAsync_EmptyDestinationPath_UsesCurrentDirectory()
    {
        var data = new byte[] { 42, 43 };
        var fileName = $"output_{Guid.NewGuid()}.bin";

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
                Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream(data, fileName: fileName))
        };

        // Capture cwd at call time so the assertion doesn't depend on cwd at check time.
        string cwdBefore = Directory.GetCurrentDirectory();
        string expectedAbsolute = Path.Combine(cwdBefore, fileName);
        string? savedPath = null;
        try
        {
            savedPath = await client.DownloadToAsync("file-cwd", string.Empty);

            // Empty destination path => file is written to cwd; returned path is just the filename (relative).
            Assert.Equal(fileName, savedPath);
            Assert.False(Path.IsPathRooted(savedPath));

            Assert.True(File.Exists(expectedAbsolute));
            Assert.Equal(data, await File.ReadAllBytesAsync(expectedAbsolute));
        }
        finally
        {
            File.Delete(expectedAbsolute);
        }
    }

    [Fact]
    public async Task DownloadToAsync_FileAlreadyExists_Throws()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"existing-{Guid.NewGuid()}.bin");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
                Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream(new byte[] { 4, 5, 6 }))
        };

        try
        {
            await Assert.ThrowsAsync<IOException>(() => client.DownloadToAsync("file-dup", tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadToAsync_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        await Assert.ThrowsAsync<ArgumentNullException>("client", () => client.DownloadToAsync("file-1", "path"));
    }

    [Fact]
    public async Task DownloadToAsync_NullFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("fileId", () => client.DownloadToAsync(null!, "path"));
    }

    [Fact]
    public async Task DownloadToAsync_NullDestinationPath_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("destinationPath", () => client.DownloadToAsync("file-1", null!));
    }

    [Fact]
    public async Task DownloadToAsync_EmptyFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentException>("fileId", () => client.DownloadToAsync(string.Empty, "path"));
    }

    [Fact]
    public async Task DownloadToAsync_WhitespaceFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentException>("fileId", () => client.DownloadToAsync("   ", "path"));
    }

    [Fact]
    public async Task DownloadAsync_HostedFileContent_PassesFileId()
    {
        var hostedFileContent = new HostedFileContent("file-hfc");
        string? capturedFileId = null;

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
            {
                capturedFileId = fileId;
                return Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream([]));
            }
        };
        using var stream = await client.DownloadAsync(hostedFileContent);
        Assert.Equal("file-hfc", capturedFileId);
    }

    [Fact]
    public async Task DownloadAsync_HostedFileContent_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        var content = new HostedFileContent("file-1");
        await Assert.ThrowsAsync<ArgumentNullException>("client", () => client.DownloadAsync(content));
    }

    [Fact]
    public async Task DownloadAsync_HostedFileContent_NullContent_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("hostedFile", () => client.DownloadAsync((HostedFileContent)null!));
    }

    [Fact]
    public async Task DownloadAsync_HostedFileContent_FlowsScope()
    {
        var hostedFileContent = new HostedFileContent("file-scoped") { Scope = "container-42" };
        HostedFileClientOptions? capturedOptions = null;

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
            {
                capturedOptions = options;
                return Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream([]));
            }
        };
        using var stream = await client.DownloadAsync(hostedFileContent);
        Assert.NotNull(capturedOptions);
        Assert.Equal("container-42", capturedOptions.Scope);
    }

    [Fact]
    public async Task DownloadAsync_HostedFileContent_ExplicitOptionsScopeWins()
    {
        var hostedFileContent = new HostedFileContent("file-scoped") { Scope = "container-from-content" };
        var explicitOptions = new HostedFileClientOptions { Scope = "container-from-options" };
        HostedFileClientOptions? capturedOptions = null;

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
            {
                capturedOptions = options;
                return Task.FromResult<HostedFileDownloadStream>(new TestHostedFileDownloadStream([]));
            }
        };
        using var stream = await client.DownloadAsync(hostedFileContent, explicitOptions);
        Assert.NotNull(capturedOptions);
        Assert.Equal("container-from-options", capturedOptions.Scope);
    }

    [Fact]
    public async Task DownloadAsDataContentAsync_ReturnsCorrectDataContent()
    {
        var data = new byte[] { 7, 8, 9 };

        using var client = new TestHostedFileClient
        {
            DownloadAsyncCallback = (fileId, options, ct) =>
            {
                Assert.Equal("file-dc", fileId);
                return Task.FromResult<HostedFileDownloadStream>(
                    new TestHostedFileDownloadStream(data, mediaType: "image/png", fileName: "photo.png"));
            }
        };
        var result = await client.DownloadAsDataContentAsync("file-dc");
        Assert.Equal(data, result.Data.ToArray());
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal("photo.png", result.Name);
    }

    [Fact]
    public async Task DownloadAsDataContentAsync_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        await Assert.ThrowsAsync<ArgumentNullException>("client", () => client.DownloadAsDataContentAsync("file-1"));
    }

    [Fact]
    public async Task DownloadAsDataContentAsync_NullFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentNullException>("fileId", () => client.DownloadAsDataContentAsync(null!));
    }

    [Fact]
    public async Task DownloadAsDataContentAsync_EmptyFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentException>("fileId", () => client.DownloadAsDataContentAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadAsDataContentAsync_WhitespaceFileId_Throws()
    {
        using var client = new TestHostedFileClient();
        await Assert.ThrowsAsync<ArgumentException>("fileId", () => client.DownloadAsDataContentAsync("   "));
    }

    [Fact]
    public void GetMetadata_CallsGetServiceWithCorrectType()
    {
        var expectedMetadata = new HostedFileClientMetadata("test-provider");
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(HostedFileClientMetadata), type);
                Assert.Null(key);
                return expectedMetadata;
            }
        };
        var result = client.GetMetadata();
        Assert.Same(expectedMetadata, result);
    }

    [Fact]
    public void GetMetadata_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        Assert.Throws<ArgumentNullException>("client", () => client.GetMetadata());
    }

    [Fact]
    public void GetServiceGeneric_CallsGetServiceWithCorrectType()
    {
        var expectedResult = "some-service-value";
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(string), type);
                Assert.Null(key);
                return expectedResult;
            }
        };
        var result = client.GetService<string>();
        Assert.Same(expectedResult, result);
    }

    [Fact]
    public void GetServiceGeneric_WithKey_PassesKey()
    {
        var expectedKey = new object();
        var expectedResult = 42;
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(int), type);
                Assert.Same(expectedKey, key);
                return expectedResult;
            }
        };
        var result = client.GetService<int>(expectedKey);
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetServiceGeneric_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        Assert.Throws<ArgumentNullException>("client", () => client.GetService<string>());
    }

    [Fact]
    public void GetRequiredService_ReturnsService()
    {
        var expectedResult = "some-service-value";
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(string), type);
                Assert.Null(key);
                return expectedResult;
            }
        };
        var result = client.GetRequiredService<string>();
        Assert.Same(expectedResult, result);
    }

    [Fact]
    public void GetRequiredService_WithKey_PassesKey()
    {
        var expectedKey = new object();
        var expectedResult = 42;
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(int), type);
                Assert.Same(expectedKey, key);
                return expectedResult;
            }
        };
        var result = client.GetRequiredService<int>(expectedKey);
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetRequiredService_NotFound_Throws()
    {
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) => null
        };
        Assert.Throws<InvalidOperationException>(() => client.GetRequiredService<string>());
    }

    [Fact]
    public void GetRequiredService_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        Assert.Throws<ArgumentNullException>("client", () => client.GetRequiredService<string>());
    }

    [Fact]
    public void GetRequiredServiceNonGeneric_ReturnsService()
    {
        var expectedResult = "some-service-value";
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) =>
            {
                Assert.Equal(typeof(string), type);
                Assert.Null(key);
                return expectedResult;
            }
        };
        var result = client.GetRequiredService(typeof(string));
        Assert.Same(expectedResult, result);
    }

    [Fact]
    public void GetRequiredServiceNonGeneric_NotFound_Throws()
    {
        using var client = new TestHostedFileClient
        {
            GetServiceCallback = (type, key) => null
        };
        Assert.Throws<InvalidOperationException>(() => client.GetRequiredService(typeof(string)));
    }

    [Fact]
    public void GetRequiredServiceNonGeneric_NullClient_Throws()
    {
        IHostedFileClient client = null!;
        Assert.Throws<ArgumentNullException>("client", () => client.GetRequiredService(typeof(string)));
    }

    [Fact]
    public void GetRequiredServiceNonGeneric_NullServiceType_Throws()
    {
        using var client = new TestHostedFileClient();
        Assert.Throws<ArgumentNullException>("serviceType", () => client.GetRequiredService(null!));
    }

    private sealed class TestHostedFileDownloadStream : HostedFileDownloadStream
    {
        private readonly MemoryStream _inner;

        public TestHostedFileDownloadStream(byte[] data, string? mediaType = null, string? fileName = null)
        {
            _inner = new MemoryStream(data);
            MediaType = mediaType;
            FileName = fileName;
        }

        public override string? MediaType { get; }
        public override string? FileName { get; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
