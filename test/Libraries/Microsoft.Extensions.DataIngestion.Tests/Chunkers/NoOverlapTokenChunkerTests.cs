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
    public class NoOverlapTokenChunkerTests : DocumentTokenChunkerTests
    {
        protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        {
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            return new DocumentTokenChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = 0 });
        }

        [Fact]
        public async Task TwoChunks()
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 600)); // each word is 1 token
            ExtractionDocument doc = new ExtractionDocument("twoChunksNoOverlapDoc");
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
            Assert.Equal(text, string.Join("", chunks.Select(c => GetText(c))));
        }

        [Fact]
        public async Task ManyChunks()
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 1500)); // each word is 1 token
            ExtractionDocument doc = new ExtractionDocument("smallChunksNoOverlapDoc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(text))
                }
            });

            IngestionChunker chunker = CreateDocumentChunker(maxTokensPerChunk: 200, overlapTokens: 0);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Equal(8, chunks.Count);
            foreach (var chunk in chunks)
            {
                Assert.True(GetText(chunk).Split(' ').Count(str => str.Contains("word")) <= 200);
            }

            Assert.Equal(text, string.Join("", chunks.Select(c => GetText(c))));
        }

        [Fact]
        public async Task VerifyTokenCount()
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 600)); // each word is 1 token
            ExtractionDocument doc = new ExtractionDocument("verifyTokenCountDoc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(text))
                }
            });

            Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            IngestionChunker chunker = CreateDocumentChunker(maxTokensPerChunk: 512, overlapTokens: 0);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

            Assert.Equal(2, chunks.Count);
            foreach (IngestionChunk chunk in chunks)
            {
                // Verify that TokenCount property is set
                Assert.True(chunk.TokenCount > 0);

                // Verify that TokenCount matches actual token count of content
                int actualTokenCount = tokenizer.CountTokens(GetText(chunk), considerNormalization: false);
                Assert.Equal(actualTokenCount, chunk.TokenCount);

                // Verify that TokenCount does not exceed max tokens per chunk
                Assert.True(chunk.TokenCount <= 512);
            }
        }
    }
}
