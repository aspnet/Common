// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentTableCellTests
{
    [Fact]
    public void HasRowAndColumnAddressWithinGrid()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 2, 2);
        ExtractionDocumentTextElement content = new(new TextContent("cell"));

        table.Cells[1, 0] = new ExtractionDocumentTableCell(content);
        section.Elements.Add(table);
        document.Sections.Add(section);

        ExtractionDocumentTableCell cell = table.Cells[1, 0]!;

        Assert.Equal(1, cell.RowIndex);
        Assert.Equal(0, cell.ColumnIndex);
    }

    [Fact]
    public void GridCoordinates_AreIndependentOfSectionOrder()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection firstSection = new(null);
        ExtractionDocumentSection secondSection = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("cell"));

        table.Cells[0, 0] = new ExtractionDocumentTableCell(content);
        firstSection.Elements.Add(table);
        document.Sections.Add(firstSection);
        document.Sections.Add(secondSection);

        ExtractionDocumentTableCell cell = table.Cells[0, 0]!;

        firstSection.Elements.Remove(table);

        secondSection.Elements.Add(table);
        Assert.Same(cell, table.Cells[0, 0]);
        Assert.Same(content, cell.Content);
        Assert.Equal(0, cell.RowIndex);
        Assert.Equal(0, cell.ColumnIndex);
        Assert.Empty(firstSection.Elements);
        Assert.Same(table, Assert.Single(secondSection.Elements));
    }

    [Fact]
    public void Constructor_RejectsNullContent()
    {
        Assert.Throws<ArgumentNullException>(
            "content",
            () => new ExtractionDocumentTableCell(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("columnHeader")]
    [InlineData("rowHeader")]
    [InlineData("ColumnHeader")]
    [InlineData("vendor\"role")]
    [InlineData(" custom ")]
    public void Constructors_PreserveRolesForContentAndEmptyCellsThroughIndexer(string? role)
    {
        ExtractionDocument document = new("cell-roles", null);
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 2);
        ExtractionDocumentTextElement text = new(new TextContent("cell"), "plain cell");
        ExtractionDocumentTableCell content = new(text, role: role);
        ExtractionDocumentTableCell empty = new(role: role);
        table.Cells[0, 0] = content;
        table.Cells[0, 1] = empty;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Equal(role, content.Role);
        Assert.Equal(role, empty.Role);
        Assert.Equal(1, content.RowSpan);
        Assert.Equal(1, empty.ColumnSpan);
        Assert.Same(text, content.Content);
        Assert.Null(empty.Content);
        Assert.Equal(new ExtractionDocumentTableCell?[] { content, empty }, table.Cells);
        Assert.Equal("plain cell", text.Text);
        Assert.Equal("cell", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Null(table.Content);
        Assert.Null(document.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Constructors_RejectWhitespaceRolesWithoutAttachingContent(string role)
    {
        ExtractionDocumentTextElement text = new(new TextContent("cell"), "plain cell");

        Assert.Throws<ArgumentException>(nameof(role), () => new ExtractionDocumentTableCell(text, role: role));
        Assert.Throws<ArgumentException>(nameof(role), () => new ExtractionDocumentTableCell(role: role));
        Assert.Equal("plain cell", text.Text);
    }

    [Fact]
    public void EmptyCell_PreservesGeometryWithoutText()
    {
        ExtractionDocument document = new("empty-cell");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 3);
        ExtractionDocumentTableCell empty = new();
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage();
        document.AddSourcePage(page);
        empty.Sources.Add(new ExtractionDocumentSourceReference(page, DocumentAnnotationTestData.Polygon()));
        table.Cells[2, 2] = empty;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Null(empty.Content);
        Assert.Equal(2, empty.RowIndex);
        Assert.Equal(2, empty.ColumnIndex);
        Assert.Equal(1, empty.RowSpan);
        Assert.Equal(1, empty.ColumnSpan);
        Assert.False(table.Cells.IsCovered(2, 2));
        Assert.Same(empty, table.Cells.GetCoveringCell(2, 2));
        Assert.Null(table.Cells.GetCoveringCell(1, 2));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), empty.Sources[0].Polygon);
        Assert.Same(page, Assert.Single(document.SourcePages));
        empty.Sources.Add(new ExtractionDocumentSourceReference(page));
        Assert.Equal(2, empty.Sources.Count);
    }

    [Theory]
    [InlineData(0, 1, "rowSpan")]
    [InlineData(-1, 1, "rowSpan")]
    [InlineData(1, 0, "columnSpan")]
    [InlineData(1, -1, "columnSpan")]
    public void Constructors_RejectNonPositiveSpans(int rowSpan, int columnSpan, string parameter)
    {
        ExtractionDocumentTextElement text = new(new TextContent("content"));
        Assert.Throws<ArgumentOutOfRangeException>(parameter, () => new ExtractionDocumentTableCell(rowSpan, columnSpan));
        Assert.Throws<ArgumentOutOfRangeException>(parameter, () => new ExtractionDocumentTableCell(text, rowSpan, columnSpan));
    }

    [Fact]
    public void CellFrames_ResolveMultipleCoordinateSystemsAfterTableReparenting()
    {
        ExtractionDocument document = new("multi-frame-cell");
        ExtractionDocumentSourceOutput output = new("operation-A");
        ExtractionDocumentSourcePage pdf = DocumentAnnotationTestData.PdfPage(output);
        ExtractionDocumentSourcePage raster = new("raster-frame", 7, new ExtractionDocumentPageDimensions(400, 200),
            ExtractionDocumentCoordinateUnit.Pixel, ExtractionDocumentCoordinateOrigin.TopLeft, output);
        document.AddSourcePage(pdf);
        document.AddSourcePage(raster);
        ExtractionDocumentPoint[] rasterPolygon =
        [
            new(20.5, 159), new(81.5, 163.5), new(89, 79.75), new(25.75, 82.5), new(17, 129.5)
        ];
        ExtractionDocumentTableCell empty = new();
        empty.Sources.Add(new ExtractionDocumentSourceReference(pdf, DocumentAnnotationTestData.Polygon()));
        empty.Sources.Add(new ExtractionDocumentSourceReference(raster, rasterPolygon));
        ExtractionDocumentTextElement text = new(new TextContent("lO0  ??"), "lO0  ??");
        ExtractionDocumentTableCell ordinary = new(text);
        document.Metadata["vendor unknown margin"] = -12.5;
        ExtractionDocumentTableElement table = new(null, 1, 3);
        table.Cells[0, 1] = empty;
        table.Cells[0, 2] = ordinary;
        ExtractionDocumentSection first = new(null);
        ExtractionDocumentSection second = new(null);
        first.Elements.Add(new ExtractionDocumentTextElement(new TextContent("first")));
        second.Elements.Add(new ExtractionDocumentTextElement(new TextContent("second")));
        first.Elements.Add(table);
        document.Sections.Add(first);
        document.Sections.Add(second);

        Assert.Equal(new[] { pdf, raster }, document.SourcePages);
        Assert.Same(output, Assert.Single(document.SourceOutputs));

        Assert.True(first.Elements.Remove(table));
        Assert.Equal(new[] { pdf, raster }, document.SourcePages);
        second.Elements.Add(table);

        Assert.Null(empty.Content);
        Assert.Same(empty, table.Cells.GetCoveringCell(0, 1));
        Assert.False(table.Cells.IsCovered(0, 1));
        Assert.Null(table.Cells.GetCoveringCell(0, 0));
        Assert.Equal(new[] { pdf, raster }, document.SourcePages);
        var sources = table.Cells.GetCoveringCell(0, 1)!.Sources;
        Assert.Equal(2, sources.Count);
        Assert.Same(document.SourcePages[0], sources[0].Page);
        Assert.Same(document.SourcePages[1], sources[1].Page);
        Assert.Equal(DocumentAnnotationTestData.Polygon(), sources[0].Polygon);
        Assert.Equal(rasterPolygon, sources[1].Polygon);
        Assert.Equal(7, sources[0].Page.PageNumber);
        Assert.Equal(7, sources[1].Page.PageNumber);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), sources[0].Page.Dimensions);
        Assert.Equal(new ExtractionDocumentPageDimensions(400, 200), sources[1].Page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, sources[0].Page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, sources[0].Page.CoordinateOrigin);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Pixel, sources[1].Page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, sources[1].Page.CoordinateOrigin);
        Assert.Same(output, sources[0].Page.SourceOutput);
        Assert.Same(output, sources[1].Page.SourceOutput);
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Empty(table.Cells.GetCoveringCell(0, 2)!.Content!.Sources);
        Assert.Equal(-12.5, document.Metadata["vendor unknown margin"]);
        Assert.Equal("lO0  ??", text.Text);
        Assert.Equal("lO0  ??", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Null(table.Content);
    }
}
