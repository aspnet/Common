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

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests
{
    public class SemanticSimilarityChunkerTests : DocumentChunkerTests
    {
        protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            TestEmbeddingGenerator<TextContent> embeddingClient = new();
#pragma warning restore CA2000 // Dispose objects before losing scope
            return CreateSemanticSimilarityChunker(embeddingClient, maxTokensPerChunk, overlapTokens);
        }

        private static IngestionChunker CreateSemanticSimilarityChunker(IEmbeddingGenerator<TextContent, Embedding<float>> embeddingClient, int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        {
            Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            return new SemanticSimilarityChunker(embeddingClient,
                new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = overlapTokens });
        }

        [Fact]
        public async Task SingleParagraph()
        {
            string text = ".NET is a free, cross-platform, open-source developer platform for building many " +
                "kinds of applications. It can run programs written in multiple languages, with C# being the most popular. " +
                "It relies on a high-performance runtime that is used in production by many high-scale apps.";
            IngestionDocument doc = new IngestionDocument("doc");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(text))
                }
            });
            using TestEmbeddingGenerator<TextContent, Embedding<float>> customGenerator = new()
            {
                GenerateAsyncCallback = static async (values, options, ct) =>
                {
                    var embeddings = values.Select(v =>
                        new Embedding<float>(new float[] { 1.0f, 2.0f, 3.0f, 4.0f }))
                        .ToArray();

                    return [.. embeddings];
                }
            };
            IngestionChunker chunker = CreateSemanticSimilarityChunker(customGenerator);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Single(chunks);
            Assert.Equal(text, GetText(chunks[0]));
        }

        [Fact]
        public async Task TwoTopicsParagraphs()
        {
            IngestionDocument doc = new IngestionDocument("doc");
            string text1 = ".NET is a free, cross-platform, open-source developer platform for building many" +
                "kinds of applications. It can run programs written in multiple languages, with C# being the most popular.";
            string text2 = "It relies on a high-performance runtime that is used in production by many high-scale apps.";
            string text3 = "Zeus is the chief deity of the Greek pantheon. He is a sky and thunder god in ancient Greek religion and mythology.";
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(text1)),
                    new ExtractionDocumentTextElement(new TextContent(text2)),
                    new ExtractionDocumentTextElement(new TextContent(text3))
                }
            });

            using TestEmbeddingGenerator<TextContent, Embedding<float>> customGenerator = new()
            {
                GenerateAsyncCallback = async (values, options, ct) =>
                {
                    var embeddings = values.Select((_, index) =>
                    {
                        return index switch
                        {
                            0 => new Embedding<float>(new float[] { 1.0f, 1.0f, 1.0f, 1.0f }),
                            1 => new Embedding<float>(new float[] { 1.0f, 1.0f, 1.0f, 1.0f }),
                            2 => new Embedding<float>(new float[] { -1.0f, -1.0f, -1.0f, -1.0f }),
                            _ => throw new InvalidOperationException("Unexpected call count")
                        };
                    }).ToArray();

                    return [.. embeddings];
                }
            };

            IngestionChunker chunker = CreateSemanticSimilarityChunker(customGenerator);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Equal(2, chunks.Count);
            Assert.Equal(text1 + Environment.NewLine + text2, GetText(chunks[0]));
            Assert.Equal(text3, GetText(chunks[1]));
        }

        [Fact]
        public async Task TwoSeparateTopicsWithAllKindsOfElements()
        {
            const string DotNetTableText =
                "Language\tType\tStatus\nC#\tObject-oriented\tPrimary\nF#\tFunctional\tOfficial\nVisual Basic\tObject-oriented\tOfficial\n" +
                "PowerShell\tScripting\tSupported\nIronPython\tDynamic\tCommunity\nIronRuby\tDynamic\tCommunity\nBoo\tObject-oriented\tCommunity\nNemerle\tFunctional/OOP\tCommunity";

            const string GodsTablePrefix =
                "God\tDomain\tSymbol\tRoman Name\nZeus\tSky & Thunder\tLightning Bolt\tJupiter\nHera\tMarriage & Family\tPeacock\tJuno\n" +
                "Poseidon\tSea & Earthquakes\tTrident\tNeptune\nAthena\tWisdom & War\tOwl\tMinerva\nApollo\tSun & Music\tLyre\tApollo\n" +
                "Artemis\tHunt & Moon\tSilver Bow\tDiana\nAphrodite\tLove & Beauty\tDove\tVenus\nAres\tWar & Courage\tSpear\tMars\n" +
                "Hephaestus\tFire & Forge\tHammer\tVulcan\nDemeter\tHarvest & Nature\tWheat\tCeres\nDionysus\tWine & Festivity\tGrapes\tBacchus";
            const string SecondChunk = "Ancient Greek Olympian Gods\nThe twelve Olympian gods were the principal deities of the Greek pantheon:\n" + GodsTablePrefix;

            IngestionDocument doc = new("dotnet-languages");
            doc.Sections.Add(new ExtractionDocumentSection
            {
                Elements =
                {
                    new ExtractionDocumentTextElement(new TextContent(".NET Supported Languages"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("The .NET platform supports multiple programming languages:")),
                    TestDocumentFactory.CreateTable(ToParagraphCells(CreateLanguageTableCells()), hasColumnHeaders: true),
                    new ExtractionDocumentTextElement(new TextContent("C# remains the most popular language for .NET development.")),
                    new ExtractionDocumentTextElement(new TextContent("Ancient Greek Olympian Gods"), role: "heading") { HeadingLevel = 1 },
                    new ExtractionDocumentTextElement(new TextContent("The twelve Olympian gods were the principal deities of the Greek pantheon:")),
                    TestDocumentFactory.CreateTable(ToParagraphCells(CreateGreekGodsTableCells()), hasColumnHeaders: true),
                    new ExtractionDocumentTextElement(new TextContent("These gods resided on Mount Olympus and ruled over different aspects of mortal and divine life."))
                }
            });

            using TestEmbeddingGenerator<TextContent, Embedding<float>> customGenerator = new()
            {
                GenerateAsyncCallback = async (values, options, ct) =>
                {
                    var embeddings = values.Select((_, index) =>
                    {
                        return index switch
                        {
                            <= 3 => new Embedding<float>(new float[] { 1.0f, 1.0f, 1.0f, 1.0f }),
                            >= 4 and <= 7 => new Embedding<float>(new float[] { -1.0f, -1.0f, -1.0f, -1.0f }),
                            _ => throw new InvalidOperationException($"Unexpected index: {index}")
                        };
                    }).ToArray();

                    return [.. embeddings];
                }
            };

            Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            int maxTokens = tokenizer.CountTokens(SecondChunk.Replace("\n", Environment.NewLine), considerNormalization: false);
            IngestionChunker chunker = CreateSemanticSimilarityChunker(customGenerator, maxTokens, 0);
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

            Assert.Equal(3, chunks.Count);
            Assert.All(chunks, chunk => Assert.Same(doc, chunk.Document));
            Assert.Equal($@".NET Supported Languages
The .NET platform supports multiple programming languages:
{DotNetTableText}
C# remains the most popular language for .NET development.",
            GetText(chunks[0]), ignoreLineEndingDifferences: true);
            Assert.Equal(SecondChunk, GetText(chunks[1]), ignoreLineEndingDifferences: true);
            Assert.Equal("God\tDomain\tSymbol\tRoman Name\nHermes\tMessages & Trade\tCaduceus\tMercury\n" +
                "These gods resided on Mount Olympus and ruled over different aspects of mortal and divine life.",
                GetText(chunks[2]), ignoreLineEndingDifferences: true);
            Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, maxTokens));

            static string[,] CreateGreekGodsTableCells() => new string[,]
                {
                    { "God", "Domain", "Symbol", "Roman Name" },
                    { "Zeus", "Sky & Thunder", "Lightning Bolt", "Jupiter" },
                    { "Hera", "Marriage & Family", "Peacock", "Juno" },
                    { "Poseidon", "Sea & Earthquakes", "Trident", "Neptune" },
                    { "Athena", "Wisdom & War", "Owl", "Minerva" },
                    { "Apollo", "Sun & Music", "Lyre", "Apollo" },
                    { "Artemis", "Hunt & Moon", "Silver Bow", "Diana" },
                    { "Aphrodite", "Love & Beauty", "Dove", "Venus" },
                    { "Ares", "War & Courage", "Spear", "Mars" },
                    { "Hephaestus", "Fire & Forge", "Hammer", "Vulcan" },
                    { "Demeter", "Harvest & Nature", "Wheat", "Ceres" },
                    { "Dionysus", "Wine & Festivity", "Grapes", "Bacchus" },
                    { "Hermes", "Messages & Trade", "Caduceus", "Mercury" }
                };

            static string[,] CreateLanguageTableCells() => new string[,]
                {
                    { "Language", "Type", "Status" },
                    { "C#", "Object-oriented", "Primary" },
                    { "F#", "Functional", "Official" },
                    { "Visual Basic", "Object-oriented", "Official" },
                    { "PowerShell", "Scripting", "Supported" },
                    { "IronPython", "Dynamic", "Community" },
                    { "IronRuby", "Dynamic", "Community" },
                    { "Boo", "Object-oriented", "Community" },
                    { "Nemerle", "Functional/OOP", "Community" }
                };
        }

        private static ExtractionDocumentElement?[,] ToParagraphCells(string[,] cells)
        {
            int rows = cells.GetLength(0);
            int cols = cells.GetLength(1);
            var result = new ExtractionDocumentElement?[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    result[i, j] = new ExtractionDocumentTextElement(new TextContent(cells[i, j]));
                }
            }

            return result;
        }
    }
}
