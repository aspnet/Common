// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentReviewFollowUpTests
{
    [Fact]
    public void DataOnlyApi_HasConcreteCollectionsWithoutParentOrDisposalContract()
    {
        ExtractionDocument document = new("data");
        ExtractionDocumentSection section = new();
        Assert.IsType<List<ExtractionDocumentSection>>(document.Sections);
        Assert.IsType<Dictionary<string, object?>>(document.Metadata);
        Assert.IsType<List<ExtractionDocumentElement>>(section.Elements);
        Assert.IsType<List<ExtractionDocumentSourceReference>>(section.Sources);
        Assert.IsType<List<ExtractionDocumentSourceReference>>(new ExtractionDocumentTableCell().Sources);
        foreach (Type type in new[] { typeof(ExtractionDocument), typeof(ExtractionDocumentElement) })
        {
            Assert.False(typeof(IDisposable).IsAssignableFrom(type));
            Assert.Null(type.GetMethod("Dispose"));
            foreach (string name in new[] { "ParentSection", "Version", "Address", "LocalId", "IsDisposed", "IsDocumentOwned" })
            {
                Assert.Null(type.GetProperty(name));
            }
        }

        foreach (Type type in typeof(ExtractionDocument).Assembly.GetExportedTypes())
        {
            Assert.Null(type.GetMethod("ToMarkdown"));
        }

        Assert.Null(typeof(ExtractionDocument).Assembly.GetType("Microsoft.Extensions.Documents.ExtractionDocumentMarkdownFormatter"));
    }

    [Fact]
    public void OrdinaryLists_AllowSharedElementsAndObserveCurrentOrderWithoutAttachmentState()
    {
        ExtractionDocument first = new("first");
        ExtractionDocument second = new("second");
        ExtractionDocumentSection section = new();
        ExtractionDocumentTextElement text = new(new TextContent("original"));
        section.Elements.AddRange([text, text]);
        first.Sections.Add(section);
        second.Sections.Add(section);
        Assert.Equal(new[] { text, text }, first.EnumerateContent());
        Assert.Equal(new[] { text, text }, second.EnumerateContent());
        section.Elements.RemoveAt(0);
        text.Content = new TextContent("changed");
        first.Sections.Clear();
        Assert.Empty(first.EnumerateContent());
        Assert.Equal("changed", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Same(text, Assert.Single(second.EnumerateContent()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicSections_AreRejectedDuringTraversal(bool self)
    {
        ExtractionDocument document = new("cycle");
        ExtractionDocumentSection root = new();
        ExtractionDocumentSection child = new();
        document.Sections.Add(root);
        root.Elements.Add(self ? root : child);
        if (!self)
        {
            child.Elements.Add(root);
        }

        Assert.Throws<InvalidOperationException>(() => document.EnumerateContent().ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullListEntries_FailExplicitlyAtTraversal(bool root)
    {
        ExtractionDocument document = new("invalid");
        ExtractionDocumentSection section = new();
        document.Sections.Add(root ? null! : section);
        if (!root)
        {
            section.Elements.Add(null!);
        }

        Assert.Throws<InvalidOperationException>(() => document.EnumerateContent().ToArray());
    }

    [Fact]
    public void NontransparentPaths_PreserveSharedSectionsAndRejectAncestorCycles()
    {
        ExtractionDocument document = new("shared");
        ExtractionDocumentSection root = new(new TextContent("local"));
        ExtractionDocumentSection shared = new();
        ExtractionDocumentTextElement text = new(new TextContent("body"));
        shared.Elements.Add(text);
        root.Elements.Add(shared);
        root.Elements.Add(shared);
        document.Sections.Add(root);
        Assert.Equal(new[] { text, text }, document.EnumerateContent());

        shared.Elements.Add(root);
        Assert.Throws<InvalidOperationException>(() => document.EnumerateContent().ToArray());

        shared.Elements.RemoveAt(1);
        document.Sections.Add(root);
        Assert.Equal(new[] { text, text, text, text }, document.EnumerateContent());
    }

    [Fact]
    public void NestedTables_SharedContentAndCyclesAreNotFlattenedByStructuralTraversal()
    {
        ExtractionDocumentTableElement outer = new(null, 1, 2);
        ExtractionDocumentTableElement inner = new(null, 1, 1);
        ExtractionDocumentTextElement shared = new(new TextContent("value"));
        outer.Cells[0, 0] = new(inner);
        outer.Cells[0, 1] = new(inner);
        inner.Cells[0, 0] = new(shared);
        Assert.Same(inner, outer.Cells[0, 0]!.Content);
        Assert.Same(inner, outer.Cells[0, 1]!.Content);
        Assert.Same(shared, inner.Cells[0, 0]!.Content);
        ExtractionDocument document = new("tables");
        ExtractionDocumentSection section = new();
        section.Elements.AddRange([outer, outer]);
        document.Sections.Add(section);
        Assert.Equal(new[] { outer, outer }, document.EnumerateContent());
        inner.Cells[0, 0] = new(outer);
        Assert.Equal(new[] { outer, outer }, document.EnumerateContent());
        Assert.Same(outer, inner.Cells[0, 0]!.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceRelationships_AreExplicitRegardlessOfCompositionOrder(bool composeFirst)
    {
        ExtractionDocument document = new("explicit");
        ExtractionDocumentSection section = new();
        ExtractionDocumentTextElement text = new(new TextContent("visible"));
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentTableCell cell = new(text);
        ExtractionDocumentSourcePage page = new("named-frame", null);
        ExtractionDocumentSourceReference reference = new(page);
        if (composeFirst)
        {
            table.Cells[0, 0] = cell;
            section.Elements.Add(table);
            document.Sections.Add(section);
        }

        cell.Sources.Add(reference);
        text.Sources.Add(reference);
        section.Sources.Add(reference);
        if (!composeFirst)
        {
            table.Cells[0, 0] = cell;
            section.Elements.Add(table);
            document.Sections.Add(section);
        }

        Assert.Empty(document.SourcePages);
        document.SourcePages.Add(page);
        Assert.Same(page, Assert.Single(document.SourcePages));
        cell.Sources.Clear();
        section.Elements.Clear();
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Same(reference, Assert.Single(text.Sources));
        Assert.Null(page.PageNumber);
    }

    [Fact]
    public void NamedUnnumberedAndOutOfOrderFrames_NeverInferAssemblyPageNumbers()
    {
        ExtractionDocument document = new("mixed");
        ExtractionDocumentSourcePage named = new("OneNote:Ideas", null);
        ExtractionDocumentSourcePage seven = new("pdf:7", 7);
        ExtractionDocumentSourcePage two = new("pdf:2", 2);
        document.SourcePages.Add(named);
        document.SourcePages.Add(seven);
        document.SourcePages.Add(two);
        ExtractionDocumentSection section = new();
        ExtractionDocumentTextElement text = new(new TextContent("named page"));
        text.Sources.Add(new(named));
        section.Elements.Add(text);
        document.Sections.Insert(0, section);
        Assert.Equal(new int?[] { null, 7, 2 }, document.SourcePages.Select(page => page.PageNumber));
        Assert.Equal("OneNote:Ideas", text.Sources[0].Page.Identifier);
        Assert.Null(text.Sources[0].Page.PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SourcePage_RejectsNonPositiveNumbers(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>("pageNumber", () => new ExtractionDocumentSourcePage("frame", number));
    }

    [Fact]
    public void Geometry_ExplicitValuesHaveCoordinateEqualityHashAndOperators()
    {
        ExtractionDocumentPoint point = new(1.25, -2.5);
        ExtractionDocumentPoint equalPoint = new(1.25, -2.5);
        ExtractionDocumentPoint differentPoint = new(1.25, 2.5);
        Assert.True(point == equalPoint);
        Assert.False(point != equalPoint);
        Assert.True(point != differentPoint);
        Assert.False(point == differentPoint);
        Assert.True(point.Equals((object)equalPoint));
        Assert.False(point.Equals(null));
        Assert.False(point.Equals("not a point"));
        Assert.Equal(point.GetHashCode(), equalPoint.GetHashCode());
        Assert.True(new ExtractionDocumentPoint(0, -0.0) == default);
        Assert.Equal(default(ExtractionDocumentPoint).GetHashCode(), new ExtractionDocumentPoint(0, -0.0).GetHashCode());
        ExtractionDocumentPageDimensions size = new(200, 100);
        ExtractionDocumentPageDimensions equalSize = new(200, 100);
        ExtractionDocumentPageDimensions differentSize = new(100, 200);
        Assert.True(size == equalSize);
        Assert.False(size != equalSize);
        Assert.True(size != differentSize);
        Assert.False(size == differentSize);
        Assert.True(size.Equals((object)equalSize));
        Assert.False(size.Equals(point));
        Assert.False(size.Equals(null));
        Assert.Equal(size.GetHashCode(), equalSize.GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>("dimensions",
            () => new ExtractionDocumentSourcePage("frame", null, default(ExtractionDocumentPageDimensions)));
    }
}
