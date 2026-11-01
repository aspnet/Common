// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>
/// Represents a table in a document.
/// </summary>
public sealed class ExtractionDocumentTableElement : ExtractionDocumentElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionDocumentTableElement"/> class.
    /// </summary>
    /// <param name="content">The local payload, or <see langword="null"/> for a structural table.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="columnCount">The number of columns.</param>
    public ExtractionDocumentTableElement(AIContent? content, int rowCount, int columnCount)
        : base(content)
    {
        _ = Throw.IfLessThan(rowCount, 0);
        _ = Throw.IfLessThan(columnCount, 0);

        Cells = new ExtractionDocumentTableCellCollection(this, rowCount, columnCount);
    }

    /// <summary>
    /// Gets the addressable nullable cells in this table.
    /// </summary>
    public ExtractionDocumentTableCellCollection Cells { get; }

}

/// <summary>
/// Represents one addressable table cell.
/// </summary>
public sealed class ExtractionDocumentTableCell
{
    private ExtractionDocumentTableElement? _table;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionDocumentTableCell"/> class.
    /// </summary>
    /// <param name="content">The element in the cell.</param>
    /// <param name="rowSpan">The positive number of rows covered by the cell.</param>
    /// <param name="columnSpan">The positive number of columns covered by the cell.</param>
    /// <param name="role">The optional producer-supplied cell role.</param>
    public ExtractionDocumentTableCell(ExtractionDocumentElement content, int rowSpan = 1, int columnSpan = 1, string? role = null)
    {
        Content = Throw.IfNull(content);
        RowSpan = Throw.IfLessThan(rowSpan, 1);
        ColumnSpan = Throw.IfLessThan(columnSpan, 1);
        Role = role is null ? null : Throw.IfNullOrWhitespace(role);
    }

    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentTableCell"/> class without content.</summary>
    public ExtractionDocumentTableCell(int rowSpan = 1, int columnSpan = 1, string? role = null)
    {
        RowSpan = Throw.IfLessThan(rowSpan, 1);
        ColumnSpan = Throw.IfLessThan(columnSpan, 1);
        Role = role is null ? null : Throw.IfNullOrWhitespace(role);
    }

    /// <summary>
    /// Gets the element in the cell.
    /// </summary>
    public ExtractionDocumentElement? Content { get; }

    /// <summary>Gets the number of rows occupied by this anchor cell.</summary>
    public int RowSpan { get; }

    /// <summary>Gets the number of columns occupied by this anchor cell.</summary>
    public int ColumnSpan { get; }

    /// <summary>Gets the optional producer-supplied cell role.</summary>
    public string? Role { get; }

    /// <summary>Gets page and region associations independent of the presence of cell content.</summary>
    public List<ExtractionDocumentSourceReference> Sources { get; } = [];

    /// <summary>
    /// Gets the row index of the cell.
    /// </summary>
    public int RowIndex { get; internal set; }

    /// <summary>
    /// Gets the column index of the cell.
    /// </summary>
    public int ColumnIndex { get; internal set; }

    internal ExtractionDocumentTableElement? Table => _table;

    internal void Attach(ExtractionDocumentTableElement table, int row, int column)
    {
        if (_table is not null)
        {
            throw new InvalidOperationException("The table cell is already owned by a table.");
        }

        _table = table;
        RowIndex = row;
        ColumnIndex = column;
    }

    internal void Detach(ExtractionDocumentTableElement table)
    {
        if (!ReferenceEquals(_table, table))
        {
            throw new InvalidOperationException("The table cell is not owned by the specified table.");
        }

        _table = null;
    }
}

/// <summary>
/// Provides explicit row and column addressing for nullable table cells.
/// </summary>
public sealed class ExtractionDocumentTableCellCollection : IEnumerable<ExtractionDocumentTableCell?>
{
    private readonly ExtractionDocumentTableElement _owner;
    private readonly ExtractionDocumentTableCell?[,] _cells;
    private readonly ExtractionDocumentTableCell?[,] _coveringCells;

