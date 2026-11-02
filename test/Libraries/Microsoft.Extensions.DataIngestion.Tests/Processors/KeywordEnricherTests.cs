// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Processors.Tests;

public class KeywordEnricherTests
{
    private static readonly IngestionDocument _document = new("test");

    [Fact]
    public void ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>("options", () => new KeywordEnricher(null!, predefinedKeywords: null, confidenceThreshold: 0.5));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ThrowsOnInvalidThreshold(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>("confidenceThreshold", () => new KeywordEnricher(new(new TestChatClient()), predefinedKeywords: null, confidenceThreshold: threshold));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowsOnInvalidMaxKeywords(int keywordCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>("maxKeywords", () => new KeywordEnricher(new(new TestChatClient()), predefinedKeywords: null, maxKeywords: keywordCount));
    }

    [Fact]
    public void ThrowsOnDuplicateKeywords()
    {
        Assert.Throws<ArgumentException>("predefinedKeywords", () => new KeywordEnricher(new(new TestChatClient()), predefinedKeywords: ["same", "same"], confidenceThreshold: 0.5));
    }

    [Fact]
    public async Task ThrowsOnNullChunks()
    {
        using TestChatClient chatClient = new();
        KeywordEnricher sut = new(new(chatClient), predefinedKeywords: null, confidenceThreshold: 0.5);

        await Assert.ThrowsAsync<ArgumentNullException>("chunks", async () =>
        {
            await foreach (var _ in sut.ProcessAsync(null!))
            {
                // No-op
            }
        });
    }

    [Theory]
    [InlineData]
    [InlineData("AI", "MEAI", "Animals", "Rabbits")]
    public async Task CanExtractKeywords(params string[] predefined)
    {
        int counter = 0;
        string[][] keywords = [["AI", "MEAI"], ["Animals", "Rabbits"]];
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                Assert.Equal(0, counter++);
                var materializedMessages = messages.ToArray();

                Assert.Equal(2, materializedMessages.Length);
                Assert.Equal(ChatRole.System, materializedMessages[0].Role);
                Assert.Equal(ChatRole.User, materializedMessages[1].Role);

                string response = JsonSerializer.Serialize(new Envelope<string[][]> { data = keywords });
                return Task.FromResult(new ChatResponse(new[]
                {
                    new ChatMessage(ChatRole.Assistant, response)
                }));
            }
        };

        KeywordEnricher sut = new(new(chatClient), predefinedKeywords: predefined, confidenceThreshold: 0.5);
        var chunks = CreateChunks().ToAsyncEnumerable();

        IReadOnlyList<IngestionChunk> got = await sut.ProcessAsync(chunks).ToListAsync();

        Assert.Equal(["AI", "MEAI"], (string[])got[0].Metadata[KeywordEnricher.MetadataKey]);
        Assert.Equal(["Animals", "Rabbits"], (string[])got[1].Metadata[KeywordEnricher.MetadataKey]);
    }

    [Fact]
    public async Task FailureDoesNotStopTheProcessing()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromException<ChatResponse>(new ExpectedException())
        };

        KeywordEnricher sut = new(new(chatClient) { LoggerFactory = loggerFactory }, ["AI", "Other"]);
        List<IngestionChunk> chunks = CreateChunks();

        IReadOnlyList<IngestionChunk> got = await sut.ProcessAsync(chunks.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal(chunks.Count, got.Count);
        Assert.All(chunks, chunk => Assert.False(chunk.HasMetadata));
        Assert.Equal(1, collector.Count); // with batching, only one log entry is expected
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.IsType<ExpectedException>(collector.LatestRecord.Exception);
    }

    private static List<IngestionChunk> CreateChunks() =>
    [
        TestChunkFactory.CreateChunk("The Microsoft.Extensions.AI libraries provide a unified approach for representing generative AI components", _document),
        TestChunkFactory.CreateChunk("Rabbits are great pets. They are friendly and make excellent companions.", _document)
    ];
}
