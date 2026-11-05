// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, ComparisonView } from '../components';
import { twoExecutionDataset } from './fixtures/richDataset';

const renderComparison = () => {
    const scoreSummary = createScoreSummary(twoExecutionDataset);
    return render(
        <ReportContextProvider dataset={twoExecutionDataset} scoreSummary={scoreSummary}>
            <ComparisonView />
        </ReportContextProvider>,
    );
};

describe('ComparisonView — ARIA table structural invariants', () => {
    it('renders no orphaned role="row" elements (every row has a table/grid/rowgroup ancestor)', () => {
        const { container } = renderComparison();
        const rows = [...container.querySelectorAll('[role="row"]')];
        expect(rows.length).toBeGreaterThan(0);
        for (const row of rows) {
            expect(row.closest('[role="table"], [role="grid"], [role="rowgroup"]')).not.toBeNull();
        }
    });

    it('renders no orphaned cell/columnheader elements (every one has a role="row" ancestor)', () => {
        const { container } = renderComparison();
        const cells = [...container.querySelectorAll('[role="cell"], [role="columnheader"]')];
        expect(cells.length).toBeGreaterThan(0);
        for (const cell of cells) {
            expect(cell.closest('[role="row"]')).not.toBeNull();
        }
    });

    it('never places aria-sort on a bare <button> (it must live on the columnheader)', () => {
        const { container } = renderComparison();
        expect(container.querySelectorAll('button[aria-sort]').length).toBe(0);
    });

    it('the default-sorted (Metric) columnheader carries aria-sort=ascending on initial render', () => {
        renderComparison();
        const nameBtn = screen.getByRole('button', { name: /metric/i });
        expect(nameBtn.closest('[role="columnheader"]')?.getAttribute('aria-sort')).toBe('ascending');
    });

    it("toggles the Δ run columnheader's aria-sort none → ascending → descending on repeated clicks", () => {
        renderComparison();
        const getDeltaBtn = () => screen.getByRole('button', { name: /Δ run/i });
        const ariaSortOf = (btn: HTMLElement) => btn.closest('[role="columnheader"]')?.getAttribute('aria-sort');

        expect(ariaSortOf(getDeltaBtn())).toBe('none');

        fireEvent.click(getDeltaBtn());
        expect(ariaSortOf(getDeltaBtn())).toBe('ascending');

        fireEvent.click(getDeltaBtn());
        expect(ariaSortOf(getDeltaBtn())).toBe('descending');
    });

    it('each data row\'s total column span (aria-colspan, default 1) equals the columnheader count', () => {
        const { container } = renderComparison();
        const columnheaderCount = container.querySelectorAll(
            '[role="table"] > [role="row"] [role="columnheader"]',
        ).length;
        expect(columnheaderCount).toBeGreaterThan(0);

        const dataRows = [...container.querySelectorAll('[role="rowgroup"] [role="row"]')];
        expect(dataRows.length).toBeGreaterThan(0);
        for (const row of dataRows) {
            const cells = [...row.querySelectorAll(':scope > [role="cell"]')];
            const span = cells.reduce((sum, c) => sum + Number(c.getAttribute('aria-colspan') ?? '1'), 0);
            expect(span).toBe(columnheaderCount);
        }
    });
});
