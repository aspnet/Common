// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public class MarkdownReaderTests : DocumentReaderConformanceTests
{
    protected override IngestionDocumentReader CreateDocumentReader(bool extractImages = false) => new MarkdownReader();

    public static new TheoryData<string> Links =>
    [
        "https://raw.githubusercontent.com/microsoft/markitdown/main/README.md"
    ];

    [Theory]
    [MemberData(nameof(Links))]
    public override Task SupportsStreams(string source) => base.SupportsStreams(source);

    [Theory]
    [MemberData(nameof(Links))]
    public override Task SupportsFiles(string source) => base.SupportsFiles(source);

    [Fact]
    public override async Task SupportsTables()
    {
        string markdownContent = """
        # Key Milestones

        | **Milestone** | **Target Date** | **Department** | **Indicator** |
        | --- | --- | --- | --- |
        | Environmental Audit | Mar 2025 | Environmental | Audit Complete |
        | Renewable Energy Launch | Jul 2025 | Facilities | Install Operational |
        | Staff Workshop | Sep 2025 | HR | Workshop Held |
        | Emissions Review | Dec 2029 | All | 25% Emissions Cut |
        """;

        IngestionDocument document = await ReadAsync(markdownContent);

        IngestionDocumentTable documentTable = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());
        Assert.Equal(5, documentTable.Cells.GetLength(0));
        Assert.Equal(4, documentTable.Cells.GetLength(1));

        string[,] expected =
        {
            { "**Milestone**", "**Target Date**", "**Department**", "**Indicator**" },
            { "Environmental Audit", "Mar 2025", "Environmental", "Audit Complete" },
            { "Renewable Energy Launch", "Jul 2025", "Facilities", "Install Operational" },
            { "Staff Workshop", "Sep 2025", "HR", "Workshop Held" },
            { "Emissions Review", "Dec 2029", "All", "25% Emissions Cut" }
        };

        Assert.Equal(expected, documentTable.Cells.Map(element => element!.GetMarkdown().Trim()));
    }

    [Fact]
    public async Task SupportsTablesWithoutTrailingPipes()
    {
        // Markdown tables without trailing pipes (|) at the end of each row should be parsed correctly.
        // This was causing IndexOutOfRangeException before the fix.
        string markdownContent = """
        # ReadyToRun Flags
        
        | Flag                                       |      Value | Description
        |:-------------------------------------------|-----------:|:-----------
        | READYTORUN_FLAG_PLATFORM_NEUTRAL_SOURCE    | 0x00000001 | Set if the original IL image was platform neutral.
        | READYTORUN_FLAG_COMPOSITE                  | 0x00000002 | The image represents a composite R2R file.
        | READYTORUN_FLAG_PARTIAL                    | 0x00000004 |
        | READYTORUN_FLAG_NONSHARED_PINVOKE_STUBS    | 0x00000008 | PInvoke stubs compiled into image are non-shareable.
        | READYTORUN_FLAG_EMBEDDED_MSIL              | 0x00000010 | Input MSIL is embedded in the R2R image.
        | READYTORUN_FLAG_COMPONENT                  | 0x00000020 | This is a component assembly of a composite R2R image
        | READYTORUN_FLAG_MULTIMODULE_VERSION_BUBBLE | 0x00000040 | This R2R module has multiple modules within its version bubble.
        | READYTORUN_FLAG_UNRELATED_R2R_CODE         | 0x00000080 | This R2R module has code in it that would not be naturally encoded.
        | READYTORUN_FLAG_PLATFORM_NATIVE_IMAGE      | 0x00000100 | The owning composite executable is in the platform native format
        """;

        IngestionDocument document = await ReadAsync(markdownContent);

        IngestionDocumentTable documentTable = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());
        Assert.Equal(10, documentTable.Cells.GetLength(0)); // 10 rows (1 header + 9 data rows)
        Assert.Equal(3, documentTable.Cells.GetLength(1));  // 3 columns

        // Verify a few key cells
        Assert.Equal("Flag", documentTable.Cells[0, 0]!.GetMarkdown().Trim());
        Assert.Equal("Value", documentTable.Cells[0, 1]!.GetMarkdown().Trim());
        Assert.Equal("Description", documentTable.Cells[0, 2]!.GetMarkdown().Trim());

        Assert.Equal("READYTORUN_FLAG_PLATFORM_NEUTRAL_SOURCE", documentTable.Cells[1, 0]!.GetMarkdown().Trim());
        Assert.Equal("0x00000001", documentTable.Cells[1, 1]!.GetMarkdown().Trim());
        Assert.Contains("platform neutral", documentTable.Cells[1, 2]!.GetMarkdown().Trim());

        Assert.Equal("READYTORUN_FLAG_PARTIAL", documentTable.Cells[3, 0]!.GetMarkdown().Trim());
        Assert.Equal("0x00000004", documentTable.Cells[3, 1]!.GetMarkdown().Trim());
        Assert.Null(documentTable.Cells[3, 2]); // Empty description cell is null
    }

    [Fact]
    public override async Task SupportsImages()
    {
        string contentType1 = "image/png";
        byte[] imageBytes1 = Enumerable.Range(0, 55).Select(i => (byte)i).ToArray();
        string contentType2 = "image/jpeg";
        byte[] imageBytes2 = Enumerable.Range(55, 111).Select(i => (byte)i).ToArray();
        string contentType3 = "image/newfancy";
        byte[] imageBytes3 = Enumerable.Range(166, 200).Select(i => (byte)i).ToArray();

        string markdownContent = $"""
        # All content types supported!

        PNG is fine!

        ![One](data:{contentType1};base64,{Convert.ToBase64String(imageBytes1)})

        JPEG is also fine!

        ![Two](data:{contentType2};base64,{Convert.ToBase64String(imageBytes2)})

        But what about a new fancy type?

        ![Three](data:{contentType3};base64,{Convert.ToBase64String(imageBytes3)})
        """;

        IngestionDocument document = await ReadAsync(markdownContent);

        Assert.NotNull(document);
        var images = document.EnumerateContent().OfType<IngestionDocumentImage>().ToArray();
        Assert.Equal(3, images.Length);
        Assert.Equal(contentType1, images[0].MediaType);
        Assert.Equal(imageBytes1, images[0].Content?.ToArray());
        Assert.Equal("One", images[0].AlternativeText);
        Assert.Equal(contentType2, images[1].MediaType);
        Assert.Equal(imageBytes2, images[1].Content?.ToArray());
        Assert.Equal("Two", images[1].AlternativeText);
        Assert.Equal(contentType3, images[2].MediaType);
        Assert.Equal(imageBytes3, images[2].Content?.ToArray());
        Assert.Equal("Three", images[2].AlternativeText);
    }

    [Fact]
    public async Task SupportsTablesWithImages()
    {
        byte[] imageBytes = Enumerable.Range(55, 111).Select(i => (byte)i).ToArray();
        string markdownContent = $"""
        # Table with Images

        | **Years** | **Logo** |
        | --- | --- |
        | 2020-2025 | ![Latest logo](data:image/png;base64,{Convert.ToBase64String(imageBytes)}) |
        """;

        IngestionDocument document = await ReadAsync(markdownContent);

        var table = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());
        Assert.Equal(2, table.Cells.GetLength(0));
        Assert.Equal(2, table.Cells.GetLength(1));

        // Each reader properly recognizes the text from the first column.
        // When it comes to the images, MarkItDown extracts them as images, while
        // other readers return nothing or ORCed text.
        Assert.Equal("**Years**", table.Cells[0, 0]!.GetMarkdown().Trim());
        Assert.Equal("**Logo**", table.Cells[0, 1]!.GetMarkdown().Trim());
        Assert.Equal("2020-2025", table.Cells[1, 0]!.GetMarkdown().Trim());

        IngestionDocumentImage img = Assert.IsType<IngestionDocumentImage>(table.Cells[1, 1]);
        Assert.Equal("image/png", img.MediaType);
        Assert.NotNull(img.Content);
        Assert.False(img.Content.Value.IsEmpty);
        Assert.Equal("Latest logo", img.AlternativeText);
    }

    [Fact]
    public async Task SupportsInlineHtml()
    {
        string markdownContent = "This has <sup>[1]</sup> inline HTML.";

        IngestionDocument document = await ReadAsync(markdownContent);

        var paragraph = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentParagraph>());
        Assert.Equal("This has <sup>[1]</sup> inline HTML.", paragraph.Text);
        Assert.Equal(markdownContent, paragraph.GetMarkdown());
    }

    [Fact]
    public async Task SupportsMultipleInlineHtmlElements()
    {
        string markdownContent = """
        Text with <strong>bold</strong>, <em>italic</em>, <sub>subscript</sub>, and <sup>superscript</sup> tags.
        """;

        IngestionDocument document = await ReadAsync(markdownContent);

        var paragraph = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentParagraph>());
        Assert.Equal("Text with <strong>bold</strong>, <em>italic</em>, <sub>subscript</sub>, and <sup>superscript</sup> tags.", paragraph.Text);
        Assert.Equal(markdownContent, paragraph.GetMarkdown());
    }

    private async Task<IngestionDocument> ReadAsync(string content)
    {
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(content));
        return await CreateDocumentReader().ReadAsync(stream, "id", "text/markdown");
    }
}
