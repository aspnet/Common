// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests
{
    public class SectionChunkerTests : DocumentChunkerTests
    {
        protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        {
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            return new SectionChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = overlapTokens });
        }

        [Fact]
        public async Task OneSection()
        {
            IngestionDocument doc = new IngestionDocument("doc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent("This is a paragraph.")),
                    new ExtractionDocumentTextElement(new TextContent("This is another paragraph."))
                }
            });
            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Single(chunks);
            string expectedResult = "This is a paragraph.\nThis is another paragraph.";
            Assert.Equal(expectedResult, GetText(chunks[0]), ignoreLineEndingDifferences: true);
        }

        [Fact]
        public async Task TwoSections()
        {
            IngestionDocument doc = new("doc")
            {
                Sections =
                {
                    new()
                    {
                        Elements =
                        {
                            new ExtractionDocumentTextElement(new TextContent("This is a paragraph.")),
                            new ExtractionDocumentTextElement(new TextContent("This is another paragraph."))
                        }
                    },
                    new()
                    {
                        Elements =
                        {
                            new ExtractionDocumentTextElement(new TextContent("This is a paragraph in section 2.")),
                            new ExtractionDocumentTextElement(new TextContent("This is another paragraph in section 2."))
                        }
                    }
                }
            };

            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

            Assert.Equal(2, chunks.Count);
            string expectedResult1 = "This is a paragraph.\nThis is another paragraph.";
            string expectedResult2 = "This is a paragraph in section 2.\nThis is another paragraph in section 2.";
            Assert.Equal(expectedResult1, GetText(chunks[0]), ignoreLineEndingDifferences: true);
            Assert.Equal(expectedResult2, GetText(chunks[1]), ignoreLineEndingDifferences: true);
        }

        [Fact]
        public async Task EmptySection()
        {
            IngestionDocument doc = new IngestionDocument("doc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements = { }
            });
            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Empty(chunks);
        }

        [Fact]
        public async Task NestedSections()
        {
            IngestionDocument doc = new("doc")
            {
                Sections =
                {
                    new()
                    {
                        Elements =
                        {
                            new ExtractionDocumentTextElement(new TextContent("Section title"), role: "heading"),
                            new ExtractionDocumentTextElement(new TextContent("This is a paragraph in section 1.")),
                            new ExtractionDocumentTextElement(new TextContent("This is another paragraph in section 1.")),
                            new ExtractionDocumentSection
                            {
                                Elements =
                                {
                                    new ExtractionDocumentTextElement(new TextContent("Subsection title"), role: "heading"),
                                    new ExtractionDocumentTextElement(new TextContent("This is a paragraph in subsection 1.1.")),
                                    new ExtractionDocumentTextElement(new TextContent("This is another paragraph in subsection 1.1.")),
                                    new ExtractionDocumentSection
                                    {
                                        Elements =
                                        {
                                            new ExtractionDocumentTextElement(new TextContent("Subsubsection title"), role: "heading"),
                                            new ExtractionDocumentTextElement(new TextContent("This is a paragraph in subsubsection 1.1.1.")),
                                            new ExtractionDocumentTextElement(new TextContent("This is another paragraph in subsubsection 1.1.1."))
                                        }
                                    },
                                    new ExtractionDocumentTextElement(new TextContent("This is the last paragraph in subsection 1.2.")),
                                }
                            }
                        }
                    }
                }
            };

            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

            Assert.Equal(4, chunks.Count);
            Assert.Equal("Section title", chunks[0].Context);
            Assert.Equal("Section title\nThis is a paragraph in section 1.\nThis is another paragraph in section 1.",
                GetText(chunks[0]), ignoreLineEndingDifferences: true);
            Assert.Equal("Section title Subsection title", chunks[1].Context);
            Assert.Equal("Section title Subsection title\nThis is a paragraph in subsection 1.1.\nThis is another paragraph in subsection 1.1.",
                GetText(chunks[1]), ignoreLineEndingDifferences: true);
            Assert.Equal("Section title Subsection title Subsubsection title", chunks[2].Context);
            Assert.Equal("Section title Subsection title Subsubsection title\nThis is a paragraph in subsubsection 1.1.1.\nThis is another paragraph in subsubsection 1.1.1.",
                GetText(chunks[2]), ignoreLineEndingDifferences: true);
            Assert.Equal("Section title Subsection title", chunks[3].Context);
            Assert.Equal("Section title Subsection title\nThis is the last paragraph in subsection 1.2.", GetText(chunks[3]), ignoreLineEndingDifferences: true);
        }

        [Fact]
        public async Task SizeLimit_TwoChunks()
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 600)); // each word is 1 token
            IngestionDocument doc = new IngestionDocument("twoChunksNoOverlapDoc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(text))
                }
            });
            IngestionChunker chunker = CreateDocumentChunker(maxTokensPerChunk: 512);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Equal(2, chunks.Count);
            Assert.True(GetText(chunks[0]).Split(' ').Length <= 512);
            Assert.True(GetText(chunks[1]).Split(' ').Length <= 512);
            Assert.Equal(text, string.Join("", chunks.Select(c => GetText(c))), ignoreLineEndingDifferences: true);
        }

        [Fact]
        public async Task SectionWithHeader()
        {
            IngestionDocument doc = new IngestionDocument("doc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent("Section 1"), role: "heading"),
                    new ExtractionDocumentTextElement(new TextContent("This is a paragraph in section 1.")),
                    new ExtractionDocumentTextElement(new TextContent("This is another paragraph in section 1."))
                }
            });
            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            IngestionChunk chunk = Assert.Single(chunks);
            string expectedResult = "Section 1\nThis is a paragraph in section 1.\nThis is another paragraph in section 1.";
            Assert.Equal(expectedResult, GetText(chunk), ignoreLineEndingDifferences: true);
            Assert.Equal("Section 1", chunk.Context);
        }
    }
}
