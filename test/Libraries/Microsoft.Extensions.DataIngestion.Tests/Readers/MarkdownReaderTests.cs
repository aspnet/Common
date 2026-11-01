// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.TestUtilities;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public class MarkdownReaderTests : DocumentReaderConformanceTests
{
    [Fact]
    public async Task PreservesRawMarkdownHeadingFooterAndExplicitTableHeaderRoles()
    {
        ExtractionDocument document = await ReadAsync("""
            ## Actual heading

            A *formatted* paragraph.

            | **Header** | Value |
            | --- | --- |
            | first | second |

            ---

            Footer text.
            """);

        ExtractionDocumentElement[] elements = document.EnumerateContent().ToArray();
        ExtractionDocumentTextElement heading = Assert.IsType<ExtractionDocumentTextElement>(elements[0]);
        Assert.Equal("heading", heading.Role);
        Assert.Equal(2, heading.HeadingLevel);
        Assert.Equal("## Actual heading", Assert.IsType<TextContent>(heading.Content).Text);
        Assert.Equal("Actual heading", heading.Text);
        ExtractionDocumentTextElement paragraph = Assert.IsType<ExtractionDocumentTextElement>(elements[1]);
        Assert.Null(paragraph.Role);
        Assert.Equal("A *formatted* paragraph.", Assert.IsType<TextContent>(paragraph.Content).Text);
        ExtractionDocumentTableElement table = Assert.IsType<ExtractionDocumentTableElement>(elements[2]);
        Assert.Null(table.Content);
        Assert.Equal("columnHeader", table.Cells[0, 0]!.Role);
        Assert.Null(table.Cells[1, 0]!.Role);
        Assert.Equal("**Header**", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text.Trim());
        ExtractionDocumentTextElement footer = Assert.IsType<ExtractionDocumentTextElement>(elements[3]);
        Assert.Equal("pageFooter", footer.Role);
        Assert.Null(footer.HeadingLevel);
        Assert.Equal("Footer text.", Assert.IsType<TextContent>(footer.Content).Text);
        Assert.Equal("Footer text.", footer.Text);
        Assert.Equal(4, elements.Length);
    }

    protected override IngestionDocumentReader CreateDocumentReader(bool extractImages = false) => new MarkdownReader();

    public static new TheoryData<string> Links =>
    [
        "https://raw.githubusercontent.com/microsoft/markitdown/main/README.md"
    ];

    [ConditionalTheory]
    [MemberData(nameof(Links))]
    public override Task SupportsStreams(string source) => base.SupportsStreams(source);

    [ConditionalTheory]
    [MemberData(nameof(Links))]
    public override Task SupportsFiles(string source) => base.SupportsFiles(source);

    [ConditionalFact]
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

        ExtractionDocument document = await ReadAsync(markdownContent);

        ExtractionDocumentTableElement documentTable = Assert.Single(document.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        Assert.Equal(5, documentTable.Cells.RowCount);
        Assert.Equal(4, documentTable.Cells.ColumnCount);

        string[,] expected =
        {
            { "**Milestone**", "**Target Date**", "**Department**", "**Indicator**" },
            { "Environmental Audit", "Mar 2025", "Environmental", "Audit Complete" },
            { "Renewable Energy Launch", "Jul 2025", "Facilities", "Install Operational" },
            { "Staff Workshop", "Sep 2025", "HR", "Workshop Held" },
            { "Emissions Review", "Dec 2029", "All", "25% Emissions Cut" }
        };

        for (int row = 0; row < documentTable.Cells.RowCount; row++)
        {
            for (int column = 0; column < documentTable.Cells.ColumnCount; column++)
            {
                Assert.Equal(expected[row, column], Assert.IsType<TextContent>(documentTable.Cells[row, column]!.Content!.Content).Text.Trim());
            }
        }
    }

    [ConditionalFact]
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

        ExtractionDocument document = await ReadAsync(markdownContent);

        ExtractionDocumentTableElement documentTable = Assert.Single(document.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        Assert.Equal(10, documentTable.Cells.RowCount); // 10 rows (1 header + 9 data rows)
        Assert.Equal(3, documentTable.Cells.ColumnCount);  // 3 columns

        // Verify a few key cells
        Assert.Equal("Flag", Assert.IsType<TextContent>(documentTable.Cells[0, 0]!.Content!.Content).Text.Trim());
        Assert.Equal("Value", Assert.IsType<TextContent>(documentTable.Cells[0, 1]!.Content!.Content).Text.Trim());
        Assert.Equal("Description", Assert.IsType<TextContent>(documentTable.Cells[0, 2]!.Content!.Content).Text.Trim());

        Assert.Equal("READYTORUN_FLAG_PLATFORM_NEUTRAL_SOURCE", Assert.IsType<TextContent>(documentTable.Cells[1, 0]!.Content!.Content).Text.Trim());
        Assert.Equal("0x00000001", Assert.IsType<TextContent>(documentTable.Cells[1, 1]!.Content!.Content).Text.Trim());
        Assert.Contains("platform neutral", Assert.IsType<TextContent>(documentTable.Cells[1, 2]!.Content!.Content).Text.Trim());

        Assert.Equal("READYTORUN_FLAG_PARTIAL", Assert.IsType<TextContent>(documentTable.Cells[3, 0]!.Content!.Content).Text.Trim());
        Assert.Equal("0x00000004", Assert.IsType<TextContent>(documentTable.Cells[3, 1]!.Content!.Content).Text.Trim());
        Assert.Null(documentTable.Cells[3, 2]); // Empty description cell is null
    }

    [ConditionalFact]
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

        ExtractionDocument document = await ReadAsync(markdownContent);

        Assert.NotNull(document);
        var images = document.EnumerateContent().OfType<ExtractionDocumentImageElement>().ToArray();
        Assert.Equal(3, images.Length);
        DataContent data1 = Assert.IsType<DataContent>(images[0].Content);
        Assert.Equal(contentType1, data1.MediaType);
        Assert.Equal(imageBytes1, data1.Data.ToArray());
        Assert.Equal("One", images[0].AlternativeText);
        Assert.Null(images[0].Source);
        DataContent data2 = Assert.IsType<DataContent>(images[1].Content);
        Assert.Equal(contentType2, data2.MediaType);
        Assert.Equal(imageBytes2, data2.Data.ToArray());
        Assert.Equal("Two", images[1].AlternativeText);
        Assert.Null(images[1].Source);
        DataContent data3 = Assert.IsType<DataContent>(images[2].Content);
        Assert.Equal(contentType3, data3.MediaType);
        Assert.Equal(imageBytes3, data3.Data.ToArray());
        Assert.Equal("Three", images[2].AlternativeText);
        Assert.Null(images[2].Source);
    }

    [ConditionalFact]
    public async Task SupportsImagesWithUrl()
    {
        string markdownContent = """
        # Images with URLs

        ![A smiling octocat](https://myoctocat.com/assets/images/base-octocat.svg)

        ![](relative/path/image.png)

        ![Logo](https://example.com/logo.png)
        """;

        ExtractionDocument document = await ReadAsync(markdownContent);

        Assert.NotNull(document);
        var images = document.EnumerateContent().OfType<ExtractionDocumentImageElement>().ToArray();
        Assert.Equal(3, images.Length);

        // Image with absolute URL and alt text
        Assert.Equal("A smiling octocat", images[0].AlternativeText);
        Assert.Equal(new Uri("https://myoctocat.com/assets/images/base-octocat.svg"), images[0].Source);
        Assert.Null(images[0].Content);
        Assert.Null(images[0].Text);

        // Image with relative URL and no alt text
        Assert.Null(images[1].AlternativeText);
        Assert.Equal(new Uri("relative/path/image.png", UriKind.Relative), images[1].Source);
        Assert.Null(images[1].Content);

        // Image with absolute URL and alt text
        Assert.Equal("Logo", images[2].AlternativeText);
        Assert.Equal(new Uri("https://example.com/logo.png"), images[2].Source);
        Assert.Null(images[2].Content);
        Assert.Null(images[2].Text);
    }

    [ConditionalFact]
    public async Task SupportsTablesWithImages()
    {
        byte[] imageBytes = Enumerable.Range(55, 111).Select(i => (byte)i).ToArray();
        string markdownContent = $"""
        # Table with Images

        | **Years** | **Logo** |
        | --- | --- |
        | 2020-2025 | ![Latest logo](data:image/png;base64,{Convert.ToBase64String(imageBytes)}) |
        """;

        ExtractionDocument document = await ReadAsync(markdownContent);

        var table = Assert.Single(document.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(2, table.Cells.ColumnCount);

        // Each reader properly recognizes the text from the first column.
        // When it comes to the images, MarkItDown extracts them as images, while
        // other readers return nothing or ORCed text.
        Assert.Equal("**Years**", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text.Trim());
        Assert.Equal("**Logo**", Assert.IsType<TextContent>(table.Cells[0, 1]!.Content!.Content).Text.Trim());
        Assert.Equal("2020-2025", Assert.IsType<TextContent>(table.Cells[1, 0]!.Content!.Content).Text.Trim());

        ExtractionDocumentImageElement img = Assert.IsType<ExtractionDocumentImageElement>(table.Cells[1, 1]!.Content);
        DataContent data = Assert.IsType<DataContent>(img.Content);
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(imageBytes, data.Data.ToArray());
        Assert.Equal("Latest logo", img.AlternativeText);
    }

    [ConditionalFact]
    public async Task SupportsInlineHtml()
    {
        string markdownContent = "This has <sup>[1]</sup> inline HTML.";

        ExtractionDocument document = await ReadAsync(markdownContent);

        var paragraph = Assert.Single(document.EnumerateContent().OfType<ExtractionDocumentTextElement>(), element => element.Role is null);
        Assert.Equal("This has <sup>[1]</sup> inline HTML.", paragraph.Text);
        Assert.Equal(markdownContent, Assert.IsType<TextContent>(paragraph.Content).Text);
    }

    [ConditionalFact]
    public async Task SupportsMultipleInlineHtmlElements()
    {
        string markdownContent = """
        Text with <strong>bold</strong>, <em>italic</em>, <sub>subscript</sub>, and <sup>superscript</sup> tags.
        """;

        ExtractionDocument document = await ReadAsync(markdownContent);

        var paragraph = Assert.Single(document.EnumerateContent().OfType<ExtractionDocumentTextElement>(), element => element.Role is null);
        Assert.Equal("Text with <strong>bold</strong>, <em>italic</em>, <sub>subscript</sub>, and <sup>superscript</sup> tags.", paragraph.Text);
        Assert.Equal(markdownContent, Assert.IsType<TextContent>(paragraph.Content).Text);
    }

    private async Task<ExtractionDocument> ReadAsync(string content)
    {
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(content));
        return await CreateDocumentReader().ReadAsync(stream, "id", "text/markdown");
    }
}
