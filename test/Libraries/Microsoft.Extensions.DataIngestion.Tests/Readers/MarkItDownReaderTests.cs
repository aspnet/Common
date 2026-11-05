// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public class MarkItDownReaderTests : DocumentReaderConformanceTests
{
    protected override IngestionDocumentReader CreateDocumentReader(bool extractImages = false)
    {
        Assert.SkipUnless(MarkItDownConditionHelper.IsInstalled.Value, "MarkItDown is not installed");

        return new MarkItDownReader(extractImages: extractImages);
    }

    protected override void SimpleAsserts(IngestionDocument document, string source, string expectedId)
    {
        Assert.NotNull(document);
        Assert.Equal(expectedId, document.Identifier);
        Assert.NotEmpty(document.Sections);

        var elements = document.EnumerateContent().ToArray();

        bool isPdf = source.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            // MarkItDown does a bad job of recognizing Headers and Tables even for simple PDF files.
            Assert.Contains(elements, element => element is IngestionDocumentHeader);
            Assert.Contains(elements, element => element is IngestionDocumentTable);
        }

        Assert.Contains(elements, element => element is IngestionDocumentParagraph);
        Assert.All(elements, element => Assert.NotEmpty(element.GetMarkdown()));
    }

    // The original purpose of the MarkItDown library was to support text-only LLMs.
    // Source: https://github.com/microsoft/markitdown/issues/56#issuecomment-2546357264
    // It can extract images, but the support is limited to some formats like docx.
    [Fact]
    public override Task SupportsImages() => SupportsImagesCore(
        new("https://winprotocoldocs-bhdugrdyduf5h2e4.b02.azurefd.net/MC-SQLR/%5bMC-SQLR%5d-240423.docx")); // SQL Server Resolution Protocol.
}
