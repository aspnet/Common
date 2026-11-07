// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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
        foreach (Type type in new[] { typeof(ExtractionDocumentElement), typeof(ExtractionDocumentTableCell) })
        {
            var property = type.GetProperty("Sources")!;
            Assert.Equal(typeof(System.Collections.Generic.List<ExtractionDocumentSourceReference>), property.PropertyType);
            Assert.False(property.CanWrite);
        }

        Assert.False(typeof(ExtractionDocumentSourceReference).GetProperty("Page")!.CanWrite);
        Assert.False(typeof(ExtractionDocumentSourceReference).GetProperty("Polygon")!.CanWrite);
        Assert.False(typeof(ExtractionDocument).GetProperty("SourcePages")!.CanWrite);
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

    [Fact]
    public void SourceReference_IteratorPolygonIsEnumeratedOnceAndSnapshotsMutableList()
    {
        List<ExtractionDocumentPoint> points = [new(1.25, 2.5), new(3.75, 4.5), new(5.25, 6.5)];
        ExtractionDocumentPoint[] expected = points.ToArray();
        int enumerations = 0;
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage();
        using DocumentAnnotationTestData.BorrowedNative native = new();

        IEnumerable<ExtractionDocumentPoint> EnumerateOnce()
        {
            if (++enumerations != 1)
            {
                throw new InvalidOperationException("The polygon must only be enumerated once.");
            }

            foreach (ExtractionDocumentPoint point in points)
            {
                yield return point;
            }
        }

        ExtractionDocumentSourceReference reference = new(page, EnumerateOnce(), native);
        Assert.Equal(1, enumerations);
        points[0] = new(99, 100);
        points.RemoveAt(2);
        points.Add(new(101, 102));
        points.Add(new(103, 104));

        Assert.Equal(expected, reference.Polygon);
        Assert.Equal(expected, reference.Polygon.ToArray());
        Assert.Equal(1, enumerations);
        Assert.Same(page, reference.Page);
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Equal(0, native.DisposeCount);
        IList<ExtractionDocumentPoint> polygon = Assert.IsAssignableFrom<IList<ExtractionDocumentPoint>>(reference.Polygon);
        Assert.True(polygon.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => polygon[0] = default);
        Assert.Throws<NotSupportedException>(() => polygon.Add(new(7, 8)));
        Assert.Throws<NotSupportedException>(() => polygon.Clear());
        Assert.Equal(expected, reference.Polygon);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceReference_IteratorPolygonPreservesEmptyAndShortValidation(int count)
    {
        int enumerations = 0;
        IEnumerable<ExtractionDocumentPoint> EnumerateOnce()
        {
            if (++enumerations != 1)
            {
                throw new InvalidOperationException("The polygon must only be enumerated once.");
            }

            for (int i = 0; i < count; i++)
            {
                yield return new(i, i + 1);
            }
        }

        ExtractionDocumentSourcePage page = new("iterator", 1);
        if (count == 0)
        {
            ExtractionDocumentSourceReference reference = new(page, EnumerateOnce());
            Assert.Empty(reference.Polygon);
            Assert.Same(page, reference.Page);
            Assert.Null(reference.GetRawRepresentation());
        }
        else
        {
            Assert.Throws<ArgumentException>("polygon", () => new ExtractionDocumentSourceReference(page, EnumerateOnce()));
        }

        Assert.Equal(1, enumerations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SourceIdentifiers_NullOrEmptyValue_Throws(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ExtractionDocumentSourcePage(value!, 1));
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
        document.SourcePages.Add(page);

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
        Assert.Throws<ArgumentOutOfRangeException>("width", () => new ExtractionDocumentPageDimensions(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => new ExtractionDocumentPageDimensions(0, value));
        ExtractionDocumentSourceReference valid = new(DocumentAnnotationTestData.PdfPage(), DocumentAnnotationTestData.Polygon());
        Assert.Equal(5, valid.Polygon.Count);
        Assert.Equal(60.125, valid.Polygon[2].Y);
    }

    [Fact]
    public void Dimensions_ExplicitEmptyEqualsDefaultWithoutTreatingPositiveDimensionsAsEmpty()
    {
        ExtractionDocumentPageDimensions empty = new(0, 0);
        ExtractionDocumentPageDimensions defaultDimensions = default;
        Assert.Equal(0, empty.Width);
        Assert.Equal(0, empty.Height);
        Assert.Equal(defaultDimensions, empty);
        Assert.True(empty.Equals(defaultDimensions));
        Assert.True(empty.Equals((object)defaultDimensions));
        Assert.True(empty == defaultDimensions);
        Assert.False(empty != defaultDimensions);
        Assert.Equal(defaultDimensions.GetHashCode(), empty.GetHashCode());

        ExtractionDocumentPageDimensions positive = new(double.Epsilon, double.Epsilon);
        Assert.Equal(double.Epsilon, positive.Width);
        Assert.Equal(double.Epsilon, positive.Height);
        Assert.NotEqual(empty, positive);
        Assert.False(empty == positive);
        Assert.True(empty != positive);
        ExtractionDocumentSourcePage page = new("tiny", 1, positive);
        Assert.Equal(positive, page.Dimensions);
    }

    [Theory]
    [InlineData(-1, 100, "width")]
    [InlineData(200, -1, "height")]
    [InlineData(-double.Epsilon, 100, "width")]
    [InlineData(200, -double.Epsilon, "height")]
    [InlineData(0, 100, "width")]
    [InlineData(200, 0, "height")]
    [InlineData(0, double.Epsilon, "width")]
    [InlineData(double.Epsilon, 0, "height")]
    [InlineData(-1, 0, "width")]
    [InlineData(0, -1, "width")]
    public void Dimensions_RejectsNegativeAndOneSidedZero(double width, double height, string parameter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(parameter, () => new ExtractionDocumentPageDimensions(width, height));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourcePage_RejectsExplicitAndDefaultEmptyDimensionsButAllowsUnknown(bool explicitEmpty)
    {
        ExtractionDocumentPageDimensions dimensions = explicitEmpty ? new(0, 0) : default;
        Assert.Throws<ArgumentOutOfRangeException>("dimensions", () => new ExtractionDocumentSourcePage("empty", 1, dimensions));

        ExtractionDocumentSourcePage unknown = new("unknown", 1, dimensions: null);
        Assert.Null(unknown.Dimensions);
        Assert.Equal("unknown", unknown.Identifier);
        Assert.Equal(1, unknown.PageNumber);
        Assert.Null(unknown.CoordinateUnit);
        Assert.Null(unknown.CoordinateOrigin);
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
