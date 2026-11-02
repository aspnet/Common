// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public sealed class PreviewContractTests
{
    [Fact]
    public void TextContent_BaseMutationRejectsNullAndOtherKindsWithoutLosingPayload()
    {
        TextContent original = new("**literal** <&| OCR 1O0?");
        ExtractionDocumentTextElement text = new(original, role: "Vendor.Custom") { HeadingLevel = int.MaxValue };
        ExtractionDocumentElement element = text;

        Assert.Throws<ArgumentNullException>("value", () => element.Content = null);
        Assert.Same(original, element.Content);
        foreach (AIContent invalid in new AIContent[] { new(), new DataContent(new byte[] { 1 }, "image/png"), new UriContent("urn:asset", "image/png") })
        {
            Assert.Throws<ArgumentException>("value", () => element.Content = invalid);
            Assert.Same(original, text.Content);
        }

        TextContent replacement = new("plain unchanged");
        element.Content = replacement;
        Assert.Same(replacement, text.Content);
        replacement.Text = "mutated **literal**";
        Assert.Equal("mutated **literal**", Assert.IsType<TextContent>(element.Content).Text);
        Assert.Equal("Vendor.Custom", text.Role);
        Assert.Equal(int.MaxValue, text.HeadingLevel);
        Assert.Null(typeof(ExtractionDocumentElement).GetProperty("Text"));
        Assert.Null(typeof(ExtractionDocumentTextElement).GetProperty("Text"));
    }

    [Fact]
    public void OtherElements_KeepFlexibleSinglePayloadState()
    {
        ExtractionDocumentElement[] elements = [new ExtractionDocumentSection(), new ExtractionDocumentTableElement(null, 1, 1), new ExtractionDocumentImageElement(), new CustomElement()];
        foreach (ExtractionDocumentElement element in elements)
        {
            AIContent payload = new();
            element.Content = payload;
            Assert.Same(payload, element.Content);
            element.Content = null;
            Assert.Null(element.Content);
        }
    }

    [Fact]
    public void RootRawRepresentation_ZeroSectionsAndNoPayloadRetainsBorrowedIdentityAndIsJsonIgnored()
    {
        using Native native = new();
        ExtractionDocument document = new("empty") { RawRepresentation = native };

        Assert.Null(document.Content);
        Assert.Empty(document.Sections);
        Assert.Empty(document.EnumerateContent());
        Assert.Same(native, document.RawRepresentation);
        using JsonDocument wire = JsonDocument.Parse(JsonSerializer.Serialize(document));
        Assert.False(wire.RootElement.TryGetProperty(nameof(document.RawRepresentation), out _));
        Assert.Equal("empty", wire.RootElement.GetProperty(nameof(document.Identifier)).GetString());
        Assert.Equal(0, native.InspectionCount);
        Assert.Equal(0, native.DisposeCount);

        object replacement = new();
        document.RawRepresentation = replacement;
        Assert.Same(replacement, document.RawRepresentation);
        Assert.Equal(0, native.DisposeCount);
        document.RawRepresentation = null;
        Assert.Null(document.RawRepresentation);
    }

    [Fact]
    public void SourcePages_OrdinaryListPreservesBlankNamedDuplicateAndMultiPageFrames()
    {
        ExtractionDocument document = new("layout");
        ExtractionDocumentSourcePage named = new("Notebook:Ideas", null);
        ExtractionDocumentSourcePage seven = new("physical:7", 7, new(200, 100),
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
        ExtractionDocumentSourcePage blank = new("blank:2", 2);
        Assert.IsType<List<ExtractionDocumentSourcePage>>(document.SourcePages);
        document.SourcePages.AddRange([named, seven, blank, named]);
        ExtractionDocumentTextElement text = new(new TextContent("unmodified"));
        ExtractionDocumentPoint[] polygon = [new(10, 20), new(30, 10), new(40, 35)];
        text.Sources.Add(new(named));
        text.Sources.Add(new(seven, polygon));
        ExtractionDocumentSection section = new();
        section.Elements.Add(text);
        document.Sections.Add(section);

        Assert.Equal(new[] { named, seven, blank, named }, document.SourcePages);
        Assert.Null(named.PageNumber);
        Assert.Equal(7, seven.PageNumber);
        Assert.Equal(2, blank.PageNumber);
        Assert.Equal(new ExtractionDocumentPageDimensions(200, 100), seven.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, seven.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, seven.CoordinateOrigin);
        Assert.Empty(text.Sources[0].Polygon);
        Assert.Equal(polygon, text.Sources[1].Polygon);
        Assert.Same(text, Assert.Single(document.EnumerateContent()));
        Assert.Null(typeof(ExtractionDocumentSourcePage).GetProperty("SourceOutput"));
        foreach (string removed in new[] { "ExtractionDocumentSourceOutput", "ExtractionDocumentArtifact", "ExtractionDocumentRawContentStatus" })
        {
            Assert.Null(typeof(ExtractionDocument).Assembly.GetType("Microsoft.Extensions.Documents." + removed));
        }
    }

    [Fact]
    public void Image_OcrAlternativeAndOriginalAssetAreIndependentOfPayloadUri()
    {
        UriContent payload = new("urn:payload", "image/png");
        ExtractionDocumentImageElement image = new(payload, text: "OCR 1O0?")
        {
            AlternativeText = "Figure caption",
            Source = new("urn:original")
        };
        Assert.Equal("urn:payload", payload.Uri.ToString());
        Assert.Equal("urn:original", image.Source.ToString());
        Assert.Equal("OCR 1O0?", image.Text);
        Assert.Equal("Figure caption", image.AlternativeText);
        image.Text = null;
        Assert.Null(image.Text);
        Assert.Same(payload, image.Content);
        Assert.Equal("Figure caption", image.AlternativeText);
    }

    private sealed class CustomElement() : ExtractionDocumentElement(null);

    private sealed class Native : IDisposable
    {
        internal int DisposeCount { get; private set; }
        internal int InspectionCount { get; private set; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Public getter is inspected by System.Text.Json if the root's JsonIgnore protection is removed.")]
        public string Value
        {
            get
            {
                InspectionCount++;
                throw new InvalidOperationException("Native results must not be serialized.");
            }
        }

        public void Dispose() => DisposeCount++;
    }
}