    internal ExtractionDocumentTableCellCollection(ExtractionDocumentTableElement owner, int rowCount, int columnCount)
    {
        _owner = owner;
        _cells = new ExtractionDocumentTableCell?[rowCount, columnCount];
        _coveringCells = new ExtractionDocumentTableCell?[rowCount, columnCount];
    }

    /// <summary>
    /// Gets the number of rows.
    /// </summary>
    public int RowCount => _cells.GetLength(0);

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int ColumnCount => _cells.GetLength(1);

    /// <summary>
    /// Gets or sets a cell by row and column address. <see langword="null"/> represents an empty cell.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <param name="column">The zero-based column index.</param>
    public ExtractionDocumentTableCell? this[int row, int column]
    {
        get
        {
            ValidateAddress(row, column);
            return _cells[row, column];
        }
        set
        {
            ValidateAddress(row, column);

            if (IsCovered(row, column))
            {
                throw new InvalidOperationException("A covered position must be edited through its anchor cell.");
            }

            ExtractionDocumentTableCell? oldCell = _cells[row, column];
            if (ReferenceEquals(oldCell, value))
            {
                return;
            }

            if (value is not null)
            {
                ValidateSpan(row, column, value, oldCell);
                ValidateCanAdd(value);
                value.Attach(_owner, row, column);
            }

            if (oldCell is not null)
            {
                SetCoverage(oldCell, null);
                oldCell.Detach(_owner);
            }

            _cells[row, column] = value;
            if (value is not null)
            {
                SetCoverage(value, value);
            }
        }
    }

    /// <summary>Gets the anchor cell covering a position, or <see langword="null"/> for an empty position.</summary>
    /// <returns>The covering anchor, or <see langword="null"/>.</returns>
    internal ExtractionDocumentTableCell? GetCoveringCell(int row, int column)
    {
        ValidateAddress(row, column);
        return _coveringCells[row, column];
    }

    /// <summary>Gets whether a position is covered by an anchor at a different address.</summary>
    /// <returns>Whether another anchor covers the position.</returns>
    internal bool IsCovered(int row, int column)
        => GetCoveringCell(row, column) is { } cell && (cell.RowIndex != row || cell.ColumnIndex != column);

    /// <inheritdoc/>
    public IEnumerator<ExtractionDocumentTableCell?> GetEnumerator()
    {
        for (int row = 0; row < RowCount; row++)
        {
            for (int column = 0; column < ColumnCount; column++)
            {
                yield return _cells[row, column];
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void ValidateCanAdd(ExtractionDocumentTableCell cell)
    {
        if (cell.Table is not null)
        {
            throw new InvalidOperationException("The table cell is already owned by a table.");
        }

        if (cell.Content is ExtractionDocumentSection || ReferenceEquals(cell.Content, _owner))
        {
            throw new InvalidOperationException("Adding the cell content would create an invalid table relationship.");
        }
    }

    private void ValidateSpan(int row, int column, ExtractionDocumentTableCell cell, ExtractionDocumentTableCell? replaced)
    {
        if (cell.RowSpan > RowCount - row || cell.ColumnSpan > ColumnCount - column)
        {
            Throw.ArgumentException(nameof(cell), "A cell span must fit within the table.");
        }

        for (int r = row; r < row + cell.RowSpan; r++)
        {
            for (int c = column; c < column + cell.ColumnSpan; c++)
            {
                if (GetCoveringCell(r, c) is { } existing && !ReferenceEquals(existing, replaced))
                {
                    throw new InvalidOperationException("Cell spans cannot overlap another cell.");
                }
            }
        }
    }

    private void ValidateAddress(int row, int column)
    {
        _ = Throw.IfLessThan(row, 0, nameof(row));
        _ = Throw.IfGreaterThanOrEqual(row, RowCount, nameof(row));
        _ = Throw.IfLessThan(column, 0, nameof(column));
        _ = Throw.IfGreaterThanOrEqual(column, ColumnCount, nameof(column));
    }

    private void SetCoverage(ExtractionDocumentTableCell cell, ExtractionDocumentTableCell? covering)
    {
        for (int row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
        {
            for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
            {
                _coveringCells[row, column] = covering;
            }
        }
    }
}
