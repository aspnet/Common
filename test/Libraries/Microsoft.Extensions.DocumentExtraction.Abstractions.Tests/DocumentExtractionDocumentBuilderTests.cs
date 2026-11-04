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
    public void Client_ExposesAbstractDirectDocumentExchangeWithoutStagingProtocol()
    {
        Type client = typeof(DocumentExtractionClient);
        Assert.True(client.IsPublic);
        Assert.True(client.IsClass);
        Assert.True(client.IsAbstract);
        Assert.False(client.IsSealed);
        Assert.False(client.IsInterface);
        Assert.Contains(typeof(IDisposable), client.GetInterfaces());
        Assert.Single(client.GetInterfaces());
        const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags DeclaredMembers = DeclaredInstance | BindingFlags.Static;
        ConstructorInfo constructor = Assert.Single(client.GetConstructors(DeclaredInstance));
        Assert.True(constructor.IsFamily);
        Assert.Empty(constructor.GetParameters());
        Assert.Empty(client.GetConstructors());
        Assert.Equal(new[] { "Dispose", "Dispose", "ExtractAsync", "GetService" },
            client.GetMethods(DeclaredMembers).Select(static method => method.Name).OrderBy(static name => name));
        Assert.Empty(client.GetFields(DeclaredMembers));
        Assert.Null(client.GetMethod("Finalize", DeclaredInstance));
        Assert.Null(client.Assembly.GetType("Microsoft.Extensions.DocumentExtraction.IDocumentExtractionClient"));
        MethodInfo extract = client.GetMethod("ExtractAsync")!;
        Assert.True(extract.IsPublic);
        Assert.True(extract.IsAbstract);
        Assert.True(extract.IsVirtual);
        Assert.False(extract.IsFinal);
        Assert.False(extract.IsStatic);
        Assert.Equal(typeof(Task<ExtractionDocument>), extract.ReturnType);
        ParameterInfo[] parameters = extract.GetParameters();
        Assert.Equal(new[] { typeof(Stream), typeof(string), typeof(string), typeof(DocumentExtractionOptions), typeof(CancellationToken) },
            parameters.Select(static parameter => parameter.ParameterType));
        Assert.Equal(new[] { "document", "identifier", "mediaType", "options", "cancellationToken" }, parameters.Select(static parameter => parameter.Name));
        Assert.All(parameters.Take(3), static parameter => Assert.False(parameter.IsOptional));
        Assert.True(parameters[3].IsOptional);
        Assert.Null(parameters[3].DefaultValue);
        Assert.True(parameters[4].IsOptional);
        Assert.True(parameters[4].DefaultValue is null || Equals(default(CancellationToken), parameters[4].DefaultValue));
        Assert.Null(client.GetMethod("ExtractPagesAsync"));
        MethodInfo getService = client.GetMethod("GetService")!;
        Assert.True(getService.IsPublic);
        Assert.True(getService.IsVirtual);
        Assert.False(getService.IsFinal);
        Assert.False(getService.IsAbstract);
        Assert.Equal(typeof(object), getService.ReturnType);
        ParameterInfo[] serviceParameters = getService.GetParameters();
        Assert.Equal(new[] { typeof(Type), typeof(object) }, serviceParameters.Select(static parameter => parameter.ParameterType));
        Assert.Equal(new[] { "serviceType", "serviceKey" }, serviceParameters.Select(static parameter => parameter.Name));
        Assert.False(serviceParameters[0].IsOptional);
        Assert.True(serviceParameters[1].IsOptional);
        Assert.Null(serviceParameters[1].DefaultValue);
        MethodInfo dispose = client.GetMethod("Dispose", Type.EmptyTypes)!;
        Assert.True(dispose.IsPublic);
        Assert.False(dispose.IsAbstract);
        Assert.True(!dispose.IsVirtual || dispose.IsFinal);
        Assert.Equal(typeof(void), dispose.ReturnType);
        MethodInfo hook = client.GetMethod("Dispose", DeclaredInstance, null, new[] { typeof(bool) }, null)!;
        Assert.True(hook.IsFamily);
        Assert.True(hook.IsVirtual);
        Assert.False(hook.IsFinal);
        Assert.False(hook.IsAbstract);
        Assert.Equal(typeof(void), hook.ReturnType);
        Assert.Equal("disposing", Assert.Single(hook.GetParameters()).Name);
#if NET8_0_OR_GREATER
        NullabilityInfoContext nullability = new();
        NullabilityInfo returnInfo = nullability.Create(extract.ReturnParameter);
        Assert.Equal(NullabilityState.NotNull, returnInfo.ReadState);
        Assert.Equal(NullabilityState.NotNull, Assert.Single(returnInfo.GenericTypeArguments).ReadState);
        Assert.All(parameters.Take(3), parameter => Assert.Equal(NullabilityState.NotNull, nullability.Create(parameter).ReadState));
        Assert.Equal(NullabilityState.Nullable, nullability.Create(parameters[3]).ReadState);
        Assert.Equal(NullabilityState.NotNull, nullability.Create(serviceParameters[0]).ReadState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(serviceParameters[1]).ReadState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(getService.ReturnParameter).ReadState);
#endif

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
        ExtractionDocumentSourcePage frame = Frame();
        ExtractionDocumentSourceReference reference = new(frame, Polygon());
        ExtractionDocumentSection root = new(new TextContent("supplied section"));
        ExtractionDocumentSection nested = new(new TextContent("supplied nested"));
        ExtractionDocumentTableElement table = new(new TextContent("supplied table"), 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("**cell**"), role: "Vendor.Custom") { HeadingLevel = 10 };
        content.Sources.Add(reference);
        content.Sources.Add(reference);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(content);
        nested.Elements.Add(table);
        root.Elements.Add(nested);
        expected.Sections.Add(root);
        expected.SourcePages.Add(frame);
        expected.SourcePages.Add(new ExtractionDocumentSourcePage("blank", 2));
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
        Assert.Equal("changed", Assert.IsType<TextContent>(content.Content).Text);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("replacement")));
        Assert.Equal("replacement", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        Assert.Equal("supplied table", Assert.IsType<TextContent>(table.Content).Text);
        Assert.Equal("supplied nested", Assert.IsType<TextContent>(nested.Content).Text);
        Assert.Equal("provider root", Assert.IsType<TextContent>(actual.Content).Text);
        Assert.Equal("changed", Assert.IsType<TextContent>(content.Content).Text);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExtractAsync_AbsentAndExplicitOptionsPreserveSpecializedBorrowedNative(int optionKind)
    {
        using MemoryStream input = new([42]);
        using NativeObservation native = new();
        ExtractionDocumentSourcePage frame = Frame();
        ExtractionDocumentPoint[] vertices = Polygon();
        ExtractionDocumentSourceReference reference = new(frame, vertices, native);
        vertices[0] = new(999, 999);
        ExtractionDocument expected = new("native") { RawRepresentation = native };
        ExtractionDocumentSection section = new();
        ExtractionDocumentImageElement image = new(new TextContent("visible"), "plain visible");
        image.Sources.Add(reference);
        section.Elements.Add(image);
        expected.Sections.Add(section);
        expected.SourcePages.Add(frame);
        using DirectAdapter adapter = new((_, _, _, _, _) => Task.FromResult(expected));
        DocumentExtractionOptions? options = optionKind switch { 1 => new(), 2 => new() { ExtractorName = "opaque:selector" }, _ => null };

        ExtractionDocument actual = await adapter.ExtractAsync(input, "native", "application/pdf", options);

        Assert.Same(expected, actual);
        Assert.Same(frame, Assert.Single(actual.SourcePages));
        Assert.Same(reference, Assert.Single(image.Sources));
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Same(native, actual.RawRepresentation);
        Assert.Equal("sdk text", native.Text);
        Assert.Equal(0.875, native.Confidence);
        Assert.Equal(new[] { 3, 8, 13 }, native.WordOffsets);
        Assert.Equal(Polygon(), reference.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), frame.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
        Assert.Equal(7, frame.PageNumber);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_SuccessCleansOwnedProviderStreamButKeepsBorrowedRootAndInput(bool hasSection)
    {
        using MemoryStream input = new([42]);
        using NativeObservation native = new();
        MemoryStream? owned = null;
        using DirectAdapter adapter = new((_, identifier, _, _, _) =>
        {
            using MemoryStream response = new([17, 23]);
            owned = response;
            ExtractionDocument result = new(identifier) { RawRepresentation = native };
            if (hasSection)
            {
                result.Sections.Add(new());
            }

            return Task.FromResult(result);
        });

        ExtractionDocument actual = await adapter.ExtractAsync(input, "success", "application/pdf");

        Assert.NotNull(owned);
        Assert.False(owned.CanRead);
        Assert.Same(native, actual.RawRepresentation);
        Assert.Equal(hasSection ? 1 : 0, actual.Sections.Count);
        Assert.Null(actual.Content);
        Assert.Empty(actual.EnumerateContent());
        adapter.Dispose();
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
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
        DocumentExtractionOptions options = new() { ExtractorName = "model:A" };
        options.AdditionalProperties.Add("setting", borrowed);
        Task<ExtractionDocument> first = adapter.ExtractAsync(inputA, "A", "application/pdf", options, cancellationA.Token);
        options.ExtractorName = "model:B";
        options.AdditionalProperties["setting"] = "B";
        Task<ExtractionDocument> second = adapter.ExtractAsync(inputB, "B", "image/png", options, cancellationB.Token);
        options.AdditionalProperties.Clear();
        Assert.NotSame(options, snapshots[0]);
        Assert.NotSame(snapshots[0], snapshots[1]);
        Assert.Equal("model:A", snapshots[0].ExtractorName);
        Assert.Same(borrowed, snapshots[0].AdditionalProperties["setting"]);
        Assert.Equal("model:B", snapshots[1].ExtractorName);
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

    [Fact]
    public async Task Client_MinimalImplementationReturnsExactDocumentAndInheritsDefaults()
    {
        using MemoryStream input = new([42]);
        using NativeObservation borrowed = new();
        using CancellationTokenSource cancellation = new();
        ExtractionDocument expected = new("minimal", new TextContent("visible")) { RawRepresentation = borrowed };
        DocumentExtractionOptions options = new() { ExtractorName = "provider:model" };
        options.AdditionalProperties.Add("native", borrowed);
        using MinimalClient minimal = new(expected);
        DocumentExtractionClient client = minimal;

        ExtractionDocument actual = await client.ExtractAsync(input, "minimal", "application/pdf", options, cancellation.Token);

        Assert.Same(expected, actual);
        Assert.Same(input, minimal.Input);
        Assert.Equal("minimal", minimal.Identifier);
        Assert.Equal("application/pdf", minimal.MediaType);
        Assert.Same(options, minimal.Options);
        Assert.Equal(cancellation.Token, minimal.Token);
        Assert.Equal(1, minimal.CallCount);
        Assert.Same(minimal, client.GetService(typeof(DocumentExtractionClient)));
        client.Dispose();
        Assert.Equal("visible", Assert.IsType<TextContent>(actual.Content).Text);
        Assert.Same(borrowed, actual.RawRepresentation);
        Assert.Same(borrowed, options.AdditionalProperties["native"]);
        Assert.Equal(0, borrowed.DisposeCount);
        Assert.Equal(0, borrowed.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public void Client_GetServiceReturnsSelfForAssignableUnkeyedTypes()
    {
        using MinimalClient client = new(new("lookup"));
        foreach (Type type in new[] { typeof(MinimalClient), typeof(DocumentExtractionClient), typeof(object), typeof(IDisposable) })
        {
            Assert.Same(client, client.GetService(type));
            Assert.Same(client, client.GetService(type, null));
        }

        Assert.Null(client.GetService(typeof(string)));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void Client_GetServiceRejectsEveryNonNullKey()
    {
        using MinimalClient client = new(new("keyed"));
        foreach (object key in new object[] { string.Empty, "sdk-key", new object(), 0 })
        {
            foreach (Type type in new[] { typeof(MinimalClient), typeof(DocumentExtractionClient), typeof(object), typeof(IDisposable), typeof(string) })
            {
                Assert.Null(client.GetService(type, key));
            }
        }

        Assert.Equal(0, client.CallCount);
        Assert.Same(client, client.GetService(typeof(MinimalClient)));
    }

    [Fact]
    public void Client_GetServiceRejectsNullServiceType()
    {
        using MinimalClient client = new(new("null-type"));
        Assert.Equal("serviceType", Assert.Throws<ArgumentNullException>(() => client.GetService(null!)).ParamName);
        Assert.Equal("serviceType", Assert.Throws<ArgumentNullException>(() => client.GetService(null!, null)).ParamName);
        Assert.Equal("serviceType", Assert.Throws<ArgumentNullException>(() => client.GetService(null!, "sdk-key")).ParamName);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void Client_DisposeDispatchesProtectedHookWithoutGlobalOnceGuard()
    {
        DisposeRecordingClient client = new();
        client.Dispose();
        IDisposable disposable = client;
        disposable.Dispose();

        Assert.Equal(new[] { true, true }, client.Disposals);
        Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
    }

    private sealed class MinimalClient(ExtractionDocument result) : DocumentExtractionClient
    {
        internal int CallCount { get; private set; }
        internal Stream? Input { get; private set; }
        internal string? Identifier { get; private set; }
        internal string? MediaType { get; private set; }
        internal DocumentExtractionOptions? Options { get; private set; }
        internal CancellationToken Token { get; private set; }

        public override Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Input = document;
            Identifier = identifier;
            MediaType = mediaType;
            Options = options;
            Token = cancellationToken;
            return Task.FromResult(result);
        }
    }

    private sealed class DisposeRecordingClient : DocumentExtractionClient
    {
        internal List<bool> Disposals { get; } = [];

        public override Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExtractionDocument(identifier));

        protected override void Dispose(bool disposing)
        {
            Disposals.Add(disposing);
            base.Dispose(disposing);
        }
    }

    private static ExtractionDocumentPoint[] Polygon() => [new(10.25, 20.5), new(40.75, 18.25), new(44.5, 60.125), new(12.875, 58.75), new(8.5, 35.25)];
    private static ExtractionDocumentSourcePage Frame() =>
        new("pdf-frame", 7, new ExtractionDocumentPageDimensions(200, 100), ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);

    // Direct implementations snapshot at their own public boundary; no core finalizer is involved.
    private sealed class DirectAdapter(Func<Stream, string, string, DocumentExtractionOptions?, CancellationToken, Task<ExtractionDocument>> extract)
        : DocumentExtractionClient
    {
        public override Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
            => extract(document, identifier, mediaType, options?.Clone(), cancellationToken);
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
}
