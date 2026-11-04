// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentTableCellCollectionTests
{
    [Fact]
    public void EnumeratesNullableCellsInRowMajorOrder()
    {
        ExtractionDocumentTableElement table = new(null, 2, 3);
        ExtractionDocumentTextElement firstContent = new(new TextContent("first"));
        ExtractionDocumentTextElement lastContent = new(new TextContent("last"));

        table.Cells[0, 1] = new ExtractionDocumentTableCell(firstContent);
        table.Cells[1, 2] = new ExtractionDocumentTableCell(lastContent);

        ExtractionDocumentTableCell?[] cells = table.Cells.ToArray();

        Assert.Equal(6, cells.Length);
        Assert.Null(cells[0]);
        Assert.Same(firstContent, cells[1]!.Content);
        Assert.Null(cells[2]);
        Assert.Null(cells[3]);
        Assert.Null(cells[4]);
        Assert.Same(lastContent, cells[5]!.Content);
        Assert.Equal(
            new ExtractionDocumentTableCell?[] { null, cells[1], null, null, null, cells[5] },
            cells);
    }

    [Fact]
    public void Indexer_PreservesNonSectionElement()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("cell"));
        document.Sections.Add(section);
        section.Elements.Add(table);

        table.Cells[0, 0] = new ExtractionDocumentTableCell(content);

        ExtractionDocumentTableCell cell = table.Cells[0, 0]!;
        Assert.Same(content, cell.Content);
        Assert.Same(cell, table.Cells[0, 0]);
    }

    [Fact]
    public void SupportsImageCellContent()
    {
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ReadOnlyMemory<byte> imageContent = new byte[] { 1, 2, 3 };
        Uri source = new("urn:table-image:opaque-1");
        ExtractionDocumentImageElement image = new(new TextContent("stored image"))
        {
            Content = new DataContent(imageContent, "image/png"),
            Source = source,
            AlternativeText = "diagram"
        };

        table.Cells[0, 0] = new ExtractionDocumentTableCell(image);

        ExtractionDocumentImageElement actual = Assert.IsType<ExtractionDocumentImageElement>(table.Cells[0, 0]!.Content);
        Assert.Same(image, actual);
        Assert.Equal(imageContent.ToArray(), Assert.IsType<DataContent>(actual.Content).Data.ToArray());
        Assert.Equal("image/png", Assert.IsType<DataContent>(actual.Content).MediaType);
        Assert.Same(source, actual.Source);
        Assert.Equal("diagram", actual.AlternativeText);
    }

    [Fact]
    public void ReplacesAndRemovesContent_ReleasesCellGridAssociation()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentTextElement oldContent = new(new TextContent("old"));
        ExtractionDocumentTextElement replacementContent = new(new TextContent("replacement"));
        document.Sections.Add(section);
        section.Elements.Add(table);

        table.Cells[0, 0] = new ExtractionDocumentTableCell(oldContent);
        ExtractionDocumentTableCell oldCell = table.Cells[0, 0]!;

        table.Cells[0, 0] = new ExtractionDocumentTableCell(replacementContent);

        ExtractionDocumentTableCell replacementCell = table.Cells[0, 0]!;
        Assert.NotSame(oldCell, replacementCell);
        Assert.Null(oldCell.Table);
        Assert.Same(oldContent, oldCell.Content);
        Assert.Same(replacementContent, replacementCell.Content);

        table.Cells[0, 0] = null;

        Assert.Null(table.Cells[0, 0]);
        Assert.Null(replacementCell.Table);
        Assert.Same(replacementContent, replacementCell.Content);
    }

    [Fact]
    public void RejectsAlreadyOwnedCell()
    {
        ExtractionDocumentTableElement firstTable = new(null, 1, 1);
        ExtractionDocumentTableElement secondTable = new(null, 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("cell"));
        ExtractionDocumentTableCell cell = new(content);
        firstTable.Cells[0, 0] = cell;

        Assert.Throws<InvalidOperationException>(() => secondTable.Cells[0, 0] = cell);

        Assert.Same(cell, firstTable.Cells[0, 0]);
        Assert.Null(secondTable.Cells[0, 0]);
        Assert.Same(content, firstTable.Cells[0, 0]!.Content);
        Assert.Equal(0, cell.RowIndex);
        Assert.Equal(0, cell.ColumnIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSectionCell_PreservesOrderedLocalPayloadsSourcesAndSpans(bool useUriContent)
    {
        ExtractionDocument document = new("mixed-cell");
        ExtractionDocumentSection root = new();
        ExtractionDocumentTableElement table = new(null, 2, 3);
        root.Elements.Add(table);
        document.Sections.Add(root);
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage();
        document.SourcePages.Add(page);
        ExtractionDocumentSourceReference cellSource = new(page, DocumentAnnotationTestData.Polygon());
        ExtractionDocumentSourceReference firstSource = new(page, [new(1.25, 2.5), new(3.75, 2.5), new(1.25, 4.5)]);
        ExtractionDocumentSourceReference imageSource = new(page, [new(5.25, 6.5), new(7.75, 6.5), new(5.25, 8.5)]);
        ExtractionDocumentSourceReference lastSource = new(page, [new(9.25, 10.5), new(11.75, 10.5), new(9.25, 12.5)]);
        TextContent paragraphA = new("Paragraph A **before** the image.");
        TextContent paragraphB = new("Paragraph B *after* the image.");
        ExtractionDocumentTextElement first = new(paragraphA, "paragraph");
        ExtractionDocumentTextElement last = new(paragraphB, "paragraph");
        byte[] bytes = [11, 22, 33, 44];
        Uri payloadUri = new("urn:mixed-cell:payload");
        Uri originalAsset = new("urn:mixed-cell:original");
        AIContent imagePayload = useUriContent
            ? new UriContent(payloadUri, "image/png")
            : new DataContent(bytes, "image/png");
        ExtractionDocumentImageElement image = new(imagePayload, "OCR 17 & 23")
        {
            Source = originalAsset,
            AlternativeText = "A diagram, not its transcription."
        };
        first.Sources.Add(firstSource);
        image.Sources.Add(imageSource);
        last.Sources.Add(lastSource);
        ExtractionDocumentSection mixed = new();
        mixed.Elements.Add(first);
        mixed.Elements.Add(image);
        mixed.Elements.Add(last);
        ExtractionDocumentTableCell cell = new(mixed, 2, 2, "rowHeader");
        cell.Sources.Add(cellSource);

        table.Cells[0, 1] = cell;

        Assert.Same(cell, table.Cells[0, 1]);
        Assert.Same(table, cell.Table);
        Assert.Equal(0, cell.RowIndex);
        Assert.Equal(1, cell.ColumnIndex);
        Assert.Equal(2, cell.RowSpan);
        Assert.Equal(2, cell.ColumnSpan);
        Assert.Equal("rowHeader", cell.Role);
        Assert.Null(table.Cells[0, 0]);
        foreach ((int row, int column) in new[] { (0, 2), (1, 1), (1, 2) })
        {
            Assert.Null(table.Cells[row, column]);
            Assert.True(table.Cells.IsCovered(row, column));
            Assert.Same(cell, table.Cells.GetCoveringCell(row, column));
        }

        Assert.Same(mixed, cell.Content);
        Assert.Equal(3, mixed.Elements.Count);
        Assert.Same(first, mixed.Elements[0]);
        Assert.Same(image, mixed.Elements[1]);
        Assert.Same(last, mixed.Elements[2]);
        Assert.Same(paragraphA, first.Content);
        Assert.Same(paragraphB, last.Content);
        Assert.NotSame(first, last);
        Assert.NotSame(paragraphA, paragraphB);
        Assert.Equal("Paragraph A **before** the image.", Assert.IsType<TextContent>(first.Content).Text);
        Assert.Equal("Paragraph B *after* the image.", Assert.IsType<TextContent>(last.Content).Text);
        Assert.Equal("paragraph", first.Role);
        Assert.Equal("paragraph", last.Role);
        Assert.Same(imagePayload, image.Content);
        if (useUriContent)
        {
            UriContent payload = Assert.IsType<UriContent>(image.Content);
            Assert.Same(payloadUri, payload.Uri);
            Assert.Equal("image/png", payload.MediaType);
        }
        else
        {
            DataContent payload = Assert.IsType<DataContent>(image.Content);
            Assert.Equal(new byte[] { 11, 22, 33, 44 }, payload.Data.ToArray());
            Assert.Equal("image/png", payload.MediaType);
        }

        Assert.Same(originalAsset, image.Source);
        Assert.Equal("OCR 17 & 23", image.Text);
        Assert.Equal("A diagram, not its transcription.", image.AlternativeText);
        Assert.Same(cellSource, Assert.Single(cell.Sources));
        Assert.Same(firstSource, Assert.Single(first.Sources));
        Assert.Same(imageSource, Assert.Single(image.Sources));
        Assert.Same(lastSource, Assert.Single(last.Sources));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), cellSource.Polygon);
        Assert.Equal(new ExtractionDocumentPoint[] { new(1.25, 2.5), new(3.75, 2.5), new(1.25, 4.5) }, firstSource.Polygon);
        Assert.Equal(new ExtractionDocumentPoint[] { new(5.25, 6.5), new(7.75, 6.5), new(5.25, 8.5) }, imageSource.Polygon);
        Assert.Equal(new ExtractionDocumentPoint[] { new(9.25, 10.5), new(11.75, 10.5), new(9.25, 12.5) }, lastSource.Polygon);
        Assert.All(new[] { cellSource, firstSource, imageSource, lastSource }, source => Assert.Same(page, source.Page));
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
        Assert.Null(document.Content);
        Assert.Null(root.Content);
        Assert.Null(table.Content);
        Assert.Null(mixed.Content);
        Assert.Empty(root.Sources);
        Assert.Empty(table.Sources);
        Assert.Empty(mixed.Sources);
        Assert.Same(table, Assert.Single(document.EnumerateContent()));

        image.Text = "Corrected OCR 17 & 28";
        Assert.Equal("Corrected OCR 17 & 28", image.Text);
        Assert.Equal("A diagram, not its transcription.", image.AlternativeText);
        image.AlternativeText = "Revised caption.";
        Assert.Equal("Revised caption.", image.AlternativeText);
        Assert.Equal("Corrected OCR 17 & 28", image.Text);
        Assert.Same(imagePayload, image.Content);
        Assert.Same(originalAsset, image.Source);
        Assert.Same(paragraphA, first.Content);
        Assert.Same(paragraphB, last.Content);
        Assert.Null(mixed.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSectionCell_PostAttachmentCyclesDoNotExpandPublicTraversal(bool includeInnerTable)
    {
        ExtractionDocument document = new("mixed-cycle");
        ExtractionDocumentSection root = new();
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentSection mixed = new();
        ExtractionDocumentTextElement paragraph = new(new TextContent("cell-only paragraph"));
        mixed.Elements.Add(paragraph);
        ExtractionDocumentTableCell cell = new(mixed);
        table.Cells[0, 0] = cell;
        root.Elements.Add(table);
        document.Sections.Add(root);
        Assert.Same(table, Assert.Single(document.EnumerateContent()));

        ExtractionDocumentTableElement? innerTable = null;
        if (includeInnerTable)
        {
            innerTable = new(null, 1, 1);
            mixed.Elements.Add(innerTable);
            innerTable.Cells[0, 0] = new ExtractionDocumentTableCell(root);
        }
        else
        {
            mixed.Elements.Add(root);
        }

        // A cycle behind a cell boundary is outside structural traversal.
        Assert.Same(table, Assert.Single(document.EnumerateContent()));
        Assert.Same(cell, table.Cells[0, 0]);
        Assert.Same(mixed, cell.Content);
        Assert.Same(paragraph, mixed.Elements[0]);
        Assert.Equal("cell-only paragraph", Assert.IsType<TextContent>(paragraph.Content).Text);
        if (includeInnerTable)
        {
            Assert.Same(innerTable, mixed.Elements[1]);
            Assert.Same(root, innerTable!.Cells[0, 0]!.Content);
        }
        else
        {
            Assert.Same(root, mixed.Elements[1]);
        }

        Assert.Null(document.Content);
        Assert.Null(table.Content);
        Assert.Null(mixed.Content);
    }

    [Fact]
    public void RejectsTableSelfContent()
    {
        ExtractionDocumentTableElement table = new(null, 1, 1);

        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 0] = new ExtractionDocumentTableCell(table));

        Assert.Null(table.Cells[0, 0]);
    }

    [Fact]
    public void IndirectTableCycles_DoNotCauseStructuralTraversalToRecurseIntoCells()
    {
        ExtractionDocumentTableElement outerTable = new(null, 1, 1);
        ExtractionDocumentTableElement innerTable = new(null, 1, 1);

        outerTable.Cells[0, 0] = new ExtractionDocumentTableCell(innerTable);

        innerTable.Cells[0, 0] = new ExtractionDocumentTableCell(outerTable);
        ExtractionDocument document = new("cycle");
        ExtractionDocumentSection section = new();
        section.Elements.Add(outerTable);
        document.Sections.Add(section);
        Assert.Same(outerTable, Assert.Single(document.EnumerateContent()));

        Assert.Same(innerTable, outerTable.Cells[0, 0]!.Content);
        Assert.Same(outerTable, innerTable.Cells[0, 0]!.Content);
    }

    [Fact]
    public void SameCellAssignment_PreservesGridAndPayload()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentTextElement content = new(new TextContent("content"));
        document.Sections.Add(section);
        section.Elements.Add(table);

        table.Cells[0, 0] = new ExtractionDocumentTableCell(content);
        ExtractionDocumentTableCell cell = table.Cells[0, 0]!;

        table.Cells[0, 0] = cell;

        Assert.Same(cell, table.Cells[0, 0]);
        Assert.Same(content, cell.Content);

        table.Cells[0, 0] = null;
        table.Cells[0, 0] = null;

        Assert.Null(table.Cells[0, 0]);
        Assert.Null(cell.Table);
    }

    [Fact]
    public void MergedCells_PreserveAnchorSpansRolesAndDistinctCoveredPositions()
    {
        ExtractionDocument document = new("merged");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 3);
        ExtractionDocumentTableCell anchor = new(new ExtractionDocumentTextElement(new TextContent("MERGED_A")), 2, 2, "header");
        ExtractionDocumentTableCell ordinary = new(new ExtractionDocumentTextElement(new TextContent("B")));
        table.Cells[0, 0] = anchor;
        table.Cells[2, 0] = ordinary;
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Equal(2, anchor.RowSpan);
        Assert.Equal(2, anchor.ColumnSpan);
        Assert.Equal("header", anchor.Role);
        Assert.False(table.Cells.IsCovered(0, 0));
        foreach ((int row, int column) in new[] { (0, 1), (1, 0), (1, 1) })
        {
            Assert.Null(table.Cells[row, column]);
            Assert.True(table.Cells.IsCovered(row, column));
            Assert.Same(anchor, table.Cells.GetCoveringCell(row, column));
        }

        Assert.Null(table.Cells[2, 2]);
        Assert.False(table.Cells.IsCovered(2, 2));
        Assert.Null(table.Cells.GetCoveringCell(2, 2));
        Assert.Equal(new ExtractionDocumentTableCell?[] { anchor, null, null, null, null, null, ordinary, null, null }, table.Cells.ToArray());
    }

    [Fact]
    public void MergedCells_OverlapCoveredEditsAndReuseRejectWithoutMutation()
    {
        ExtractionDocument document = new("invalid-merged");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 3);
        ExtractionDocumentTextElement text = new(new TextContent("MERGED_A"));
        using DocumentAnnotationTestData.CountingStream stream = new(new byte[] { 1, 2, 3 });
        text.Sources.Add(new(new("merged-page", 1), rawRepresentation: stream));
        ExtractionDocumentTableCell anchor = new(text, 2, 2);
        table.Cells[0, 0] = anchor;
        section.Elements.Add(table);
        document.Sections.Add(section);
        ExtractionDocumentTableCell?[] before = table.Cells.ToArray();
        ExtractionDocumentTableCell overlap = new(2, 1);
        ExtractionDocumentTableCell candidate = new();

        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 1] = candidate);
        Assert.Throws<InvalidOperationException>(() => table.Cells[1, 1] = null);
        Assert.Throws<InvalidOperationException>(() => table.Cells[1, 0] = null);
        table.Cells[2, 2] = candidate;
        Assert.Throws<InvalidOperationException>(() => table.Cells[1, 2] = overlap);
        Assert.Throws<InvalidOperationException>(() => table.Cells[2, 1] = candidate);
        table.Cells[2, 2] = null;
        Assert.Equal(before, table.Cells.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, stream.ToArray());
        Assert.Equal(0, stream.DisposeCount);

        table.Cells[0, 0] = null;
        Assert.False(table.Cells.IsCovered(1, 1));
        Assert.Null(table.Cells.GetCoveringCell(1, 1));
        Assert.Equal(0, stream.DisposeCount);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public void MergedCells_OutOfBoundsSpanRejectsWithoutTakingOwnership(int rowSpan, int columnSpan)
    {
        ExtractionDocument document = new("bounds");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 3);
        section.Elements.Add(table);
        document.Sections.Add(section);
        ExtractionDocumentTableCell candidate = new(rowSpan, columnSpan);

        Assert.Throws<ArgumentException>("cell", () => table.Cells[2, 2] = candidate);
        Assert.Null(table.Cells[2, 2]);
        table.Cells[0, 0] = candidate;
    }

    [Fact]
    public void MergedCells_ProspectiveSpanCrossingCoveredSlotOnlyRejectsWithoutMutation()
    {
        ExtractionDocument document = new("covered-slot-overlap");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 3, 3);
        ExtractionDocumentTextElement anchorText = new(new TextContent("MERGED_A"));
        ExtractionDocumentTableCell anchor = new(anchorText, 2, 2);
        table.Cells[1, 1] = anchor;
        section.Elements.Add(table);
        document.Sections.Add(section);
        using DocumentAnnotationTestData.CountingStream stream = new(new byte[] { 61, 62, 63 });
        ExtractionDocumentTextElement candidateText = new(new TextContent("candidate **value**"));
        candidateText.Sources.Add(new(new("candidate-page", 1), rawRepresentation: stream));
        ExtractionDocumentTableCell candidate = new(candidateText, 2, 1);
        ExtractionDocumentTableCell?[] before = table.Cells.ToArray();

        // Candidate occupies (0,2) and (1,2), never the existing anchor at (1,1).
        Assert.Null(table.Cells[0, 2]);
        Assert.False(table.Cells.IsCovered(0, 2));
        Assert.Null(table.Cells.GetCoveringCell(0, 2));
        Assert.Null(table.Cells[1, 2]);
        Assert.True(table.Cells.IsCovered(1, 2));
        Assert.Same(anchor, table.Cells.GetCoveringCell(1, 2));

        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 2] = candidate);

        Assert.Equal(before, table.Cells.ToArray());
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                bool inAnchor = row >= 1 && column >= 1;
                Assert.Equal(inAnchor && (row != 1 || column != 1), table.Cells.IsCovered(row, column));
                Assert.Same(inAnchor ? anchor : null, table.Cells.GetCoveringCell(row, column));
            }
        }

        Assert.Same(anchor, table.Cells[1, 1]);
        Assert.Empty(document.SourcePages);
        Assert.Equal(new byte[] { 61, 62, 63 }, stream.ToArray());
        Assert.Equal(0, stream.DisposeCount);

        // Success in another attached table proves rejection took neither cell nor content ownership.
        ExtractionDocument other = new("candidate-owner");
        ExtractionDocumentSection otherSection = new(null);
        ExtractionDocumentTableElement otherTable = new(null, 2, 1);
        otherSection.Elements.Add(otherTable);
        other.Sections.Add(otherSection);
        otherTable.Cells[0, 0] = candidate;
        other.SourcePages.Add(candidateText.Sources[0].Page);
        Assert.Same(candidate, otherTable.Cells[0, 0]);
        Assert.Same(candidate, otherTable.Cells.GetCoveringCell(1, 0));
        Assert.True(otherTable.Cells.IsCovered(1, 0));
        Assert.Same(candidateText.Sources[0].Page, Assert.Single(other.SourcePages));
        Assert.Equal("candidate **value**", Assert.IsType<TextContent>(candidateText.Content).Text);
        Assert.Equal(before, table.Cells.ToArray());

        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(new byte[] { 61, 62, 63 }, stream.ToArray());
        Assert.Equal(0, stream.DisposeCount);
        stream.Dispose();
        Assert.False(stream.CanRead);
    }

    [Fact]
    public void MergedAnchorReplacement_ReleasesCoveredSlotsAndRetainsExplicitSourceData()
    {
        ExtractionDocument document = new("replacement-lifetime");
        using DocumentAnnotationTestData.CountingStream stream = new(DocumentAnnotationTestData.OriginalBytes());
        using DocumentAnnotationTestData.BorrowedNative native = new();
        document.RawRepresentation = native;
        ExtractionDocumentSourcePage frame = DocumentAnnotationTestData.PdfPage();
        document.SourcePages.Add(frame);
        ExtractionDocumentTextElement oldText = new(new TextContent("lO0? &"));
        ExtractionDocumentTableCell oldAnchor = new(oldText, 2, 2, "rowHeader");
        oldAnchor.Sources.Add(new ExtractionDocumentSourceReference(frame, DocumentAnnotationTestData.Polygon()));
        ExtractionDocumentTableElement table = new(null, 4, 4);
        table.Cells[0, 0] = oldAnchor;
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(table);
        document.Sections.Add(section);
        Assert.Same(oldAnchor, table.Cells.GetCoveringCell(1, 1));
        Assert.True(table.Cells.IsCovered(1, 1));

        ExtractionDocumentTextElement replacementText = new(new TextContent("replacement?"));
        ExtractionDocumentTableCell replacement = new(replacementText);
        table.Cells[0, 0] = replacement;

        Assert.Same(replacement, table.Cells.GetCoveringCell(0, 0));
        foreach ((int row, int column) in new[] { (0, 1), (1, 0), (1, 1) })
        {
            Assert.Null(table.Cells[row, column]);
            Assert.Null(table.Cells.GetCoveringCell(row, column));
            Assert.False(table.Cells.IsCovered(row, column));
        }

        Assert.Same(frame, Assert.Single(document.SourcePages));
        Assert.Equal(0, stream.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), stream.ToArray());

        ExtractionDocumentTableCell empty = new();
        table.Cells[0, 1] = empty;
        Assert.Same(empty, table.Cells.GetCoveringCell(0, 1));
        Assert.False(table.Cells.IsCovered(0, 1));
        table.Cells[2, 2] = oldAnchor;

        Assert.Equal(4, table.Cells.RowCount);
        Assert.Equal(4, table.Cells.ColumnCount);
        Assert.Equal(2, oldAnchor.RowIndex);
        Assert.Equal(2, oldAnchor.ColumnIndex);
        Assert.Equal(2, oldAnchor.RowSpan);
        Assert.Equal(2, oldAnchor.ColumnSpan);
        Assert.Equal("rowHeader", oldAnchor.Role);
        Assert.Equal("lO0? &", Assert.IsType<TextContent>(oldText.Content).Text);
        Assert.Equal("replacement?", Assert.IsType<TextContent>(replacementText.Content).Text);
        Assert.Null(empty.Content);
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                ExtractionDocumentTableCell? covering = null;
                if (row >= 2 && column >= 2)
                {
                    covering = oldAnchor;
                }
                else if (row == 0 && column == 0)
                {
                    covering = replacement;
                }
                else if (row == 0 && column == 1)
                {
                    covering = empty;
                }

                Assert.Same(covering, table.Cells.GetCoveringCell(row, column));
                Assert.Equal(row >= 2 && column >= 2 && (row != 2 || column != 2), table.Cells.IsCovered(row, column));
            }
        }

        Assert.Equal(
            new ExtractionDocumentTableCell?[]
            {
                replacement, empty, null, null, null, null, null, null,
                null, null, oldAnchor, null, null, null, null, null
            },
            table.Cells.ToArray());
        Assert.Null(table.Content);
        Assert.Same(frame, Assert.Single(document.SourcePages));
        Assert.Same(frame, oldAnchor.Sources[0].Page);
        Assert.Equal(DocumentAnnotationTestData.Polygon(), oldAnchor.Sources[0].Polygon);
        Assert.Same(native, document.RawRepresentation);
        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), stream.ToArray());

        stream.Dispose();
        Assert.Equal(1, stream.DisposeCount);
        Assert.False(stream.CanRead);
        Assert.Equal(0, native.DisposeCount);
    }
}
