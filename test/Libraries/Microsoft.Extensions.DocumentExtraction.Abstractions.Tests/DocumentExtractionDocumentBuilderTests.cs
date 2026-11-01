// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction.Tests;

// The legacy filename is retained; there is deliberately no extraction document builder.
public sealed class DocumentExtractionDocumentBuilderTests
{
    [Fact]
    public void Interface_ExposesDirectDocumentExchangeWithoutStagingProtocol()
    {
        Type client = typeof(IDocumentExtractionClient);
        Assert.Contains(typeof(IDisposable), client.GetInterfaces());
        Assert.Equal(new[] { "ExtractAsync", "GetService" }, client.GetMethods().Select(static method => method.Name).OrderBy(static name => name));
        MethodInfo extract = client.GetMethod("ExtractAsync")!;
        Assert.Equal(typeof(Task<ExtractionDocument>), extract.ReturnType);
        ParameterInfo[] parameters = extract.GetParameters();
        Assert.Equal(new[] { typeof(Stream), typeof(string), typeof(string), typeof(DocumentExtractionOptions), typeof(CancellationToken) },
            parameters.Select(static parameter => parameter.ParameterType));
        Assert.Equal(new[] { "document", "identifier", "mediaType", "options", "cancellationToken" }, parameters.Select(static parameter => parameter.Name));
        Assert.All(parameters.Take(3), static parameter => Assert.False(parameter.IsOptional));
        Assert.True(parameters[3].IsOptional);
        Assert.Null(parameters[3].DefaultValue);
        Assert.True(parameters[4].IsOptional);
        Assert.Null(client.GetMethod("ExtractPagesAsync"));
        Assert.Equal(typeof(object), client.GetMethod("GetService")!.ReturnType);

        foreach (string name in new[]
        {
            "DocumentExtractionPage", "DocumentExtractionObservation", "DocumentExtractionText",
            "DocumentExtractionImage", "DocumentExtractionTable", "DocumentExtractionTableCell",
            "DocumentExtractionExtension", "DocumentExtractionResult", "DocumentExtractionDocumentBuilder",
            "DocumentExtractionClientExtensions", "DocumentExtractionPageResultExtensions"
        })
        {
            Assert.Null(client.Assembly.GetType("Microsoft.Extensions.DocumentExtraction." + name));
        }
    }

