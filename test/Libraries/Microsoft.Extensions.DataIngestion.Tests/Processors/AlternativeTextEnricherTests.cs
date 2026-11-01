// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Processors.Tests;

public class AlternativeTextEnricherTests
{
    private readonly ReadOnlyMemory<byte> _imageContent = new byte[256];

    [Fact]
    public async Task NestedTableImages_AreEnrichedOnceAndRetainSourceIdentity()
    {
        int calls = 0;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                calls++;
                ChatMessage user = messages.Last();
                DataContent payload = Assert.IsType<DataContent>(Assert.Single(user.Contents));
                Assert.Equal(new byte[] { 1, 2, 3 }, payload.Data.ToArray());
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    JsonSerializer.Serialize(new Envelope<string[]> { data = new[] { "nested image description" } }))));
            }
        };
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 1, 2, 3 }, "image/png"));
        ExtractionDocumentSourcePage page = new("frame", null);
        ExtractionDocumentSourceReference source = new(page);
        image.Sources.Add(source);
        ExtractionDocumentTableElement inner = new(null, 1, 1);
        inner.Cells[0, 0] = new(image);
        ExtractionDocumentTableElement outer = new(null, 1, 1);
        outer.Cells[0, 0] = new(inner);
        ExtractionDocument document = new("nested")
        {
            Sections = { new() { Elements = { outer, image } } }
        };
        ImageAlternativeTextEnricher enricher = new(new(client));

        ExtractionDocument result = await enricher.ProcessAsync(document);

        Assert.Same(document, result);
        Assert.Equal(1, calls);
        Assert.Equal("nested image description", image.AlternativeText);
        Assert.Same(image, inner.Cells[0, 0]!.Content);
        Assert.Same(source, Assert.Single(image.Sources));
        Assert.Same(page, source.Page);
    }

    [Fact]
    public async Task NestedTableCycles_AreRejectedBeforeChatRequests()
    {
        int calls = 0;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                calls++;
                throw new InvalidOperationException("No chat request expected.");
            }
        };
        ExtractionDocumentTableElement first = new(null, 1, 1);
        ExtractionDocumentTableElement second = new(null, 1, 1);
        first.Cells[0, 0] = new(second);
        second.Cells[0, 0] = new(first);
        ExtractionDocument document = new("cycle") { Sections = { new() { Elements = { first } } } };
        ImageAlternativeTextEnricher enricher = new(new(client));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.ProcessAsync(document));

        Assert.Contains("Cyclic table", error.Message);
        Assert.Equal(0, calls);
        Assert.Same(second, first.Cells[0, 0]!.Content);
    }

    [Fact]
    public void ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>("options", () => new ImageAlternativeTextEnricher(null!));
    }

    [Fact]
    public async Task ThrowsOnNullDocument()
    {
        using TestChatClient chatClient = new();

        ImageAlternativeTextEnricher sut = new(new(chatClient));

        await Assert.ThrowsAsync<ArgumentNullException>("document", async () => await sut.ProcessAsync(null!));
    }

    [Fact]
    public async Task CanGenerateImageAltText()
    {
        const string PreExistingAltText = "Pre-existing alt text";

        string[] descriptions = { "First alt text", "Second alt text" };
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                var materializedMessages = messages.ToArray();

                Assert.Equal(2, materializedMessages.Length);
                Assert.Equal(ChatRole.System, materializedMessages[0].Role);
                Assert.Equal(ChatRole.User, materializedMessages[1].Role);
                Assert.Equal(2, materializedMessages[1].Contents.Count);

                Assert.All(materializedMessages[1].Contents, content =>
                {
                    DataContent dataContent = Assert.IsType<DataContent>(content);
                    Assert.Equal("image/png", dataContent.MediaType);
                    Assert.Equal(_imageContent.ToArray(), dataContent.Data.ToArray());
                });

                return Task.FromResult(new ChatResponse(new[]
                {
                    new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new Envelope<string[]> { data = descriptions }))
                }));
            }
        };
        ImageAlternativeTextEnricher sut = new(new(chatClient));

        ExtractionDocumentImageElement documentImage = new()
        {
            AlternativeText = null,
            Content = new DataContent(_imageContent, "image/png")
        };

        ExtractionDocumentImageElement tableCell = new()
        {
            AlternativeText = null,
            Content = new DataContent(_imageContent, "image/png")
        };

        ExtractionDocumentImageElement imageWithAltText = new()
        {
            AlternativeText = PreExistingAltText,
            Content = new DataContent(_imageContent, "image/png")
        };

        ExtractionDocumentImageElement imageWithNoContent = new()
        {
            AlternativeText = null,
            Content = null
        };

        ExtractionDocument document = new("withImage")
        {
            Sections =
            {
                new ExtractionDocumentSection
                {
                    Elements =
                    {
                        documentImage,
                        TestDocumentFactory.CreateTable(new ExtractionDocumentElement[,] { { tableCell } }),
                        imageWithAltText,
                        imageWithNoContent
                    }
                }
            }
        };

        Assert.Same(document, await sut.ProcessAsync(document));

        Assert.Equal(descriptions[0], documentImage.AlternativeText);
        Assert.Equal(descriptions[1], tableCell.AlternativeText);
        Assert.Same(PreExistingAltText, imageWithAltText.AlternativeText);
        Assert.Null(imageWithNoContent.AlternativeText);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 7)]
    [InlineData(15, 3)]
    public async Task SendsOneRequestPerBatchSize(int batchSize, int batchCount)
    {
        int callsCount = 0;
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callsCount++;

                var materializedMessages = messages.ToArray();

                // One system message + one User message with all the contents
                Assert.Equal(2, materializedMessages.Length);
                Assert.Equal(ChatRole.System, materializedMessages[0].Role);
                Assert.Equal(ChatRole.User, materializedMessages[1].Role);
                Assert.Equal(batchSize, materializedMessages[1].Contents.Count);

                Assert.All(materializedMessages[1].Contents, content =>
                {
                    DataContent dataContent = Assert.IsType<DataContent>(content);
                    Assert.Equal("image/png", dataContent.MediaType);
                    Assert.Equal(_imageContent.ToArray(), dataContent.Data.ToArray());
                });

                Envelope<string[]> data = new() { data = Enumerable.Range(0, batchSize).Select(i => i.ToString()).ToArray() };
                return Task.FromResult(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(data)) }));
            }
        };

        ImageAlternativeTextEnricher sut = new(new(chatClient) { BatchSize = batchSize });

        ExtractionDocument document = CreateDocument(batchSize, batchCount, _imageContent);

        await sut.ProcessAsync(document);
        Assert.Equal(batchCount, callsCount);
        Assert.Equal(batchSize * batchCount, document.EnumerateContent().Count());
        Assert.Equal(
            Enumerable.Range(0, batchCount).SelectMany(_ => Enumerable.Range(0, batchSize).Select(i => i.ToString())),
            document.EnumerateContent().OfType<ExtractionDocumentImageElement>().Select(image => image.AlternativeText));
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

        EnricherOptions options = new(chatClient) { LoggerFactory = loggerFactory };
        ImageAlternativeTextEnricher sut = new(options);

        const int BatchCount = 2;
        ExtractionDocument document = CreateDocument(options.BatchSize, BatchCount, _imageContent);
        ExtractionDocument got = await sut.ProcessAsync(document);

        Assert.Same(document, got);
        Assert.All(document.EnumerateContent().OfType<ExtractionDocumentImageElement>(), image => Assert.Null(image.AlternativeText));
        Assert.Equal(BatchCount, collector.Count);
        Assert.All(collector.GetSnapshot(), record =>
        {
            Assert.Equal(LogLevel.Error, record.Level);
            Assert.IsType<ExpectedException>(record.Exception);
        });
    }

    private static ExtractionDocument CreateDocument(int batchSize, int batchCount, ReadOnlyMemory<byte> imageContent)
    {
        ExtractionDocumentSection rootSection = new();
        for (int i = 0; i < batchSize * batchCount; i++)
        {
            ExtractionDocumentImageElement image = new()
            {
                Content = new DataContent(imageContent, "image/png"),
                AlternativeText = null
            };

            rootSection.Elements.Add(image);
        }

        return new("batchTest")
        {
            Sections = { rootSection }
        };
    }
}
