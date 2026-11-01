// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class HeaderChunkerTests : DocumentChunkerTests
{
    protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        => new HeaderChunker(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = maxTokensPerChunk });

    [Fact]
    public async Task CanChunkNonTrivialDocument()
    {
        ExtractionDocument doc = new("nonTrivial");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header 1"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("Header 1_1"), role: "heading") { HeadingLevel = 2 },
                        new ExtractionDocumentTextElement(new TextContent("Paragraph 1_1_1")),
                        new ExtractionDocumentTextElement(new TextContent("Header 1_1_1"), role: "heading") { HeadingLevel = 3 },
                            new ExtractionDocumentTextElement(new TextContent("Paragraph 1_1_1_1")),
                            new ExtractionDocumentTextElement(new TextContent("Paragraph 1_1_1_2")),
                        new ExtractionDocumentTextElement(new TextContent("Header 1_1_2"), role: "heading") { HeadingLevel = 3 },
                            new ExtractionDocumentTextElement(new TextContent("Paragraph 1_1_2_1")),
                            new ExtractionDocumentTextElement(new TextContent("Paragraph 1_1_2_2")),
                    new ExtractionDocumentTextElement(new TextContent("Header 1_2"), role: "heading") { HeadingLevel = 2 },
                        new ExtractionDocumentTextElement(new TextContent("Paragraph 1_2_1")),
                        new ExtractionDocumentTextElement(new TextContent("Header 1_2_1"), role: "heading") { HeadingLevel = 3 },
                            new ExtractionDocumentTextElement(new TextContent("Paragraph 1_2_1_1")),
            }
        });

        HeaderChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4")));
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.Equal(5, chunks.Count);

        Assert.Equal("Header 1 Header 1_1", chunks[0].Context);
        Assert.Equal($"Header 1 Header 1_1\nParagraph 1_1_1", GetText(chunks[0]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header 1 Header 1_1 Header 1_1_1", chunks[1].Context);
        Assert.Equal($"Header 1 Header 1_1 Header 1_1_1\nParagraph 1_1_1_1\nParagraph 1_1_1_2", GetText(chunks[1]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header 1 Header 1_1 Header 1_1_2", chunks[2].Context);
        Assert.Equal($"Header 1 Header 1_1 Header 1_1_2\nParagraph 1_1_2_1\nParagraph 1_1_2_2", GetText(chunks[2]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header 1 Header 1_2", chunks[3].Context);
        Assert.Equal($"Header 1 Header 1_2\nParagraph 1_2_1", GetText(chunks[3]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header 1 Header 1_2 Header 1_2_1", chunks[4].Context);
        Assert.Equal($"Header 1 Header 1_2 Header 1_2_1\nParagraph 1_2_1_1", GetText(chunks[4]), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task CanRespectTokenLimit()
    {
        ExtractionDocument doc = new("longOne");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header A"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("Header B"), role: "heading") { HeadingLevel = 2 },
                        new ExtractionDocumentTextElement(new TextContent("Header C"), role: "heading") { HeadingLevel = 3 },
                            new ExtractionDocumentTextElement(new TextContent("This is a very long text. It's expressed with plenty of tokens"))
            }
        });

        HeaderChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 13 });
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal("Header A Header B Header C", chunk.Context));
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 13));
        Assert.Equal("Header A Header B Header C\nThis is a very long text", GetText(chunks[0]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A Header B Header C\n. It's expressed with plenty", GetText(chunks[1]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A Header B Header C\n of tokens", GetText(chunks[2]), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task ThrowsWhenLimitIsTooLowToFitAnythingMoreThanContext()
    {
        ExtractionDocument doc = new("longOne");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header A"), role: "heading") { HeadingLevel = 1 }, // 2 tokens
                    new ExtractionDocumentTextElement(new TextContent("Header B"), role: "heading") { HeadingLevel = 2 }, // 2 tokens
                        new ExtractionDocumentTextElement(new TextContent("Header C"), role: "heading") { HeadingLevel = 3 }, // 2 tokens
                            new ExtractionDocumentTextElement(new TextContent("This is a very long text. It's expressed with plenty of tokens"))
            }
        });

        HeaderChunker lessThanContext = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 5 });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await lessThanContext.ProcessAsync(doc).ToListAsync());

        HeaderChunker sameAsContext = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 6 });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sameAsContext.ProcessAsync(doc).ToListAsync());
    }

    [Fact]
    public async Task CanSplitLongerParagraphsOnNewLine()
    {
        ExtractionDocument doc = new("withNewLines");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header A"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("Header B"), role: "heading") { HeadingLevel = 2 },
                        new ExtractionDocumentTextElement(new TextContent("Header C"), role: "heading") { HeadingLevel = 3 },
                            new ExtractionDocumentTextElement(new TextContent(
                                "This is a very long text. It's expressed with plenty of tokens. And it contains a new line.\nWith some text after the new line.")),
                            new ExtractionDocumentTextElement(new TextContent("And following paragraph."))
            }
        });

        HeaderChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 30 });
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Header A Header B Header C", chunks[0].Context);
        Assert.Equal($"Header A Header B Header C\nThis is a very long text. It's expressed with plenty of tokens. And it contains a new line.\n",
            GetText(chunks[0]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A Header B Header C", chunks[1].Context);
        Assert.Equal($"Header A Header B Header C\nWith some text after the new line.\nAnd following paragraph.", GetText(chunks[1]), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task ThrowsWhenHeaderSeparatorAndSingleRowExceedTokenLimit()
    {
        ExtractionDocument document = CreateDocumentWithLargeTable();

        // A structural table has tab-separated cells, not a fabricated Markdown separator.
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        int minimumRowTokens = tokenizer.CountTokens(
            "Header A\none\ttwo\tthree\tfour\tfive\n0\t1\t2\t3\t4".Replace("\n", Environment.NewLine), considerNormalization: false);
        HeaderChunker chunker = new(new(tokenizer) { MaxTokensPerChunk = minimumRowTokens - 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await chunker.ProcessAsync(document).ToListAsync());
    }

    [Fact]
    public async Task CanSplitLargeTableIntoMultipleChunks_MultipleRowsPerChunk()
    {
        ExtractionDocument document = CreateDocumentWithLargeTable();

        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        const string FirstChunk = "Header A\nThis is some text that describes why we need the following table.\none\ttwo\tthree\tfour\tfive\n0\t1\t2\t3\t4\n5\t6\t7\t8\t9\n10\t11\t12\t13\t14";
        int maxTokens = tokenizer.CountTokens(FirstChunk.Replace("\n", Environment.NewLine), considerNormalization: false);
        HeaderChunker chunker = new(new(tokenizer) { MaxTokensPerChunk = maxTokens });
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal("Header A", chunk.Context));
        Assert.Equal(FirstChunk, GetText(chunks[0]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A\none\ttwo\tthree\tfour\tfive\n15\t16\t17\t18\t19\n20\t21\t22\t23\t24\nAnd some follow up.",
            GetText(chunks[1]), ignoreLineEndingDifferences: true);
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, maxTokens));
    }

    [Fact]
    public async Task CanSplitLargeTableIntoMultipleChunks_OneRowPerChunk()
    {
        ExtractionDocument document = CreateDocumentWithLargeTable();

        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        const string LastChunk = "Header A\none\ttwo\tthree\tfour\tfive\n20\t21\t22\t23\t24\nAnd some follow up.";
        int maxTokens = tokenizer.CountTokens(LastChunk.Replace("\n", Environment.NewLine), considerNormalization: false);
        HeaderChunker chunker = new(new(tokenizer) { MaxTokensPerChunk = maxTokens });
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(document).ToListAsync();

        Assert.Equal(6, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal("Header A", chunk.Context));
        Assert.All(chunks, chunk => Assert.InRange(tokenizer.CountTokens(GetText(chunk)), 1, maxTokens));

        Assert.Equal("""
            Header A
            This is some text that describes why we need the following table.
            """ + "\none\ttwo\tthree\tfour\tfive", GetText(chunks[0]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A\none\ttwo\tthree\tfour\tfive\n0\t1\t2\t3\t4", GetText(chunks[1]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A\none\ttwo\tthree\tfour\tfive\n5\t6\t7\t8\t9", GetText(chunks[2]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A\none\ttwo\tthree\tfour\tfive\n10\t11\t12\t13\t14", GetText(chunks[3]), ignoreLineEndingDifferences: true);
        Assert.Equal("Header A\none\ttwo\tthree\tfour\tfive\n15\t16\t17\t18\t19", GetText(chunks[4]), ignoreLineEndingDifferences: true);
        Assert.Equal(LastChunk, GetText(chunks[5]), ignoreLineEndingDifferences: true);
    }

    private static ExtractionDocument CreateDocumentWithLargeTable()
    {
        ExtractionDocumentTableElement table = TestDocumentFactory.CreateTable(CreateTableCells(), hasColumnHeaders: true);

        ExtractionDocument doc = new("withNewLines");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header A"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("This is some text that describes why we need the following table.")),
                    table,
                    new ExtractionDocumentTextElement(new TextContent("And some follow up."))
            }
        });

        return doc;

        static ExtractionDocumentElement?[,] CreateTableCells()
        {
            var cells = new ExtractionDocumentElement[6, 5]; // 6 rows (1 header + 5 data rows), 5 columns

            // Header row
            cells[0, 0] = new ExtractionDocumentTextElement(new TextContent("one"));
            cells[0, 1] = new ExtractionDocumentTextElement(new TextContent("two"));
            cells[0, 2] = new ExtractionDocumentTextElement(new TextContent("three"));
            cells[0, 3] = new ExtractionDocumentTextElement(new TextContent("four"));
            cells[0, 4] = new ExtractionDocumentTextElement(new TextContent("five"));

            // Data rows (0-29)
            int number = 0;
            for (int row = 1; row <= 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    cells[row, col] = new ExtractionDocumentTextElement(new TextContent(number.ToString()));
                    number++;
                }
            }

            return cells;
        }
    }

    [Fact]
    public async Task VerifyTokenCountIsSet()
    {
        ExtractionDocument doc = new("tokenCountTest");
        doc.Sections.Add(new()
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("Header 1"), role: "heading") { HeadingLevel = 1 },
                new ExtractionDocumentTextElement(new TextContent("This is a test paragraph with some content.")),
                new ExtractionDocumentTextElement(new TextContent("This is another paragraph with more content."))
            }
        });

        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        HeaderChunker chunker = new(new(tokenizer));
        IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

        foreach (IngestionChunk chunk in chunks)
        {
            // Verify that TokenCount property is set and greater than zero
            Assert.True(chunk.TokenCount > 0);

            // Verify that TokenCount matches actual token count of content
            int actualTokenCount = tokenizer.CountTokens(GetText(chunk), considerNormalization: false);
            Assert.Equal(actualTokenCount, chunk.TokenCount);
        }
    }

    // We need plenty of more tests here, especially for edge cases:
    // - sentence splitting
    // - markdown splitting (e.g. lists, code blocks etc.)
}
