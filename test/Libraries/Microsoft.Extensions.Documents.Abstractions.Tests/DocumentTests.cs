// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public partial class DocumentTests
{
    [Fact]
    public void StoresIdentifierAndInitialState()
    {
        ExtractionDocument document = new("document-id", new TextContent("stored document"));

        Assert.Equal("document-id", document.Identifier);
        Assert.Equal("stored document", Assert.IsType<TextContent>(document.Content).Text);
        Assert.Empty(document.Sections);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RejectsNullOrEmptyIdentifier(string? identifier)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ExtractionDocument(identifier!));
    }

    [Fact]
    public void LocalTextPayload_RetainsExactSuppliedValue()
    {
        ExtractionDocument document = new("document", new TextContent("stored markdown"));

        Assert.Equal("stored markdown", Assert.IsType<TextContent>(document.Content).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullableRootPayload_PreservesMissingVersusEmptyText(string? markdown)
    {
        ExtractionDocument document = new("document", markdown is null ? null : new TextContent(markdown));

        Assert.Equal(markdown, (document.Content as TextContent)?.Text);
    }

    [Fact]
    public void Sections_RetainImagePayloadWithoutStoredAncestorAggregate()
    {
        ExtractionDocument document = new("document", null);
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(new ExtractionDocumentImageElement(new TextContent("stored image markdown"))
        {
            Source = new Uri("urn:image:semantic"),
            AlternativeText = "semantic image"
        });

        document.Sections.Add(section);

        Assert.Null(document.Content);
        ExtractionDocumentImageElement image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(document.EnumerateContent()));
        Assert.Equal("stored image markdown", Assert.IsType<TextContent>(image.Content).Text);
        Assert.Equal("urn:image:semantic", image.Source!.ToString());
        Assert.Equal("semantic image", image.AlternativeText);
    }

    [Fact]
    public void MultipleSections_PreserveContentOrderWithoutJoiningPayloads()
    {
        ExtractionDocument document = new("document", null);
        ExtractionDocumentSection first = new(null);
        ExtractionDocumentSection second = new(null);
        first.Elements.Add(new ExtractionDocumentTextElement(new TextContent("first semantic")));
        second.Elements.Add(new ExtractionDocumentTextElement(new TextContent("second semantic")));
        document.Sections.Add(first);
        document.Sections.Add(second);

        Assert.Equal(new[] { first, second }, document.Sections);
        Assert.Equal(new[] { "first semantic", "second semantic" },
            document.EnumerateContent().Select(element => Assert.IsType<TextContent>(element.Content).Text));
    }

    [Fact]
    public void Content_SameReferencePreservesPayload()
    {
        ExtractionDocument document = new("document", new TextContent("stored markdown"));
        AIContent? payload = document.Content;

        document.Content = payload;

        Assert.Same(payload, document.Content);
    }

    [Fact]
    public void Content_ChangedReferenceUpdatesPayload()
    {
        ExtractionDocument document = new("document", new TextContent("stored markdown"));
        Assert.Equal("stored markdown", Assert.IsType<TextContent>(document.Content).Text);
        document.Content = new TextContent("updated markdown");

        Assert.Equal("updated markdown", Assert.IsType<TextContent>(document.Content).Text);
    }

    [Fact]
    public void EnumerateContent_ReturnsOrdinaryAndNestedElementsInDepthFirstOrder()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection first = new(null);
        ExtractionDocumentTextElement firstText = new(new TextContent("first text"));
        ExtractionDocumentSection nested = new(null);
        ExtractionDocumentTextElement nestedText = new(new TextContent("nested text"));
        ExtractionDocumentImageElement image = new(new TextContent("image"));
        ExtractionDocumentTextElement extension = new(new TextContent("extension"), role: "application-extra");
        ExtractionDocumentTableElement table = new(null, 1, 1);
        ExtractionDocumentSection second = new(null);
        ExtractionDocumentTextElement secondText = new(new TextContent("second text"));

        nested.Elements.Add(nestedText);
        first.Elements.Add(firstText);
        first.Elements.Add(nested);
        first.Elements.Add(image);
        first.Elements.Add(extension);
        first.Elements.Add(table);
        second.Elements.Add(secondText);
        document.Sections.Add(first);
        document.Sections.Add(second);

        ExtractionDocumentElement[] content = document.EnumerateContent().ToArray();

        Assert.Equal(
            new ExtractionDocumentElement[] { firstText, nestedText, image, extension, table, secondText },
            content);
    }

    [Fact]
    public void EnumerateContent_DoesNotReturnSectionNodes()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection root = new(null);
        ExtractionDocumentSection nested = new(null);
        root.Elements.Add(nested);
        nested.Elements.Add(new ExtractionDocumentTextElement(new TextContent("content")));
        document.Sections.Add(root);

        ExtractionDocumentElement[] content = document.EnumerateContent().ToArray();

        Assert.NotEmpty(content);
        Assert.All(content, element => Assert.False(element is ExtractionDocumentSection));
    }

    [Fact]
    public void DocumentTraversal_PreservesOriginIdentityAcrossHierarchyChanges()
    {
        ExtractionDocument document = new("origin");
        ExtractionDocumentSourceOutput output = new("operation-A");
        ExtractionDocumentSourcePage page7 = DocumentAnnotationTestData.PdfPage(output);
        ExtractionDocumentSourcePage page2 = new("urn:frame:page2", 2, sourceOutput: output);
        ExtractionDocumentTextElement text7 = new(new TextContent("same text"));
        ExtractionDocumentTextElement text2 = new(new TextContent("same text"));
        ExtractionDocumentSourceReference region = new(page7, DocumentAnnotationTestData.Polygon());
        text7.Sources.Add(region);
        text2.Sources.Add(new ExtractionDocumentSourceReference(page2, DocumentAnnotationTestData.Polygon()));
        ExtractionDocumentSection first = new(null);
        ExtractionDocumentSection second = new(null);
        first.Elements.Add(text7);
        second.Elements.Add(text2);
        document.Sections.Add(first);
        document.Sections.Add(second);
        document.AddSourcePage(page7);
        document.AddSourcePage(page2);
        Assert.Equal(new[] { page7, page2 }, document.SourcePages);

        first.Elements.Remove(text7);
        second.Elements.Insert(0, text7);

        Assert.Equal(new[] { text7, text2 }, document.EnumerateContent());
        Assert.Equal(new int?[] { 7, 2 }, document.SourcePages.Select(page => page.PageNumber));
        Assert.Same(page7, text7.Sources[0].Page);
        Assert.Equal("urn:frame:pdf-A", region.Page.Identifier);
        Assert.Equal(DocumentAnnotationTestData.Polygon(), region.Polygon);
        Assert.Same(output, Assert.Single(document.SourceOutputs));
    }

    [Fact]
    public void BlankPage_PreservesSourceFrameWithoutFabricatedSemanticContent()
    {
        ExtractionDocument document = new("blank");
        ExtractionDocumentSourceOutput output = new("blank-operation");
        ExtractionDocumentSourcePage page = new("urn:frame:blank-page2", 2, new ExtractionDocumentPageDimensions(200, 100),
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft, output);
        document.AddSourcePage(page);
        document.Sections.Add(new ExtractionDocumentSection());

        Assert.Null(document.Content);
        Assert.Empty(document.EnumerateContent());
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal(2, page.PageNumber);
        Assert.Equal("urn:frame:blank-page2", page.Identifier);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Equal(ExtractionDocumentRawContentStatus.Unavailable, output.ContentStatus);
    }

    [Fact]
    public void DocumentRawOutput_MultiplePagesShareOneBodyWithoutSemanticLeakage()
    {
        ExtractionDocument document = new("shared-body", new TextContent("visible"));
        using DocumentAnnotationTestData.CountingStream stream = new(DocumentAnnotationTestData.OriginalBytes());
        using DocumentAnnotationTestData.BorrowedNative native = new();
        ExtractionDocumentArtifact artifact = new("application/json", stream.ToArray());
        ExtractionDocumentSourceOutput output = new("operation-A", native, artifact);
        ExtractionDocumentSourcePage page7 = DocumentAnnotationTestData.PdfPage(output);
        ExtractionDocumentSourcePage page2 = new("frame-2", 2, sourceOutput: output);
        document.AddSourcePage(page7);
        document.AddSourcePage(page2);
        document.AddSourcePage(page7);
        document.AddSourceOutput(output);

        Assert.Equal(new[] { page7, page2 }, document.SourcePages);
        Assert.Same(output, page7.SourceOutput);
        Assert.Same(page7.SourceOutput, page2.SourceOutput);
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Equal(stream.ToArray(), page2.SourceOutput!.GetRawContent()!.Content.ToArray());
        Assert.Equal(DocumentAnnotationTestData.OriginalBytes(), stream.ToArray());
        Assert.Equal("visible", Assert.IsType<TextContent>(document.Content).Text);
        Assert.Empty(document.EnumerateContent());
        Assert.Equal(0, stream.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
    }

    [Fact]
    public void DocumentMetadataAndSources_AreConsumableWithoutExtractionResult()
    {
        ExtractionDocument document = new("shared-annotations");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement text = new(new TextContent("visible **text**"), "visible text");
        section.Elements.Add(text);
        document.Sections.Add(section);
        var payload = new { target = text, nested = new { unknown = new object[] { 3, "x" } }, vendorFlag = "kept" };
        ExtractionDocumentSourceOutput output = new("operation", providerName: "synthetic", modelId: "engine:v1");
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage(output);
        document.AddSourcePage(page);
        document.Metadata["vendor-extra"] = payload;
        text.Sources.Add(new ExtractionDocumentSourceReference(page, DocumentAnnotationTestData.Polygon()));

        Assert.Same(payload, document.Metadata["vendor-extra"]);
        Assert.Same(text, payload.target);
        Assert.Equal(new object[] { 3, "x" }, payload.nested.unknown);
        Assert.Equal("kept", payload.vendorFlag);
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Equal("engine:v1", page.SourceOutput!.ModelId);
        Assert.Same(text, Assert.Single(document.EnumerateContent()));
        Assert.Equal("visible text", text.Text);
    }

    [Fact]
    public void SourceRegistries_RejectIdentityConflictsAndAllowFrameNamesAcrossOperations()
    {
        ExtractionDocument document = new("identities");
        ExtractionDocumentSourceOutput first = new("operation-A");
        ExtractionDocumentSourceOutput second = new("operation-B");
        ExtractionDocumentSourcePage firstPage = new("same-frame", 7, sourceOutput: first);
        ExtractionDocumentSourcePage secondPage = new("same-frame", 7, sourceOutput: second);
        document.AddSourcePage(firstPage);
        document.AddSourcePage(secondPage);
        Assert.Throws<InvalidOperationException>(() => document.AddSourceOutput(new ExtractionDocumentSourceOutput("operation-A")));
        Assert.Throws<InvalidOperationException>(() => document.AddSourcePage(new ExtractionDocumentSourcePage("same-frame", 2, sourceOutput: first)));
        ExtractionDocumentSourceReference conflicting = new(new ExtractionDocumentSourcePage("same-frame", 7, sourceOutput: first), DocumentAnnotationTestData.Polygon());
        ExtractionDocumentSection section = new(null);
        document.Sections.Add(section);
        section.Sources.Add(conflicting);
        Assert.Same(conflicting, Assert.Single(section.Sources));
        Assert.Throws<InvalidOperationException>(() => document.AddSourcePage(conflicting.Page));
        Assert.Equal(new[] { first, second }, document.SourceOutputs);
        Assert.Equal(new[] { firstPage, secondPage }, document.SourcePages);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<ExtractionDocumentSourcePage>)document.SourcePages).Clear());
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<ExtractionDocumentSourceOutput>)document.SourceOutputs).Clear());
    }

}
