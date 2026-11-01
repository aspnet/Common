// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentTextElementTests
{
    [Fact]
    public void StoresRolePayloadAndSemanticText()
    {
        ExtractionDocumentTextElement element = new(new TextContent("stored markdown"), "plain text", "pageFooter");

        Assert.Equal("pageFooter", element.Role);
        Assert.Equal("stored markdown", Assert.IsType<TextContent>(element.Content).Text);
        Assert.Equal("plain text", element.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("heading")]
    [InlineData("pageHeader")]
    [InlineData("pageFooter")]
    [InlineData("vendor:caption")]
    [InlineData("HeAdInG")]
    [InlineData(" Custom Role ")]
    public void Role_NullKnownAndCustomValuesPreserveTextSourcesAndPayload(string? role)
    {
        ExtractionDocument document = new("role", null);
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent("**citation** [1]"), "citation [1]", role);
        ExtractionDocumentSourcePage page = new("citation-page", 7);
        document.AddSourcePage(page);
        ExtractionDocumentSourceReference citation = new(page, DocumentAnnotationTestData.Polygon());
        element.Sources.Add(citation);
        section.Elements.Add(element);
        document.Sections.Add(section);

        Assert.Equal(role, element.Role);
        Assert.Null(element.HeadingLevel);
        Assert.Equal("**citation** [1]", Assert.IsType<TextContent>(element.Content).Text);
        Assert.Equal("citation [1]", element.Text);
        Assert.Null(document.Content);
        Assert.Same(citation, Assert.Single(element.Sources));
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Equal(DocumentAnnotationTestData.Polygon(), citation.Polygon);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Role_EmptyOrWhitespaceRejected(string role)
    {
        Assert.Throws<ArgumentException>(nameof(role), () => new ExtractionDocumentTextElement(new TextContent("markdown"), role: role));
    }

    [Fact]
    public void HeadingAndPageHeader_AreDistinguishedByRoleWithoutChangingPayload()
    {
        ExtractionDocumentTextElement heading = new(new TextContent("Title"), role: "heading") { HeadingLevel = 1 };
        ExtractionDocumentTextElement pageHeader = new(new TextContent("Title"), role: "pageHeader");
        ExtractionDocumentTextElement custom = new(new TextContent("ordinary **text**"), role: "VendorHeading") { HeadingLevel = 10 };

        Assert.Equal("heading", heading.Role);
        Assert.Equal("pageHeader", pageHeader.Role);
        Assert.NotEqual(heading.Role, pageHeader.Role);
        Assert.Equal(1, heading.HeadingLevel);
        Assert.Null(pageHeader.HeadingLevel);
        Assert.Equal("VendorHeading", custom.Role);
        Assert.Equal(10, custom.HeadingLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void HeadingLevel_AcceptsOneThroughTen(int level)
    {
        ExtractionDocumentTextElement element = new(new TextContent("header"), role: "heading")
        {
            HeadingLevel = level
        };

        Assert.Equal(level, element.HeadingLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void HeadingLevel_RejectsZeroAndEleven(int level)
    {
        ExtractionDocumentTextElement element = new(new TextContent("header"), role: "heading")
        {
            HeadingLevel = 5
        };

        Assert.Throws<ArgumentOutOfRangeException>("value", () => element.HeadingLevel = level);
        Assert.Equal(5, element.HeadingLevel);
    }

    [Fact]
    public void HeadingLevel_SameValuePreservesHeadingAndPayload()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ExtractionDocumentTextElement element = new(new TextContent("header"), role: "heading")
        {
            HeadingLevel = 3
        };
        document.Sections.Add(section);
        section.Elements.Add(element);

        element.HeadingLevel = 3;

        element.HeadingLevel = null;
        Assert.Null(element.HeadingLevel);
        Assert.Equal("heading", element.Role);
    }
}
