// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable MEAI001
#pragma warning disable OPENAI001

namespace Microsoft.Extensions.AI;

public sealed class OpenAIHostedFileClientIntegrationTests : IDisposable
{
    private readonly IHostedFileClient? _client = IntegrationTestHelpers.GetOpenAIClient()?.AsIHostedFileClient();

    public void Dispose()
    {
        _client?.Dispose();
    }

    [Fact]
    public async Task Upload_Download_Delete_Roundtrip()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.jsonl";
        string content = """{"prompt": "hello", "completion": "world"}""";
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);
        string? uploadedFileId = null;

        try
        {
            // Upload with "fine-tune" purpose since "assistants" files cannot be downloaded via the Files API.
            using var uploadStream = new MemoryStream(contentBytes);
            var uploadedFile = await _client!.UploadAsync(uploadStream, "application/jsonl", fileName, new HostedFileClientOptions { Purpose = "fine-tune" });
            uploadedFileId = uploadedFile.FileId;

            Assert.NotNull(uploadedFile.FileId);
            Assert.NotEmpty(uploadedFile.FileId);
            Assert.Equal(fileName, uploadedFile.Name);
            Assert.Equal("application/x-ndjson", uploadedFile.MediaType);
            Assert.NotNull(uploadedFile.Purpose);
            Assert.NotNull(uploadedFile.SizeInBytes);
            Assert.Equal(contentBytes.Length, uploadedFile.SizeInBytes);
            Assert.NotNull(uploadedFile.CreatedAt);
            Assert.NotNull(uploadedFile.RawRepresentation);

            // Download (with retry - files may not be immediately available)
            string downloadedContent = await RetryAsync(async () =>
            {
                using var downloadStream = await _client.DownloadAsync(uploadedFileId);
                Assert.Equal("application/x-ndjson", downloadStream.MediaType);
                Assert.Equal(fileName, downloadStream.FileName);
                using var reader = new StreamReader(downloadStream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            });

            Assert.Equal(content, downloadedContent);

            // Delete
            bool deleted = await _client.DeleteAsync(uploadedFileId);
            Assert.True(deleted);
            uploadedFileId = null;

            // Verify not found after deletion
            var fileInfo = await _client.GetFileInfoAsync(uploadedFile.FileId);
            Assert.Null(fileInfo);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }
        }
    }

    [Fact]
    public async Task Upload_ListFiles_VerifyPresent()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.txt";
        string? uploadedFileId = null;

        try
        {
            using var uploadStream = new MemoryStream("list test"u8.ToArray());
            var uploadedFile = await _client!.UploadAsync(uploadStream, "text/plain", fileName, new HostedFileClientOptions { Purpose = "assistants" });
            uploadedFileId = uploadedFile.FileId;

            var files = new List<HostedFileContent>();
            await foreach (var file in _client.ListFilesAsync(new HostedFileClientOptions { Purpose = "assistants" }))
            {
                files.Add(file);
            }

            Assert.Contains(files, f => f.FileId == uploadedFileId && f.MediaType == "text/plain" && f.Name == fileName);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }
        }
    }

    [Fact]
    public async Task GetFileInfo_ReturnsMetadata()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.txt";
        byte[] contentBytes = "metadata test"u8.ToArray();
        string? uploadedFileId = null;

        try
        {
            using var uploadStream = new MemoryStream(contentBytes);
            var uploadedFile = await _client!.UploadAsync(uploadStream, "text/plain", fileName, new HostedFileClientOptions { Purpose = "assistants" });
            uploadedFileId = uploadedFile.FileId;

            var fileInfo = await _client.GetFileInfoAsync(uploadedFileId);

            Assert.NotNull(fileInfo);
            Assert.Equal(uploadedFileId, fileInfo.FileId);
            Assert.Equal(fileName, fileInfo.Name);
            Assert.Equal("text/plain", fileInfo.MediaType);
            Assert.NotNull(fileInfo.SizeInBytes);
            Assert.True(fileInfo.SizeInBytes > 0);
            Assert.NotNull(fileInfo.Purpose);
            Assert.NotNull(fileInfo.CreatedAt);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }
        }
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        SkipIfNotEnabled();

        bool deleted = await _client!.DeleteAsync("file-nonexistent000000000000");
        Assert.False(deleted);
    }

    [Fact]
    public async Task GetFileInfo_NonExistent_ReturnsNull()
    {
        SkipIfNotEnabled();

        var fileInfo = await _client!.GetFileInfoAsync("file-nonexistent000000000000");
        Assert.Null(fileInfo);
    }

    [Fact]
    public async Task Upload_DataContent_Extension()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.txt";
        byte[] contentBytes = "data content test"u8.ToArray();
        string? uploadedFileId = null;

        try
        {
            var dataContent = new DataContent(contentBytes, "text/plain") { Name = fileName };
            var uploadedFile = await _client!.UploadAsync(dataContent, new HostedFileClientOptions { Purpose = "assistants" });
            uploadedFileId = uploadedFile.FileId;

            Assert.NotNull(uploadedFile.FileId);
            Assert.NotEmpty(uploadedFile.FileId);
            Assert.Equal("text/plain", uploadedFile.MediaType);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }
        }
    }

    [Fact]
    public async Task Download_AsDataContent_Extension()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.jsonl";
        string content = """{"prompt": "hello", "completion": "world"}""";
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);
        string? uploadedFileId = null;

        try
        {
            using var uploadStream = new MemoryStream(contentBytes);
            var uploadedFile = await _client!.UploadAsync(uploadStream, "application/jsonl", fileName, new HostedFileClientOptions { Purpose = "fine-tune" });
            uploadedFileId = uploadedFile.FileId;

            var dataContent = await RetryAsync(() => _client.DownloadAsDataContentAsync(uploadedFileId));
            Assert.Equal(content, Encoding.UTF8.GetString(dataContent.Data.ToArray()));
            Assert.Equal("application/x-ndjson", dataContent.MediaType);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }
        }
    }

    [Fact]
    public async Task Upload_DownloadTo_Extension()
    {
        SkipIfNotEnabled();

        string fileName = $"test-{Guid.NewGuid():N}.jsonl";
        string content = """{"prompt": "hello", "completion": "world"}""";
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);
        string? uploadedFileId = null;
        string? tempDir = null;

        try
        {
            using var uploadStream = new MemoryStream(contentBytes);
            var uploadedFile = await _client!.UploadAsync(uploadStream, "application/jsonl", fileName, new HostedFileClientOptions { Purpose = "fine-tune" });
            uploadedFileId = uploadedFile.FileId;

            tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string savedPath = await RetryAsync(() => _client.DownloadToAsync(uploadedFileId, tempDir));

            Assert.True(File.Exists(savedPath));
            string savedContent = File.ReadAllText(savedPath);
            Assert.Equal(content, savedContent);
        }
        finally
        {
            if (uploadedFileId is not null)
            {
                await _client!.DeleteAsync(uploadedFileId);
            }

            if (tempDir is not null && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Container_Upload_Download_Delete_Roundtrip()
    {
        SkipIfNotEnabled();

        // Create a container-scoped file client
        using var containerClient = IntegrationTestHelpers.GetOpenAIClient()!.GetContainerClient().AsIHostedFileClient();

        // First, use the chat client with code interpreter to get a container ID
        using var chatClient = IntegrationTestHelpers.GetOpenAIClient()!
            .GetResponsesClient()
            .AsIChatClient(TestRunnerConfiguration.Instance["OpenAI:ChatModel"] ?? "gpt-4o-mini");

        var response = await chatClient.GetResponseAsync(
            "Calculate 2+2 using Python",
            new ChatOptions { Tools = [new HostedCodeInterpreterTool()] });

        // Extract container ID from the code interpreter result
        string? containerId = null;
        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is CodeInterpreterToolResultContent { RawRepresentation: OpenAI.Responses.CodeInterpreterCallResponseItem cicri })
                {
                    containerId = cicri.ContainerId;
                }
            }
        }

        Assert.NotNull(containerId);

        // Upload a file to the container
        string content2 = """{"data": "test"}""";
        byte[] contentBytes = Encoding.UTF8.GetBytes(content2);
        using var uploadStream = new MemoryStream(contentBytes);
        var uploadedFile = await containerClient.UploadAsync(
            uploadStream, "application/json", "test-data.json",
            new HostedFileClientOptions { Scope = containerId });

        Assert.NotNull(uploadedFile.FileId);
        Assert.NotEmpty(uploadedFile.FileId);
        Assert.Equal(containerId, uploadedFile.Scope);

        // List files in the container
        var files = new List<HostedFileContent>();
        await foreach (var file in containerClient.ListFilesAsync(new HostedFileClientOptions { Scope = containerId }))
        {
            files.Add(file);
        }

        Assert.Contains(files, f => f.FileId == uploadedFile.FileId);

        // Get file info
        var fileInfo = await containerClient.GetFileInfoAsync(uploadedFile.FileId, new HostedFileClientOptions { Scope = containerId });
        Assert.NotNull(fileInfo);
        Assert.Equal(uploadedFile.FileId, fileInfo.FileId);

        // Download the file
        using var downloadStream = await containerClient.DownloadAsync(uploadedFile.FileId, new HostedFileClientOptions { Scope = containerId });
        Assert.NotNull(downloadStream);
        using var reader = new StreamReader(downloadStream, Encoding.UTF8);
        string downloadedContent = await reader.ReadToEndAsync();
        Assert.Equal(content2, downloadedContent);

        // Delete the file
        bool deleted = await containerClient.DeleteAsync(uploadedFile.FileId, new HostedFileClientOptions { Scope = containerId });
        Assert.True(deleted);
    }

    [Fact]
    public async Task CodeInterpreter_ProducesDownloadableOutputs()
    {
        SkipIfNotEnabled();

        using var chatClient = IntegrationTestHelpers.GetOpenAIClient()!
            .GetResponsesClient()
            .AsIChatClient(TestRunnerConfiguration.Instance["OpenAI:ChatModel"] ?? "gpt-4o-mini");

        using var fileClient = IntegrationTestHelpers.GetOpenAIClient()!.GetContainerClient().AsIHostedFileClient();

        // Ask the model to create a file via code interpreter
        var response = await chatClient.GetResponseAsync(
            "Use Python to create a JSON file at /mnt/data/output.json containing the numbers 1 through 5 as an array. "
            + "Do not include any text explanation, just run the code.",
            new ChatOptions { Tools = [new HostedCodeInterpreterTool()] });

        // Find the code interpreter result and its container
        string? containerId = null;
        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is CodeInterpreterToolResultContent { RawRepresentation: OpenAI.Responses.CodeInterpreterCallResponseItem cicri })
                {
                    containerId = cicri.ContainerId;
                }
            }
        }

        Assert.NotNull(containerId);

        // List files in the container — the code interpreter should have created at least the output file
        var containerFiles = new List<HostedFileContent>();
        await foreach (var file in fileClient.ListFilesAsync(new HostedFileClientOptions { Scope = containerId }))
        {
            containerFiles.Add(file);
        }

        Assert.NotEmpty(containerFiles);

        // Download a container file and verify it's non-empty
        var firstFile = containerFiles[0];
        using var downloadStream = await fileClient.DownloadAsync(firstFile, new HostedFileClientOptions { Scope = containerId });
        Assert.NotNull(downloadStream);
        using var ms = new MemoryStream();
        await downloadStream.CopyToAsync(ms);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task CodeInterpreter_Upload_ProcessedByCodeInterpreter()
    {
        SkipIfNotEnabled();

        using var chatClient = IntegrationTestHelpers.GetOpenAIClient()!
            .GetResponsesClient()
            .AsIChatClient(TestRunnerConfiguration.Instance["OpenAI:ChatModel"] ?? "gpt-4o-mini");

        using var fileClient = IntegrationTestHelpers.GetOpenAIClient()!.GetContainerClient().AsIHostedFileClient();

        // First, use code interpreter to get a container ID
        var setupResponse = await chatClient.GetResponseAsync(
            "Calculate 1+1 using Python",
            new ChatOptions { Tools = [new HostedCodeInterpreterTool()] });

        string? containerId = null;
        foreach (var msg in setupResponse.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is CodeInterpreterToolResultContent { RawRepresentation: OpenAI.Responses.CodeInterpreterCallResponseItem cicri })
                {
                    containerId = cicri.ContainerId;
                }
            }
        }

        Assert.NotNull(containerId);

        // Upload a CSV file to the container
        string csvContent = "name,value\nalpha,1\nbeta,2\ngamma,3";
        using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var uploadedFile = await fileClient.UploadAsync(
            uploadStream, "text/csv", "data.csv",
            new HostedFileClientOptions { Scope = containerId });

        Assert.NotNull(uploadedFile.FileId);
        Assert.Equal(containerId, uploadedFile.Scope);

        // Ask the model to process the uploaded file, continuing the same conversation
        var processResponse = await chatClient.GetResponseAsync(
            "Read the CSV file at /mnt/data/data.csv and tell me the sum of the 'value' column. Reply with just the number.",
            new ChatOptions
            {
                Tools = [new HostedCodeInterpreterTool()],
                ConversationId = setupResponse.ConversationId,
            });

        // The response should mention "6" (1+2+3)
        string responseText = processResponse.Text ?? "";
        Assert.Contains("6", responseText);
    }

    /// <summary>Retries a download operation to handle the delay between upload and file availability.</summary>
    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 5, int delayMs = 2000)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await action();
            }
            catch (ClientResultException) when (i < maxRetries - 1)
            {
                await Task.Delay(delayMs * (i + 1));
            }
        }

        return await action();
    }

    private void SkipIfNotEnabled()
    {
        string? skipIntegration = TestRunnerConfiguration.Instance["SkipIntegrationTests"];

        Assert.SkipUnless(skipIntegration is null && _client is not null, "Client is not enabled.");
    }
}
