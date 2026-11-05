// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion.Tests.Utils;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public abstract class DocumentReaderConformanceTests
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    protected abstract IngestionDocumentReader CreateDocumentReader(bool extractImages = false);

    [Fact]
    public async Task ThrowsWhenIdentifierIsNotProvided()
    {
        var reader = CreateDocumentReader();

        await Assert.ThrowsAsync<ArgumentNullException>("identifier", async () => await reader.ReadAsync(new FileInfo("fileName.txt"), identifier: null!));
        await Assert.ThrowsAsync<ArgumentException>("identifier", async () => await reader.ReadAsync(new FileInfo("fileName.txt"), identifier: string.Empty));

        using MemoryStream stream = new();
        await Assert.ThrowsAsync<ArgumentNullException>("identifier", async () => await reader.ReadAsync(stream, identifier: null!, mediaType: "some"));
        await Assert.ThrowsAsync<ArgumentException>("identifier", async () => await reader.ReadAsync(stream, identifier: string.Empty, mediaType: "some"));
    }

    [Fact]
    public async Task ThrowsIfCancellationRequestedStream()
    {
        var reader = CreateDocumentReader();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        using MemoryStream stream = new();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await reader.ReadAsync(stream, "id", "mediaType", cts.Token));
    }

    [Fact]
    public async Task ThrowsIfCancellationRequestedFile()
    {
        string filePath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".txt");
#if NET
        await File.WriteAllTextAsync(filePath, "This is a test file for cancellation token.");
#else
        File.WriteAllText(filePath, "This is a test file for cancellation token.");
#endif

        var reader = CreateDocumentReader();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await reader.ReadAsync(new FileInfo(filePath), cts.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    public static TheoryData<string> Links =>
    [
        "https://winprotocoldocs-bhdugrdyduf5h2e4.b02.azurefd.net/MS-NRBF/%5bMS-NRBF%5d-190313.pdf", // PDF file
        "https://winprotocoldocs-bhdugrdyduf5h2e4.b02.azurefd.net/MS-NRBF/%5bMS-NRBF%5d-190313.docx", // DOCX file
        "https://www.bondcap.com/report/pdf/Trends_Artificial_Intelligence.pdf", // PDF file (presentation)
    ];

    [Theory]
    [MemberData(nameof(Links))]
    public virtual async Task SupportsStreams(string source)
    {
        using HttpResponseMessage response = await DownloadAsync(new(source));

        IngestionDocument document = await CreateDocumentReader().ReadAsync(
            await response.Content.ReadAsStreamAsync(),
            source, mediaType: response.Content.Headers.ContentType?.MediaType!);

        SimpleAsserts(document, source, source);
    }

    [Theory]
    [MemberData(nameof(Links))]
    public virtual async Task SupportsFiles(string source)
    {
        FileInfo inputFile = await DownloadToFileAsync(new Uri(source));

        try
        {
            IngestionDocument document = await CreateDocumentReader().ReadAsync(inputFile);

            SimpleAsserts(document, inputFile.FullName, inputFile.FullName);
        }
        finally
        {
            inputFile.Delete();
        }
    }

    [Fact]
    public virtual Task SupportsImages() => SupportsImagesCore(
        new("https://winprotocoldocs-bhdugrdyduf5h2e4.b02.azurefd.net/MC-SQLR/%5bMC-SQLR%5d.pdf")); // SQL Server Resolution Protocol

    protected async Task SupportsImagesCore(Uri source)
    {
        FileInfo inputFile = await DownloadToFileAsync(source);

        try
        {
            var reader = CreateDocumentReader(extractImages: true);
            var document = await reader.ReadAsync(inputFile);

            SimpleAsserts(document, inputFile.FullName, expectedId: inputFile.FullName);
            var elements = document.EnumerateContent().ToArray();
            Assert.Contains(elements, element => element is IngestionDocumentImage img && img.Content.HasValue && !string.IsNullOrEmpty(img.MediaType));
        }
        finally
        {
            inputFile.Delete();
        }
    }

    [Fact]
    public virtual async Task SupportsTables()
    {
        string[,] expected =
        {
            { "Milestone", "Target Date", "Department", "Indicator" },
            { "Environmental Audit", "Mar 2025", "Environmental", "Audit Complete" },
            { "Renewable Energy Launch", "Jul 2025", "Facilities", "Install Operational" },
            { "Staff Workshop", "Sep 2025", "HR", "Workshop Held" },
            { "Emissions Review", "Dec 2029", "All", "25% Emissions Cut" }
        };
        using Stream wordDoc = DocxHelper.CreateDocumentWithTable(expected);

        var document = await CreateDocumentReader().ReadAsync(wordDoc, "doc", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        IngestionDocumentTable documentTable = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());
        Assert.Equal(5, documentTable.Cells.GetLength(0));
        Assert.Equal(4, documentTable.Cells.GetLength(1));

        Assert.Equal(expected, documentTable.Cells.Map(NormalizeCell));
    }

    protected static async Task<HttpResponseMessage> DownloadAsync(Uri uri)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(uri);

#if !NET
            // .NET Framework HttpClient does not automatically follow permanent redirects.
            if (response.StatusCode == (System.Net.HttpStatusCode)308)
            {
                string? redirectUri = response.Headers.Location?.ToString();
                Assert.False(string.IsNullOrEmpty(redirectUri), "Redirect URI is null or empty.");
                response.Dispose();
                response = await _httpClient.GetAsync(new Uri(redirectUri!));
            }
#endif

            Assert.True(response.IsSuccessStatusCode);
            return response;
        }
        catch (Exception ex)
        {
            Assert.Skip($"Unable to download the test file: '{ex.Message}'");
            throw; // Unreachable, but needed to satisfy compiler return requirement.
        }
    }

    protected static async Task<FileInfo> DownloadToFileAsync(Uri uri)
    {
        using HttpResponseMessage response = await DownloadAsync(uri);

        string extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "application/pdf" => ".pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            _ when uri.OriginalString.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) => ".pdf",
            _ when uri.OriginalString.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) => ".docx",
            _ => string.Empty
        };

        FileInfo file = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension));
        using FileStream inputStream = new(file.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
        await response.Content.CopyToAsync(inputStream);

        return file;
    }

    protected virtual void SimpleAsserts(IngestionDocument document, string source, string expectedId)
    {
        Assert.NotNull(document);
        Assert.Equal(expectedId, document.Identifier);
        Assert.NotEmpty(document.Sections);

        var elements = document.EnumerateContent().ToArray();
        Assert.Contains(elements, element => element is IngestionDocumentHeader);
        Assert.Contains(elements, element => element is IngestionDocumentParagraph);
        Assert.Contains(elements, element => element is IngestionDocumentTable);
        Assert.All(elements.Where(element => element is not IngestionDocumentImage), element => Assert.NotEmpty(element.GetMarkdown()));
    }

    private static string? NormalizeCell(IngestionDocumentElement? ingestionDocumentElement)
    {
        Assert.NotNull(ingestionDocumentElement);

        // Some readers add extra spaces or asterisks for bold/italic text for headers.
        return ingestionDocumentElement.GetMarkdown().Trim().Trim('*');
    }
}
