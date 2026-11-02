// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

internal static class MarkdownParser
{
    internal static ExtractionDocument Parse(string markdown, string identifier)
    {
        _ = Throw.IfNullOrEmpty(markdown);
        _ = Throw.IfNullOrEmpty(identifier);

        // Markdig's "UseAdvancedExtensions" option includes many common extensions beyond
        // CommonMark, such as citations, figures, footnotes, grid tables, mathematics
        // task lists, diagrams, and more.
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        MarkdownDocument markdownDocument = Markdown.Parse(markdown, pipeline);
        return Map(markdownDocument, markdown, identifier);
    }

#if !NET
    internal static System.Threading.Tasks.Task<string> ReadToEndAsync(this System.IO.StreamReader reader, System.Threading.CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested ? System.Threading.Tasks.Task.FromCanceled<string>(cancellationToken) : reader.ReadToEndAsync();
#endif

    private static ExtractionDocument Map(MarkdownDocument markdownDocument, string documentMarkdown, string identifier)
    {
        ExtractionDocumentSection rootSection = new();
        ExtractionDocument result = new(identifier)
        {
            Sections = { rootSection }
        };

        bool previousWasBreak = false;
        foreach (Block block in markdownDocument)
        {
            if (block is ThematicBreakBlock breakBlock)
            {
                // We have encountered a thematic break (horizontal rule): ----------- etc.
                previousWasBreak = true;
                continue;
            }

            if (block is LinkReferenceDefinitionGroup linkReferenceGroup)
            {
                continue; // In the future, we might want to handle links differently.
            }

            if (IsEmptyBlock(block))
            {
                continue;
            }

            rootSection.Elements.Add(MapBlock(documentMarkdown, previousWasBreak, block));
            previousWasBreak = false;
        }

        return result;
    }

    private static bool IsEmptyBlock(Block block) // Block with no text. Sample: QuoteBlock the next block is a quote.
        => block is LeafBlock emptyLeafBlock && (emptyLeafBlock.Inline is null || emptyLeafBlock.Inline.FirstChild is null);

    private static ExtractionDocumentElement MapBlock(string documentMarkdown, bool previousWasBreak, Block block)
    {
        string elementMarkdown = documentMarkdown.Substring(block.Span.Start, block.Span.Length);

        ExtractionDocumentElement element = block switch
        {
            LeafBlock leafBlock => MapLeafBlockToElement(leafBlock, previousWasBreak, elementMarkdown),
            ListBlock listBlock => MapListBlock(listBlock, previousWasBreak, documentMarkdown),
            QuoteBlock quoteBlock => MapQuoteBlock(quoteBlock, previousWasBreak, documentMarkdown),
            Table table => MapTable(table, documentMarkdown),
            _ => throw new NotSupportedException($"Block type '{block.GetType().Name}' is not supported.")
        };

        return element;
    }

    private static ExtractionDocumentElement MapLeafBlockToElement(LeafBlock block, bool previousWasBreak, string elementMarkdown)
        => block switch
        {
            HeadingBlock heading => new ExtractionDocumentTextElement(new TextContent(elementMarkdown), role: "heading")
            {
                HeadingLevel = heading.Level
            },
            ParagraphBlock when previousWasBreak => new ExtractionDocumentTextElement(new TextContent(elementMarkdown), role: "pageFooter"),
            ParagraphBlock image when image.Inline!.Descendants<LinkInline>().FirstOrDefault() is LinkInline link && link.IsImage => MapImage(link),
            ParagraphBlock => new ExtractionDocumentTextElement(new TextContent(elementMarkdown)),
            CodeBlock => new ExtractionDocumentTextElement(new TextContent(elementMarkdown)),
            _ => throw new NotSupportedException($"Block type '{block.GetType().Name}' is not supported.")
        };

    private static ExtractionDocumentImageElement MapImage(LinkInline link)
    {
        ExtractionDocumentImageElement result = new();

        // ![Alt text](data:image/type;base64,...)
        if (link.FirstChild is LiteralInline literal)
        {
            result.AlternativeText = literal.Content.ToString();
        }

        if (link.Url is not null)
        {
            if (link.Url.StartsWith("data:image/", StringComparison.Ordinal))
            {
                // Parse the data URL format: data:image/{type};base64,{data}
                ReadOnlySpan<char> url = link.Url.AsSpan("data:".Length);

                // Find the semicolon that separates media type from encoding
                int semicolonIndex = url.IndexOf(';');
                if (semicolonIndex > 0)
                {
                    ReadOnlySpan<char> mediaType = url.Slice(0, semicolonIndex);

                    // Find the comma that separates encoding from data
                    int commaIndex = url.IndexOf(',');
                    if (commaIndex > semicolonIndex)
                    {
                        // Check if it's base64 encoded
                        ReadOnlySpan<char> encoding = url.Slice(semicolonIndex + 1, commaIndex - semicolonIndex - 1);
                        if (encoding.SequenceEqual("base64".AsSpan()))
                        {
                            result.Content = new DataContent(Convert.FromBase64String(url.Slice(commaIndex + 1).ToString()), mediaType.ToString());
                        }
                    }
                }
            }
            else if (Uri.TryCreate(link.Url, UriKind.RelativeOrAbsolute, out Uri? address))
            {
                result.Source = address;
            }
        }

        return result;
    }

    private static ExtractionDocumentSection MapListBlock(ListBlock listBlock, bool previousWasBreak, string documentMarkdown)
    {
        ExtractionDocumentSection list = new();
        foreach (Block? item in listBlock)
        {
            if (item is not ListItemBlock listItemBlock)
            {
                continue;
            }

            foreach (Block? child in listItemBlock)
            {
                if (child is not LeafBlock leafBlock || IsEmptyBlock(leafBlock))
                {
                    continue; // Skip empty blocks in lists
                }

                string childMarkdown = documentMarkdown.Substring(leafBlock.Span.Start, leafBlock.Span.Length);
                ExtractionDocumentElement element = MapLeafBlockToElement(leafBlock, previousWasBreak, childMarkdown);
                list.Elements.Add(element);
            }
        }

        return list;
    }

    private static ExtractionDocumentSection MapQuoteBlock(QuoteBlock quoteBlock, bool previousWasBreak, string documentMarkdown)
    {
        ExtractionDocumentSection quote = new();
        foreach (Block child in quoteBlock)
        {
            if (IsEmptyBlock(child))
            {
                continue; // Skip empty blocks in quotes
            }

            quote.Elements.Add(MapBlock(documentMarkdown, previousWasBreak, child));
        }

        return quote;
    }

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable S3967 // Multidimensional arrays should not be used
    private static ExtractionDocumentTableElement MapTable(Table table, string outputContent)
    {
        int firstRowIndex = SkipFirstRow(table, outputContent) ? 1 : 0;

        // Calculate the actual number of columns by examining the rows.
        // table.ColumnDefinitions.Count can vary: for tables WITH trailing pipes it's (columns + 1),
        // but for tables WITHOUT trailing pipes it's equal to the actual column count.
        int columnCount = GetColumnCount(table, firstRowIndex);
        var result = new ExtractionDocumentTableElement(null, table.Count - firstRowIndex, columnCount);

        for (int rowIndex = firstRowIndex; rowIndex < table.Count; rowIndex++)
        {
            var tableRow = (TableRow)table[rowIndex];
            int columnIndex = 0;
            for (int cellIndex = 0; cellIndex < tableRow.Count; cellIndex++)
            {
                var tableCell = (TableCell)tableRow[cellIndex];
                var content = tableCell.Count switch
                {
                    0 => null,
                    1 => MapBlock(outputContent, previousWasBreak: false, tableCell[0]),
                    _ => throw new NotSupportedException($"Cells with {tableCell.Count} elements are not supported.")
                };

                if (content is not null || tableCell.ColumnSpan > 1)
                {
                    result.Cells[rowIndex - firstRowIndex, columnIndex] = content is null
                        ? new ExtractionDocumentTableCell(columnSpan: tableCell.ColumnSpan)
                        : new ExtractionDocumentTableCell(content, columnSpan: tableCell.ColumnSpan, role: tableRow.IsHeader ? "columnHeader" : null);
                }

                columnIndex += tableCell.ColumnSpan;
            }
        }

        return result;

        static int GetColumnCount(Table table, int firstRowIndex)
        {
            int maxColumns = 0;
            for (int rowIndex = firstRowIndex; rowIndex < table.Count; rowIndex++)
            {
                var tableRow = (TableRow)table[rowIndex];
                int columnCount = 0;
                for (int cellIndex = 0; cellIndex < tableRow.Count; cellIndex++)
                {
                    var tableCell = (TableCell)tableRow[cellIndex];
                    columnCount += tableCell.ColumnSpan;
                }

                maxColumns = Math.Max(maxColumns, columnCount);
            }

            return maxColumns;
        }

        // Some parsers like MarkItDown include a row with invalid markdown before the separator row:
        // |  |  |  |  |
        // | --- | --- | --- | --- |
        static bool SkipFirstRow(Table table, string outputContent)
        {
            if (table.Count > 0)
            {
                var firstRow = (TableRow)table[0];
                for (int cellIndex = 0; cellIndex < firstRow.Count; cellIndex++)
                {
                    var tableCell = (TableCell)firstRow[cellIndex];
                    if (tableCell.Count > 0 && MapBlock(outputContent, previousWasBreak: false, tableCell[0]).Content is TextContent text &&
                        !string.IsNullOrWhiteSpace(text.Text))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }
    }
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional
#pragma warning restore S3967 // Multidimensional arrays should not be used
}
