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

        TestEmbeddingGenerator<string> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });
        using VectorStoreWriter<string> vectorStoreWriter = new(testVectorStore, dimensionCount: TestEmbeddingGenerator<string>.DimensionCount);

        using IngestionPipeline<string> pipeline = new(CreateReader(), CreateChunker(), vectorStoreWriter);
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(_sampleFiles).ToListAsync();

        Assert.Equal(_sampleFiles.Count, ingestionResults.Count);
        AssertAllIngestionsSucceeded(ingestionResults);

        Assert.True(embeddingGenerator.WasCalled, "Embedding generator should have been called.");

        var retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => _sampleFiles.Any(info => info.FullName == (string)record["documentid"]!), top: 1000)
            .ToListAsync();

        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, (Guid)retrieved[i]["key"]!);
            Assert.NotEmpty((string)retrieved[i]["content"]!);
            Assert.Contains((string)retrieved[i]["documentid"]!, _sampleFiles.Select(info => info.FullName));
        }

        AssertActivities(activities, "ProcessFiles");
    }

    [Fact]
    public async Task CanProcessDocumentsInDirectory()
    {
        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<string> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });
        using VectorStoreWriter<string> vectorStoreWriter = new(testVectorStore, dimensionCount: TestEmbeddingGenerator<string>.DimensionCount);

        using IngestionPipeline<string> pipeline = new(CreateReader(), CreateChunker(), vectorStoreWriter);

        DirectoryInfo directory = new("TestFiles");
        List<IngestionResult> ingestionResults = await pipeline.ProcessAsync(directory, "*.md").ToListAsync();
        Assert.Equal(directory.EnumerateFiles("*.md").Count(), ingestionResults.Count);
        AssertAllIngestionsSucceeded(ingestionResults);

        Assert.True(embeddingGenerator.WasCalled, "Embedding generator should have been called.");

        var retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => ((string)record["documentid"]!).StartsWith(directory.FullName), top: 1000)
            .ToListAsync();

        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, (Guid)retrieved[i]["key"]!);
            Assert.NotEmpty((string)retrieved[i]["content"]!);
            Assert.StartsWith(directory.FullName, (string)retrieved[i]["documentid"]!);
        }

        AssertActivities(activities, "ProcessDirectory");
    }

    [Fact]
    public async Task ChunksCanBeMoreThanJustText()
    {
        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<DataContent> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });
        using VectorStoreWriter<DataContent> vectorStoreWriter = new(testVectorStore, dimensionCount: TestEmbeddingGenerator<DataContent>.DimensionCount);
        using IngestionPipeline<DataContent> pipeline = new(CreateReader(), new ImageChunker(), vectorStoreWriter);

        Assert.False(embeddingGenerator.WasCalled);
        var ingestionResults = await pipeline.ProcessAsync(_sampleFiles).ToListAsync();
        AssertAllIngestionsSucceeded(ingestionResults);

        var retrieved = await vectorStoreWriter.VectorStoreCollection
            .GetAsync(record => ((string)record["documentid"]!).EndsWith(_withImage.Name), top: 100)
            .ToListAsync();

        Assert.True(embeddingGenerator.WasCalled);
        Assert.NotEmpty(retrieved);
        for (int i = 0; i < retrieved.Count; i++)
        {
            Assert.NotEqual(Guid.Empty, (Guid)retrieved[i]["key"]!);
            Assert.EndsWith(_withImage.Name, (string)retrieved[i]["documentid"]!);
        }

        AssertActivities(activities, "ProcessFiles");
    }

    internal class ImageChunker : IngestionChunker<DataContent>
    {
        public override IAsyncEnumerable<IngestionChunk<DataContent>> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
            => document.EnumerateContent()
                    .OfType<IngestionDocumentImage>()
                    .Select(image => new IngestionChunk<DataContent>(
                        content: new(image.Content.GetValueOrDefault(), image.MediaType!),
                        document: document))
                    .ToAsyncEnumerable();
    }

    [Fact]
    public async Task SingleFailureDoesNotTearDownEntirePipeline()
    {
        int failed = 0;
        MarkdownReader workingReader = new();
        TestReader failingForFirstReader = new(
            (source, identifier, mediaType, cancellationToken) => failed++ == 0
                    ? Task.FromException<IngestionDocument>(new ExpectedException())
                    : workingReader.ReadAsync(source, identifier, mediaType, cancellationToken));

        List<Activity> activities = [];
        using TracerProvider tracerProvider = CreateTraceProvider(activities);

        TestEmbeddingGenerator<string> embeddingGenerator = new();
        using InMemoryVectorStore testVectorStore = new(new() { EmbeddingGenerator = embeddingGenerator });
        using VectorStoreWriter<string> vectorStoreWriter = new(testVectorStore, dimensionCount: TestEmbeddingGenerator<string>.DimensionCount);

        using IngestionPipeline<string> pipeline = new(failingForFirstReader, CreateChunker(), vectorStoreWriter);

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

    private static IngestionChunker<string> CreateChunker() => new HeaderChunker(new(TiktokenTokenizer.CreateForModel("gpt-4")));

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

        var failed = activities.Where(act => act.Status == ActivityStatusCode.Error).ToList();
        Assert.Equal(expectedFailedActivitiesCount, failed.Count);
        Assert.All(failed, a => Assert.Equal(ExpectedException.ExceptionMessage, a.StatusDescription));
    }
}
