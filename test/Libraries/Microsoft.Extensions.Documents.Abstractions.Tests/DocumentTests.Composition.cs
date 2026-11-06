// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public partial class DocumentTests
{
    [Theory]
    [InlineData("section", false)]
    [InlineData("section", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("image", false)]
    [InlineData("image", true)]
    [InlineData("table", false)]
    [InlineData("table", true)]
    [InlineData("empty-cell", false)]
    [InlineData("empty-cell", true)]
    [InlineData("cell-content", false)]
    [InlineData("cell-content", true)]
    public void Sources_EachOwnerRetainsAssociationsWithExplicitRegistration(string owner, bool attachFirst)
    {
        ExtractionDocument document = new("all-owners");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTableElement table = new(null, 1, 2);
        ExtractionDocumentTableCell empty = new();
        ExtractionDocumentTextElement cellText = new(new TextContent("cell"));
        table.Cells[0, 0] = empty;
        table.Cells[0, 1] = new ExtractionDocumentTableCell(cellText);
        ExtractionDocumentElement element = owner switch
        {
            "text" => new ExtractionDocumentTextElement(new TextContent("visible")),
            "image" => new ExtractionDocumentImageElement(new TextContent("visible")),
            _ => table
        };
        section.Elements.Add(element);
        var sources = owner switch
        {
            "section" => section.Sources,
            "empty-cell" => empty.Sources,
            "cell-content" => cellText.Sources,
            _ => element.Sources
        };
        if (attachFirst)
        {
            document.Sections.Add(section);
        }

        using DocumentAnnotationTestData.CountingStream stream = new(new byte[] { 31, 32 });
        using DocumentAnnotationTestData.BorrowedNative native = new();
        document.RawRepresentation = native;
        ExtractionDocumentSourcePage page = new("frame", 4);
        ExtractionDocumentSourceReference reference = new(page, rawRepresentation: native);
        sources.Add(reference);
        if (!attachFirst)
        {
            Assert.Empty(document.SourcePages);
            document.Sections.Add(section);
        }

        Assert.Empty(document.SourcePages);
        document.SourcePages.Add(page);
        Assert.Same(reference, Assert.Single(sources));
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal(new byte[] { 31, 32 }, stream.ToArray());
        sources.Clear();
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal(0, stream.DisposeCount);
        stream.Dispose();
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Same(native, document.RawRepresentation);
        Assert.Equal(0, native.DisposeCount);
    }

    [Fact]
    public void ApplicationOnlyComposition_PreservesNestedContentWithoutSources()
    {
        ExtractionDocument document = new("application", null);
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentSection nested = new(null);
        ExtractionDocumentTextElement text = new(new TextContent("**local**"));
        ExtractionDocumentImageElement image = new(new TextContent("")) { Source = new Uri("urn:local:image"), AlternativeText = "local diagram" };
        ExtractionDocumentTextElement extension = new(new TextContent("extension"), role: "application-extra");
        ExtractionDocumentTableElement table = new(null, 1, 2);
        ExtractionDocumentTextElement cellText = new(new TextContent("cell|value"));
        table.Cells[0, 0] = new ExtractionDocumentTableCell(cellText);
        table.Cells[0, 1] = new ExtractionDocumentTableCell();
        image.Content = new DataContent(new byte[] { 7, 8 }, "image/png");
        nested.Elements.Add(text);
        nested.Elements.Add(table);
        section.Elements.Add(nested);
        section.Elements.Add(image);
        section.Elements.Add(extension);
        document.Sections.Add(section);

        Assert.Null(document.Content);
        Assert.Equal("**local**", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Equal("cell|value", Assert.IsType<TextContent>(cellText.Content).Text);
        Assert.Equal("application-extra", extension.Role);
        Assert.Equal(new byte[] { 7, 8 }, Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal(new ExtractionDocumentElement[] { text, table, image, extension }, document.EnumerateContent());
        Assert.All(new ExtractionDocumentElement[] { section, nested, text, table, cellText, image, extension }, element => Assert.Empty(element.Sources));
        Assert.All(table.Cells, cell => Assert.Empty(cell!.Sources));
        Assert.Empty(document.SourcePages);
        Assert.Equal(new byte[] { 7, 8 }, Assert.IsType<DataContent>(image.Content).Data.ToArray());
    }
}
