// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentElementTests
{
    [Fact]
    public void NullSectionContent_PreservesDescendantsWithoutStoredAggregate()
    {
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("semantic paragraph")));

        Assert.Null(section.Content);
        Assert.Equal("semantic paragraph", Assert.IsType<TextContent>(Assert.Single(section.Elements).Content).Text);
    }

    [Fact]
    public void TextPayload_IsAuthoritativeAndCanBeChanged()
    {
        ExtractionDocumentTextElement element = new(new TextContent("stored markdown"));

        Assert.Equal("stored markdown", Assert.IsType<TextContent>(element.Content).Text);

        element.Content = new TextContent("updated markdown");

        Assert.Equal("updated markdown", Assert.IsType<TextContent>(element.Content).Text);
    }

    [Fact]
    public void Sources_PreserveDuplicateInsertionOrder()
    {
        ExtractionDocumentTextElement element = new(new TextContent("content"));
        ExtractionDocumentSourcePage page = new("page-1", 1);
        ExtractionDocumentSourceReference first = new(page);
        ExtractionDocumentSourceReference duplicate = new(page);
        ExtractionDocumentSourceReference last = new(new("page-2", 2));

        element.Sources.Add(first);
        element.Sources.Add(duplicate);
        element.Sources.Add(first);
        element.Sources.Add(last);

        Assert.Equal(new[] { first, duplicate, first, last }, element.Sources);
        Assert.Equal(new[] { "page-1", "page-1", "page-1", "page-2" }, element.Sources.Select(reference => reference.Page.Identifier));
    }

    [Fact]
    public void Sources_InsertReplaceRemoveAndClear_KeepExplicitRegistriesIndependent()
    {
        ExtractionDocument document = new("edits");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent("content"));
        section.Elements.Add(element);
        document.Sections.Add(section);
        ExtractionDocumentSourceReference first = new(new("first", 1));
        ExtractionDocumentSourceReference second = new(new("second", 2));
        element.Sources.Insert(0, first);
        element.Sources[0] = second;
        Assert.Empty(document.SourcePages);
        document.SourcePages.Add(first.Page);
        document.SourcePages.Add(second.Page);
        Assert.Same(second, Assert.Single(element.Sources));
        Assert.Equal(new[] { first.Page, second.Page }, document.SourcePages);
        element.Sources.RemoveAt(0);
        element.Sources.Add(first);
        element.Sources.Clear();
        Assert.Empty(element.Sources);
        Assert.Equal(2, document.SourcePages.Count);
    }

    [Fact]
    public void BorrowedSourceObjects_PreserveIdentityAndOrder()
    {
        ExtractionDocumentTextElement element = new(new TextContent("content"));
        using MemoryStream firstContent = new(new byte[] { 1 });
        using MemoryStream secondContent = new(new byte[] { 2 });

        ExtractionDocumentSourceReference firstSource = new(new("first-page", 1), rawRepresentation: firstContent);
        ExtractionDocumentSourceReference secondSource = new(new("second-page", 2), rawRepresentation: secondContent);
        element.Sources.Add(firstSource);
        element.Sources.Add(secondSource);
        element.Sources.Add(firstSource);

        Assert.Equal(new[] { firstSource, secondSource, firstSource }, element.Sources);
        Assert.Same(firstContent, firstSource.GetRawRepresentation());
        Assert.Same(secondContent, secondSource.GetRawRepresentation());
        ExtractionDocument document = new("ordered-bodies");
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(element);
        document.Sections.Add(section);
        document.SourcePages.Add(firstSource.Page);
        document.SourcePages.Add(secondSource.Page);
        Assert.Equal(new[] { firstSource.Page, secondSource.Page }, document.SourcePages);
    }

    [Fact]
    public void RawMetadataAndSources_AreOrdinaryMutableCollections()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent("content"));
        document.Sections.Add(section);
        section.Elements.Add(element);

        ExtractionDocumentSourceReference page = new(new("page-raw", 1));
        document.Metadata["provider-key"] = "provider-value";
        element.Sources.Add(page);
        document.SourcePages.Add(page.Page);

        Assert.Equal("provider-value", document.Metadata["provider-key"]);
        Assert.Same(page, element.Sources[0]);
        Assert.Same(page.Page, Assert.Single(document.SourcePages));
    }

    [Fact]
    public void StructuralTraversal_RejectsSelfAndAncestorCycles()
    {
        ExtractionDocument document = new("cycles");
        ExtractionDocumentSection section = new(null);
        document.Sections.Add(section);

        section.Elements.Add(section);
        InvalidOperationException selfException = Assert.Throws<InvalidOperationException>(() => document.EnumerateContent().ToArray());
        section.Elements.Clear();

        ExtractionDocumentSection child = new(null);
        section.Elements.Add(child);
        child.Elements.Add(section);
        InvalidOperationException ancestorException = Assert.Throws<InvalidOperationException>(() => document.EnumerateContent().ToArray());

        Assert.Contains("cyclic", selfException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cyclic", ancestorException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(section.Elements);
        Assert.Same(section, Assert.Single(child.Elements));
    }

    [Fact]
    public void SameScalarAssignments_PreserveContentAndRole()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent("stored"), role: "heading")
        {
            HeadingLevel = 2
        };
        document.Sections.Add(section);
        section.Elements.Add(element);
        AIContent? payload = element.Content;

        element.Content = payload;
        element.HeadingLevel = 2;

        Assert.Same(payload, element.Content);
        Assert.Equal("heading", element.Role);
    }

    [Fact]
    public void Constructor_AllowsEmptyTextPayload()
    {
        ExtractionDocument document = new("empty-constructor");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent(""));
        Assert.Equal("", Assert.IsType<TextContent>(element.Content).Text);
        document.Sections.Add(section);
        section.Elements.Add(element);

        AIContent? payload = element.Content;
        element.Content = payload;
        Assert.Same(payload, element.Content);
    }

    [Fact]
    public void Constructor_RejectsNullTextContent()
    {
        Assert.Throws<ArgumentNullException>(
            "content",
            () => new ExtractionDocumentTextElement(null!));
    }

    [Fact]
    public void Associations_PreserveAbsentAndPartialGeometryWithoutFabrication()
    {
        ExtractionDocument document = new("partial");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement text = new(new TextContent("visible"));
        ExtractionDocumentSourcePage dimensionsOnly = new("dimensions-only", 7, new ExtractionDocumentPageDimensions(200, 100));
        ExtractionDocumentSourcePage polygonOnly = new("polygon-only", 2);
        ExtractionDocumentSourceReference region = new(polygonOnly, DocumentAnnotationTestData.Polygon());
        Assert.Empty(text.Sources);
        text.Sources.Add(region);
        section.Elements.Add(text);
        document.Sections.Add(section);
        document.SourcePages.Add(polygonOnly);
        document.SourcePages.Add(dimensionsOnly);

        Assert.Same(region, Assert.Single(text.Sources));
        Assert.Null(region.GetRawRepresentation());
        Assert.Null(polygonOnly.Dimensions);
        Assert.Null(polygonOnly.CoordinateUnit);
        Assert.Null(polygonOnly.CoordinateOrigin);
        Assert.Equal(DocumentAnnotationTestData.Polygon(), region.Polygon);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), dimensionsOnly.Dimensions);
        Assert.Null(dimensionsOnly.CoordinateUnit);
        Assert.Null(dimensionsOnly.CoordinateOrigin);
        Assert.Equal(new[] { polygonOnly, dimensionsOnly }, document.SourcePages);
        Assert.Equal("visible", Assert.IsType<TextContent>(text.Content).Text);
    }

    [Fact]
    public void EmptyTextContent_IsValidButNullMutationIsRejectedAtomically()
    {
        ExtractionDocument document = new("empty");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement text = new(new TextContent(""));
        section.Elements.Add(text);
        document.Sections.Add(section);
        Assert.Equal("", Assert.IsType<TextContent>(text.Content).Text);

        text.Content = new TextContent("temporary");
        text.Content = new TextContent("");
        Assert.Equal("", Assert.IsType<TextContent>(text.Content).Text);
        AIContent? previous = text.Content;
        Assert.Throws<ArgumentNullException>("value", () => text.Content = null);
        Assert.Same(previous, text.Content);
    }

    [Fact]
    public void LocalPayloads_KeepRawBodiesAndNativeMarkersSeparate()
    {
        ExtractionDocument document = new("projections", null);
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement text = new(new TextContent("visible **text**"));
        ExtractionDocumentImageElement image = new(new TextContent("")) { Source = new Uri("urn:image:page7"), AlternativeText = "diagram only" };
        ExtractionDocumentTextElement extension = new(new TextContent("extension"), role: "vendor-extra");
        ExtractionDocumentElement[] elements = [section, text, image, extension];
        ExtractionDocumentSourcePage page = new("page", 1);
        document.SourcePages.Add(page);
        foreach (ExtractionDocumentElement element in elements)
        {
            element.Sources.Add(new ExtractionDocumentSourceReference(page, rawRepresentation: native));
        }

        section.Elements.Add(text);
        section.Elements.Add(image);
        section.Elements.Add(extension);
        document.Sections.Add(section);

        Assert.Equal(new ExtractionDocumentElement[] { text, image, extension }, document.EnumerateContent());
        Assert.DoesNotContain(native.Marker, Assert.IsType<TextContent>(text.Content).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_BODY_ONLY_91", Assert.IsType<TextContent>(text.Content).Text, StringComparison.Ordinal);
        Assert.Null(document.Content);
        Assert.Null(section.Content);
        Assert.Equal("", Assert.IsType<TextContent>(image.Content).Text);
        Assert.Equal("visible **text**", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Null(image.Text);
        Assert.Equal("extension", Assert.IsType<TextContent>(extension.Content).Text);
        Assert.Same(native, text.Sources[0].GetRawRepresentation());
        Assert.DoesNotContain(typeof(ExtractionDocumentSourceReference).GetProperties(), property => property.Name == "RawRepresentation");
        Assert.Equal(0, native.DisposeCount);
    }
}
