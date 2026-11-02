// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionDocumentTests
{
    private readonly ExtractionDocumentElement?[,] _rows =
    {
        { new ExtractionDocumentTextElement(new TextContent("header")) },
        { new ExtractionDocumentTextElement(new TextContent("row1")) },
        { new ExtractionDocumentTextElement(new TextContent("row2")) }
    };

    [Fact]
    public void EnumeratorFlattensTheStructureAndPreservesOrder()
    {
        ExtractionDocument doc = new("withSubSections");
        doc.Sections.Add(new ExtractionDocumentSection(new TextContent("first section"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("header"), role: "heading"),
                new ExtractionDocumentTextElement(new TextContent("paragraph")),
                TestDocumentFactory.CreateTable(_rows, new TextContent("table")),
                new ExtractionDocumentSection(new TextContent("nested section"))
                {
                    Elements =
                    {
                        new ExtractionDocumentTextElement(new TextContent("nested header"), role: "heading"),
                        new ExtractionDocumentTextElement(new TextContent("nested paragraph"))
                    }
                }
            }
        });
        doc.Sections.Add(new ExtractionDocumentSection(new TextContent("second section"))
        {
            Elements =
            {
                new ExtractionDocumentTextElement(new TextContent("header 2"), role: "heading"),
                new ExtractionDocumentTextElement(new TextContent("paragraph 2"))
            }
        });

        ExtractionDocumentElement[] flatElements = doc.EnumerateContent().ToArray();

        Assert.Equal(7, flatElements.Length);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[0]).Role);
        Assert.Equal("header", Assert.IsType<TextContent>(flatElements[0].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[1]).Role);
        Assert.Equal("paragraph", Assert.IsType<TextContent>(flatElements[1].Content).Text);
        Assert.IsType<ExtractionDocumentTableElement>(flatElements[2]);
        Assert.Equal("table", Assert.IsType<TextContent>(flatElements[2].Content).Text);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[3]).Role);
        Assert.Equal("nested header", Assert.IsType<TextContent>(flatElements[3].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[4]).Role);
        Assert.Equal("nested paragraph", Assert.IsType<TextContent>(flatElements[4].Content).Text);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(flatElements[5]).Role);
        Assert.Equal("header 2", Assert.IsType<TextContent>(flatElements[5].Content).Text);
        Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(flatElements[6]).Role);
        Assert.Equal("paragraph 2", Assert.IsType<TextContent>(flatElements[6].Content).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyParagraphDocumentCantBeCreated(string? input)
    {
        // The shared model rejects a null payload, not empty or null text within TextContent.
        Assert.Throws<ArgumentNullException>("content", () => new ExtractionDocumentTextElement(null!));
        TextContent content = new(input);
        ExtractionDocumentTextElement paragraph = new(content);
        Assert.Same(content, paragraph.Content);
        Assert.Equal(string.Empty, content.Text);
        Assert.Null(paragraph.Role);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidHeaderLevelThrows(int level)
    {
        ExtractionDocumentTextElement header = new(new TextContent("header"), role: "heading");

        Assert.Throws<ArgumentOutOfRangeException>("value", () => header.HeadingLevel = level);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void PositiveHeaderLevelsArePreserved(int level)
    {
        ExtractionDocumentTextElement header = new(new TextContent("header"), role: "heading") { HeadingLevel = level };

        Assert.Equal(level, header.HeadingLevel);
        Assert.Equal("header", Assert.IsType<TextContent>(header.Content).Text);
        Assert.Equal("heading", header.Role);
    }
}
