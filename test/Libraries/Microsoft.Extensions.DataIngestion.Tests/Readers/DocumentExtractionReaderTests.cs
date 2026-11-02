// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public class DocumentExtractionReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsExactDocumentAndBorrowsClientAndStream()
    {
        using MemoryStream nativeResult = new(new byte[] { 4, 5, 6 });
        ExtractionDocumentSourcePage page = new("frame", null);
        byte[] providerBytes = [7, 8, 9];
        ExtractionDocumentImageElement image = new(new DataContent((byte[])providerBytes.Clone(), "image/png"), "OCR");
        ExtractionDocument document = new("provider-id", new TextContent("local"))
        {
            SourcePages = { page },
            RawRepresentation = nativeResult,
            Sections = { new() { Elements = { image } } }
        };
        using MemoryStream stream = new(new byte[] { 1, 2, 3 });
        using FakeExtractionClient client = new((input, identifier, mediaType, options, cancellationToken) =>
        {
            if (identifier != "second-request")
            {
                Assert.Same(stream, input);
                Assert.Equal("requested-id", identifier);
            }

            Assert.Equal("application/pdf", mediaType);
            Assert.Null(options);
            return Task.FromResult(document);
        });
        DocumentExtractionReader reader = new(client);

        IngestionDocument result = await reader.ReadAsync(stream, "requested-id", "application/pdf");

        Assert.Same(document, result.ExtractedDocument);
        Assert.Equal("requested-id", result.Identifier);
        Assert.Equal("provider-id", document.Identifier);
        Assert.Same(document.Sections, result.Sections);
        Assert.Same(page, Assert.Single(result.ExtractedDocument.SourcePages));
        Assert.Same(nativeResult, result.ExtractedDocument.RawRepresentation);
        Assert.Equal("local", Assert.IsType<TextContent>(result.ExtractedDocument.Content).Text);
        Assert.True(stream.CanRead);
        providerBytes[0] = 0;
        stream.Dispose();
        Assert.Same(image, Assert.Single(Assert.Single(result.Sections).Elements));
        Assert.Equal(new byte[] { 7, 8, 9 }, Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal("OCR", image.Text);
        Assert.True(nativeResult.CanRead);
        Assert.False(client.Disposed);
        Assert.Equal(1, client.Calls);
        using MemoryStream secondStream = new();
        IngestionDocument second = await reader.ReadAsync(secondStream, "second-request", "application/pdf");
        Assert.NotSame(result, second);
        Assert.Same(document, second.ExtractedDocument);
        Assert.Equal("second-request", second.Identifier);
        Assert.Equal("requested-id", result.Identifier);
        Assert.Equal("provider-id", document.Identifier);
        Assert.Equal(2, client.Calls);
        Assert.True(secondStream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_ClonesConstructionOptionsAndEachCallButBorrowsOpaqueValues()
    {
        object borrowed = new();
        DocumentExtractionOptions options = new() { ExtractorName = "original" };
        options.AdditionalProperties.Add("opaque", borrowed);
        DocumentExtractionOptions? first = null;
        using FakeExtractionClient client = new((input, identifier, mediaType, received, cancellationToken) =>
        {
            Assert.NotNull(received);
            Assert.NotSame(options, received);
            Assert.Equal("original", received.ExtractorName);
            Assert.Same(borrowed, received.AdditionalProperties["opaque"]);
            Assert.False(received.AdditionalProperties.ContainsKey("later"));
            if (first is not null)
            {
                Assert.NotSame(first, received);
            }

            first = received;
            received.ExtractorName = "mutated by client";
            received.AdditionalProperties.Clear();
            return Task.FromResult(new ExtractionDocument(identifier));
        });
        DocumentExtractionReader reader = new(client, options);
        options.ExtractorName = "mutated by caller";
        options.AdditionalProperties.Add("later", 17);
        using MemoryStream stream = new();

        await reader.ReadAsync(stream, "first", "text/plain");
        await reader.ReadAsync(stream, "second", "text/plain");

        Assert.Equal(2, client.Calls);
        Assert.Equal("mutated by caller", options.ExtractorName);
        Assert.Equal(17, options.AdditionalProperties["later"]);
        Assert.True(stream.CanRead);
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task ReadAsync_PreCancellationDoesNotInvokeOrDisposeBorrowedInputs()
    {
        using FakeExtractionClient client = new((input, identifier, mediaType, options, token) => Task.FromResult(new ExtractionDocument(identifier)));
        DocumentExtractionReader reader = new(client);
        using MemoryStream stream = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(stream, "id", "text/plain", cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, client.Calls);
        Assert.True(stream.CanRead);
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task ReadAsync_ForwardsCancellationAndPreservesFailureAndBorrowedInputs()
    {
        using CancellationTokenSource cancellation = new();
        InvalidOperationException expected = new("deterministic extraction failure");
        using FakeExtractionClient client = new((input, identifier, mediaType, options, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            return Task.FromException<ExtractionDocument>(expected);
        });
        DocumentExtractionReader reader = new(client);
        using MemoryStream stream = new();

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(stream, "id", "text/plain", cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(1, client.Calls);
        Assert.True(stream.CanRead);
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task ReadAsync_ValidatesArgumentsBeforeCallingClient()
    {
        Assert.Throws<ArgumentNullException>("client", () => new DocumentExtractionReader(null!));
        using FakeExtractionClient client = new((input, identifier, mediaType, options, token) => Task.FromResult(new ExtractionDocument(identifier)));
        DocumentExtractionReader reader = new(client);
        using MemoryStream stream = new();
        await Assert.ThrowsAsync<ArgumentNullException>("source", () => reader.ReadAsync((Stream)null!, "id", "text/plain"));
        await Assert.ThrowsAsync<ArgumentNullException>("identifier", () => reader.ReadAsync(stream, null!, "text/plain"));
        await Assert.ThrowsAsync<ArgumentException>("identifier", () => reader.ReadAsync(stream, "", "text/plain"));
        await Assert.ThrowsAsync<ArgumentNullException>("mediaType", () => reader.ReadAsync(stream, "id", null!));
        await Assert.ThrowsAsync<ArgumentException>("mediaType", () => reader.ReadAsync(stream, "id", ""));
        Assert.Equal(0, client.Calls);
        Assert.True(stream.CanRead);
    }

    private sealed class FakeExtractionClient(
        Func<Stream, string, string, DocumentExtractionOptions?, CancellationToken, Task<ExtractionDocument>> callback) : IDocumentExtractionClient
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }

        public Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return callback(document, identifier, mediaType, options, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }
}
