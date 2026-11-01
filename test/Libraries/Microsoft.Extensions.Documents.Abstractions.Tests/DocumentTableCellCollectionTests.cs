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

    [Fact]
    public void RejectsSectionContent()
    {
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentSection section = new(null);

        Assert.Throws<InvalidOperationException>(() => table.Cells[0, 0] = new ExtractionDocumentTableCell(section));

        Assert.Null(table.Cells[0, 0]);
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
        ExtractionDocumentArtifact artifact = new("application/octet-stream", stream.ToArray());
        text.Sources.Add(new(new("merged-page", 1, sourceOutput: new("merged-operation", rawContent: artifact))));
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
        Assert.Equal(new byte[] { 1, 2, 3 }, artifact.Content.ToArray());
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
        ExtractionDocumentTextElement anchorText = new(new TextContent("MERGED_A"), "merged anchor");
        ExtractionDocumentTableCell anchor = new(anchorText, 2, 2);
        table.Cells[1, 1] = anchor;
        section.Elements.Add(table);
        document.Sections.Add(section);
        using DocumentAnnotationTestData.CountingStream stream = new(new byte[] { 61, 62, 63 });
        ExtractionDocumentArtifact artifact = new("application/octet-stream", stream.ToArray());
        ExtractionDocumentTextElement candidateText = new(new TextContent("candidate **value**"), "candidate value");
        candidateText.Sources.Add(new(new("candidate-page", 1, sourceOutput: new("candidate-operation", rawContent: artifact))));
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
        Assert.Empty(document.SourceOutputs);
        Assert.Equal(new byte[] { 61, 62, 63 }, stream.ToArray());
        Assert.Equal(0, stream.DisposeCount);

        // Success in another attached table proves rejection took neither cell nor content ownership.
        ExtractionDocument other = new("candidate-owner");
        ExtractionDocumentSection otherSection = new(null);
        ExtractionDocumentTableElement otherTable = new(null, 2, 1);
        otherSection.Elements.Add(otherTable);
        other.Sections.Add(otherSection);
        otherTable.Cells[0, 0] = candidate;
        other.AddSourcePage(candidateText.Sources[0].Page);
        Assert.Same(candidate, otherTable.Cells[0, 0]);
        Assert.Same(candidate, otherTable.Cells.GetCoveringCell(1, 0));
        Assert.True(otherTable.Cells.IsCovered(1, 0));
        Assert.Same(artifact, Assert.Single(other.SourceOutputs).GetRawContent());
        Assert.Same(candidateText.Sources[0].Page, Assert.Single(other.SourcePages));
        Assert.Equal("candidate value", candidateText.Text);
        Assert.Equal(before, table.Cells.ToArray());

        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(new byte[] { 61, 62, 63 }, stream.ToArray());
        Assert.Equal(0, stream.DisposeCount);
        stream.Dispose();
        Assert.False(stream.CanRead);
        Assert.Equal(new byte[] { 61, 62, 63 }, artifact.Content.ToArray());
    }

    [Fact]
    public void MergedAnchorReplacement_ReleasesCoveredSlotsAndRetainsExplicitSourceData()
    {
        ExtractionDocument document = new("replacement-lifetime");
        using DocumentAnnotationTestData.CountingStream stream = new(DocumentAnnotationTestData.OriginalBytes());
        ExtractionDocumentArtifact artifact = new("application/json", stream.ToArray());
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSourceOutput output = new("operation-A", native, artifact);
        ExtractionDocumentSourcePage frame = DocumentAnnotationTestData.PdfPage(output);
        document.AddSourcePage(frame);
        ExtractionDocumentTextElement oldText = new(new TextContent("lO0? &"), "lO0? &");
        ExtractionDocumentTableCell oldAnchor = new(oldText, 2, 2, "rowHeader");
        oldAnchor.Sources.Add(new ExtractionDocumentSourceReference(frame, DocumentAnnotationTestData.Polygon()));
        ExtractionDocumentTableElement table = new(null, 4, 4);
        table.Cells[0, 0] = oldAnchor;
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(table);
        document.Sections.Add(section);
        Assert.Same(oldAnchor, table.Cells.GetCoveringCell(1, 1));
        Assert.True(table.Cells.IsCovered(1, 1));

        ExtractionDocumentTextElement replacementText = new(new TextContent("replacement?"), "replacement?");
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
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Equal(0, stream.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(123, output.GetRawContent()!.Content.Span[0]);
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
        Assert.Equal("lO0? &", oldText.Text);
        Assert.Equal("replacement?", replacementText.Text);
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
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Same(artifact, output.GetRawContent());
        Assert.Same(native, output.GetRawRepresentation());
        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(34, output.GetRawContent()!.Content.Span[1]);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), stream.ToArray());

        stream.Dispose();
        Assert.Equal(1, stream.DisposeCount);
        Assert.False(stream.CanRead);
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), artifact.Content.ToArray());
        Assert.Equal(0, native.DisposeCount);
    }
}
