// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DataIngestion;

internal static class TestDocumentFactory
{
    internal static ExtractionDocumentTableElement CreateTable(
        ExtractionDocumentElement?[,] elements, AIContent? content = null, bool hasColumnHeaders = false)
    {
        ExtractionDocumentTableElement table = new(content, elements.GetLength(0), elements.GetLength(1));
        for (int row = 0; row < table.Cells.RowCount; row++)
        {
            for (int column = 0; column < table.Cells.ColumnCount; column++)
            {
                if (elements[row, column] is { } element)
                {
                    table.Cells[row, column] = new ExtractionDocumentTableCell(
                        element, role: hasColumnHeaders && row == 0 ? "columnHeader" : null);
                }
            }
        }

        return table;
    }
}
