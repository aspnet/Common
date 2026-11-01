// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

#pragma warning disable S881 // Increment (++) and decrement (--) operators should not be used in a method call or mixed with other operators in an expression

public sealed class IngestionPipelineTests : IDisposable
{
    private readonly FileInfo _withTable;
    private readonly FileInfo _withImage;
    private readonly IReadOnlyList<FileInfo> _sampleFiles;
    private readonly DirectoryInfo _sampleDirectory;

    public IngestionPipelineTests()
    {
        _sampleDirectory = Directory.CreateDirectory(Path.Combine("TestFiles"));

        _withTable = new(Path.Combine("TestFiles", "withTable.md"));
        const string FirstFileContent = """
            # First Document

            This is the content of the first document.

            ## Subsection

            More content in section 1.

            ## Table

            What a nice table!

            | Header1 | Header2 |
            |---------|---------|
            | Cell1   | Cell2   |
            | Cell3   | Cell4   |
            """;
        File.WriteAllText(_withTable.FullName, FirstFileContent);

        _withImage = new(Path.Combine("TestFiles", "withImage.md"));
        string secondFileContent = $"""
            # Second Document

            Content for the second document goes here.

            ## Another Subsection

            Additional content in section 2.

            It comes with an image!

            ![Sample Image](data:image/png;base64,{Convert.ToBase64String(new byte[1000])})
            """;
        File.WriteAllText(_withImage.FullName, secondFileContent);

        _sampleFiles = [_withTable, _withImage];
    }

    public void Dispose()
    {
        _sampleDirectory.Delete(recursive: true);
    }

    [Fact]
    public async Task CanProcessDocuments()
    {
        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);

        using IngestionPipeline pipeline = new(CreateReader(), CreateChunker(), vectorStoreWriter);
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(_sampleFiles).ToListAsync();

        Assert.Equal(_sampleFiles.Count, ingestionResults.Count);
        AssertAllIngestionsSucceeded(ingestionResults);

        Assert.True(embeddingGenerator.WasCalled, "Embedding generator should have been called.");

