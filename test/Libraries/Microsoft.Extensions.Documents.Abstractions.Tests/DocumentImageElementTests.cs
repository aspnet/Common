// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.Documents.Tests;

public class DocumentImageElementTests
{
    [Fact]
    public void PreservesContentMediaTypeSourceAndAlternativeText()
    {
        ExtractionDocumentImageElement image = new(new TextContent("stored image"));
        ReadOnlyMemory<byte> content = new byte[] { 1, 2, 3 };
        Uri source = new("urn:image:opaque-7");

        DataContent payload = new(content, "image/png");
        image.Content = payload;
        image.Source = source;
        image.AlternativeText = "diagram";

        Assert.Same(payload, image.Content);
        Assert.Equal(content.ToArray(), Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal("image/png", payload.MediaType);
        Assert.Same(source, image.Source);
        Assert.Equal("diagram", image.AlternativeText);
    }

    [Fact]
    public void SourceAndAlternativeText_DoNotReplaceLocalPayload()
    {
        ExtractionDocumentImageElement image = new(new TextContent("stored image"))
        {
            Source = new Uri("urn:image:opaque-8"),
            AlternativeText = "a diagram"
        };

        Assert.Equal("stored image", Assert.IsType<TextContent>(image.Content).Text);
        Assert.Equal("urn:image:opaque-8", image.Source!.ToString());
        Assert.Equal("a diagram", image.AlternativeText);
    }

    [Fact]
    public void AbsentSource_DoesNotDiscardLocalPayloadOrAlternativeText()
    {
        ExtractionDocumentImageElement image = new(new TextContent("stored image markdown"))
        {
            AlternativeText = "unused alternative text"
        };

        Assert.Null(image.Source);
        Assert.Equal("stored image markdown", Assert.IsType<TextContent>(image.Content).Text);
        Assert.Equal("unused alternative text", image.AlternativeText);
    }

    [Fact]
    public void SameScalarAssignments_PreserveImagePayloadAndDescription()
    {
        ExtractionDocument document = new("document");
        ExtractionDocumentSection section = new(null);
        ReadOnlyMemory<byte> content = new byte[] { 4, 5 };
        Uri source = new("urn:image:opaque-9");
        DataContent payload = new(content, "image/jpeg");
        ExtractionDocumentImageElement image = new(payload)
        {
            Source = source,
            AlternativeText = "photo"
        };
        document.Sections.Add(section);
        section.Elements.Add(image);

        image.Content = payload;
        image.Source = source;
        image.AlternativeText = "photo";

        Assert.Same(payload, image.Content);
        Assert.Equal("image/jpeg", payload.MediaType);
        Assert.Same(source, image.Source);
        Assert.Equal("photo", image.AlternativeText);
    }

    [Fact]
    public void ImageOnlyContent_PreservesBytesSourceDescriptionAndDetachedArtifact()
    {
        ExtractionDocument document = new("image-only", null);
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02];
        using DocumentAnnotationTestData.CountingStream stream = new(bytes);
        Uri source = new("urn:image:page7");
        ExtractionDocumentArtifact artifact = new("image/png", stream.ToArray(), source);
        DataContent payload = new(bytes, "image/png");
        ExtractionDocumentImageElement image = new(payload)
        {
            Source = source,
            AlternativeText = "diagram only"
        };
        ExtractionDocumentSourceOutput output = new("image-operation", rawContent: artifact);
        ExtractionDocumentSourcePage page = DocumentAnnotationTestData.PdfPage(output);
        document.AddSourcePage(page);
        image.Sources.Add(new ExtractionDocumentSourceReference(page, DocumentAnnotationTestData.Polygon()));
        ExtractionDocumentSection section = new(null);
        section.Elements.Add(image);
        document.Sections.Add(section);

        Assert.Same(payload, image.Content);
        Assert.Equal(bytes, Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal(bytes, stream.ToArray());
        Assert.Equal("image/png", payload.MediaType);
        Assert.Same(source, image.Source);
        Assert.Same(source, artifact.Source);
        Assert.Equal("diagram only", image.AlternativeText);
        Assert.Null(image.Text);
        Assert.Null(document.Content);
        Assert.Same(image, Assert.Single(document.EnumerateContent()));
        Assert.Same(page, Assert.Single(document.SourcePages));
        Assert.Same(artifact, Assert.Single(document.SourceOutputs).GetRawContent());
        Assert.Equal(0, stream.DisposeCount);
        stream.Dispose();
        Assert.Equal(bytes, artifact.Content.ToArray());
        Assert.Equal(bytes, Assert.IsType<DataContent>(image.Content).Data.ToArray());
    }

}
