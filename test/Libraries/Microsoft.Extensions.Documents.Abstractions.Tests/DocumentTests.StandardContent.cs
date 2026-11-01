// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public partial class DocumentTests
{
    [Fact]
    public void RootOnlyPayload_ReusesCurrentStringWithoutFormatting()
    {
        string markdown = new('x', 4096);
        TextContent payload = new(markdown);
        ExtractionDocument document = new("root-only", payload);

        Assert.Same(payload, document.Content);
        Assert.Same(markdown, Assert.IsType<TextContent>(document.Content).Text);
        payload.Text = "**changed**";
        Assert.Equal("**changed**", Assert.IsType<TextContent>(document.Content).Text);
        document.Content = null;
        Assert.Null(document.Content);
        Assert.Empty(document.EnumerateContent());
    }

    [Fact]
    public void LeafSectionPayload_RemainsLocalAndNullable()
    {
        string markdown = new('x', 4096);
        ExtractionDocumentSection section = new(new TextContent(markdown));

        Assert.Same(markdown, Assert.IsType<TextContent>(section.Content).Text);
        Assert.Empty(section.Elements);
        section.Content = null;
        Assert.Null(section.Content);
    }

    [Fact]
    public void TransparentContainers_PreserveSingleDescendantAndPayloadIdentity()
    {
        string markdown = new('x', 4096);
        TextContent payload = new(markdown);
        ExtractionDocumentTextElement text = new(payload);
        ExtractionDocumentSection inner = new();
        ExtractionDocumentSection outer = new();
        inner.Elements.Add(text);
        outer.Elements.Add(inner);
        ExtractionDocument document = new("transparent");
        document.Sections.Add(outer);

        Assert.Null(document.Content);
        Assert.Null(outer.Content);
        Assert.Null(inner.Content);
        Assert.Same(inner, Assert.Single(outer.Elements));
        Assert.Same(text, Assert.Single(inner.Elements));
        Assert.Same(text, Assert.Single(document.EnumerateContent()));
        Assert.Same(payload, text.Content);
        Assert.Same(markdown, payload.Text);
        payload.Text = "updated";
        Assert.Equal("updated", Assert.IsType<TextContent>(text.Content).Text);
    }

    [Fact]
    public void FlatTextPayloads_PreserveEmptyEntriesAndObserveEdits()
    {
        TextContent first = new("**first**");
        ExtractionDocumentSection section = new();
        section.Elements.Add(new ExtractionDocumentTextElement(first));
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent(string.Empty)));
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("last|value")));
        ExtractionDocument document = new("flat");
        document.Sections.Add(section);

        Assert.Equal(new[] { "**first**", "", "last|value" },
            document.EnumerateContent().Select(element => Assert.IsType<TextContent>(element.Content).Text));
        first.Text = "changed";
        Assert.Equal(new[] { "changed", "", "last|value" },
            document.EnumerateContent().Select(element => Assert.IsType<TextContent>(element.Content).Text));
    }

    [Fact]
    public void EmptyLocalPayloads_AreDistinctFromAbsentPayloads()
    {
        ExtractionDocument document = new("empty-local", new TextContent(string.Empty));
        ExtractionDocumentSection section = new(new TextContent(string.Empty));
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("child")));
        document.Sections.Add(section);

        Assert.Equal("", Assert.IsType<TextContent>(document.Content).Text);
        Assert.Equal("", Assert.IsType<TextContent>(section.Content).Text);
        document.Content = null;
        Assert.Null(document.Content);
        Assert.Equal("", Assert.IsType<TextContent>(section.Content).Text);
        section.Content = null;
        Assert.Null(section.Content);
        Assert.Equal("child", Assert.IsType<TextContent>(Assert.Single(document.EnumerateContent()).Content).Text);
    }

    [Fact]
    public void CurrentPayloadKind_RemainsVisibleWithoutImplicitConversion()
    {
        ExtractionDocumentSection section = new();
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("first")));
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("second")) { Content = new AIContent() });
        ExtractionDocument document = new("unsupported-flat");
        document.Sections.Add(section);

        Assert.Equal(section.Elements, document.EnumerateContent());
        Assert.IsType<AIContent>(section.Elements[1].Content);
        Assert.Equal("first", Assert.IsType<TextContent>(section.Elements[0].Content).Text);
    }

    [Fact]
    public void CustomElements_PreserveLocalPayloadAndGridIdentity()
    {
        CustomElement custom = new(new TextContent("custom|value"));
        ExtractionDocumentSection section = new();
        section.Elements.Add(custom);
        ExtractionDocument document = new("custom-projection");
        document.Sections.Add(section);

        Assert.Same(custom, Assert.Single(document.EnumerateContent()));
        Assert.Equal("custom|value", Assert.IsType<TextContent>(custom.Content).Text);
        section.Elements.Insert(0, new ExtractionDocumentTextElement(new TextContent("prefix")));
        Assert.Equal(section.Elements, document.EnumerateContent());

        CustomElement cellContent = new(new TextContent("cell|value"));
        ExtractionDocumentTableElement table = new(null, 1, 1);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(cellContent);
        section.Elements.Add(table);
        Assert.Equal(section.Elements, document.EnumerateContent());
        Assert.Same(cellContent, table.Cells[0, 0]!.Content);
        Assert.Equal("cell|value", Assert.IsType<TextContent>(cellContent.Content).Text);
    }

    [Fact]
    public void StandardTextContent_PreservesSharedStringPayloadAndIndependentSemanticText()
    {
        string markdown = new(['*', '*', 'O', 'C', 'R', '*', '*']);
        TextContent payload = new(markdown);
        ExtractionDocument document = new("shared-text", payload);
        ExtractionDocumentSection section = new(payload);
        ExtractionDocumentTextElement text = new(payload, "OCR", "heading") { HeadingLevel = 2 };
        section.Elements.Add(text);
        document.Sections.Add(section);

        Assert.Same(payload, document.Content);
        Assert.Same(payload, section.Content);
        Assert.Same(payload, text.Content);
        Assert.Same(markdown, payload.Text);
        Assert.Equal("OCR", text.Text);
        Assert.Equal("heading", text.Role);
        Assert.Equal(2, text.HeadingLevel);
    }

    [Fact]
    public void NullableContainers_KeepLocalContentSeparateFromDescendants()
    {
        ExtractionDocument document = new("local", new TextContent("root introduction"));
        ExtractionDocumentSection section = new(new TextContent("section introduction"));
        ExtractionDocumentSection transparent = new();
        ExtractionDocumentTableElement table = new(new TextContent("table caption"), 1, 1);
        ExtractionDocumentTextElement cellText = new(new TextContent("leaf|value"), "leaf value");
        table.Cells[0, 0] = new ExtractionDocumentTableCell(cellText);
        transparent.Elements.Add(table);
        section.Elements.Add(transparent);
        document.Sections.Add(section);

        Assert.Equal("root introduction", Assert.IsType<TextContent>(document.Content).Text);
        Assert.Equal("section introduction", Assert.IsType<TextContent>(section.Content).Text);
        Assert.Null(transparent.Content);
        Assert.Same(table, Assert.Single(document.EnumerateContent()));
        Assert.Same(cellText, table.Cells[0, 0]!.Content);
        Assert.Equal("leaf value", cellText.Text);

        cellText.Content = new TextContent("edited");
        Assert.Equal("edited", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        Assert.Equal("leaf value", cellText.Text);
        Assert.Equal("table caption", Assert.IsType<TextContent>(table.Content).Text);
    }

    [Fact]
    public void NullPayloads_AreAbsentRatherThanFabricatedText()
    {
        ExtractionDocument document = new("empty");
        ExtractionDocumentSection section = new();
        ExtractionDocumentImageElement image = new();
        section.Elements.Add(image);
        document.Sections.Add(section);

        Assert.Null(document.Content);
        Assert.Null(section.Content);
        Assert.Null(image.Content);
        Assert.Null(image.Text);
        Assert.Null(image.AlternativeText);
        Assert.Same(image, Assert.Single(document.EnumerateContent()));
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("root", true)]
    [InlineData("section", false)]
    [InlineData("section", true)]
    [InlineData("table", false)]
    [InlineData("table", true)]
    public void StructuralLocalImagePayloads_RemainIndependentOfChildren(string owner, bool uri)
    {
        AIContent payload = uri ? new UriContent("urn:local:image", "image/png") :
            new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        ExtractionDocument document = new("local-image", owner == "root" ? payload : null);
        ExtractionDocumentSection section = new(owner == "section" ? payload : null);
        ExtractionDocumentTableElement table = new(owner == "table" ? payload : null, 1, 1);
        ExtractionDocumentTextElement text = new(new TextContent("cell"));
        table.Cells[0, 0] = new ExtractionDocumentTableCell(text);
        section.Elements.Add(table);
        document.Sections.Add(section);
        AIContent? selected = owner switch
        {
            "root" => document.Content,
            "section" => section.Content,
            _ => table.Content
        };
        Assert.Same(payload, selected);
        Assert.Same(text, table.Cells[0, 0]!.Content);
        Assert.Empty(document.SourceOutputs);
    }

    [Fact]
    public void BorrowedPayloadMutation_IsVisibleWithoutChangingSemanticText()
    {
        TextContent payload = new("first");
        ExtractionDocument document = new("mutable");
        ExtractionDocumentSection section = new();
        ExtractionDocumentTextElement text = new(payload, "stable semantic");
        section.Elements.Add(text);
        document.Sections.Add(section);

        payload.Text = "second";

        Assert.Equal("second", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Same(payload, text.Content);
        Assert.Equal("stable semantic", text.Text);
        text.Content = new TextContent("second");
        Assert.NotSame(payload, text.Content);
        Assert.Equal("second", Assert.IsType<TextContent>(text.Content).Text);
    }

    [Fact]
    public void OwnedDataBuffer_IsBorrowedByActualImageWithoutCopy()
    {
        byte[] owned = [1, 2, 3];
        DataContent payload = new(owned, "image/png");
        ExtractionDocumentImageElement image = new(payload, "OCR diagram") { AlternativeText = "diagram" };

        Assert.Same(payload, image.Content);
        Assert.True(MemoryMarshal.TryGetArray(payload.Data, out ArraySegment<byte> segment));
        Assert.Same(owned, segment.Array);
        Assert.Equal(0, segment.Offset);
        Assert.Equal(3, segment.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Data.ToArray());
        Assert.Equal("OCR diagram", image.Text);
    }

    [Fact]
    public void MutableProviderBuffer_RequiresExplicitCopyAtOwnershipBoundary()
    {
        byte[] providerBuffer = [1, 2, 3];
        ExtractionDocumentImageElement borrowed = new(new DataContent(providerBuffer, "image/png"));

        // The adapter, not ExtractionDocument or DataContent, owns the decision to retain a stable copy.
        byte[] retained = providerBuffer.ToArray();
        ExtractionDocumentImageElement retainedImage = new(new DataContent(retained, "image/png"));
        providerBuffer[0] = 9;

        DataContent borrowedPayload = Assert.IsType<DataContent>(borrowed.Content);
        DataContent retainedPayload = Assert.IsType<DataContent>(retainedImage.Content);
        Assert.Equal(new byte[] { 9, 2, 3 }, borrowedPayload.Data.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, retainedPayload.Data.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(retainedPayload.Data, out ArraySegment<byte> segment));
        Assert.Same(retained, segment.Array);
        Assert.NotSame(providerBuffer, segment.Array);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StandardImagePayloads_KeepCanonicalReferenceAndProvenanceIndependent(bool data, bool provenance)
    {
        AIContent payload = data ? new DataContent(new byte[] { 1, 2, 3 }, "image/png") :
            new UriContent(new Uri("urn:canonical:image"), "image/png");
        Uri? source = provenance ? new Uri("urn:provenance:image") : null;
        ExtractionDocumentImageElement image = new(payload, "OCR text")
        {
            Source = source,
            AlternativeText = "independent alternative"
        };
        Assert.Same(payload, image.Content);
        Assert.Same(source, image.Source);
        Assert.Equal("OCR text", image.Text);
        Assert.Equal("independent alternative", image.AlternativeText);
        if (payload is UriContent uri)
        {
            Assert.Equal("urn:canonical:image", uri.Uri.ToString());
            Assert.Equal("image/png", uri.MediaType);
        }
        else
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<DataContent>(payload).Data.ToArray());
            Assert.Equal("image/png", Assert.IsType<DataContent>(payload).MediaType);
        }
    }

    [Theory]
    [InlineData("root")]
    [InlineData("section")]
    [InlineData("table")]
    [InlineData("text")]
    [InlineData("image")]
    public void ArbitraryStandardPayload_RemainsInspectableAtEachOwner(string owner)
    {
        AIContent payload = new();
        ExtractionDocument document = new("unsupported", owner == "root" ? payload : null);
        ExtractionDocumentElement element = owner switch
        {
            "section" => new ExtractionDocumentSection(payload),
            "table" => new ExtractionDocumentTableElement(payload, 1, 1),
            "image" => new ExtractionDocumentImageElement(payload) { Source = new Uri("urn:must-not-mask-unsupported") },
            _ => new ExtractionDocumentTextElement(new TextContent("initial")) { Content = payload }
        };
        if (owner != "root")
        {
            ExtractionDocumentSection section = new();
            section.Elements.Add(element);
            document.Sections.Add(section);
            Assert.Same(element, Assert.Single(section.Elements));
            if (element is ExtractionDocumentSection)
            {
                Assert.Empty(document.EnumerateContent());
            }
            else
            {
                Assert.Same(element, Assert.Single(document.EnumerateContent()));
            }
        }

        Assert.Same(payload, owner == "root" ? document.Content : element.Content);
        Assert.Empty(document.SourceOutputs);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NonImageDataAndUri_AreNotConvertedToText(bool uri, bool onImage)
    {
        AIContent payload = uri ? new UriContent("urn:raw:body", "application/json") :
            new DataContent(new byte[] { 123, 125 }, "application/json");
        ExtractionDocument document = new("raw", onImage ? null : payload);
        ExtractionDocumentImageElement image = new(payload, "semantic OCR")
        {
            Source = new Uri("urn:provenance:must-not-hide-invalid-type"),
            AlternativeText = "raw is not an image"
        };
        if (onImage)
        {
            ExtractionDocumentSection section = new();
            section.Elements.Add(image);
            document.Sections.Add(section);
            Assert.Same(image, Assert.Single(document.EnumerateContent()));
        }

        Assert.Equal("semantic OCR", image.Text);
        Assert.Same(payload, image.Content);
        Assert.Same(payload, onImage ? image.Content : document.Content);
        Assert.Equal("application/json", uri ? Assert.IsType<UriContent>(payload).MediaType : Assert.IsType<DataContent>(payload).MediaType);
    }

    [Fact]
    public void RealTableAndSourceMultiplicity_PreserveSharedPayloadAndDistinctElements()
    {
        ExtractionDocument document = new("table-identity");
        ExtractionDocumentSection section = new();
        ExtractionDocumentTableElement table = new(null, 1, 2);
        TextContent shared = new("same|payload");
        ExtractionDocumentTextElement first = new(shared, "first semantic");
        ExtractionDocumentTextElement second = new(shared, "second semantic");
        ExtractionDocumentSourceOutput output = new("provider");
        ExtractionDocumentSourcePage page = new("page", 1, sourceOutput: output);
        document.AddSourcePage(page);
        ExtractionDocumentSourceReference reference = new(page);
        first.Sources.Add(reference);
        first.Sources.Add(reference);
        second.Sources.Add(reference);
        table.Sources.Add(reference);
        ExtractionDocumentTableCell firstCell = new(first);
        firstCell.Sources.Add(reference);
        table.Cells[0, 0] = firstCell;
        table.Cells[0, 1] = new ExtractionDocumentTableCell(second);
        section.Elements.Add(table);
        document.Sections.Add(section);

        Assert.Same(firstCell, table.Cells[0, 0]);
        Assert.Same(first, table.Cells[0, 0]!.Content);
        Assert.Same(second, table.Cells[0, 1]!.Content);
        Assert.NotSame(first, second);
        Assert.Same(shared, first.Content);
        Assert.Same(shared, second.Content);
        Assert.Equal(new[] { reference, reference }, first.Sources);
        Assert.Same(reference, Assert.Single(firstCell.Sources));
        Assert.Same(reference, Assert.Single(table.Sources));
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Same(output, Assert.Single(document.SourceOutputs));
        Assert.Equal("first semantic", first.Text);
        Assert.Equal("second semantic", second.Text);
    }

    [Fact]
    public void ActualMeaiSerializationAndTextSelection_ConsumeDocumentPayloadsWithoutDocumentWireContract()
    {
        ExtractionDocumentTextElement text = new(new TextContent("**literal markdown**"), "plain OCR");
        ExtractionDocumentImageElement image = new(new DataContent(new byte[] { 1, 2, 3 }, "image/png"), "diagram OCR")
        {
            AlternativeText = "diagram alternative",
            Source = new Uri("urn:provenance")
        };
        UriContent uriPayload = new("urn:canonical:image", "image/jpeg");
        ExtractionDocumentImageElement reference = new(uriPayload);
        ChatMessage message = new(ChatRole.User, [text.Content!, image.Content!, reference.Content!]);

        string json = JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions);
        using JsonDocument wire = JsonDocument.Parse(json);
        JsonElement contents = wire.RootElement.GetProperty("contents");
        Assert.Equal(3, contents.GetArrayLength());
        Assert.Equal("text", contents[0].GetProperty("$type").GetString());
        Assert.Equal("**literal markdown**", contents[0].GetProperty("text").GetString());
        Assert.Equal("data:image/png;base64,AQID", contents[1].GetProperty("uri").GetString());
        Assert.Equal("urn:canonical:image", contents[2].GetProperty("uri").GetString());
        Assert.Equal("image/jpeg", contents[2].GetProperty("mediaType").GetString());
        Assert.Equal("**literal markdown**", message.Text);
        Assert.Same(image.Content, message.Contents[1]);
        Assert.Same(uriPayload, message.Contents[2]);

        // A text-only consumer must select semantic text explicitly; this is not MEDI ingestion acceptance.
        ChatMessage semantic = new(ChatRole.User, [new TextContent(text.Text!), new TextContent(image.Text!)]);
        Assert.Equal("plain OCRdiagram OCR", semantic.Text);
        Assert.DoesNotContain("AQID", semantic.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("urn:", semantic.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("diagram alternative", semantic.Text, StringComparison.Ordinal);
        Assert.Equal("diagram alternative", image.AlternativeText);
        Assert.Equal("urn:provenance", image.Source!.ToString());
    }

    private sealed class CustomElement(AIContent? content) : ExtractionDocumentElement(content);
}
