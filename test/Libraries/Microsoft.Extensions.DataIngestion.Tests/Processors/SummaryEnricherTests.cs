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

public class SummaryEnricherTests
{
    private static readonly IngestionDocument _document = new("test");

    [Fact]
    public void ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>("options", () => new SummaryEnricher(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowsOnInvalidMaxKeywords(int wordCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>("maxWordCount", () => new SummaryEnricher(new(new TestChatClient()), maxWordCount: wordCount));
    }

    [Fact]
    public async Task ThrowsOnNullChunks()
    {
        using TestChatClient chatClient = new();
        SummaryEnricher sut = new(new(chatClient));

        await Assert.ThrowsAsync<ArgumentNullException>("chunks", async () =>
        {
            await foreach (var _ in sut.ProcessAsync(null!))
            {
                // No-op
            }
        });
    }

    [Fact]
    public async Task CanProvideSummary()
    {
        int counter = 0;
        string[] summaries = { "First summary.", "Second summary." };
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                Assert.Equal(0, counter++);
                var materializedMessages = messages.ToArray();

                Assert.Equal(2, materializedMessages.Length);
                Assert.Equal(ChatRole.System, materializedMessages[0].Role);
                Assert.Equal(ChatRole.User, materializedMessages[1].Role);

                string response = JsonSerializer.Serialize(new Envelope<string[]> { data = summaries });
                return Task.FromResult(new ChatResponse(new[]
                {
                    new ChatMessage(ChatRole.Assistant, response)
                }));
            }
        };
        SummaryEnricher sut = new(new(chatClient));
        var input = CreateChunks().ToAsyncEnumerable();

        var chunks = await sut.ProcessAsync(input).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(summaries[0], (string)chunks[0].Metadata[SummaryEnricher.MetadataKey]!);
        Assert.Equal(summaries[1], (string)chunks[1].Metadata[SummaryEnricher.MetadataKey]!);
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

        SummaryEnricher sut = new(new(chatClient) { LoggerFactory = loggerFactory });
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
        TestChunkFactory.CreateChunk("I love programming! It's so much fun and rewarding.", _document),
        TestChunkFactory.CreateChunk("I hate bugs. They are so frustrating and time-consuming.", _document)
    ];
}
