// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentTableElementTests
{
    [Fact]
    public void NestedTables_PreserveHierarchyAndLiteralText()
    {
        ExtractionDocumentTextElement leaf = new(new TextContent("L|R"), "L and R");
        ExtractionDocumentTableElement inner = new(null, 1, 1);
        ExtractionDocumentTableElement middle = new(null, 1, 1);
        ExtractionDocumentTableElement outer = new(null, 1, 1);
        inner.Cells[0, 0] = new ExtractionDocumentTableCell(leaf);
        middle.Cells[0, 0] = new ExtractionDocumentTableCell(inner);
        outer.Cells[0, 0] = new ExtractionDocumentTableCell(middle);

        Assert.Same(leaf, inner.Cells[0, 0]!.Content);
        Assert.Same(inner, middle.Cells[0, 0]!.Content);
        Assert.Same(middle, outer.Cells[0, 0]!.Content);
        Assert.Equal("L and R", leaf.Text);
        Assert.Equal("L|R", Assert.IsType<TextContent>(leaf.Content).Text);
    }

    [Fact]
    public void NestedTable_PreservesLiteralRoleTextAndImageAlternative()
    {
        ExtractionDocumentTextElement text = new(new TextContent("A|<&"));
        ExtractionDocumentImageElement image = new(new UriContent("urn:image", "image/png"))
        {
            AlternativeText = "alt|\"<&"
        };
        ExtractionDocumentTableElement inner = new(null, 1, 2);
        ExtractionDocumentTableCell first = new(text, role: "vendor|\"<");
        inner.Cells[0, 0] = first;
        inner.Cells[0, 1] = new ExtractionDocumentTableCell(image);
        ExtractionDocumentTableElement outer = new(null, 1, 1);
        outer.Cells[0, 0] = new ExtractionDocumentTableCell(inner);

        Assert.Equal("vendor|\"<", first.Role);
        Assert.Equal("alt|\"<&", image.AlternativeText);
        Assert.Equal("A|<&", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Same(image, inner.Cells[0, 1]!.Content);
        Assert.Equal(1, inner.Cells.RowCount);
        Assert.Equal(2, inner.Cells.ColumnCount);
    }

    [Fact]
    public void Coverage_TracksSpanReplacementRemovalAndRejectedEdits()
    {
        ExtractionDocument document = new("coverage");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 4);
        section.Elements.Add(table);
        document.Sections.Add(section);
        ExtractionDocumentTableCell wide = new(new ExtractionDocumentTextElement(new TextContent("wide")), 2, 3);
        ExtractionDocumentTableCell corner = new(new ExtractionDocumentTextElement(new TextContent("corner")), role: "rowHeader");
        table.Cells[0, 0] = wide;
        table.Cells[2, 3] = corner;
        AssertCoverage(wide, 2, 3);

        Assert.Throws<InvalidOperationException>(() => table.Cells[1, 1] = new ExtractionDocumentTableCell());
        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 3] = new ExtractionDocumentTableCell(3, 1));
        Assert.Throws<ArgumentException>(() => table.Cells[0, 0] = new ExtractionDocumentTableCell(4, 1));
        AssertCoverage(wide, 2, 3);

        ExtractionDocumentTableCell small = new(new ExtractionDocumentTextElement(new TextContent("small")));
        table.Cells[0, 0] = small;
        AssertCoverage(small, 1, 1);
        table.Cells[0, 0] = null;
        AssertCoverage(null, 0, 0);
        table.Cells[0, 0] = wide;
        AssertCoverage(wide, 2, 3);

        void AssertCoverage(ExtractionDocumentTableCell? anchor, int rows, int columns)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    bool inSpan = row < rows && column < columns;
                    ExtractionDocumentTableCell? cornerCell = row == 2 && column == 3 ? corner : null;
                    ExtractionDocumentTableCell? expected = inSpan ? anchor : cornerCell;
                    Assert.Same(expected, table.Cells.GetCoveringCell(row, column));
                    Assert.Equal(inSpan && (row != 0 || column != 0),
                        table.Cells.IsCovered(row, column));
                    Assert.Same(row == 0 && column == 0 ? anchor : cornerCell,
                        table.Cells[row, column]);
                }
            }
        }
    }

    [Fact]
    public void StoresDimensionsAndCreatesNullableCells()
    {
        ExtractionDocumentTableElement table = new(new TextContent("stored table"), 2, 3);

        Assert.Equal("stored table", Assert.IsType<TextContent>(table.Content).Text);
        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(3, table.Cells.ColumnCount);
        Assert.Equal(
            new ExtractionDocumentTableCell?[] { null, null, null, null, null, null },
            table.Cells.ToArray());
    }

    [Theory]
    [InlineData(-1, 0, "rowCount")]
    [InlineData(0, -1, "columnCount")]
    public void RejectsNegativeDimensions(int rowCount, int columnCount, string parameterName)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            parameterName,
            () => new ExtractionDocumentTableElement(null, rowCount, columnCount));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void ZeroDimensions_RetainLocalPayloadWithoutFabricatingCells(int rowCount, int columnCount)
    {
        ExtractionDocumentTableElement table = new(new TextContent("stored table"), rowCount, columnCount);

        Assert.Equal(rowCount, table.Cells.RowCount);
        Assert.Equal(columnCount, table.Cells.ColumnCount);
        Assert.Empty(table.Cells);
        Assert.Equal("stored table", Assert.IsType<TextContent>(table.Content).Text);
    }

    [Theory]
    [InlineData(-1, 0, "row")]
    [InlineData(2, 0, "row")]
    [InlineData(0, -1, "column")]
    [InlineData(0, 2, "column")]
    public void RejectsInvalidCellAddresses(int row, int column, string parameterName)
    {
        ExtractionDocumentTableElement table = new(null, 2, 2);
        ExtractionDocumentTextElement text = new(new TextContent("candidate"));
        ExtractionDocumentTableCell candidate = new(text);

        Assert.Throws<ArgumentOutOfRangeException>(
            parameterName,
            () => _ = table.Cells[row, column]);
        Assert.Throws<ArgumentOutOfRangeException>(
            parameterName,
            () => table.Cells[row, column] = candidate);
        Assert.Throws<ArgumentOutOfRangeException>(
            parameterName,
            () => table.Cells[row, column] = null);

        Assert.Null(candidate.Table);
        Assert.Equal(
            new ExtractionDocumentTableCell?[] { null, null, null, null },
            table.Cells.ToArray());
    }

    [Fact]
    public void CellGrid_PreservesPayloadsAndNullPositions()
    {
        ExtractionDocumentTableElement table = new(null, 2, 3);
        table.Cells[0, 1] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("one")));
        table.Cells[1, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("two")));

        Assert.Equal(6, table.Cells.Count());
        Assert.Null(table.Cells[0, 0]);
        Assert.Null(table.Cells[0, 2]);
        Assert.Null(table.Cells[1, 1]);
        Assert.Null(table.Cells[1, 2]);
        Assert.Equal("one", Assert.IsType<TextContent>(table.Cells[0, 1]!.Content!.Content).Text);
        Assert.Equal("two", Assert.IsType<TextContent>(table.Cells[1, 0]!.Content!.Content).Text);
    }

    [Fact]
    public void CellPayload_PreservesLiteralPipes()
    {
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("left|right")));

        Assert.Equal("left|right", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
    }

    [Fact]
    public void AbsentTablePayload_DoesNotAggregateCellPayloads()
    {
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("semantic cell")));

        Assert.Null(table.Content);
        Assert.Equal("semantic cell", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
    }

    [Fact]
    public void MergedCells_PreserveAnchorContentRolesAndCoveredPositions()
    {
        ExtractionDocumentTableElement table = new(null, 3, 3);
        ExtractionDocumentTableCell anchor = new(new ExtractionDocumentTextElement(new TextContent("MERGED_A<&")), 2, 2, "columnHeader");
        table.Cells[0, 0] = anchor;
        table.Cells[2, 0] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("B")), role: "rowHeader");
        ExtractionDocumentTableCell empty = new(role: "vendor\"role");
        table.Cells[2, 2] = empty;
        ExtractionDocumentTableCell?[] before = table.Cells.ToArray();

        Assert.Equal(before, table.Cells.ToArray());
        Assert.Equal("columnHeader", anchor.Role);
        Assert.Equal("MERGED_A<&", Assert.IsType<TextContent>(anchor.Content!.Content).Text);
        Assert.Equal("rowHeader", table.Cells[2, 0]!.Role);
        Assert.Equal("vendor\"role", empty.Role);
        Assert.Same(anchor, table.Cells.GetCoveringCell(1, 1));
        Assert.Null(table.Cells[1, 1]);
        Assert.False(table.Cells.IsCovered(2, 2));
        Assert.Null(empty.Content);
        Assert.Null(table.Content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("columnHeader")]
    [InlineData("rowHeader")]
    [InlineData("ColumnHeader")]
    [InlineData("header")]
    [InlineData("vendor\"role")]
    public void CellRoles_AreExplicitWithoutCorrectingTranscription(string? role)
    {
        ExtractionDocument document = new("explicit-roles", null);
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 1);

        // Header text is not permission to invent a table-cell role or repair recognition.
        ExtractionDocumentTextElement text = new(new TextContent(" INV0ICE  1O0? <&| "), " INV0ICE  1O0? <&| ", role: "heading");
        ExtractionDocumentTableCell cell = new(text, role: role);
        table.Cells[0, 0] = cell;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Null(table.Content);
        Assert.Null(document.Content);
        Assert.Equal(" INV0ICE  1O0? <&| ", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Equal(" INV0ICE  1O0? <&| ", text.Text);
        Assert.Equal(role, cell.Role);
        Assert.Same(cell, table.Cells.GetCoveringCell(0, 0));
        Assert.False(table.Cells.IsCovered(0, 0));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 3)]
    public void NonOriginSingleAxisSpans_RespectDeclaredGrid(int rowSpan, int columnSpan)
    {
        ExtractionDocument document = new("single-axis");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 4);
        ExtractionDocumentTableCell empty = new();
        ExtractionDocumentTextElement text = new(new TextContent("A|<"), "A|<");
        ExtractionDocumentTableCell anchor = new(text, rowSpan, columnSpan);
        table.Cells[0, 0] = empty;
        table.Cells[1, 1] = anchor;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Equal(3, table.Cells.RowCount);
        Assert.Equal(4, table.Cells.ColumnCount);
        Assert.Equal(1, anchor.RowIndex);
        Assert.Equal(1, anchor.ColumnIndex);
        Assert.Equal(rowSpan, anchor.RowSpan);
        Assert.Equal(columnSpan, anchor.ColumnSpan);
        Assert.Equal("A|<", text.Text);
        Assert.Null(anchor.Role);
        Assert.Equal(
            new ExtractionDocumentTableCell?[] { empty, null, null, null, null, anchor, null, null, null, null, null, null },
            table.Cells.ToArray());
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                bool inSpan = row >= 1 && row < 1 + rowSpan && column >= 1 && column < 1 + columnSpan;
                ExtractionDocumentTableCell? covering = inSpan ? anchor : null;
                if (row == 0 && column == 0)
                {
                    covering = empty;
                }

                Assert.Same(covering, table.Cells.GetCoveringCell(row, column));
                Assert.Equal(inSpan && (row != 1 || column != 1), table.Cells.IsCovered(row, column));
            }
        }

        Assert.Null(table.Content);
        Assert.Same(table, Assert.Single(document.EnumerateContent()));
    }

    [Fact]
    public void EmptyCellSources_RemainDistinctFromMissingCells()
    {
        ExtractionDocument document = new("projection-loss", null);
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 3);
        ExtractionDocumentTableCell empty = new();
        ExtractionDocumentSourcePage frame = DocumentAnnotationTestData.PdfPage();
        document.AddSourcePage(frame);
        empty.Sources.Add(new ExtractionDocumentSourceReference(frame, DocumentAnnotationTestData.Polygon(), "NATIVE_ONLY"));
        ExtractionDocumentTextElement text = new(new TextContent("INV0ICE  1O0?"), "INV0ICE  1O0?");
        table.Cells[0, 1] = empty;
        table.Cells[0, 2] = new ExtractionDocumentTableCell(text);
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Null(table.Cells[0, 0]);
        Assert.Null(table.Cells.GetCoveringCell(0, 0));
        Assert.False(table.Cells.IsCovered(0, 0));
        Assert.Same(empty, table.Cells.GetCoveringCell(0, 1));
        Assert.False(table.Cells.IsCovered(0, 1));
        Assert.Null(empty.Content);
        Assert.Same(frame, Assert.Single(empty.Sources).Page);
        Assert.Same(frame, Assert.Single(document.SourcePages));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), empty.Sources[0].Polygon);
        Assert.Equal("NATIVE_ONLY", empty.Sources[0].GetRawRepresentation());
        Assert.Equal("INV0ICE  1O0?", text.Text);
        Assert.Null(table.Content);
        Assert.Null(document.Content);

        // Removing a cell does not remove its explicitly registered source frame.
        table.Cells[0, 1] = null;

        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells.GetCoveringCell(0, 1));
        Assert.Same(frame, Assert.Single(document.SourcePages));
    }

    [Fact]
    public void NoSuppliedCells_RetainSourceAndEmptyGrid()
    {
        string providerMarkdown = $"|A|B|{Environment.NewLine}|---|---|{Environment.NewLine}|1O0?|B|";
        ExtractionDocument document = new("unstructured-table");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 2, 2);
        ExtractionDocumentSourceOutput output = new("provider-table", rawRepresentation: providerMarkdown);
        table.Sources.Add(new ExtractionDocumentSourceReference(new ExtractionDocumentSourcePage("provider-page", 1, sourceOutput: output)));
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(2, table.Cells.ColumnCount);
        Assert.Equal(new ExtractionDocumentTableCell?[] { null, null, null, null }, table.Cells.ToArray());
        Assert.Null(table.Cells.GetCoveringCell(0, 0));
        Assert.False(table.Cells.IsCovered(0, 0));
        Assert.Same(table, Assert.Single(document.EnumerateContent()));
        Assert.Null(table.Content);
        Assert.Same(providerMarkdown, output.GetRawRepresentation());
        Assert.Null(document.Content);
        Assert.Equal(new ExtractionDocumentTableCell?[] { null, null, null, null }, table.Cells.ToArray());
    }

    [Fact]
    public void ExplicitEmptyCell_RetainsSourceAndGridPosition()
    {
        string providerMarkdown = $"|A|B|{Environment.NewLine}|---|---|{Environment.NewLine}|1O0?|B|";
        ExtractionDocument document = new("explicit-empty-table");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 2, 2);
        ExtractionDocumentSourceOutput output = new("provider-table", rawRepresentation: providerMarkdown);
        table.Sources.Add(new ExtractionDocumentSourceReference(new ExtractionDocumentSourcePage("provider-page", 1, sourceOutput: output)));
        ExtractionDocumentTableCell empty = new();
        table.Cells[1, 1] = empty;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Null(table.Content);
        Assert.Same(providerMarkdown, output.GetRawRepresentation());
        Assert.Null(document.Content);
        Assert.Equal(new ExtractionDocumentTableCell?[] { null, null, null, empty }, table.Cells.ToArray());
        Assert.Null(empty.Content);
        Assert.Same(empty, table.Cells.GetCoveringCell(1, 1));
        Assert.False(table.Cells.IsCovered(1, 1));
        Assert.Null(table.Cells.GetCoveringCell(0, 0));
    }

    [Fact]
    public void RemovingAllCells_RetainsSourceAndCurrentEmptyGrid()
    {
        string providerMarkdown = $"|A|B|{Environment.NewLine}|---|---|{Environment.NewLine}|1O0?|B|";
        ExtractionDocument document = new("cleared-table");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 2, 2);
        ExtractionDocumentSourceOutput output = new("provider-table", rawRepresentation: providerMarkdown);
        table.Sources.Add(new ExtractionDocumentSourceReference(new ExtractionDocumentSourcePage("provider-page", 1, sourceOutput: output)));
        ExtractionDocumentTextElement text = new(new TextContent("mapped A"), "mapped A");
        ExtractionDocumentTableCell populated = new(text);
        ExtractionDocumentTableCell empty = new();
        table.Cells[0, 0] = populated;
        table.Cells[1, 1] = empty;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Null(table.Content);
        Assert.Same(providerMarkdown, output.GetRawRepresentation());

        table.Cells[0, 0] = null;

        Assert.Same(empty, table.Cells.GetCoveringCell(1, 1));
        table.Cells[1, 1] = null;

        Assert.Null(table.Content);
        Assert.Null(document.Content);
        Assert.Equal(new ExtractionDocumentTableCell?[] { null, null, null, null }, table.Cells.ToArray());
        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(2, table.Cells.ColumnCount);
        Assert.Equal("mapped A", text.Text);
        Assert.Equal("mapped A", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Same(table, Assert.Single(document.EnumerateContent()));
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                Assert.Null(table.Cells.GetCoveringCell(row, column));
                Assert.False(table.Cells.IsCovered(row, column));
            }
        }

    }
}
