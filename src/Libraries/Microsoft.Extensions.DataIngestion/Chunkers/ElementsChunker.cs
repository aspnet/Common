// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

internal sealed class ElementsChunker
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokensPerChunk;

    internal ElementsChunker(IngestionChunkerOptions options)
    {
        _ = Throw.IfNull(options);
        _tokenizer = options.Tokenizer;
        _maxTokensPerChunk = options.MaxTokensPerChunk;
    }

    internal IEnumerable<IngestionChunk> Process(
        ExtractionDocument document, string context, List<ExtractionDocumentElement> elements,
        IReadOnlyList<ExtractionDocumentElement>? contextElements = null, string? localContent = null,
        IReadOnlyDictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>>? elementSources = null)
    {
        List<IngestionChunk> chunks = [];
        List<ExtractionDocumentElement> sources = contextElements is null ? [] : new(contextElements);
        string current = context;
        bool hasContent = false;
        if (CountTokens(context) >= _maxTokensPerChunk)
        {
            ThrowTokenCountExceeded();
        }

        AppendContent(localContent, null);
        foreach (ExtractionDocumentElement element in elements)
        {
            string? content = element.GetSemanticContent();
            if (content is null || content.Length == 0)
            {
                continue;
            }

            if (TryAppend(content))
            {
                AddSource(element);
                if (element is ExtractionDocumentTableElement wholeTable && IngestionDocumentElementExtensions.UsesTableCells(wholeTable))
                {
                    AddCellSources(wholeTable, 0, wholeTable.Cells.RowCount);
                }
            }
            else if (element is ExtractionDocumentTableElement table &&
                IngestionDocumentElementExtensions.UsesTableCells(table) &&
                table.Cells.RowCount > 0)
            {
                bool hasHeader = HasColumnHeaders(table);
                string header = hasHeader ? IngestionDocumentElementExtensions.GetSemanticRow(table, 0) : string.Empty;
                for (int row = 0; row < table.Cells.RowCount; row++)
                {
                    string rowText = IngestionDocumentElementExtensions.GetSemanticRow(table, row);
                    if (!TryAppend(rowText))
                    {
                        Commit();
                        if (hasHeader && row > 0 && !TryAppend(header))
                        {
                            ThrowTokenCountExceeded();
                        }

                        if (hasHeader && row > 0)
                        {
                            AddCellSources(table, 0, 1);
                        }

                        if (!TryAppend(rowText))
                        {
                            ThrowTokenCountExceeded();
                        }
                    }

                    AddSource(table);
                    AddCellSources(table, row, row + 1);
                }
            }
            else
            {
                AppendContent(content, element);
            }
        }

        Commit();
        return chunks;

        string Combine(string content) => current.Length == 0 ? content : current + Environment.NewLine + content;

        bool TryAppend(string content)
        {
            string candidate = Combine(content);
            if (CountTokens(candidate) > _maxTokensPerChunk)
            {
                return false;
            }

            current = candidate;
            hasContent = true;
            return true;
        }

        void AppendContent(string? content, ExtractionDocumentElement? element)
        {
            if (content is null || content.Length == 0)
            {
                return;
            }

            while (content.Length > 0)
            {
                if (TryAppend(content))
                {
                    AddSource(element);
                    return;
                }

                int budget = _maxTokensPerChunk - CountTokens(current);
                int index = 0;
                while (budget > 0)
                {
                    index = _tokenizer.GetIndexByTokenCount(content, budget, out _, out _, considerNormalization: false);
                    if (index == 0 || CountTokens(Combine(content.Substring(0, index))) <= _maxTokensPerChunk)
                    {
                        break;
                    }

                    budget--;
                }

                if (index == 0)
                {
                    if (!hasContent)
                    {
                        ThrowTokenCountExceeded();
                    }

                    Commit();
                    continue;
                }

                int newLineIndex = content.LastIndexOf('\n', index - 1, index);
                if (newLineIndex > 0)
                {
                    index = newLineIndex + 1;
                }

                if (!TryAppend(content.Substring(0, index)))
                {
                    ThrowTokenCountExceeded();
                }

                AddSource(element);
                content = content.Substring(index);
                Commit();
            }
        }

        void AddSource(ExtractionDocumentElement? element)
        {
            if (element is not null && elementSources is not null && elementSources.TryGetValue(element, out var provenance))
            {
                foreach (var source in provenance)
                {
                    if (!sources.Contains(source))
                    {
                        sources.Add(source);
                    }

                    if (element is ExtractionDocumentTableElement && ReferenceEquals(source, element))
                    {
                        // Cell sources are added only for the rows included in this chunk.
                        break;
                    }
                }
            }

            if (element is not null && !sources.Contains(element))
            {
                sources.Add(element);
            }
        }

        void AddCellSources(ExtractionDocumentTableElement table, int firstRow, int endRow)
        {
            for (int row = firstRow; row < endRow; row++)
            {
                for (int column = 0; column < table.Cells.ColumnCount; column++)
                {
                    AddSource(table.Cells[row, column]?.Content);
                }
            }
        }

        void Commit()
        {
            if (!hasContent)
            {
                return;
            }

            chunks.Add(new(new TextContent(current), document, CountTokens(current), context) { SourceElements = sources.ToArray() });
            current = context;
            hasContent = false;
            sources.Clear();
            if (contextElements is not null)
            {
                sources.AddRange(contextElements);
            }
        }
    }

    private static bool HasColumnHeaders(ExtractionDocumentTableElement table)
    {
        bool hasHeader = false;
        for (int column = 0; column < table.Cells.ColumnCount; column++)
        {
            if (table.Cells[0, column] is { } cell)
            {
                if (!string.Equals(cell.Role, "columnHeader", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                hasHeader = true;
            }
        }

        return hasHeader;
    }

    private static void ThrowTokenCountExceeded()
        => throw new InvalidOperationException("Can't fit in the current chunk. Consider increasing max tokens per chunk.");

    private int CountTokens(string content) => _tokenizer.CountTokens(content, considerNormalization: false);
}
