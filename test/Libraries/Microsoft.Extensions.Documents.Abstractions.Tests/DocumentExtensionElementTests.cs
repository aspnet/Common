// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentExtensionElementTests
{
    [Fact]
    public void Cleanup_RemovesObsoletePublicTypesAndMembers()
    {
        var assembly = typeof(ExtractionDocument).Assembly;
        foreach (string name in new[] { "DocumentArtifactCollection", "DocumentExtensionElement", "DocumentTextKind" })
        {
            Assert.Null(assembly.GetType("Microsoft.Extensions.Documents." + name));
        }

        foreach (Type type in new[]
        {
            typeof(ExtractionDocument), typeof(ExtractionDocumentElement), typeof(ExtractionDocumentSection),
            typeof(ExtractionDocumentTextElement), typeof(ExtractionDocumentImageElement), typeof(ExtractionDocumentTableElement)
        })
        {
            Assert.Null(type.GetProperty("StoredMarkdown"));
            Assert.Null(type.GetMethod("GetMarkdown"));
            Assert.Null(type.GetProperty("Markdown"));
            Assert.Equal(typeof(AIContent), type.GetProperty("Content")!.PropertyType);
        }

        foreach (string name in new[] { "Metadata", "HasMetadata", "Artifacts" })
        {
            Assert.Null(typeof(ExtractionDocumentElement).GetProperty(name));
            Assert.Null(typeof(ExtractionDocumentElement).GetProperty(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        }

        Assert.Null(typeof(ExtractionDocumentTextElement).GetProperty("HeaderLevel"));
        Assert.Null(typeof(ExtractionDocumentTextElement).GetProperty("Kind"));
        Assert.Equal(typeof(int?), typeof(ExtractionDocumentTextElement).GetProperty("HeadingLevel")!.PropertyType);
        Assert.Equal(typeof(string), typeof(ExtractionDocumentTextElement).GetProperty("Role")!.PropertyType);
        Assert.False(typeof(ExtractionDocumentTextElement).GetProperty("Role")!.CanWrite);
        var parameters = Assert.Single(typeof(ExtractionDocumentTextElement).GetConstructors()).GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("content", parameters[0].Name);
        Assert.Equal("role", parameters[1].Name);
        Assert.Equal(typeof(TextContent), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.False(parameters[0].IsOptional);
        Assert.True(parameters[1].IsOptional);
        Assert.Null(parameters[1].DefaultValue);
        Assert.Equal(typeof(System.Collections.Generic.Dictionary<string, object>),
            typeof(ExtractionDocument).GetProperty("Metadata")!.PropertyType);
        Assert.Null(typeof(ExtractionDocument).GetProperty("Artifacts"));
    }

    [Fact]
    public void RootMetadataAndSources_PreserveOpaqueNestedPayloadAndNativeObservations()
    {
        ExtractionDocument document = new("payload");
        using DocumentAnnotationTestData.BorrowedNative nestedNative = new();
        var payload = new { kind = "vendor-extra", nested = new { unknown = new object[] { 3, "x" }, native = nestedNative } };
        var attributes = new { vendorFlag = "kept", provenance = "urn:synthetic:engine:v1", native = nestedNative };
        ExtractionDocumentTextElement extension = new(new TextContent("visible **text**"), role: "vendor-extra");
        ExtractionDocumentSourceReference first = new(DocumentAnnotationTestData.PdfPage(), DocumentAnnotationTestData.Polygon(), attributes);
        ExtractionDocumentSourceReference second = new(new ExtractionDocumentSourcePage("frame-2", 2), DocumentAnnotationTestData.Polygon());
        extension.Sources.Add(first);
        extension.Sources.Add(second);
        document.SourcePages.Add(first.Page);
        document.SourcePages.Add(second.Page);
        document.RawRepresentation = payload;
        document.Metadata["vendor-extra"] = payload;
        document.Metadata["vendorFlag"] = "kept";
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(extension);
        document.Sections.Add(section);

        Assert.Same(payload, document.Metadata["vendor-extra"]);
        Assert.Equal("vendor-extra", extension.Role);
        Assert.Equal(new object[] { 3, "x" }, payload.nested.unknown);
        Assert.Same(attributes, first.GetRawRepresentation());
        Assert.Equal("kept", attributes.vendorFlag);
        Assert.Equal("urn:synthetic:engine:v1", attributes.provenance);
        Assert.Equal(new[] { first, second }, extension.Sources);
        Assert.Equal(new[] { first.Page, second.Page }, document.SourcePages);
        Assert.Same(extension, Assert.Single(document.EnumerateContent()));
        Assert.Equal("kept", document.Metadata["vendorFlag"]);
        Assert.Equal("visible **text**", Assert.IsType<TextContent>(extension.Content).Text);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<ExtractionDocumentPoint>)first.Polygon).Clear());
        Assert.Same(payload, document.RawRepresentation);
        Assert.Same(attributes, first.GetRawRepresentation());
        Assert.Same(nestedNative, payload.nested.native);
        Assert.Same(nestedNative, attributes.native);
        Assert.Equal(0, nestedNative.DisposeCount);
    }
}