    [Fact]
    public async Task ExtractAsync_ReturnsCanonicalDocumentWithIndependentNestedPayloads()
    {
        using MemoryStream input = new([42]);
        ExtractionDocument expected = new("canonical", new TextContent("provider root"));
        ExtractionDocumentSourceOutput output = new("operation", providerName: "synthetic", modelId: "actual:model");
        ExtractionDocumentSourcePage frame = Frame(output);
        ExtractionDocumentSourceReference reference = new(frame, Polygon());
        ExtractionDocumentSection root = new(new TextContent("supplied section"));
        ExtractionDocumentSection nested = new(new TextContent("supplied nested"));
        ExtractionDocumentTableElement table = new(new TextContent("supplied table"), 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("**cell**"), "plain cell", "Vendor.Custom") { HeadingLevel = 10 };
        content.Sources.Add(reference);
        content.Sources.Add(reference);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(content);
        nested.Elements.Add(table);
        root.Elements.Add(nested);
        expected.Sections.Add(root);
        expected.AddSourcePage(frame);
        expected.AddSourcePage(new ExtractionDocumentSourcePage("blank", 2));
        using DirectAdapter adapter = new((_, _, _, _, _) => Task.FromResult(expected));

        ExtractionDocument actual = await adapter.ExtractAsync(input, "canonical", "application/pdf");

        Assert.Same(expected, actual);
        Assert.Equal("canonical", actual.Identifier);
        Assert.Same(root, Assert.Single(actual.Sections));
        Assert.Same(nested, Assert.Single(root.Elements));
        Assert.Same(table, Assert.Single(nested.Elements));
        Assert.Equal(new[] { reference, reference }, content.Sources);
        Assert.Equal(new[] { frame.Identifier, "blank" }, actual.SourcePages.Select(static page => page.Identifier));
        Assert.Equal(new int?[] { 7, 2 }, actual.SourcePages.Select(static page => page.PageNumber));
        Assert.Same(frame, actual.SourcePages[0]);
        Assert.Same(output, Assert.Single(actual.SourceOutputs));
        Assert.Same(table, Assert.Single(actual.EnumerateContent()));
        Assert.Same(content, table.Cells[0, 0]!.Content);
        Assert.Equal("**cell**", Assert.IsType<TextContent>(content.Content).Text);
        Assert.Equal("provider root", Assert.IsType<TextContent>(actual.Content).Text);
        Assert.Equal("supplied section", Assert.IsType<TextContent>(root.Content).Text);
        Assert.Equal("supplied nested", Assert.IsType<TextContent>(nested.Content).Text);
        Assert.Equal("supplied table", Assert.IsType<TextContent>(table.Content).Text);
        Assert.Equal("Vendor.Custom", content.Role);
        Assert.Equal(10, content.HeadingLevel);
        Assert.Empty(actual.Metadata);
        content.Content = new TextContent("changed");
        Assert.Equal("changed", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        Assert.Equal("plain cell", content.Text);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("replacement"), "replacement plain"));
        Assert.Equal("replacement", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        Assert.Equal("replacement plain", table.Cells[0, 0]!.Content!.Text);
        Assert.Equal("supplied table", Assert.IsType<TextContent>(table.Content).Text);
        Assert.Equal("supplied nested", Assert.IsType<TextContent>(nested.Content).Text);
        Assert.Equal("provider root", Assert.IsType<TextContent>(actual.Content).Text);
        Assert.Equal("plain cell", content.Text);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_OriginalBytesSurviveProviderResourceCleanup()
    {
        using MemoryStream input = new([42]);
        using NativeObservation native = new();
        byte[] bytes = OriginalBytes();
        using CountingStream stream = new(bytes);
        ExtractionDocumentArtifact artifact = new("application/json", stream.ToArray());
        ExtractionDocumentSourceOutput output = new("operation", native, artifact);
        ExtractionDocument expected = new("body-owner");
        expected.AddSourceOutput(output);
        expected.AddSourcePage(Frame(output));
        using DirectAdapter adapter = new((_, _, _, _, _) => Task.FromResult(expected));
        ExtractionDocument actual = await adapter.ExtractAsync(input, "body-owner", "application/pdf",
            new DocumentExtractionOptions { CaptureRawContent = true });

        Assert.Same(expected, actual);
        Assert.Same(output, Assert.Single(actual.SourceOutputs));
        Assert.Same(artifact, output.GetRawContent());
        Assert.Equal(ExtractionDocumentRawContentStatus.Retained, output.ContentStatus);
        Assert.Equal(bytes, stream.ToArray());
        Assert.Same(native, output.GetRawRepresentation());
        adapter.Dispose();
        Assert.Equal(0, stream.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(32, stream.ReadByte());
        stream.Dispose();
        Assert.Equal(1, stream.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Equal(bytes, output.GetRawContent()!.Content.ToArray());
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExtractAsync_DefaultAndDisabledCapturePreserveSpecializedBorrowedNative(int optionKind)
    {
        using MemoryStream input = new([42]);
        using NativeObservation native = new();
        ExtractionDocumentSourceOutput output = new("operation", native, contentCaptureDisabled: true, modelId: "actual:model");
        ExtractionDocumentSourcePage frame = Frame(output);
        ExtractionDocumentPoint[] vertices = Polygon();
        ExtractionDocumentSourceReference reference = new(frame, vertices, native);
        vertices[0] = new(999, 999);
        ExtractionDocument expected = new("native");
        ExtractionDocumentSection section = new();
        ExtractionDocumentImageElement image = new(new TextContent("visible"), "plain visible");
        image.Sources.Add(reference);
        section.Elements.Add(image);
        expected.Sections.Add(section);
        expected.AddSourcePage(frame);
        using DirectAdapter adapter = new((_, _, _, _, _) => Task.FromResult(expected));
        DocumentExtractionOptions? options = optionKind switch { 1 => new(), 2 => new() { CaptureRawContent = false }, _ => null };

        ExtractionDocument actual = await adapter.ExtractAsync(input, "native", "application/pdf", options);

        Assert.Same(expected, actual);
        Assert.Same(output, Assert.Single(actual.SourceOutputs));
        Assert.Same(frame, Assert.Single(actual.SourcePages));
        Assert.Same(reference, Assert.Single(image.Sources));
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Equal("sdk text", native.Text);
        Assert.Equal(0.875, native.Confidence);
        Assert.Equal(new[] { 3, 8, 13 }, native.WordOffsets);
        Assert.Equal(Polygon(), reference.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), frame.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
        Assert.Equal(7, frame.PageNumber);
        Assert.Equal("actual:model", output.ModelId);
        Assert.Null(output.GetRawContent());
        Assert.Equal(ExtractionDocumentRawContentStatus.Disabled, output.ContentStatus);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_RequestedCaptureWithoutOriginalBytesRemainsUnavailable()
    {
        using MemoryStream input = new([42]);
        using NativeObservation native = new();
        ExtractionDocument expected = new("unavailable");
        ExtractionDocumentSourceOutput output = new("operation", native);
        expected.AddSourceOutput(output);
        using DirectAdapter adapter = new((_, _, _, options, _) =>
        {
            Assert.True(options!.CaptureRawContent);
            return Task.FromResult(expected);
        });

        ExtractionDocument actual = await adapter.ExtractAsync(input, "unavailable", "application/pdf",
            new DocumentExtractionOptions { CaptureRawContent = true });

        Assert.Same(expected, actual);
        Assert.Same(output, Assert.Single(actual.SourceOutputs));
        Assert.Equal(ExtractionDocumentRawContentStatus.Unavailable, output.ContentStatus);
        Assert.Null(output.GetRawContent());
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task DirectAdapter_SnapshotsOptionsAndIsolatesConcurrentCalls()
    {
        using MemoryStream inputA = new([42]);
        using MemoryStream inputB = new([43]);
        using NativeObservation borrowed = new();
        using CancellationTokenSource cancellationA = new();
        using CancellationTokenSource cancellationB = new();
        ExtractionDocument resultA = new("A");
        ExtractionDocument resultB = new("B");
        TaskCompletionSource<ExtractionDocument> gateA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ExtractionDocument> gateB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<DocumentExtractionOptions> snapshots = [];
        List<CancellationToken> tokens = [];
        using DirectAdapter adapter = new((_, identifier, _, options, token) =>
        {
            snapshots.Add(options!);
            tokens.Add(token);
            return identifier == "A" ? gateA.Task : gateB.Task;
        });
        DocumentExtractionOptions options = new() { ExtractorName = "model:A", CaptureRawContent = true };
        options.AdditionalProperties.Add("setting", borrowed);
        Task<ExtractionDocument> first = adapter.ExtractAsync(inputA, "A", "application/pdf", options, cancellationA.Token);
        options.ExtractorName = "model:B";
        options.CaptureRawContent = false;
        options.AdditionalProperties["setting"] = "B";
        Task<ExtractionDocument> second = adapter.ExtractAsync(inputB, "B", "image/png", options, cancellationB.Token);
        options.AdditionalProperties.Clear();
        Assert.NotSame(options, snapshots[0]);
        Assert.NotSame(snapshots[0], snapshots[1]);
        Assert.Equal("model:A", snapshots[0].ExtractorName);
        Assert.True(snapshots[0].CaptureRawContent);
        Assert.Same(borrowed, snapshots[0].AdditionalProperties["setting"]);
        Assert.Equal("model:B", snapshots[1].ExtractorName);
        Assert.False(snapshots[1].CaptureRawContent);
        Assert.Equal("B", snapshots[1].AdditionalProperties["setting"]);
        snapshots[0].AdditionalProperties.Clear();
        Assert.Equal("B", snapshots[1].AdditionalProperties["setting"]);
        Assert.Empty(options.AdditionalProperties);
        Assert.Equal(new[] { cancellationA.Token, cancellationB.Token }, tokens);
        gateB.SetResult(resultB);
        Assert.Same(resultB, await second);
        Assert.False(first.IsCompleted);
        gateA.SetResult(resultA);
        Assert.Same(resultA, await first);
        Assert.Equal(0, borrowed.DisposeCount);
        Assert.Equal(42, inputA.ReadByte());
        Assert.Equal(43, inputB.ReadByte());
    }

    private static ExtractionDocumentPoint[] Polygon() => [new(10.25, 20.5), new(40.75, 18.25), new(44.5, 60.125), new(12.875, 58.75), new(8.5, 35.25)];
    private static ExtractionDocumentSourcePage Frame(ExtractionDocumentSourceOutput output) =>
        new("pdf-frame", 7, new ExtractionDocumentPageDimensions(200, 100), ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft, output);
    private static byte[] OriginalBytes() => System.Text.Encoding.UTF8.GetBytes(
        " {\r\n \"unknown\" : { \"z\" : [3, 1] }, \"pages\" : [7,2], \"marker\":\"RAW_BODY_ONLY_91\" \r\n}\n");

    // Direct implementations snapshot at their own public boundary; no core finalizer is involved.
    private sealed class DirectAdapter(Func<Stream, string, string, DocumentExtractionOptions?, CancellationToken, Task<ExtractionDocument>> extract)
        : IDocumentExtractionClient
    {
        public Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
            => extract(document, identifier, mediaType, options?.Clone(), cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose()
        {
        }
    }

    private sealed class NativeObservation : IDisposable
    {
        internal string Text { get; } = "sdk text";
        internal double Confidence { get; } = 0.875;
        internal int[] WordOffsets { get; } = [3, 8, 13];
        internal int DisposeCount { get; private set; }
        internal int ToStringCount { get; private set; }
        public void Dispose() => DisposeCount++;
        public override string ToString()
        {
            ToStringCount++;
            return "NATIVE_ONLY_92";
        }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
