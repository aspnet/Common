// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

internal static class MarkdownParser
{
    internal static IngestionDocument Parse(string markdown, string identifier)
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

    private static IngestionDocument Map(MarkdownDocument markdownDocument, string documentMarkdown, string identifier)
    {
        IngestionDocumentSection rootSection = new(documentMarkdown);
        IngestionDocument result = new(identifier)
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

    private static IngestionDocumentElement MapBlock(string documentMarkdown, bool previousWasBreak, Block block)
    {
        string elementMarkdown = documentMarkdown.Substring(block.Span.Start, block.Span.Length);

        IngestionDocumentElement element = block switch
        {
            LeafBlock leafBlock => MapLeafBlockToElement(leafBlock, previousWasBreak, elementMarkdown),
            ListBlock listBlock => MapListBlock(listBlock, previousWasBreak, documentMarkdown, elementMarkdown),
            QuoteBlock quoteBlock => MapQuoteBlock(quoteBlock, previousWasBreak, documentMarkdown, elementMarkdown),
            Table table => new IngestionDocumentTable(elementMarkdown, GetCells(table, documentMarkdown)),
            _ => throw new NotSupportedException($"Block type '{block.GetType().Name}' is not supported.")
        };

        return element;
    }

    private static IngestionDocumentElement MapLeafBlockToElement(LeafBlock block, bool previousWasBreak, string elementMarkdown)
        => block switch
        {
            HeadingBlock heading => new IngestionDocumentHeader(elementMarkdown)
            {
                Text = GetText(heading.Inline),
                Level = heading.Level
            },
            ParagraphBlock footer when previousWasBreak => new IngestionDocumentFooter(elementMarkdown)
            {
                Text = GetText(footer.Inline),
            },
            ParagraphBlock image when image.Inline!.Descendants<LinkInline>().FirstOrDefault() is LinkInline link && link.IsImage => MapImage(elementMarkdown, link),
            ParagraphBlock paragraph => new IngestionDocumentParagraph(elementMarkdown)
            {
                Text = GetText(paragraph.Inline),
            },
            CodeBlock codeBlock => new IngestionDocumentParagraph(elementMarkdown)
            {
                Text = GetText(codeBlock.Inline),
            },
            _ => throw new NotSupportedException($"Block type '{block.GetType().Name}' is not supported.")
        };

    private static IngestionDocumentImage MapImage(string elementMarkdown, LinkInline link)
    {
        IngestionDocumentImage result = new(elementMarkdown);

        // ![Alt text](data:image/type;base64,...)
        if (link.FirstChild is LiteralInline literal)
        {
            result.AlternativeText = literal.Content.ToString();
        }

        if (link.Url is not null && link.Url.StartsWith("data:image/", StringComparison.Ordinal))
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
                        result.Content = Convert.FromBase64String(url.Slice(commaIndex + 1).ToString());
                        result.MediaType = mediaType.ToString();
                    }
                }
            }
        }

        return result;
    }

    private static IngestionDocumentSection MapListBlock(ListBlock listBlock, bool previousWasBreak, string documentMarkdown, string listMarkdown)
    {
        IngestionDocumentSection list = new(listMarkdown);
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
                IngestionDocumentElement element = MapLeafBlockToElement(leafBlock, previousWasBreak, childMarkdown);
                list.Elements.Add(element);
            }
        }

        return list;
    }

    private static IngestionDocumentSection MapQuoteBlock(QuoteBlock quoteBlock, bool previousWasBreak, string documentMarkdown, string elementMarkdown)
    {
        IngestionDocumentSection quote = new(elementMarkdown);
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

    private static string? GetText(ContainerInline? containerInline)
    {
        Debug.Assert(containerInline != null, "ContainerInline should not be null here.");
        Debug.Assert(containerInline!.FirstChild != null, "FirstChild should not be null here.");

        if (ReferenceEquals(containerInline.FirstChild, containerInline.LastChild))
        {
            // If there is only one child, return its text.
            return containerInline.FirstChild!.ToString();
        }

        StringBuilder content = new(100);
        foreach (Inline inline in containerInline)
        {
#pragma warning disable IDE0058 // Expression value is never used
            if (inline is LiteralInline literalInline)
            {
                content.Append(literalInline.Content);
            }
            else if (inline is LineBreakInline)
            {
                content.AppendLine(); // Append a new line for line breaks
            }
            else if (inline is ContainerInline another)
            {
                // EmphasisInline is also a ContainerInline, but it does not get any special treatment,
                // as we use raw text here (instead of a markdown, where emphasis can be expressed).
                content.Append(GetText(another));
            }
            else if (inline is CodeInline codeInline)
            {
                content.Append(codeInline.Content);
            }
            else if (inline is HtmlInline htmlInline)
            {
                content.Append(htmlInline.Tag);
            }
            else if (inline is AutolinkInline autolinkInline)
            {
                content.Append(autolinkInline.Url);
            }
            else if (inline is HtmlEntityInline htmlEntityInline)
            {
                content.Append(htmlEntityInline.Transcoded);
            }
            else
            {
                throw new NotSupportedException($"Inline type '{inline.GetType().Name}' is not supported.");
            }
#pragma warning restore IDE0058 // Expression value is never used
        }

        return content.ToString();
    }

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable S3967 // Multidimensional arrays should not be used
    private static IngestionDocumentElement?[,] GetCells(Table table, string outputContent)
    {
        int firstRowIndex = SkipFirstRow(table, outputContent) ? 1 : 0;

        // Calculate the actual number of columns by examining the rows.
        // table.ColumnDefinitions.Count can vary: for tables WITH trailing pipes it's (columns + 1),
        // but for tables WITHOUT trailing pipes it's equal to the actual column count.
        int columnCount = GetColumnCount(table, firstRowIndex);
        var cells = new IngestionDocumentElement?[table.Count - firstRowIndex, columnCount];

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

                for (int columnSpan = 0; columnSpan < tableCell.ColumnSpan; columnSpan++, columnIndex++)
                {
                    // tableCell.ColumnIndex defaults to -1, so it's not used here.
                    cells[rowIndex - firstRowIndex, columnIndex] = content;
                }
            }
        }

        return cells;

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
                    if (!string.IsNullOrWhiteSpace(MapBlock(outputContent, previousWasBreak: false, tableCell[0]).Text))
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
