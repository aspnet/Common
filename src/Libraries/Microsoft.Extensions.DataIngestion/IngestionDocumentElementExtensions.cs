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
        => string.IsNullOrEmpty(GetSemanticContent(table.Content));

    internal static string GetSemanticRow(ExtractionDocumentTableElement table, int row)
        => GetSemanticRow(table, row, [table]);

    internal static IEnumerable<ExtractionDocumentElement> EnumerateCellSources(ExtractionDocumentTableElement table, int firstRow, int endRow)
    {
        HashSet<ExtractionDocumentElement> path = [table];
        for (int row = firstRow; row < endRow; row++)
        {
            for (int column = 0; column < table.Cells.ColumnCount; column++)
            {
                if (table.Cells[row, column]?.Content is { } content)
                {
                    foreach (var source in EnumerateSources(content, path))
                    {
                        yield return source;
                    }
                }
            }
        }
    }

    internal static IEnumerable<(ExtractionDocumentElement? Element, string? Content, IReadOnlyList<ExtractionDocumentElement> Sources)> EnumerateSemanticContent(IngestionDocument document)
    {
        yield return (null, GetSemanticContent(document.ExtractedDocument.Content), Array.Empty<ExtractionDocumentElement>());
        foreach (ExtractionDocumentSection section in document.Sections)
        {
            foreach (var item in EnumerateSectionContent(section, [], []))
            {
                yield return item;
            }
        }
    }

    private static string? GetSemanticContent(ExtractionDocumentElement element, HashSet<ExtractionDocumentElement> path)
    {
        if (element is ExtractionDocumentImageElement image)
        {
            string? description = string.IsNullOrEmpty(image.AlternativeText) ? image.Text : image.AlternativeText;
            return !string.IsNullOrEmpty(description) && image.Source is not null
                ? $"![{description}]({image.Source.OriginalString})"
                : description;
        }

        string? localContent = GetSemanticContent(element.Content);
        if (!string.IsNullOrEmpty(localContent) || element is not ExtractionDocumentTableElement table)
        {
            return localContent;
        }

        Enter(table, path);

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

    private static string GetSemanticRow(ExtractionDocumentTableElement table, int row, HashSet<ExtractionDocumentElement> path)
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
                _ = text.Append(GetCellContent(content, path));
            }
        }

        return text.ToString();
    }

    private static string? GetCellContent(ExtractionDocumentElement element, HashSet<ExtractionDocumentElement> path)
    {
        if (element is not ExtractionDocumentSection section)
        {
            return GetSemanticContent(element, path);
        }

        Enter(section, path);
        try
        {
            StringBuilder text = new();
            Append(GetSemanticContent(section.Content));
            foreach (var child in section.Elements)
            {
                if (child is null)
                {
                    throw new InvalidOperationException("The hierarchy contains a null entry.");
                }

                Append(GetCellContent(child, path));
            }

            return text.ToString();

            void Append(string? content)
            {
                if (!string.IsNullOrEmpty(content))
                {
                    if (text.Length > 0)
                    {
                        _ = text.AppendLine();
                    }

                    _ = text.Append(content);
                }
            }
        }
        finally
        {
            _ = path.Remove(section);
        }
    }

    private static IEnumerable<ExtractionDocumentElement> EnumerateSources(ExtractionDocumentElement element, HashSet<ExtractionDocumentElement> path)
    {
        Enter(element, path);
        try
        {
            yield return element;
            if (element is ExtractionDocumentSection section)
            {
                foreach (var child in section.Elements)
                {
                    foreach (var source in EnumerateSources(child, path))
                    {
                        yield return source;
                    }
                }
            }
            else if (element is ExtractionDocumentTableElement table && UsesTableCells(table))
            {
                foreach (var cell in table.Cells)
                {
                    if (cell?.Content is { } content)
                    {
                        foreach (var source in EnumerateSources(content, path))
                        {
                            yield return source;
                        }
                    }
                }
            }
        }
        finally
        {
            _ = path.Remove(element);
        }
    }

    private static void Enter(ExtractionDocumentElement element, HashSet<ExtractionDocumentElement> path)
    {
        if (element is null || !path.Add(element))
        {
            throw new InvalidOperationException(element is ExtractionDocumentTableElement
                ? "Cyclic table relationships are not supported."
                : "Null entries and cyclic relationships are not supported in the hierarchy.");
        }
    }

    private static IEnumerable<(ExtractionDocumentElement? Element, string? Content, IReadOnlyList<ExtractionDocumentElement> Sources)> EnumerateSectionContent(
        ExtractionDocumentSection section, List<ExtractionDocumentElement> ancestors, HashSet<ExtractionDocumentElement> path)
    {
        Enter(section, path);

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
                        foreach (var source in EnumerateSources(table, path))
                        {
                            if (!sources.Contains(source))
                            {
                                sources.Add(source);
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
