// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    [InlineData("DocumentToken")]
    [InlineData("Section")]
    [InlineData("Header")]
    [InlineData("SemanticSimilarity")]
    public async Task AllBuiltInChunkers_ConsumeExactEnvelopeGraphOnce(string kind)
    {
        ExtractionDocumentTextElement cell = new(new TextContent("cell"));
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 1, 2 }, "image/png"), "unused OCR")
        {
            AlternativeText = "description"
        };
        ExtractionDocumentTableElement table = new(null, 1, 2);
        table.Cells[0, 0] = new(cell, role: "columnHeader");
        table.Cells[0, 1] = new(image, role: "columnHeader");
        ExtractionDocumentSection nested = new(new TextContent("nested")) { Elements = { table } };
        ExtractionDocumentSection section = new(new TextContent("section")) { Elements = { nested } };
        ExtractionDocument graph = new("provider", new TextContent("document")) { Sections = { section } };
        IngestionDocument document = new("persistence", graph);
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        IngestionChunkerOptions options = new(tokenizer) { MaxTokensPerChunk = 100, OverlapTokens = 0 };
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = new()
        {
            GenerateAsyncCallback = (values, _, _) =>
            {
                TextContent[] inputs = values.ToArray();
                embedded.AddRange(inputs.Select(value => value.Text));
                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(new float[] { 1, 0, 0 }))));
            }
        };
        IngestionChunker chunker = kind switch
        {
            "DocumentToken" => new DocumentTokenChunker(options),
            "Section" => new SectionChunker(options),
            "Header" => new HeaderChunker(options),
            "SemanticSimilarity" => new SemanticSimilarityChunker(generator, options),
            _ => throw new InvalidOperationException(kind)
        };

        List<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        Assert.Equal(string.Join(Environment.NewLine, "document", "section", "nested", "cell\tdescription"),
            string.Join(Environment.NewLine, chunks.Select(Text)));
        Assert.All(chunks, chunk =>
        {
            Assert.Same(document, chunk.Document);
            Assert.Same(graph, chunk.Document.ExtractedDocument);
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.InRange(chunk.TokenCount, 1, 100);
            Assert.Equal(chunk.SourceElements.Distinct().Count(), chunk.SourceElements.Count);
        });
        ExtractionDocumentElement[] sources = chunks.SelectMany(chunk => chunk.SourceElements).Distinct().ToArray();
        Assert.Equal(new ExtractionDocumentElement[] { section, nested, table, cell, image }, sources);
        Assert.Same(cell, table.Cells[0, 0]!.Content);
        Assert.Same(image, table.Cells[0, 1]!.Content);
        Assert.Equal("provider", graph.Identifier);
        Assert.Equal("persistence", document.Identifier);
        if (kind == "SemanticSimilarity")
        {
            Assert.Equal(new[] { "section", "nested", "cell\tdescription" }, embedded);
        }
        else
        {
            Assert.Empty(embedded);
        }

        ExtractionDocumentElement[][] snapshots = chunks.Select(chunk => chunk.SourceElements.ToArray()).ToArray();
        graph.Sections.Clear();
        section.Elements.Clear();
        nested.Elements.Clear();
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(snapshots[i], chunks[i].SourceElements);
        }

        Assert.Equal(new ExtractionDocumentElement[] { section, nested, table, cell, image },
            chunks.SelectMany(chunk => chunk.SourceElements).Distinct());
    }

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
        IngestionDocument document = new("rows") { Sections = { new() { Elements = { table } } } };
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
        IngestionDocument document = new("locals", new ExtractionDocument("graph", new TextContent("document"))) { Sections = { section } };
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
        IngestionDocument document = new("ranges") { Sections = { section } };

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
        IngestionDocument document = new("cycle") { Sections = { new() { Elements = { first } } } };

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
        ExtractionDocument graph = new("provider", new TextContent("document"))
        {
            Sections = { section },
            SourcePages = { unknown, numbered, blank },
            RawRepresentation = nativeResult
        };
        IngestionDocument document = new("manual", graph);
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
        Assert.Same(nativeResult, document.ExtractedDocument.RawRepresentation);
        Assert.Same(nativeObservation, reference.GetRawRepresentation());
        Assert.Equal(new ExtractionDocumentPoint[] { new(12, 24), new(60, 24), new(60, 48) }, reference.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(612, 792), numbered.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, numbered.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, numbered.CoordinateOrigin);
        Assert.Null(unknown.PageNumber);
        Assert.Equal(7, numbered.PageNumber);
        Assert.Equal(new[] { unknown, numbered, blank }, document.ExtractedDocument.SourcePages);
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
        IngestionDocument document = new("image") { Sections = { section } };
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
        IngestionDocument document = new("table") { Sections = { new() { Elements = { table } } } };

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
        IngestionDocument document = new("heading") { Sections = { section } };
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
        IngestionDocument document = new("roles") { Sections = { new() { Elements = { heading, custom } } } };
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
        IngestionDocument document = new("deep-headings")
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
        IngestionDocument document = new("mutable") { Sections = { new() { Elements = { text, image } } } };

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
        IngestionDocument document = new("overlap") { Sections = { new() { Elements = { leaf } } } };
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
        ExtractionDocument graph = new("provider-document")
        {
            Sections = { new() { Elements = { table } } },
            SourcePages = { page },
            RawRepresentation = nativeResult
        };
        IngestionDocument document = new("consumer-document", graph);
        using TestEmbeddingGenerator<AIContent> embeddings = new();
        using InMemoryVectorStore store = new(new() { EmbeddingGenerator = embeddings });
        VectorStoreCollection<Guid, DurableRecord> collection =
            store.GetIngestionRecordCollection<DurableRecord>("durable", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using DurableWriter writer = new(collection, calls);
        using IngestionPipeline pipeline = new(Tokens(100), writer);
        pipeline.DocumentProcessors.Add(new LocalDocumentProcessor(calls));
        pipeline.ChunkProcessors.Add(new LocalChunkProcessor(calls));

        IngestionDocument result = await pipeline.ProcessAsync(document);
        DurableRecord record = Assert.Single(await collection.GetAsync(r => r.DocumentId == document.Identifier, top: 10).ToListAsync());

        Assert.Same(document, result);
        Assert.Same(graph, result.ExtractedDocument);
        Assert.Equal("provider-document", graph.Identifier);
        Assert.Equal("consumer-document", record.DocumentId);
        Assert.Empty(await collection.GetAsync(r => r.DocumentId == graph.Identifier, top: 10).ToListAsync());
        Assert.Equal(new[] { "document", "chunk", "record" }, calls);
        Assert.Equal("processor-local" + Environment.NewLine + "durable text", Assert.IsType<TextContent>(record.Content).Text);
        Assert.Equal("consumer-document/frame/0/0", record.DurableSource);
        Assert.Equal("processed", record.Classification);
        Assert.NotEqual(Guid.Empty, record.Key);
        Assert.True(embeddings.WasCalled);
        Assert.DoesNotContain("PRIVATE_RAW", record.SerializedContent!);
        Assert.Same(nativeResult, document.ExtractedDocument.RawRepresentation);
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
        await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ProcessAsync(new IngestionDocument("cancel"), cancellation.Token));
        await Assert.ThrowsAsync<ArgumentNullException>("document", () => pipeline.ProcessAsync((IngestionDocument)null!));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ProcessAsync(new[] { new FileInfo("never-opened.md") }).ToListAsync());
        Assert.Contains("reader", error.Message);
        Assert.Empty(calls);
        Assert.False(embeddings.WasCalled);
    }

    [Theory]
    [InlineData("DocumentToken")]
    [InlineData("Section")]
    [InlineData("Header")]
    [InlineData("SemanticSimilarity")]
    public async Task MixedCells_AllChunkers_ConsumeOrderedCellSectionAndPreserveEnvelope(string kind)
    {
        object native = new();
        object observation = new();
        ExtractionDocumentSourcePage unknown = new("canvas", null);
        ExtractionDocumentSourcePage numbered = new("page-seven", 7, dimensions: new(612, 792),
            coordinateUnit: ExtractionDocumentCoordinateUnit.Point, coordinateOrigin: ExtractionDocumentCoordinateOrigin.BottomLeft);
        ExtractionDocumentSourcePage blank = new("blank", 8);
        ExtractionDocumentSourcePage cellOnly = new("cell-only", 9);
        ExtractionDocumentPoint[] polygon = [new(12, 24), new(60, 24), new(60, 48)];
        ExtractionDocumentSourceReference source = new(numbered, polygon, rawRepresentation: observation);
        ExtractionDocumentTextElement paragraphA = new(new TextContent("paragraphA"), role: "heading") { HeadingLevel = 1 };
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 1, 2, 3 }, "image/png"), "unused OCR");
        image.Sources.Add(source);
        ExtractionDocumentTextElement paragraphB = new(new TextContent("paragraphB"));
        ExtractionDocumentSection cellSection = new(new TextContent("cell-local"))
        {
            Elements = { paragraphA, image, paragraphB }
        };
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new(cellSection);
        ExtractionDocumentSourceReference cellReference = new(cellOnly);
        table.Cells[0, 0]!.Sources.Add(cellReference);
        ExtractionDocumentTextElement after = new(new TextContent("after"));
        ExtractionDocumentSection nested = new(new TextContent("nested")) { Elements = { table, after } };
        ExtractionDocumentSection section = new(new TextContent("section")) { Elements = { nested } };
        section.Sources.Add(new(unknown));
        TextContent documentLocal = new("document");
        ExtractionDocument graph = new("provider", documentLocal)
        {
            Sections = { section },
            SourcePages = { unknown, numbered, blank, cellOnly },
            RawRepresentation = native
        };
        graph.Metadata.Add("native", native);
        IngestionDocument document = new("persistence", graph);
        polygon[0] = new(0, 0);
        int chatCalls = 0;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, _, _) =>
            {
                chatCalls++;
                Assert.Same(image.Content, Assert.Single(messages.Last().Contents));
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    JsonSerializer.Serialize(new Envelope<string[]> { data = new[] { "description" } }))));
            }
        };
        ImageAlternativeTextEnricher enricher = new(new(client));
        Assert.Same(document, await enricher.ProcessAsync(document));
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        IngestionChunker chunker = MixedCellsChunker(kind, tokenizer, 100, generator);

        List<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        string cellText = string.Join(Environment.NewLine, "cell-local", "paragraphA", "description", "paragraphB");
        Assert.Equal(string.Join(Environment.NewLine, "document", "section", "nested", cellText, "after"),
            string.Join(Environment.NewLine, chunks.Select(Text)));
        Assert.Equal(1, chatCalls);
        Assert.Equal("description", image.AlternativeText);
        Assert.Equal("unused OCR", image.Text);
        Assert.Equal("cell-local", Assert.IsType<TextContent>(cellSection.Content).Text);
        Assert.Equal(new ExtractionDocumentElement[] { table, after }, document.EnumerateContent());
        Assert.Equal(new ExtractionDocumentElement[] { table, after }, graph.EnumerateContent());
        ExtractionDocumentElement[] expectedSources = [section, nested, table, cellSection, paragraphA, image, paragraphB, after];
        Assert.Equal(expectedSources, chunks.SelectMany(chunk => chunk.SourceElements).Distinct());
        Assert.All(chunks, chunk =>
        {
            Assert.Same(document, chunk.Document);
            Assert.Same(graph, chunk.Document.ExtractedDocument);
            Assert.Equal(string.Empty, chunk.Context);
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.InRange(chunk.TokenCount, 1, 100);
            Assert.Equal(chunk.SourceElements.Distinct().Count(), chunk.SourceElements.Count);
            Assert.DoesNotContain("unused OCR", Text(chunk));
        });
        Assert.Equal(kind == "SemanticSimilarity" ? new[] { "section", "nested", cellText, "after" } : Array.Empty<string>(), embedded);
        Assert.Same(section, Assert.Single(graph.Sections));
        Assert.Same(nested, Assert.Single(section.Elements));
        Assert.Same(cellSection, table.Cells[0, 0]!.Content);
        Assert.Equal(new ExtractionDocumentElement[] { paragraphA, image, paragraphB }, cellSection.Elements);
        Assert.Same(documentLocal, graph.Content);
        Assert.Same(native, graph.RawRepresentation);
        Assert.Same(native, graph.Metadata["native"]);
        Assert.Equal("provider", graph.Identifier);
        Assert.Equal("persistence", document.Identifier);
        Assert.Equal(new[] { unknown, numbered, blank, cellOnly }, graph.SourcePages);
        Assert.Null(unknown.PageNumber);
        Assert.Equal(7, numbered.PageNumber);
        Assert.Equal(8, blank.PageNumber);
        Assert.Equal(new ExtractionDocumentPageDimensions(612, 792), numbered.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, numbered.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, numbered.CoordinateOrigin);
        Assert.Same(source, Assert.Single(image.Sources));
        Assert.Same(numbered, source.Page);
        Assert.Same(observation, source.GetRawRepresentation());
        Assert.Equal(new ExtractionDocumentPoint[] { new(12, 24), new(60, 24), new(60, 48) }, source.Polygon);
        Assert.Same(cellReference, Assert.Single(table.Cells[0, 0]!.Sources));

        // Cells are not ExtractionDocumentElements. Their own Sources have no independent carrier.
        Assert.DoesNotContain(chunks.SelectMany(chunk => chunk.SourceElements).SelectMany(element => element.Sources),
            reference => ReferenceEquals(reference, cellReference) || ReferenceEquals(reference.Page, blank));
        Assert.Contains(source, chunks.SelectMany(chunk => chunk.SourceElements).SelectMany(element => element.Sources));

        ExtractionDocumentElement[][] snapshots = chunks.Select(chunk => chunk.SourceElements.ToArray()).ToArray();
        cellSection.Elements.Clear();
        nested.Elements.Clear();
        graph.Sections.Clear();
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(snapshots[i], chunks[i].SourceElements);
        }

        Assert.Equal(expectedSources, chunks.SelectMany(chunk => chunk.SourceElements).Distinct());
    }

    [Theory]
    [InlineData("Section", false)]
    [InlineData("Section", true)]
    [InlineData("Header", false)]
    [InlineData("Header", true)]
    [InlineData("SemanticSimilarity", false)]
    [InlineData("SemanticSimilarity", true)]
    public async Task MixedCells_StructuralChunkers_SplitRowsWithExactBudgetsAndExplicitHeaders(string kind, bool hasHeader)
    {
        ExtractionDocumentSection[] cells =
        [
            new(new TextContent("alpha")) { Elements = { new ExtractionDocumentTextElement(new TextContent("head"), role: "heading") } },
            new(new TextContent("beta")) { Elements = { new ExtractionDocumentTextElement(new TextContent("body")) } },
            new(new TextContent("gamma")) { Elements = { new ExtractionDocumentTextElement(new TextContent("tail")) } }
        ];
        ExtractionDocumentTextElement[] neighbors =
        [
            new(new TextContent("x")), new(new TextContent("y")), new(new TextContent("z"))
        ];
        ExtractionDocumentTableElement table = new(null, 3, 2);
        for (int row = 0; row < 3; row++)
        {
            cells[row].Elements.Add(new ExtractionDocumentImageElement(new DataContent(new byte[] { (byte)row }, "image/png"))
            {
                AlternativeText = "image"
            });
            cells[row].Elements.Add(new ExtractionDocumentTextElement(new TextContent("end")));
            string? role = hasHeader && row == 0 ? "CoLuMnHeAdEr" : null;
            table.Cells[row, 0] = new(cells[row], role: role);
            table.Cells[row, 1] = new(neighbors[row], role: role);
        }

        ExtractionDocumentSection section = new() { Elements = { table } };
        IngestionDocument document = new("split-mixed") { Sections = { section } };
        string[] rows =
        [
            string.Join(Environment.NewLine, "alpha", "head", "image", "end") + "\tx",
            string.Join(Environment.NewLine, "beta", "body", "image", "end") + "\ty",
            string.Join(Environment.NewLine, "gamma", "tail", "image", "end") + "\tz"
        ];
        string first = rows[0] + Environment.NewLine + rows[1];
        string second = hasHeader ? rows[0] + Environment.NewLine + rows[2] : rows[2];
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        int limit = tokenizer.CountTokens(first, considerNormalization: false);
        Assert.True(tokenizer.CountTokens(string.Join(Environment.NewLine, rows), considerNormalization: false) > limit);
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);

        List<IngestionChunk> chunks = await MixedCellsChunker(kind, tokenizer, limit, generator).ProcessAsync(document).ToListAsync();

        Assert.Equal(new[] { first, second }, chunks.Select(Text));
        Assert.Equal(limit, chunks[0].TokenCount);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(string.Empty, chunk.Context);
            Assert.InRange(chunk.TokenCount, 1, limit);
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.Same(document, chunk.Document);
        });
        ExtractionDocumentElement[] RowSources(int row) => [cells[row], .. cells[row].Elements, neighbors[row]];
        Assert.Equal(new ExtractionDocumentElement[] { section, table }.Concat(RowSources(0)).Concat(RowSources(1)), chunks[0].SourceElements);
        Assert.Contains(section, chunks[1].SourceElements);
        Assert.Contains(table, chunks[1].SourceElements);
        Assert.Equal((hasHeader ? RowSources(0) : Array.Empty<ExtractionDocumentElement>()).Concat(RowSources(2)),
            chunks[1].SourceElements.Where(source => !ReferenceEquals(source, section) && !ReferenceEquals(source, table)));
        Assert.Equal(hasHeader ? 12 : 7, chunks[1].SourceElements.Count);
        Assert.DoesNotContain(cells[2], chunks[0].SourceElements);
        Assert.DoesNotContain(cells[1].Elements[0], chunks[1].SourceElements);
        Assert.Equal(hasHeader, chunks[1].SourceElements.Contains(cells[0].Elements[0]));
        Assert.Equal(kind == "SemanticSimilarity" ? new[] { string.Join(Environment.NewLine, rows) } : Array.Empty<string>(), embedded);
    }

    [Theory]
    [InlineData("DocumentToken")]
    [InlineData("Section")]
    [InlineData("Header")]
    [InlineData("SemanticSimilarity")]
    public async Task MixedCells_AllChunkers_ProducerTableTextIsAuthoritative(string kind)
    {
        ExtractionDocumentSection cell = new(new TextContent("hidden local"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("hidden paragraphA")),
                new ExtractionDocumentImageElement(new DataContent(new byte[] { 9 }, "image/png"), "hidden OCR"),
                new ExtractionDocumentTextElement(new TextContent("hidden paragraphB"))
            }
        };

        // Malformed hidden descendants must not be interpreted when a table has authoritative text.
        cell.Elements.Add(cell);
        ExtractionDocumentTableElement table = new(new TextContent("producer table"), 1, 1);
        table.Cells[0, 0] = new(cell);
        ExtractionDocumentSection section = new() { Elements = { table } };
        IngestionDocument document = new("authoritative") { Sections = { section } };
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);

        IngestionChunk chunk = Assert.Single(await MixedCellsChunker(kind, TiktokenTokenizer.CreateForModel("gpt-4o"), 100, generator)
            .ProcessAsync(document).ToListAsync());

        Assert.Equal("producer table", Text(chunk));
        Assert.Equal(new ExtractionDocumentElement[] { section, table }, chunk.SourceElements);
        Assert.Same(cell, table.Cells[0, 0]!.Content);
        Assert.Same(cell, cell.Elements[cell.Elements.Count - 1]);
        Assert.Equal(kind == "SemanticSimilarity" ? new[] { "producer table" } : Array.Empty<string>(), embedded);
    }

    [Fact]
    public async Task MixedCells_Enricher_DeduplicatesImageIdentityAcrossSharedSections()
    {
        DataContent payload = new(new byte[] { 4, 5, 6 }, "image/png");
        ExtractionDocumentImageElement first = new(payload, "first OCR");
        ExtractionDocumentImageElement second = new(payload, "second OCR");
        ExtractionDocumentSection shared = new(new TextContent("local"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("paragraphA")), first, second,
                new ExtractionDocumentTextElement(new TextContent("paragraphB"))
            }
        };
        ExtractionDocumentTableElement table = new(new TextContent("producer table"), 1, 2);
        table.Cells[0, 0] = new(shared);
        table.Cells[0, 1] = new(shared);
        IngestionDocument document = new("image-dag") { Sections = { new() { Elements = { table, shared, first } } } };
        int calls = 0;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, _, _) =>
            {
                calls++;
                AIContent[] inputs = messages.Last().Contents.ToArray();
                Assert.Equal(2, inputs.Length);
                Assert.All(inputs, input => Assert.Same(payload, input));
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    JsonSerializer.Serialize(new Envelope<string[]> { data = new[] { "first description", "second description" } }))));
            }
        };
        ImageAlternativeTextEnricher enricher = new(new(client) { BatchSize = 10 });

        Assert.Same(document, await enricher.ProcessAsync(document));
        Assert.Same(document, await enricher.ProcessAsync(document));

        Assert.Equal(1, calls);
        Assert.Equal("first description", first.AlternativeText);
        Assert.Equal("second description", second.AlternativeText);
        Assert.Equal("first OCR", first.Text);
        Assert.Equal("second OCR", second.Text);
        Assert.Same(first, shared.Elements[1]);
        Assert.Same(second, shared.Elements[2]);
        Assert.Same(shared, table.Cells[0, 0]!.Content);
        Assert.Same(shared, table.Cells[0, 1]!.Content);
        Assert.Equal("producer table", Assert.IsType<TextContent>(table.Content).Text);
    }

    [Theory]
    [InlineData("DocumentToken")]
    [InlineData("Section")]
    [InlineData("Header")]
    [InlineData("SemanticSimilarity")]
    public async Task MixedCells_AllChunkers_AllowSharedCellSectionDag(string kind)
    {
        ExtractionDocumentTextElement paragraphA = new(new TextContent("paragraphA"));
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 7 }, "image/png")) { AlternativeText = "description" };
        ExtractionDocumentTextElement paragraphB = new(new TextContent("paragraphB"));
        ExtractionDocumentSection shared = new(new TextContent("local")) { Elements = { paragraphA, image, paragraphB } };
        ExtractionDocumentTableElement nestedTable = new(null, 1, 1);
        nestedTable.Cells[0, 0] = new(shared);
        ExtractionDocumentTableElement table = new(null, 1, 2);
        table.Cells[0, 0] = new(shared);
        table.Cells[0, 1] = new(nestedTable);
        ExtractionDocumentSection section = new() { Elements = { table } };
        IngestionDocument document = new("dag") { Sections = { section } };
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);

        IngestionChunk chunk = Assert.Single(await MixedCellsChunker(kind, TiktokenTokenizer.CreateForModel("gpt-4o"), 100, generator)
            .ProcessAsync(document).ToListAsync());

        string text = string.Join(Environment.NewLine, "local", "paragraphA", "description", "paragraphB");
        Assert.Equal(text + "\t" + text, Text(chunk));
        Assert.Equal(new ExtractionDocumentElement[] { section, table, shared, paragraphA, image, paragraphB, nestedTable }, chunk.SourceElements);
        Assert.Same(shared, table.Cells[0, 0]!.Content);
        Assert.Same(shared, nestedTable.Cells[0, 0]!.Content);
        Assert.Equal(kind == "SemanticSimilarity" ? new[] { text + "\t" + text } : Array.Empty<string>(), embedded);
    }

    [Theory]
    [InlineData("DocumentToken", "Section")]
    [InlineData("DocumentToken", "Table")]
    [InlineData("DocumentToken", "Mixed")]
    [InlineData("DocumentToken", "Null")]
    [InlineData("Section", "Section")]
    [InlineData("Section", "Table")]
    [InlineData("Section", "Mixed")]
    [InlineData("Section", "Null")]
    [InlineData("Header", "Section")]
    [InlineData("Header", "Table")]
    [InlineData("Header", "Mixed")]
    [InlineData("Header", "Null")]
    [InlineData("SemanticSimilarity", "Section")]
    [InlineData("SemanticSimilarity", "Table")]
    [InlineData("SemanticSimilarity", "Mixed")]
    [InlineData("SemanticSimilarity", "Null")]
    public async Task MixedCells_AllChunkers_RejectActivePathCyclesAndNullMutation(string kind, string mutation)
    {
        IngestionDocument document = MixedCellsMalformedGraph(mutation);
        ExtractionDocumentSection root = Assert.Single(document.Sections);
        ExtractionDocumentElement[] original = root.Elements.ToArray();
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MixedCellsChunker(kind, TiktokenTokenizer.CreateForModel("gpt-4o"), 100, generator).ProcessAsync(document).ToListAsync());

        Assert.Empty(embedded);
        Assert.Same(root, Assert.Single(document.Sections));
        Assert.Equal(original, root.Elements);
    }

    [Theory]
    [InlineData("Section")]
    [InlineData("Table")]
    [InlineData("Mixed")]
    [InlineData("Null")]
    public async Task MixedCells_Enricher_RejectsActivePathCyclesAndNullMutation(string mutation)
    {
        IngestionDocument document = MixedCellsMalformedGraph(mutation);
        ExtractionDocumentSection root = Assert.Single(document.Sections);
        ExtractionDocumentElement[] original = root.Elements.ToArray();
        int calls = 0;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                calls++;
                throw new InvalidOperationException("No chat expected.");
            }
        };
        ImageAlternativeTextEnricher enricher = new(new(client));

        await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.ProcessAsync(document));

        Assert.Equal(0, calls);
        Assert.Same(root, Assert.Single(document.Sections));
        Assert.Equal(original, root.Elements);
    }

    [Fact]
    public void MixedCells_TableCollection_RejectsDirectSelfCycleWithoutReplacingCell()
    {
        ExtractionDocumentSection section = new(new TextContent("local"));
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new(section);
        ExtractionDocumentTableCell original = table.Cells[0, 0]!;

        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 0] = new(table));

        Assert.Same(original, table.Cells[0, 0]);
        Assert.Same(section, table.Cells[0, 0]!.Content);
        Assert.Equal("local", Assert.IsType<TextContent>(section.Content).Text);
    }

    [Fact]
    public async Task MixedCells_DocumentTokenChunker_CountsSeparatorsWithinTightBudget()
    {
        ExtractionDocumentTextElement paragraphA = new(new TextContent("paragraphA"));
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 8 }, "image/png")) { AlternativeText = "description" };
        ExtractionDocumentTextElement paragraphB = new(new TextContent("paragraphB"));
        ExtractionDocumentSection cell = new(new TextContent("local")) { Elements = { paragraphA, image, paragraphB } };
        ExtractionDocumentTextElement neighbor = new(new TextContent("neighbor"));
        ExtractionDocumentTableElement table = new(null, 1, 2);
        table.Cells[0, 0] = new(cell);
        table.Cells[0, 1] = new(neighbor);
        ExtractionDocumentSection section = new() { Elements = { table } };
        IngestionDocument document = new("tight") { Sections = { section } };
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

        List<IngestionChunk> chunks = await Tokens(5).ProcessAsync(document).ToListAsync();

        Assert.True(chunks.Count > 1);
        Assert.Equal(string.Join(Environment.NewLine, "local", "paragraphA", "description", "paragraphB") + "\tneighbor",
            string.Concat(chunks.Select(Text)));
        Assert.Equal(5, chunks[0].TokenCount);
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.TokenCount, 1, 5);
            Assert.Equal(tokenizer.CountTokens(Text(chunk), considerNormalization: false), chunk.TokenCount);
            Assert.Same(document, chunk.Document);
            Assert.Contains(table, chunk.SourceElements);
        });
        Assert.Equal(new ExtractionDocumentElement[] { section, table, cell, paragraphA, image, paragraphB, neighbor },
            chunks.SelectMany(chunk => chunk.SourceElements).Distinct());
    }

    [Theory]
    [InlineData("DocumentToken")]
    [InlineData("Section")]
    [InlineData("Header")]
    [InlineData("SemanticSimilarity")]
    public async Task MixedCells_AllChunkers_DescendNestedCellSectionsWithoutEmptySeparators(string kind)
    {
        ExtractionDocumentTextElement paragraph = new(new TextContent("paragraph"));
        ExtractionDocumentSection nested = new(new TextContent("inner")) { Elements = { paragraph } };
        ExtractionDocumentSection empty = new(new TextContent(string.Empty));
        ExtractionDocumentSection cell = new(new TextContent("outer")) { Elements = { empty, nested, empty } };
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new(cell);
        ExtractionDocumentSection section = new() { Elements = { table } };
        IngestionDocument document = new("nested-cell") { Sections = { section } };
        List<string> embedded = [];
        using TestEmbeddingGenerator<TextContent, Embedding<float>> generator = MixedCellsEmbeddings(embedded);

        IngestionChunk chunk = Assert.Single(await MixedCellsChunker(kind, TiktokenTokenizer.CreateForModel("gpt-4o"), 100, generator)
            .ProcessAsync(document).ToListAsync());

        string expected = string.Join(Environment.NewLine, "outer", "inner", "paragraph");
        Assert.Equal(expected, Text(chunk));
        Assert.Equal(new ExtractionDocumentElement[] { section, table, cell, empty, nested, paragraph }, chunk.SourceElements);
        Assert.Equal("outer", Assert.IsType<TextContent>(cell.Content).Text);
        Assert.Equal("inner", Assert.IsType<TextContent>(nested.Content).Text);
        Assert.Equal(kind == "SemanticSimilarity" ? new[] { expected } : Array.Empty<string>(), embedded);
    }

    private static IngestionDocument MixedCellsMalformedGraph(string mutation)
    {
        ExtractionDocumentSection cell = new();
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new(cell);
        switch (mutation)
        {
            case "Section":
                cell.Elements.Add(cell);
                break;
            case "Table":
                ExtractionDocumentTableElement nestedTable = new(null, 1, 1);
                nestedTable.Cells[0, 0] = new(cell);
                table.Cells[0, 0] = new(nestedTable);
                cell.Elements.Add(nestedTable);
                break;
            case "Mixed":
                cell.Elements.Add(table);
                break;
            case "Null":
                cell.Elements.Add(null!);
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        return new("malformed-mixed") { Sections = { new() { Elements = { table } } } };
    }

    private static TestEmbeddingGenerator<TextContent, Embedding<float>> MixedCellsEmbeddings(List<string> embedded) => new()
    {
        GenerateAsyncCallback = (values, _, _) =>
        {
            TextContent[] inputs = values.ToArray();
            embedded.AddRange(inputs.Select(value => value.Text));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                inputs.Select(_ => new Embedding<float>(new float[] { 1, 0, 0 }))));
        }
    };

    private static IngestionChunker MixedCellsChunker(string kind, Tokenizer tokenizer, int limit,
        TestEmbeddingGenerator<TextContent, Embedding<float>> generator)
    {
        IngestionChunkerOptions options = new(tokenizer) { MaxTokensPerChunk = limit, OverlapTokens = 0 };
        return kind switch
        {
            "DocumentToken" => new DocumentTokenChunker(options),
            "Section" => new SectionChunker(options),
            "Header" => new HeaderChunker(options),
            "SemanticSimilarity" => new SemanticSimilarityChunker(generator, options),
            _ => throw new InvalidOperationException(kind)
        };
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
        public override Task<IngestionDocument> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
        {
            calls.Add("document");
            document.ExtractedDocument.Content = new TextContent("processor-local");
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
