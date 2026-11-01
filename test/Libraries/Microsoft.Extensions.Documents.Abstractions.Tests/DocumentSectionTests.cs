// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentSectionTests
{
    [Fact]
    public void StoresLocalPayloadAndStartsWithEmptyElements()
    {
        ExtractionDocumentSection section = new(new TextContent("stored section markdown"));

        Assert.Equal("stored section markdown", Assert.IsType<TextContent>(section.Content).Text);
        Assert.Empty(section.Elements);
    }

    [Fact]
    public void LocalPayloadMutation_DoesNotPopulateSectionElements()
    {
        ExtractionDocumentSection section = new(new TextContent("stored section markdown"));

        Assert.IsType<TextContent>(section.Content).Text = "plain section text";
        Assert.Equal("plain section text", Assert.IsType<TextContent>(section.Content).Text);
        Assert.Empty(section.Elements);
    }

    [Fact]
    public void NestedSections_AreEnumeratedDepthFirst()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection root = new(null);
        ExtractionDocumentTextElement first = new(new TextContent("first"));
        ExtractionDocumentSection nested = new(null);
        ExtractionDocumentTextElement nestedFirst = new(new TextContent("nested-first"));
        ExtractionDocumentTextElement nestedLast = new(new TextContent("nested-last"));
        ExtractionDocumentTextElement last = new(new TextContent("last"));

        nested.Elements.Add(nestedFirst);
        nested.Elements.Add(nestedLast);
        root.Elements.Add(first);
        root.Elements.Add(nested);
        root.Elements.Add(last);
        document.Sections.Add(root);

        ExtractionDocumentElement[] content = document.EnumerateContent().ToArray();

        Assert.Equal(new ExtractionDocumentElement[] { first, nestedFirst, nestedLast, last }, content);
        Assert.DoesNotContain(nested, content);
    }

}
