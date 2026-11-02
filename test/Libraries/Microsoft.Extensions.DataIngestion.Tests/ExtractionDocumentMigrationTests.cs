// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class ExtractionDocumentMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderChunker_RepeatsOnlyExplicitColumnHeadersAndAttributesActualRows(bool hasHeader)
    {
        ExtractionDocumentTextElement first = new(new TextContent("alpha"));
        ExtractionDocumentTextElement second = new(new TextContent("beta"));
        ExtractionDocumentTextElement third = new(new TextContent("gamma"));
        ExtractionDocumentTableElement table = new(null, 3, 1);
        table.Cells[0, 0] = new(first, role: hasHeader ? "columnHeader" : null);
        table.Cells[1, 0] = new(second);
        table.Cells[2, 0] = new(third);
        ExtractionDocument document = new("rows") { Sections = { new() { Elements = { table } } } };
        HeaderChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = 3 });

        List<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal("alpha" + Environment.NewLine + "beta", Text(chunks[0]));
        Assert.Equal(hasHeader ? "alpha" + Environment.NewLine + "gamma" : "gamma", Text(chunks[1]));
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 3));
        Assert.Contains(first, chunks[0].SourceElements);
        Assert.Contains(second, chunks[0].SourceElements);
        Assert.DoesNotContain(third, chunks[0].SourceElements);
        Assert.DoesNotContain(second, chunks[1].SourceElements);
        Assert.Equal(hasHeader, chunks[1].SourceElements.Contains(first));
        Assert.Contains(third, chunks[1].SourceElements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuralChunkers_ConsumeDocumentAndNestedSectionPayloadsOnce(bool useSectionChunker)
    {
        ExtractionDocumentSection nested = new(new TextContent("nested")) { Elements = { new ExtractionDocumentTextElement(new TextContent("leaf")) } };
        ExtractionDocumentSection section = new(new TextContent("section")) { Elements = { nested } };
        ExtractionDocument document = new("locals", new TextContent("document")) { Sections = { section } };
        IngestionChunkerOptions options = new(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = 100 };
        IngestionChunker chunker = useSectionChunker ? new SectionChunker(options) : new HeaderChunker(options);

        List<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        Assert.Equal(useSectionChunker ? 4 : 2, chunks.Count);
        Assert.Equal(string.Join(Environment.NewLine, "document", "section", "nested", "leaf"),
            string.Join(Environment.NewLine, chunks.Select(Text)));
        Assert.Same(document, chunks[0].Document);
        Assert.Contains(section, chunks[chunks.Count - 1].SourceElements);
        Assert.Contains(nested, chunks[chunks.Count - 1].SourceElements);
    }

    [Fact]
    public async Task DocumentTokenChunker_AttributesOnlyContributingRanges()
    {
        ExtractionDocumentTextElement first = new(new TextContent(string.Join(" ", Enumerable.Repeat("first", 10))));
        ExtractionDocumentTextElement last = new(new TextContent(string.Join(" ", Enumerable.Repeat("last", 10))));
        ExtractionDocumentSection section = new() { Elements = { first, last } };
        ExtractionDocument document = new("ranges") { Sections = { section } };

        List<IngestionChunk> chunks = await Tokens(5).ProcessAsync(document).ToListAsync();

        Assert.Equal(5, chunks.Count);
        Assert.Equal("first first first first first", Text(chunks[0]));
        Assert.Equal(new ExtractionDocumentElement[] { section, first }, chunks[0].SourceElements);
        Assert.Equal(new ExtractionDocumentElement[] { section, last }, chunks[chunks.Count - 1].SourceElements);
        Assert.DoesNotContain(first, chunks[chunks.Count - 1].SourceElements);
        Assert.Equal(5, chunks[0].TokenCount);
    }

    [Fact]
    public async Task SemanticProjection_RejectsIndirectTableCycles()
    {
        ExtractionDocumentTableElement first = new(null, 1, 1);
        ExtractionDocumentTableElement second = new(null, 1, 1);
        first.Cells[0, 0] = new(second);
        second.Cells[0, 0] = new(first);
        ExtractionDocument document = new("cycle") { Sections = { new() { Elements = { first } } } };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Tokens(100).ProcessAsync(document).ToListAsync());

        Assert.Contains("Cyclic table", error.Message);
        Assert.Same(second, first.Cells[0, 0]!.Content);
        Assert.Same(first, second.Cells[0, 0]!.Content);
    }

    [Fact]
    public async Task DocumentTokenChunker_ConsumesLocalPayloadsOnceWithNestedCellsAndImageSources()
    {
        object nativeResult = new { Value = "PRIVATE_PROVIDER_RESPONSE" };
        object nativeObservation = new { Value = "PRIVATE_OBSERVATION" };
        ExtractionDocumentSourcePage unknown = new("canvas", null);
        ExtractionDocumentSourcePage numbered = new("page-seven", 7,
            dimensions: new(612, 792), coordinateUnit: ExtractionDocumentCoordinateUnit.Point,
            coordinateOrigin: ExtractionDocumentCoordinateOrigin.BottomLeft);
        ExtractionDocumentSourcePage blank = new("blank-frame", 10);
        ExtractionDocumentPoint[] polygon = [new(12, 24), new(60, 24), new(60, 48)];
        ExtractionDocumentSourceReference reference = new(numbered, polygon, rawRepresentation: nativeObservation);
        ExtractionDocumentTextElement cellText = new(new TextContent("cell"));
        cellText.Sources.Add(reference);
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 1, 2 }, "image/png"), "OCR")
        {
            AlternativeText = "description"
        };
        ExtractionDocumentTableElement table = new(null, 2, 2);
        table.Cells[0, 0] = new(cellText);
        table.Cells[0, 0]!.Sources.Add(reference);
        table.Cells[0, 1] = new(image);
        table.Cells[1, 0] = new(new ExtractionDocumentTextElement(new TextContent("last")));
        ExtractionDocumentSection nested = new(new TextContent("nested"))
        {
            Elements = { table }
        };
        ExtractionDocumentSection section = new(new TextContent("section"))
        {
            Elements = { new ExtractionDocumentTextElement(new TextContent("plain **literal**")), nested }
        };
        section.Sources.Add(new(unknown));
        ExtractionDocument document = new("manual", new TextContent("document"))
        {
            Sections = { section },
            SourcePages = { unknown, numbered, blank },
            RawRepresentation = nativeResult
        };
        polygon[0] = new(0, 0);

        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        DocumentTokenChunker chunker = new(new(tokenizer) { MaxTokensPerChunk = 100, OverlapTokens = 0 });
        IngestionChunk chunk = Assert.Single(await chunker.ProcessAsync(document).ToListAsync());

        string expected = string.Join(Environment.NewLine, "document", "section", "plain **literal**", "nested", "cell\tdescription", "last\t");
        Assert.Equal(expected, Text(chunk));
        Assert.Equal(tokenizer.CountTokens(expected, considerNormalization: false), chunk.TokenCount);
        Assert.Same(document, chunk.Document);
        Assert.Equal(new ExtractionDocumentElement[] { section, section.Elements[0], nested, table, cellText, image, table.Cells[1, 0]!.Content! },
            chunk.SourceElements);
        Assert.Same(reference, table.Cells[0, 0]!.Sources[0]);
        Assert.Same(reference, cellText.Sources[0]);
        Assert.Same(numbered, reference.Page);
        Assert.Same(nativeResult, document.RawRepresentation);
        Assert.Same(nativeObservation, reference.GetRawRepresentation());
        Assert.Equal(new ExtractionDocumentPoint[] { new(12, 24), new(60, 24), new(60, 48) }, reference.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(612, 792), numbered.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, numbered.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, numbered.CoordinateOrigin);
        Assert.Null(unknown.PageNumber);
        Assert.Equal(7, numbered.PageNumber);
        Assert.Equal(new[] { unknown, numbered, blank }, document.SourcePages);
        Assert.Equal(10, blank.PageNumber);
        Assert.DoesNotContain(chunk.SourceElements.SelectMany(element => element.Sources), source => ReferenceEquals(source.Page, blank));
        Assert.DoesNotContain("PRIVATE", Text(chunk));
        Assert.DoesNotContain("OCR", Text(chunk));
    }

    [Theory]
    [InlineData(null, "recognized text", "recognized text")]
    [InlineData("", "recognized text", "recognized text")]
    [InlineData("accessible description", "recognized text", "accessible description")]
    public async Task ImageProjection_PrefersAlternativeTextThenOcr(string? alternative, string ocr, string expected)
    {
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 3, 4 }, "image/png"), ocr) { AlternativeText = alternative };
        ExtractionDocumentSection section = new() { Elements = { image } };
        ExtractionDocument document = new("image") { Sections = { section } };
        IngestionChunk chunk = Assert.Single(await Tokens(100).ProcessAsync(document).ToListAsync());
        Assert.Equal(expected, Text(chunk));
        Assert.Equal(new ExtractionDocumentElement[] { section, image }, chunk.SourceElements);
        Assert.Same(image.Content, Assert.IsType<ExtractionDocumentImageElement>(chunk.SourceElements[1]).Content);
    }

    [Fact]
    public async Task TableProjection_PrefersProducerPayloadWithoutDuplicatingCells()
    {
        ExtractionDocumentTextElement cell = new(new TextContent("not semantic when producer payload exists"));
        ExtractionDocumentTableElement table = new(new TextContent("producer's plain table"), 1, 1);
        table.Cells[0, 0] = new(cell);
        ExtractionDocument document = new("table") { Sections = { new() { Elements = { table } } } };

        IngestionChunk chunk = Assert.Single(await Tokens(100).ProcessAsync(document).ToListAsync());

        Assert.Equal("producer's plain table", Text(chunk));
        Assert.Contains(table, chunk.SourceElements);
        Assert.DoesNotContain(cell, chunk.SourceElements);
        Assert.Same(table, document.EnumerateContent().Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderAndSectionChunkers_PreserveHeadingPayloadAndSourceIdentity(bool useSectionChunker)
    {
        ExtractionDocumentTextElement heading = new(new TextContent("A plain heading"), role: "HeAdInG") { HeadingLevel = 2 };
        ExtractionDocumentTextElement footer = new(new TextContent("footer text"), role: "pageFooter");
        ExtractionDocumentTextElement body = new(new TextContent("body"));
        ExtractionDocumentSourcePage page = new("unknown", null);
        heading.Sources.Add(new(page));
        ExtractionDocumentSection section = new() { Elements = { heading, body, footer } };
        ExtractionDocument document = new("heading") { Sections = { section } };
        IngestionChunkerOptions options = new(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = 100 };
        IngestionChunker chunker = useSectionChunker ? new SectionChunker(options) : new HeaderChunker(options);

        IngestionChunk chunk = Assert.Single(await chunker.ProcessAsync(document).ToListAsync());

        Assert.Equal("A plain heading", chunk.Context);
        Assert.Equal(string.Join(Environment.NewLine, "A plain heading", "body", "footer text"), Text(chunk));
        Assert.Equal(new ExtractionDocumentElement[] { section, heading, body, footer }, chunk.SourceElements);
        Assert.Same(page, heading.Sources.Single().Page);
        Assert.Null(page.PageNumber);
        Assert.DoesNotContain("#", Text(chunk));
    }

    [Fact]
    public async Task HeaderChunker_UnspecifiedHeadingLevelAndCustomRolesAreNotFabricated()
    {
        ExtractionDocumentTextElement heading = new(new TextContent("Unknown level"), role: "heading");
        ExtractionDocumentTextElement custom = new(new TextContent("custom body"), role: "customHeading");
        ExtractionDocument document = new("roles") { Sections = { new() { Elements = { heading, custom } } } };
        IngestionChunk chunk = Assert.Single(await new HeaderChunker(
            new(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = 100 }).ProcessAsync(document).ToListAsync());
        Assert.Equal("Unknown level", chunk.Context);
        Assert.Equal("Unknown level" + Environment.NewLine + "custom body", Text(chunk));
        Assert.Contains(heading, chunk.SourceElements);
        Assert.Contains(custom, chunk.SourceElements);
        Assert.Null(heading.HeadingLevel);
    }

    [Fact]
    public async Task HeaderChunker_PreservesSparsePositiveLevelsAndClearsDeeperContext()
    {
        ExtractionDocumentTextElement root = new(new TextContent("Root"), role: "heading") { HeadingLevel = 11 };
        ExtractionDocumentTextElement deepest = new(new TextContent("Deep"), role: "heading") { HeadingLevel = int.MaxValue };
        ExtractionDocumentTextElement sibling = new(new TextContent("Sibling"), role: "heading") { HeadingLevel = 12 };
        ExtractionDocumentTextElement first = new(new TextContent("first"));
        ExtractionDocumentTextElement second = new(new TextContent("second"));
        ExtractionDocument document = new("deep-headings")
        {
            Sections = { new() { Elements = { root, deepest, first, sibling, second } } }
        };
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

        List<IngestionChunk> chunks = await new HeaderChunker(new(tokenizer) { MaxTokensPerChunk = 10 })
            .ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Root Deep", chunks[0].Context);
        Assert.Equal("Root Deep" + Environment.NewLine + "first", Text(chunks[0]));
        Assert.Equal("Root Sibling", chunks[1].Context);
        Assert.Equal("Root Sibling" + Environment.NewLine + "second", Text(chunks[1]));
        Assert.Contains(root, chunks[0].SourceElements);
        Assert.Contains(deepest, chunks[0].SourceElements);
        Assert.DoesNotContain(sibling, chunks[0].SourceElements);
        Assert.Contains(root, chunks[1].SourceElements);
        Assert.Contains(sibling, chunks[1].SourceElements);
        Assert.DoesNotContain(deepest, chunks[1].SourceElements);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.InRange(chunk.TokenCount, 1, 10);
        });
    }

    [Fact]
    public async Task DocumentTokenChunker_UsesMutatedAuthoritativeTextAndOriginalImageSource()
    {
        ExtractionDocumentTextElement text = new(new TextContent("before"), role: "customRole");
        TextContent replacement = new("after **literal**");
        ExtractionDocumentElement element = text;
        element.Content = replacement;
        Assert.Throws<ArgumentNullException>("value", () => element.Content = null);
        foreach (AIContent invalid in new AIContent[]
        {
            new DataContent(new byte[] { 1 }, "image/png"),
            new UriContent("https://example.test/invalid.png", "image/png")
        })
        {
            Assert.Throws<ArgumentException>("value", () => element.Content = invalid);
            Assert.Same(replacement, element.Content);
        }

        ExtractionDocumentImageElement image = new(new UriContent("https://example.test/current.png", "image/png"), "OCR")
        {
            Source = new("https://example.test/original.png"),
            AlternativeText = "description"
        };
        ExtractionDocument document = new("mutable") { Sections = { new() { Elements = { text, image } } } };

        IngestionChunk chunk = Assert.Single(await Tokens(100).ProcessAsync(document).ToListAsync());

        Assert.Same(replacement, text.Content);
        Assert.Equal("customRole", text.Role);
        Assert.Equal("after **literal**" + Environment.NewLine + "![description](https://example.test/original.png)", Text(chunk));
        Assert.DoesNotContain("before", Text(chunk));
        Assert.DoesNotContain("current.png", Text(chunk));
        Assert.DoesNotContain("OCR", Text(chunk));
        Assert.Equal(new Uri("https://example.test/current.png"), Assert.IsType<UriContent>(image.Content).Uri);
        Assert.Equal(new Uri("https://example.test/original.png"), image.Source);
        Assert.Equal("OCR", image.Text);
        Assert.Contains(text, chunk.SourceElements);
        Assert.Contains(image, chunk.SourceElements);
    }

    [Theory]
    [InlineData(0, 2, 5)]
    [InlineData(1, 3, 5)]
    [InlineData(4, 6, 5)]
    public async Task DocumentTokenChunker_ExactOverlapLimitsWithoutOverlapOnlyTrailingChunk(int overlap, int expectedCount, int limit)
    {
        // Ten "word" tokens; the final chunk must not be followed by an overlap-only chunk.
        string input = string.Join(" ", Enumerable.Repeat("word", 10));
        ExtractionDocumentTextElement leaf = new(new TextContent(input));
        ExtractionDocument document = new("overlap") { Sections = { new() { Elements = { leaf } } } };
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        List<IngestionChunk> chunks = await new DocumentTokenChunker(
            new(tokenizer) { MaxTokensPerChunk = limit, OverlapTokens = overlap }).ProcessAsync(document).ToListAsync();

        // Formula pinned to ten input tokens, five-token windows, and strict forward progress.
        Assert.Equal(expectedCount, chunks.Count);
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.TokenCount, 1, limit);
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.Contains(leaf, chunk.SourceElements);
        });
        Assert.Equal(limit, chunks[0].TokenCount);
        string rebuilt = Text(chunks[0]);
        for (int i = 1; i < chunks.Count; i++)
        {
            string prior = Text(chunks[i - 1]);
            int overlapStart = overlap == 0 ? prior.Length :
                tokenizer.GetIndexByTokenCountFromEnd(prior, overlap, out _, out _, considerNormalization: false);
            string suffix = overlap == 0 ? string.Empty : prior.Substring(overlapStart);
            Assert.StartsWith(suffix, Text(chunks[i]));
            rebuilt += Text(chunks[i]).Substring(suffix.Length);
        }

        Assert.Equal(input, rebuilt);
        Assert.True(chunks[chunks.Count - 1].TokenCount > overlap);
    }

    [Fact]
    public async Task DirectPipeline_OrdersProcessorsAndPersistsDurableCellReferences()
    {
        List<string> calls = [];
        object nativeResult = new { Value = "PRIVATE_RAW" };
        ExtractionDocumentSourcePage page = new("frame", 9);
        ExtractionDocumentTextElement cell = new(new TextContent("durable text"));
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new(cell);
        table.Cells[0, 0]!.Sources.Add(new(page));
        ExtractionDocument document = new("consumer-document")
        {
            Sections = { new() { Elements = { table } } },
            SourcePages = { page },
            RawRepresentation = nativeResult
        };
        using TestEmbeddingGenerator<AIContent> embeddings = new();
        using InMemoryVectorStore store = new(new() { EmbeddingGenerator = embeddings });
        VectorStoreCollection<Guid, DurableRecord> collection =
            store.GetIngestionRecordCollection<DurableRecord>("durable", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using DurableWriter writer = new(collection, calls);
        using IngestionPipeline pipeline = new(Tokens(100), writer);
        pipeline.DocumentProcessors.Add(new LocalDocumentProcessor(calls));
        pipeline.ChunkProcessors.Add(new LocalChunkProcessor(calls));

        ExtractionDocument result = await pipeline.ProcessAsync(document);
        DurableRecord record = Assert.Single(await collection.GetAsync(r => r.DocumentId == document.Identifier, top: 10).ToListAsync());

        Assert.Same(document, result);
        Assert.Equal(new[] { "document", "chunk", "record" }, calls);
        Assert.Equal("processor-local" + Environment.NewLine + "durable text", Assert.IsType<TextContent>(record.Content).Text);
        Assert.Equal("consumer-document/frame/0/0", record.DurableSource);
        Assert.Equal("processed", record.Classification);
        Assert.NotEqual(Guid.Empty, record.Key);
        Assert.True(embeddings.WasCalled);
        Assert.DoesNotContain("PRIVATE_RAW", record.SerializedContent!);
        Assert.Same(nativeResult, document.RawRepresentation);
        Assert.Same(page, table.Cells[0, 0]!.Sources.Single().Page);

        Guid previousKey = record.Key;
        calls.Clear();
        await pipeline.ProcessAsync(document);
        DurableRecord replacement = Assert.Single(await collection.GetAsync(r => r.DocumentId == document.Identifier, top: 10).ToListAsync());
        Assert.NotEqual(previousKey, replacement.Key);
        Assert.Equal(record.DurableSource, replacement.DurableSource);
        Assert.Equal(new[] { "document", "chunk", "record" }, calls);
    }

    [Fact]
    public async Task ReaderlessPipeline_RejectsFilesAndPreCanceledDocumentsBeforeSideEffects()
    {
        List<string> calls = [];
        using TestEmbeddingGenerator<AIContent> embeddings = new();
        using InMemoryVectorStore store = new(new() { EmbeddingGenerator = embeddings });
        using DurableWriter writer = new(
            store.GetIngestionRecordCollection<DurableRecord>("guard", TestEmbeddingGenerator<AIContent>.DimensionCount), calls);
        using IngestionPipeline pipeline = new(Tokens(100), writer);
        pipeline.DocumentProcessors.Add(new LocalDocumentProcessor(calls));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ProcessAsync(new ExtractionDocument("cancel"), cancellation.Token));
        await Assert.ThrowsAsync<ArgumentNullException>("document", () => pipeline.ProcessAsync((ExtractionDocument)null!));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ProcessAsync(new[] { new FileInfo("never-opened.md") }).ToListAsync());
        Assert.Contains("reader", error.Message);
        Assert.Empty(calls);
        Assert.False(embeddings.WasCalled);
    }

    private static DocumentTokenChunker Tokens(int limit) => new(
        new(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = limit, OverlapTokens = 0 });

    private static string Text(IngestionChunk chunk) => Assert.IsType<TextContent>(chunk.Content).Text!;

    public class DurableRecord : IngestionChunkVectorRecord
    {
        [VectorStoreVector(TestEmbeddingGenerator<AIContent>.DimensionCount)]
        public override AIContent? Embedding => Content;

        [VectorStoreData]
        public string? DurableSource { get; set; }

        [VectorStoreData]
        public string? Classification { get; set; }
    }

    private sealed class DurableWriter(VectorStoreCollection<Guid, DurableRecord> collection, List<string> calls)
        : VectorStoreWriter<DurableRecord>(collection)
    {
        protected override DurableRecord CreateRecord(IngestionChunk chunk)
        {
            calls.Add("record");
            DurableRecord record = base.CreateRecord(chunk);
            record.Key = Guid.NewGuid();
            ExtractionDocumentTableElement table = Assert.Single(chunk.SourceElements.OfType<ExtractionDocumentTableElement>());
            ExtractionDocumentTableCell cell = table.Cells[0, 0]!;
            ExtractionDocumentSourcePage page = Assert.Single(cell.Sources).Page;
            record.DurableSource = $"{chunk.Document.Identifier}/{page.Identifier}/{cell.RowIndex}/{cell.ColumnIndex}";
            return record;
        }

        protected override void SetMetadata(DurableRecord record, string key, object? value)
        {
            Assert.Equal("classification", key);
            record.Classification = Assert.IsType<string>(value);
        }
    }

    private sealed class LocalDocumentProcessor(List<string> calls) : IngestionDocumentProcessor
    {
        public override Task<ExtractionDocument> ProcessAsync(ExtractionDocument document, CancellationToken cancellationToken = default)
        {
            calls.Add("document");
            document.Content = new TextContent("processor-local");
            return Task.FromResult(document);
        }
    }

    private sealed class LocalChunkProcessor(List<string> calls) : IngestionChunkProcessor
    {
        public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(IAsyncEnumerable<IngestionChunk> chunks,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
            {
                calls.Add("chunk");
                Assert.StartsWith("processor-local", Text(chunk));
                chunk.Metadata.Add("classification", "processed");
                yield return chunk;
            }
        }
    }
}
