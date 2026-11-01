// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentReferencesTests
{
    [Fact]
    public void ReducedApi_ExposesTypedSourcesWithoutOwnershipContract()
    {
        var assembly = typeof(ExtractionDocument).Assembly;
        foreach (string name in new[] { "DocumentAnnotation", "DocumentRegion", "DocumentConfidence", "DocumentPageReference", "DocumentEvidenceReference" })
        {
            Assert.Null(assembly.GetType("Microsoft.Extensions.Documents." + name));
        }

        foreach (Type type in new[] { typeof(ExtractionDocument), typeof(ExtractionDocumentElement), typeof(ExtractionDocumentTableCell) })
        {
            foreach (string name in new[] { "Annotations", "Evidence", "PageReferences", "HasMetadata", "Address", "LocalId", "Version", "IsDisposed" })
            {
                Assert.Null(type.GetProperty(name));
            }
        }

        Assert.Null(typeof(ExtractionDocument).GetProperty("Sources"));
        Assert.Null(typeof(ExtractionDocument).GetProperty("Artifacts"));
        Assert.Null(typeof(ExtractionDocumentArtifact).GetProperty("IsDisposed"));
        Assert.Null(typeof(ExtractionDocumentArtifact).GetProperty("IsDocumentOwned"));
        Assert.Null(typeof(ExtractionDocumentSourceOutput).GetProperty("NativeStatus"));
        foreach (Type type in new[] { typeof(ExtractionDocumentElement), typeof(ExtractionDocumentTableCell) })
        {
            var property = type.GetProperty("Sources")!;
            Assert.Equal(typeof(System.Collections.Generic.List<ExtractionDocumentSourceReference>), property.PropertyType);
            Assert.False(property.CanWrite);
        }

        Assert.False(typeof(ExtractionDocumentSourceReference).GetProperty("Page")!.CanWrite);
        Assert.False(typeof(ExtractionDocumentSourceReference).GetProperty("Polygon")!.CanWrite);
        Assert.False(typeof(ExtractionDocument).GetProperty("SourcePages")!.CanWrite);
        Assert.False(typeof(ExtractionDocument).GetProperty("SourceOutputs")!.CanWrite);
        foreach (string name in new[] { "SetContent", "GetCoveringCell", "IsCovered" })
        {
            Assert.Null(typeof(ExtractionDocumentTableCellCollection).GetMethod(name));
        }

        foreach (string name in new[] { "GetCoveringCell", "IsCovered" })
        {
            var method = typeof(ExtractionDocumentTableCellCollection).GetMethod(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(method.IsAssembly);
        }

        Assert.Null(typeof(ExtractionDocumentTableCell).GetProperty("Kind"));
        Assert.Null(typeof(ExtractionDocumentTableCell).GetProperty("CellKind"));
        Assert.Equal(typeof(string), typeof(ExtractionDocumentTableCell).GetProperty("Role")!.PropertyType);
        Assert.False(typeof(ExtractionDocumentTableCell).GetProperty("Role")!.CanWrite);
        var indexer = typeof(ExtractionDocumentTableCellCollection).GetProperty("Item")!;
        Assert.Equal(typeof(ExtractionDocumentTableCell), indexer.PropertyType);
        Assert.True(indexer.CanRead);
        Assert.True(indexer.CanWrite);
        Assert.All(typeof(ExtractionDocumentTableCell).GetConstructors(), constructor =>
        {
            var parameters = constructor.GetParameters();
            var role = parameters[parameters.Length - 1];
            Assert.Equal("role", role.Name);
            Assert.Equal(typeof(string), role.ParameterType);
            Assert.True(role.IsOptional);
            Assert.Null(role.DefaultValue);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceReference_NullOrEmptyPolygonIsPageOnly(bool empty)
    {
        ExtractionDocumentSourcePage page = new("unknown", 3);
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSourceReference reference = new(page, empty ? Array.Empty<ExtractionDocumentPoint>() : null, native);
        ExtractionDocumentSourceReference omitted = new(page);
        Assert.Same(page, reference.Page);
        Assert.Empty(reference.Polygon);
        Assert.Empty(omitted.Polygon);
        Assert.Null(omitted.GetRawRepresentation());
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Null(page.Dimensions);
        Assert.Null(page.CoordinateUnit);
        Assert.Null(page.CoordinateOrigin);
        Assert.Equal(0, native.DisposeCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceReference_RejectsShortPolygons(int count)
    {
        Assert.Throws<ArgumentException>("polygon", () => new ExtractionDocumentSourceReference(new("page", 1), new ExtractionDocumentPoint[count]));
    }

    [Fact]
    public void SourceReference_MinimumTriangleSnapshotsVerticesAndBorrowsNative()
    {
        ExtractionDocumentPoint[] points = [new(1, 2), new(3, 4), new(5, 6)];
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSourcePage page = new("triangle", 1);
        ExtractionDocumentSourceReference reference = new(page, points, native);
        points[2] = new(9, 10);
        Assert.Equal(new[] { new ExtractionDocumentPoint(1, 2), new ExtractionDocumentPoint(3, 4), new ExtractionDocumentPoint(5, 6) }, reference.Polygon);
        Assert.Same(page, reference.Page);
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Equal(0, native.DisposeCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SourceIdentifiers_NullOrEmptyValue_Throws(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ExtractionDocumentSourcePage(value!, 1));
        Assert.ThrowsAny<ArgumentException>(() => new ExtractionDocumentSourceOutput(value!));
    }

    [Fact]
    public void DocumentArtifact_PreservesMediaTypeContentAndSource()
    {
        using MemoryStream content = new(new byte[] { 1, 2, 3 });
        Uri source = new("urn:document-artifact:opaque");

        ExtractionDocumentArtifact artifact = new("application/octet-stream", content.ToArray(), source);

        Assert.Equal("application/octet-stream", artifact.MediaType);
        Assert.Equal(content.ToArray(), artifact.Content.ToArray());
        Assert.Same(source, artifact.Source);
        Assert.Equal(3, artifact.Content.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DocumentArtifact_NullOrEmptyMediaType_Throws(string? mediaType)
    {
        using MemoryStream content = new();

        Assert.ThrowsAny<ArgumentException>(() => new ExtractionDocumentArtifact(mediaType!, content.ToArray()));
    }

    [Fact]
    public void Geometry_PreservesOrderedFractionalVerticesAndFrameIdentity()
    {
        ExtractionDocument document = new("geometry");
        ExtractionDocumentPoint[] supplied = DocumentAnnotationTestData.Polygon();
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage();
        ExtractionDocumentSourceReference region = new(page, supplied);
        supplied[0] = new ExtractionDocumentPoint(999, 999);
        ExtractionDocumentTextElement text = new(new TextContent("visible"));
        text.Sources.Add(region);
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(text);
        document.Sections.Add(section);
        document.AddSourcePage(page);

        Assert.Equal(DocumentAnnotationTestData.Polygon(), region.Polygon);
        Assert.Same(page, region.Page);
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal("urn:frame:pdf-A", page.Identifier);
        Assert.Equal(7, page.PageNumber);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<ExtractionDocumentPoint>)region.Polygon)[0] = default);
    }

    [Fact]
    public void Geometry_KnownPdfAndRasterFixturesProduceDistinctExpectedOverlayVertices()
    {
        ExtractionDocumentSourceReference pdf = new(DocumentAnnotationTestData.PdfPage(), DocumentAnnotationTestData.Polygon());
        ExtractionDocumentPoint[] expected =
        [
            new(20.5, 159), new(81.5, 163.5), new(89, 79.75),
            new(25.75, 82.5), new(17, 129.5)
        ];
        ExtractionDocumentSourcePage rasterPage = new("urn:frame:raster-A", 7, new ExtractionDocumentPageDimensions(400, 200),
            ExtractionDocumentCoordinateUnit.Pixel, ExtractionDocumentCoordinateOrigin.TopLeft);
        ExtractionDocumentSourceReference raster = new(rasterPage, expected);

        Assert.Equal(expected, ProjectKnownOverlay(pdf));
        Assert.Equal(expected, ProjectKnownOverlay(raster));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), pdf.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(400, 200), raster.Page.Dimensions);
        Assert.NotEqual(pdf.Polygon[0], raster.Polygon[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Geometry_UnknownUnitOrOriginDoesNotGuessConversion(bool unknownUnit)
    {
        ExtractionDocumentSourcePage page = new("partial", 7, new ExtractionDocumentPageDimensions(200, 100),
            unknownUnit ? null : ExtractionDocumentCoordinateUnit.Point,
            unknownUnit ? ExtractionDocumentCoordinateOrigin.BottomLeft : null);
        ExtractionDocumentSourceReference region = new(page, DocumentAnnotationTestData.Polygon());

        Assert.Null(ProjectKnownOverlay(region));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), region.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), page.Dimensions);
        Assert.Equal(unknownUnit ? null : ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
        Assert.Equal(unknownUnit ? ExtractionDocumentCoordinateOrigin.BottomLeft : null, page.CoordinateOrigin);
    }

    [Fact]
    public void Geometry_UnknownDimensionsDoNotInferSizeFromKnownFrameOrPolygon()
    {
        ExtractionDocumentSourcePage page = new("no-size", 7, coordinateUnit: ExtractionDocumentCoordinateUnit.Point,
            coordinateOrigin: ExtractionDocumentCoordinateOrigin.BottomLeft);
        ExtractionDocumentSourceReference reference = new(page, DocumentAnnotationTestData.Polygon());
        Assert.Null(page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
        Assert.Equal(DocumentAnnotationTestData.Polygon(), reference.Polygon);
        Assert.Null(ProjectKnownOverlay(reference));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Geometry_RejectsNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>("x", () => new ExtractionDocumentPoint(value, 2));
        Assert.Throws<ArgumentOutOfRangeException>("y", () => new ExtractionDocumentPoint(2, value));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ExtractionDocumentPageDimensions(value, 100));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ExtractionDocumentPageDimensions(200, value));
        ExtractionDocumentSourceReference valid = new(DocumentAnnotationTestData.PdfPage(), DocumentAnnotationTestData.Polygon());
        Assert.Equal(5, valid.Polygon.Count);
        Assert.Equal(60.125, valid.Polygon[2].Y);
    }

    [Fact]
    public void SourceCarriers_RejectInvalidFramesAndReferences()
    {
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage();
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ExtractionDocumentPageDimensions(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ExtractionDocumentPageDimensions(200, -1));
        Assert.Throws<ArgumentOutOfRangeException>("pageNumber", () => new ExtractionDocumentSourcePage("frame", 0));
        Assert.Throws<ArgumentOutOfRangeException>("dimensions", () => new ExtractionDocumentSourcePage("frame", 1, default(ExtractionDocumentPageDimensions)));
        Assert.Throws<ArgumentOutOfRangeException>("coordinateUnit", () => new ExtractionDocumentSourcePage("frame", 1, coordinateUnit: (ExtractionDocumentCoordinateUnit)99));
        Assert.Throws<ArgumentOutOfRangeException>("coordinateOrigin", () => new ExtractionDocumentSourcePage("frame", 1, coordinateOrigin: (ExtractionDocumentCoordinateOrigin)99));
        Assert.Throws<ArgumentNullException>("page", () => new ExtractionDocumentSourceReference(null!));
        Assert.Throws<ArgumentException>("polygon", () => new ExtractionDocumentSourceReference(page, new[] { new ExtractionDocumentPoint(1, 2), new ExtractionDocumentPoint(3, 4) }));
        ExtractionDocumentTextElement text = new(new TextContent("visible"));
        ExtractionDocumentTableCell cell = new();
        text.Sources.Add(null!);
        cell.Sources.Add(null!);
        Assert.Null(Assert.Single(text.Sources));
        Assert.Null(Assert.Single(cell.Sources));
    }

    [Fact]
    public void RawOutput_PreservesOriginalBytesAndBorrowedNativeIdentity()
    {
        ExtractionDocument document = new("raw");
        using DocumentAnnotationTestData.BorrowedNative native = new();
        using DocumentAnnotationTestData.CountingStream stream = new(DocumentAnnotationTestData.OriginalBytes());
        ExtractionDocumentArtifact artifact = new("application/json", stream.ToArray());
        ExtractionDocumentSourceOutput output = new("operation-A", native, artifact, providerName: "synthetic", modelId: "model:v1");
        document.AddSourceOutput(output);

        Assert.Equal(ExtractionDocumentRawContentStatus.Retained, output.ContentStatus);
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Same(artifact, output.GetRawContent());
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), stream.ToArray());
        Assert.Equal("synthetic", output.ProviderName);
        Assert.Equal("model:v1", output.ModelId);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, stream.DisposeCount);
    }

    [Fact]
    public void RawOutput_DetachedContentSurvivesTransportAndTemporaryStreamDisposal()
    {
        ExtractionDocument document = new("detached");
        DocumentAnnotationTestData.CountingStream transport = new(DocumentAnnotationTestData.OriginalBytes());
        using DocumentAnnotationTestData.CountingStream detached = new(transport.ToArray());
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentArtifact artifact = new("application/json", detached.ToArray());
        ExtractionDocumentSourceOutput output = new("operation", native, artifact);
        transport.Dispose();
        detached.Dispose();
        document.AddSourcePage(DocumentAnnotationTestData.PdfPage(output));

        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), output.GetRawContent()!.Content.ToArray());
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(1, detached.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(1, detached.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), output.GetRawContent()!.Content.ToArray());
    }

    [Fact]
    public void RawOutput_ContentCaptureDisabledUnavailableAndPartialRetentionRemainDistinct()
    {
        ExtractionDocumentSourceOutput unavailable = new("unavailable");
        ExtractionDocumentSourceOutput disabled = new("disabled", contentCaptureDisabled: true);
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSourceOutput nativeOnly = new("native-only", native);
        ExtractionDocumentArtifact content = new("application/json", DocumentAnnotationTestData.OriginalBytes());
        ExtractionDocumentSourceOutput contentOnly = new("content-only", rawContent: content);

        Assert.Equal(ExtractionDocumentRawContentStatus.Unavailable, unavailable.ContentStatus);
        Assert.Null(unavailable.GetRawRepresentation());
        Assert.Null(unavailable.GetRawContent());
        Assert.Equal(ExtractionDocumentRawContentStatus.Disabled, disabled.ContentStatus);
        Assert.Null(disabled.GetRawRepresentation());
        Assert.Null(disabled.GetRawContent());
        Assert.Same(native, nativeOnly.GetRawRepresentation());
        Assert.Equal(ExtractionDocumentRawContentStatus.Unavailable, nativeOnly.ContentStatus);
        Assert.Null(contentOnly.GetRawRepresentation());
        Assert.Same(content, contentOnly.GetRawContent());
        Assert.Equal(ExtractionDocumentRawContentStatus.Retained, contentOnly.ContentStatus);
        Assert.Throws<ArgumentException>("contentCaptureDisabled", () => new ExtractionDocumentSourceOutput("bad", rawContent: content, contentCaptureDisabled: true));
        Assert.Same(content, new ExtractionDocumentSourceOutput("shared", rawContent: content).GetRawContent());
        Assert.Equal(0, native.DisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RawOutput_NativeAvailability_IsIndependentOfContentCapture(bool contentCaptureDisabled, bool hasNative)
    {
        using DocumentAnnotationTestData.BorrowedNative native = new();
        object? representation = hasNative ? native : null;
        ExtractionDocumentSourceOutput output = new("operation", representation, contentCaptureDisabled: contentCaptureDisabled);

        Assert.Same(representation, output.GetRawRepresentation());
        Assert.Equal(contentCaptureDisabled ? ExtractionDocumentRawContentStatus.Disabled : ExtractionDocumentRawContentStatus.Unavailable, output.ContentStatus);
        Assert.Null(output.GetRawContent());
        Assert.Equal(0, native.DisposeCount);
    }

    [Fact]
    public void RawOutput_ContentCaptureDisabled_PreservesBorrowedNative()
    {
        ExtractionDocument document = new("native-only");
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSourceOutput output = new("operation", native, contentCaptureDisabled: true);
        document.AddSourcePage(DocumentAnnotationTestData.PdfPage(output));

        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Equal(ExtractionDocumentRawContentStatus.Disabled, output.ContentStatus);
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Equal(0, native.DisposeCount);
    }

    [Fact]
    public void RawOutput_ContentCaptureDisabled_RejectsContentWithoutDisposingBorrowedResources()
    {
        using DocumentAnnotationTestData.CountingStream stream = new(DocumentAnnotationTestData.OriginalBytes());
        ExtractionDocumentArtifact content = new("application/json", stream.ToArray());
        using DocumentAnnotationTestData.BorrowedNative native = new();

        Assert.Throws<ArgumentException>("contentCaptureDisabled",
            () => new ExtractionDocumentSourceOutput("operation", native, content, contentCaptureDisabled: true));
        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), content.Content.ToArray());
    }

    // Consumer-owned oracle for these two known fixtures only; not a production conversion API.
    private static ExtractionDocumentPoint[]? ProjectKnownOverlay(ExtractionDocumentSourceReference region)
    {
        if (region.Page.Dimensions == new ExtractionDocumentPageDimensions(200, 100) &&
            region.Page.CoordinateUnit == ExtractionDocumentCoordinateUnit.Point &&
            region.Page.CoordinateOrigin == ExtractionDocumentCoordinateOrigin.BottomLeft)
        {
            ExtractionDocumentPoint[] result = new ExtractionDocumentPoint[region.Polygon.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new ExtractionDocumentPoint(2 * region.Polygon[i].X, 2 * (100 - region.Polygon[i].Y));
            }

            return result;
        }

        return region.Page.Dimensions == new ExtractionDocumentPageDimensions(400, 200) &&
            region.Page.CoordinateUnit == ExtractionDocumentCoordinateUnit.Pixel &&
            region.Page.CoordinateOrigin == ExtractionDocumentCoordinateOrigin.TopLeft
                ? System.Linq.Enumerable.ToArray(region.Polygon)
                : null;
    }
}