        List<IngestionChunkVectorRecord> retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => _sampleFiles.Any(info => info.FullName == record.DocumentId), top: 1000)
            .ToListAsync();

        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, retrieved[i].Key);
            Assert.NotEmpty(retrieved[i].SerializedContent!);
            Assert.NotNull(retrieved[i].Content);
            Assert.Contains(retrieved[i].DocumentId, _sampleFiles.Select(info => info.FullName));
        }

        AssertActivities(activities, "ProcessFiles");
    }

    [Fact]
    public async Task CanProcessDocumentsInDirectory()
    {
        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks-dir", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);

        using IngestionPipeline pipeline = new(CreateReader(), CreateChunker(), vectorStoreWriter);

        DirectoryInfo directory = new("TestFiles");
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(directory, "*.md").ToListAsync();
        Assert.Equal(directory.EnumerateFiles("*.md").Count(), ingestionResults.Count);
        AssertAllIngestionsSucceeded(ingestionResults);

        Assert.True(embeddingGenerator.WasCalled, "Embedding generator should have been called.");

        List<IngestionChunkVectorRecord> retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => record.DocumentId.StartsWith(directory.FullName), top: 1000)
            .ToListAsync();

        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, retrieved[i].Key);
            Assert.NotEmpty(retrieved[i].SerializedContent!);
            Assert.NotNull(retrieved[i].Content);
            Assert.StartsWith(directory.FullName, retrieved[i].DocumentId);
        }

        AssertActivities(activities, "ProcessDirectory");
    }

    [Fact]
    public async Task ChunksCanBeMoreThanJustText()
    {
        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks-img", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);
        using IngestionPipeline pipeline = new(CreateReader(), new ImageChunker(), vectorStoreWriter);

        Assert.False(embeddingGenerator.WasCalled);
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(_sampleFiles).ToListAsync();
        AssertAllIngestionsSucceeded(ingestionResults);

        List<IngestionChunkVectorRecord> retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => record.DocumentId.EndsWith(_withImage.Name), top: 100)
            .ToListAsync();

        Assert.True(embeddingGenerator.WasCalled);
        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, retrieved[i].Key);
            Assert.EndsWith(_withImage.Name, retrieved[i].DocumentId);
        }

        AssertActivities(activities, "ProcessFiles");
    }

    /// <summary>
    /// Demonstrates a chunker that produces chunks of multiple content types (TextContent and DataContent).
    /// </summary>
    [Fact]
    public async Task ChunkerCanProduceMultipleContentTypes()
    {
        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks-multi", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);

        // Create a document that explicitly has both text and image elements
        ExtractionDocument document = new("multi-content-doc");
        document.Sections.Add(new ExtractionDocumentSection
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("This is textual content for embedding.")),
                new ExtractionDocumentImageElement(new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")),
            }
        });

        MultiContentTypeChunker chunker = new();
        List<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        // Verify the chunker produces both content types
        Assert.Contains(chunks, c => c.Content is TextContent);
        Assert.Contains(chunks, c => c.Content is DataContent);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.Same(document, chunk.Document));
        Assert.Same(document.Sections[0].Elements[0].Content, Assert.Single(chunks, chunk => chunk.Content is TextContent).Content);
        Assert.Same(document.Sections[0].Elements[1].Content, Assert.Single(chunks, chunk => chunk.Content is DataContent).Content);

        // Write to vector store and verify serialized content
        await vectorStoreWriter.WriteAsync(chunks.ToAsyncEnumerable());
        Assert.True(embeddingGenerator.WasCalled);

        List<IngestionChunkVectorRecord> retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => record.DocumentId == "multi-content-doc", top: 100)
            .ToListAsync();

        Assert.NotEmpty(retrieved);

        // Verify we got both text and data content types serialized
        Assert.Contains(retrieved, r => r.SerializedContent?.Contains("\"$type\": \"text\"", StringComparison.Ordinal) == true);
        Assert.Contains(retrieved, r => r.SerializedContent?.Contains("\"$type\": \"data\"", StringComparison.Ordinal) == true);
        Assert.Equal(2, retrieved.Count);
        Assert.All(retrieved, record => Assert.NotEqual(Guid.Empty, record.Key));
        Assert.Equal("This is textual content for embedding.",
            Assert.IsType<TextContent>(Assert.Single(retrieved, record => record.Content is TextContent).Content).Text);
        DataContent storedImage = Assert.IsType<DataContent>(Assert.Single(retrieved, record => record.Content is DataContent).Content);
        Assert.Equal("image/png", storedImage.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, storedImage.Data.ToArray());
    }

    /// <summary>
    /// Demonstrates using IngestionPipeline with an embedding generator for vector store storage.
    /// </summary>
    [Fact]
    public async Task PipelineWorksWithEmbeddingGenerator()
    {
        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks-aicontent", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);

        using IngestionPipeline pipeline = new(CreateReader(), CreateChunker(), vectorStoreWriter);
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(_sampleFiles).ToListAsync();

        AssertAllIngestionsSucceeded(ingestionResults);
        Assert.True(embeddingGenerator.WasCalled, "The embedding generator should have been called.");

        List<IngestionChunkVectorRecord> retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => _sampleFiles.Any(info => info.FullName == record.DocumentId), top: 1000)
            .ToListAsync();

        Assert.NotEmpty(retrieved);
        Assert.All(retrieved, r => Assert.NotEmpty(r.SerializedContent!));
    }

    internal class ImageChunker : IngestionChunker
    {
        public override IAsyncEnumerable<IngestionChunk> ProcessAsync(ExtractionDocument document, CancellationToken cancellationToken = default)
            => document.EnumerateContent()
                    .OfType<ExtractionDocumentImageElement>()
                    .Where(image => image.Content is DataContent)
                    .Select(image => new IngestionChunk(
                        content: image.Content!,
                        document: document,
                        tokenCount: 123)) // made up number as we currently don't have the ability to easily count exact tokens
                    .ToAsyncEnumerable();
    }

    internal sealed class MultiContentTypeChunker : IngestionChunker
    {
        public override IAsyncEnumerable<IngestionChunk> ProcessAsync(ExtractionDocument document, CancellationToken cancellationToken = default)
        {
            List<IngestionChunk> chunks = [];

            foreach (ExtractionDocumentElement element in document.EnumerateContent())
            {
                if (element is ExtractionDocumentImageElement image && image.Content is DataContent)
                {
                    chunks.Add(new IngestionChunk(
                        content: image.Content,
                        document: document,
                        tokenCount: 100));
                }
                else
                {
                    if (element.Content is TextContent { Text.Length: > 0 } text)
                    {
                        chunks.Add(new IngestionChunk(
                            content: text,
                            document: document,
                            tokenCount: 50));
                    }
                }
            }

            return chunks.ToAsyncEnumerable();
        }
    }

    [Fact]
    public async Task SingleFailureDoesNotTearDownEntirePipeline()
    {
        int failed = 0;
        MarkdownReader workingReader = new();
        TestReader failingForFirstReader = new(
            (source, identifier, mediaType, cancellationToken) => failed++ == 0
                    ? Task.FromException<ExtractionDocument>(new ExpectedException())
                    : workingReader.ReadAsync(source, identifier, mediaType, cancellationToken));

        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });

        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection = testVectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
            "chunks-fail", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> vectorStoreWriter = new(collection);

        using IngestionPipeline pipeline = new(failingForFirstReader, CreateChunker(), vectorStoreWriter);

        await Verify(pipeline.ProcessAsync(_sampleFiles));
        await Verify(pipeline.ProcessAsync(_sampleDirectory));

        async Task Verify(IAsyncEnumerable<IngestionResult> results)
        {
            List<IngestionResult> ingestionResults = await results.ToListAsync();

            Assert.Equal(_sampleFiles.Count, ingestionResults.Count);
            Assert.All(ingestionResults, result => Assert.NotEmpty(result.DocumentId));
            IngestionResult ingestionResult = Assert.Single(ingestionResults, result => !result.Succeeded);
            Assert.IsType<ExpectedException>(ingestionResult.Exception);
            AssertErrorActivities(activities, expectedFailedActivitiesCount: 1);

            activities.Clear();
            failed = 0;
        }
    }

    private static IngestionDocumentReader CreateReader() => new MarkdownReader();

    private static IngestionChunker CreateChunker() => new HeaderChunker(new(TiktokenTokenizer.CreateForModel("gpt-4")));

    private static TracerProvider CreateTraceProvider(List<Activity> activities)
        => Sdk.CreateTracerProviderBuilder()
            .AddSource("Experimental.Microsoft.Extensions.DataIngestion")
            .ConfigureResource(r => r.AddService("inmemory-test"))
            .AddInMemoryExporter(activities)
            .Build();

    private static void AssertAllIngestionsSucceeded(List<IngestionResult> ingestionResults)
    {
        Assert.NotEmpty(ingestionResults);
        Assert.All(ingestionResults, result => Assert.True(result.Succeeded));
        Assert.All(ingestionResults, result => Assert.NotEmpty(result.DocumentId));
        Assert.All(ingestionResults, result => Assert.NotNull(result.Document));
        Assert.All(ingestionResults, result => Assert.Null(result.Exception));
    }

    private static void AssertActivities(List<Activity> activities, string rootActivityName)
    {
        Assert.NotEmpty(activities);
        Assert.All(activities, a => Assert.Equal("Experimental.Microsoft.Extensions.DataIngestion", a.Source.Name));
        Assert.Single(activities, a => a.OperationName == rootActivityName);
        Assert.Contains(activities, a => a.OperationName == "ProcessFile");
    }

    private static void AssertErrorActivities(List<Activity> activities, int expectedFailedActivitiesCount)
    {
        Assert.NotEmpty(activities);
        Assert.All(activities, a => Assert.Equal("Experimental.Microsoft.Extensions.DataIngestion", a.Source.Name));

        List<Activity> failed = activities.Where(act => act.Status == ActivityStatusCode.Error).ToList();
        Assert.Equal(expectedFailedActivitiesCount, failed.Count);
        Assert.All(failed, a => Assert.Equal(ExpectedException.ExceptionMessage, a.StatusDescription));
    }
}
