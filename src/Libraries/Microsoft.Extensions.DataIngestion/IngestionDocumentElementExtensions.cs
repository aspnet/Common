// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DataIngestion;

internal static class IngestionDocumentElementExtensions
{
    internal static string? GetSemanticContent(this ExtractionDocumentElement element)
        => GetSemanticContent(element, []);

    internal static string? GetSemanticContent(AIContent? content) => (content as TextContent)?.Text;

    internal static bool UsesTableCells(ExtractionDocumentTableElement table)
        => string.IsNullOrEmpty(GetSemanticContent(table.Content) ?? table.Text);

    internal static string GetSemanticRow(ExtractionDocumentTableElement table, int row)
        => GetSemanticRow(table, row, [table]);

    internal static IEnumerable<(ExtractionDocumentElement? Element, string? Content, IReadOnlyList<ExtractionDocumentElement> Sources)> EnumerateSemanticContent(ExtractionDocument document)
    {
        yield return (null, GetSemanticContent(document.Content), Array.Empty<ExtractionDocumentElement>());
        foreach (ExtractionDocumentSection section in document.Sections)
        {
            foreach (var item in EnumerateSectionContent(section, [], []))
            {
                yield return item;
            }
        }
    }

    private static string? GetSemanticContent(ExtractionDocumentElement element, HashSet<ExtractionDocumentTableElement> path)
    {
        if (element is ExtractionDocumentImageElement image)
        {
            string? description = string.IsNullOrEmpty(image.AlternativeText) ? image.Text : image.AlternativeText;
            return !string.IsNullOrEmpty(description) && image.Source is not null
                ? $"![{description}]({image.Source.OriginalString})"
                : description;
        }

        string? localContent = GetSemanticContent(element.Content) ?? element.Text;
        if (!string.IsNullOrEmpty(localContent) || element is not ExtractionDocumentTableElement table)
        {
            return localContent;
        }

        if (!path.Add(table))
        {
            throw new InvalidOperationException("Cyclic table relationships are not supported.");
        }

        try
        {
            StringBuilder text = new();
            for (int row = 0; row < table.Cells.RowCount; row++)
            {
                if (row > 0)
                {
                    _ = text.AppendLine();
                }

                _ = text.Append(GetSemanticRow(table, row, path));
            }

            return text.ToString();
        }
        finally
        {
            _ = path.Remove(table);
        }
    }

    private static string GetSemanticRow(ExtractionDocumentTableElement table, int row, HashSet<ExtractionDocumentTableElement> path)
    {
        StringBuilder text = new();
        for (int column = 0; column < table.Cells.ColumnCount; column++)
        {
            if (column > 0)
            {
                _ = text.Append('\t');
            }

            if (table.Cells[row, column]?.Content is { } content)
            {
                _ = text.Append(GetSemanticContent(content, path));
            }
        }

        return text.ToString();
    }

    private static IEnumerable<(ExtractionDocumentElement? Element, string? Content, IReadOnlyList<ExtractionDocumentElement> Sources)> EnumerateSectionContent(
        ExtractionDocumentSection section, List<ExtractionDocumentElement> ancestors, HashSet<ExtractionDocumentSection> path)
    {
        if (section is null || !path.Add(section))
        {
            throw new InvalidOperationException("Null entries and cyclic relationships are not supported in the hierarchy.");
        }

        ancestors.Add(section);
        try
        {
            yield return (section, section.GetSemanticContent(), ancestors.ToArray());
            foreach (ExtractionDocumentElement element in section.Elements)
            {
                if (element is null)
                {
                    throw new InvalidOperationException("The hierarchy contains a null element.");
                }

                if (element is ExtractionDocumentSection nested)
                {
                    foreach (var item in EnumerateSectionContent(nested, ancestors, path))
                    {
                        yield return item;
                    }
                }
                else
                {
                    List<ExtractionDocumentElement> sources = new(ancestors) { element };
                    if (element is ExtractionDocumentTableElement table && UsesTableCells(table))
                    {
                        foreach (var cell in table.Cells)
                        {
                            if (cell?.Content is { } cellElement)
                            {
                                sources.Add(cellElement);
                            }
                        }
                    }

                    yield return (element, element.GetSemanticContent(), sources.ToArray());
                }
            }
        }
        finally
        {
            ancestors.RemoveAt(ancestors.Count - 1);
            _ = path.Remove(section);
        }
    }
}
