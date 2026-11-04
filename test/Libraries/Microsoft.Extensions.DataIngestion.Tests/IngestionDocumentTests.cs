// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionDocumentTests
{
    [Fact]
    public void LegacyConstructor_CreatesDistinctEmptyGraphs()
    {
        IngestionDocument first = new("legacy");
        IngestionDocument second = new("legacy");
        Assert.Equal("legacy", first.Identifier);
        Assert.Equal("legacy", first.ExtractedDocument.Identifier);
        Assert.NotSame(first.ExtractedDocument, second.ExtractedDocument);
        Assert.Same(first.ExtractedDocument.Sections, first.Sections);
        Assert.Empty(first.Sections);
        Assert.Empty(first.EnumerateContent());
        Assert.Null(first.ExtractedDocument.Content);
        Assert.Empty(first.ExtractedDocument.Metadata);
        Assert.Empty(first.ExtractedDocument.SourcePages);
    }

    [Fact]
    public void Constructors_ValidateIdentifierAndGraph()
    {
        ExtractionDocument graph = new("provider");
        Assert.Throws<ArgumentNullException>("identifier", () => new IngestionDocument(null!));
        Assert.Throws<ArgumentException>("identifier", () => new IngestionDocument(""));
        Assert.Throws<ArgumentNullException>("identifier", () => new IngestionDocument(null!, graph));
        Assert.Throws<ArgumentException>("identifier", () => new IngestionDocument("", graph));
        Assert.Throws<ArgumentNullException>("extractedDocument", () => new IngestionDocument("id", null!));
        Assert.Equal("provider", graph.Identifier);
        Assert.Empty(graph.Sections);
    }

    [Fact]
    public void Envelope_RetainsExactGraphAndIndependentIdsAndSharesListMutations()
    {
        object native = new();
        TextContent content = new("root local");
        ExtractionDocumentSourcePage blank = new("blank", 2);
        ExtractionDocument graph = new("provider-id", content) { RawRepresentation = native, SourcePages = { blank } };
        graph.Metadata.Add("producer", native);
        IngestionDocument document = new("persistence-id", graph);
        ExtractionDocumentTextElement leaf = new(new TextContent("leaf"));
        ExtractionDocumentSection section = new(new TextContent("section")) { Elements = { leaf } };
        document.Sections.Add(section);
        Assert.Same(graph, document.ExtractedDocument);
        Assert.Equal("persistence-id", document.Identifier);
        Assert.Equal("provider-id", graph.Identifier);
        Assert.Same(graph.Sections, document.Sections);
        Assert.Same(section, Assert.Single(graph.Sections));
        Assert.Same(leaf, Assert.Single(document.EnumerateContent()));
        Assert.Equal(graph.EnumerateContent(), document.EnumerateContent());
        Assert.Same(content, document.ExtractedDocument.Content);
        Assert.Same(native, document.ExtractedDocument.Metadata["producer"]);
        Assert.Same(native, document.ExtractedDocument.RawRepresentation);
        Assert.Same(blank, Assert.Single(document.ExtractedDocument.SourcePages));
        graph.Sections.Clear();
        Assert.Empty(document.Sections);
        Assert.Empty(document.EnumerateContent());
        graph.Sections.Add(section);
        Assert.Same(section, Assert.Single(document.Sections));
        document.Sections.Remove(section);
        Assert.Empty(graph.Sections);
        Assert.Equal("provider-id", graph.Identifier);
        Assert.Equal("persistence-id", document.Identifier);
    }

    [Fact]
    public void PublicSurface_UsesSealedGetOnlyEnvelopeAndExactRootSignatures()
    {
        Type root = typeof(IngestionDocument);
        Assert.True(root.IsPublic);
        Assert.True(root.IsSealed);
        foreach (string name in new[] { "Identifier", "ExtractedDocument", "Sections" })
        {
            Assert.Null(root.GetProperty(name)!.SetMethod);
        }

        Assert.Equal(typeof(ExtractionDocument), root.GetProperty("ExtractedDocument")!.PropertyType);
        Assert.Equal(typeof(List<ExtractionDocumentSection>), root.GetProperty("Sections")!.PropertyType);
        Assert.Equal(typeof(IEnumerable<ExtractionDocumentElement>), root.GetMethod("EnumerateContent")!.ReturnType);
        foreach (string name in new[] { "Content", "Metadata", "SourcePages", "RawRepresentation" })
        {
            Assert.Null(root.GetProperty(name));
        }

        Assert.Equal(typeof(Task<IngestionDocument>),
            typeof(IngestionDocumentReader).GetMethod("ReadAsync", new[] { typeof(Stream), typeof(string), typeof(string), typeof(CancellationToken) })!.ReturnType);
        Assert.Equal(typeof(Task<IngestionDocument>),
            typeof(IngestionDocumentProcessor).GetMethod("ProcessAsync", new[] { root, typeof(CancellationToken) })!.ReturnType);
        Assert.Equal(typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(IngestionChunker).GetMethod("ProcessAsync", new[] { root, typeof(CancellationToken) })!.ReturnType);
        Assert.Equal(root, typeof(IngestionChunk).GetProperty("Document")!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<ExtractionDocumentElement>), typeof(IngestionChunk).GetProperty("SourceElements")!.PropertyType);
        Assert.Equal(typeof(List<IngestionDocumentProcessor>), typeof(IngestionPipeline).GetProperty("DocumentProcessors")!.PropertyType);
        Assert.Equal(typeof(List<IngestionChunkProcessor>), typeof(IngestionPipeline).GetProperty("ChunkProcessors")!.PropertyType);
        Assert.Equal(typeof(Task<IngestionDocument>),
            typeof(IngestionPipeline).GetMethod("ProcessAsync", new[] { root, typeof(CancellationToken) })!.ReturnType);
        Assert.Equal(root, typeof(IngestionResult).GetProperty("Document")!.PropertyType);
        Assert.Equal(typeof(Task),
            typeof(IngestionChunkWriter).GetMethod("WriteAsync", new[] { typeof(IAsyncEnumerable<IngestionChunk>), typeof(CancellationToken) })!.ReturnType);
        Assert.Equal(typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(IngestionChunkProcessor).GetMethod("ProcessAsync", new[] { typeof(IAsyncEnumerable<IngestionChunk>), typeof(CancellationToken) })!.ReturnType);
        foreach (Type reader in new[] { typeof(MarkdownReader), typeof(MarkItDownReader), typeof(MarkItDownMcpReader), typeof(DocumentExtractionReader) })
        {
            Assert.Equal(typeof(Task<IngestionDocument>),
                reader.GetMethod("ReadAsync", new[] { typeof(Stream), typeof(string), typeof(string), typeof(CancellationToken) })!.ReturnType);
        }
    }

    private readonly ExtractionDocumentElement?[,] _rows =
    {
        { new ExtractionDocumentTextElement(new TextContent("header")) },
        { new ExtractionDocumentTextElement(new TextContent("row1")) },
        { new ExtractionDocumentTextElement(new TextContent("row2")) }
    };

    [Fact]
    public void EnumeratorFlattensTheStructureAndPreservesOrder()
    {
        IngestionDocument doc = new("withSubSections");
        doc.Sections.Add(new ExtractionDocumentSection(new TextContent("first section"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("header"), role: "heading"),
                new ExtractionDocumentTextElement(new TextContent("paragraph")),
                TestDocumentFactory.CreateTable(_rows, new TextContent("table")),
                new ExtractionDocumentSection(new TextContent("nested section"))
                {
                    Elements =
                    {
                        new ExtractionDocumentTextElement(new TextContent("nested header"), role: "heading"),
                        new ExtractionDocumentTextElement(new TextContent("nested paragraph"))
                    }
                }
            }
        });
        doc.Sections.Add(new ExtractionDocumentSection(new TextContent("second section"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("header 2"), role: "heading"),
                new ExtractionDocumentTextElement(new TextContent("paragraph 2"))
            }
        });

        ExtractionDocumentElement[] flatElements = doc.EnumerateContent().ToArray();

        Assert.Equal(7, flatElements.Length);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[0]).Role);
        Assert.Equal("header", Assert.IsType<TextContent>(flatElements[0].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[1]).Role);
        Assert.Equal("paragraph", Assert.IsType<TextContent>(flatElements[1].Content).Text);
        Assert.IsType<ExtractionDocumentTableElement>(flatElements[2]);
        Assert.Equal("table", Assert.IsType<TextContent>(flatElements[2].Content).Text);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[3]).Role);
        Assert.Equal("nested header", Assert.IsType<TextContent>(flatElements[3].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[4]).Role);
        Assert.Equal("nested paragraph", Assert.IsType<TextContent>(flatElements[4].Content).Text);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[5]).Role);
        Assert.Equal("header 2", Assert.IsType<TextContent>(flatElements[5].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[6]).Role);
        Assert.Equal("paragraph 2", Assert.IsType<TextContent>(flatElements[6].Content).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyParagraphDocumentCantBeCreated(string? input)
    {
        // The shared model rejects a null payload, not empty or null text within TextContent.
        Assert.Throws<ArgumentNullException>("content", () => new ExtractionDocumentTextElement(null!));
        TextContent content = new(input);
        ExtractionDocumentTextElement paragraph = new(content);
        Assert.Same(content, paragraph.Content);
        Assert.Equal(string.Empty, content.Text);
        Assert.Null(paragraph.Role);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidHeaderLevelThrows(int level)
    {
        ExtractionDocumentTextElement header = new(new TextContent("header"), role: "heading");

        Assert.Throws<ArgumentOutOfRangeException>("value", () => header.HeadingLevel = level);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void PositiveHeaderLevelsArePreserved(int level)
    {
        ExtractionDocumentTextElement header = new(new TextContent("header"), role: "heading") { HeadingLevel = level };

        Assert.Equal(level, header.HeadingLevel);
        Assert.Equal("header", Assert.IsType<TextContent>(header.Content).Text);
        Assert.Equal("heading", header.Role);
    }
}
