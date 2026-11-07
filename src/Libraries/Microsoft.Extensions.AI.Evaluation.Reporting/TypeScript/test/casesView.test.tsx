// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { ReportContextProvider, createScoreSummary, CasesView } from '../components';
import { toolCallDataset, richDataset } from './fixtures/richDataset';

const renderCases = (dataset: Dataset) => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
            <CasesView />
        </ReportContextProvider>,
    );
};

// Case rows label themselves "<name> (passed|failed)". Expanded MetricRow buttons use
// ", failed" (no parens), so this parenthesized query never matches them and can't inflate.
const CASE_ROW = /\((?:passed|failed)\)/i;

type MediaListener = (event: MediaQueryListEvent) => void;

const createMediaQuery = () => {
    const media = '(max-width: 1200px)';
    const listeners = new Set<MediaListener>();
    const query = {
        matches: false,
        media,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn((_type: string, listener: MediaListener) => listeners.add(listener)),
        removeEventListener: vi.fn((_type: string, listener: MediaListener) => listeners.delete(listener)),
        dispatchEvent: vi.fn(() => true),
        setWidth(width: number) {
            query.matches = width <= 1200;
            listeners.forEach((listener) => listener({ matches: query.matches, media } as MediaQueryListEvent));
        },
    };
    return query;
};

let detailQuery: ReturnType<typeof createMediaQuery>;

beforeEach(() => {
    detailQuery = createMediaQuery();
    vi.stubGlobal('matchMedia', vi.fn(() => detailQuery));
});

afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
});

const expandFirstCase = () => {
    fireEvent.click(screen.getAllByRole('button', { name: CASE_ROW })[0]);
    return screen.getByRole('region', { name: /detail/i });
};

const detailHeadings = () => within(document.querySelector<HTMLElement>('.eval-twopane')!)
    .getAllByRole('heading', { level: 2 })
    .map((heading) => heading.textContent);

describe('CasesView — rows + expand + focus contract', () => {
    it('renders the single tool-call scenario as a passed case row', () => {
        renderCases(toolCallDataset);
        const rows = screen.getAllByRole('button', { name: CASE_ROW });
        expect(rows).toHaveLength(1);
        // The tool-call scenario passes; a broken isLeafFailed would flip this to "(failed)".
        expect(rows[0].getAttribute('aria-label')).toMatch(/\(passed\)/i);
    });

    it('expands the inline detail without moving focus away from the disclosure button', () => {
        renderCases(toolCallDataset);
        const row = screen.getAllByRole('button', { name: CASE_ROW })[0];

        row.focus();

        expect(screen.queryByRole('region', { name: /detail/i })).not.toBeInTheDocument();

        fireEvent.click(row);

        const detail = screen.getByRole('region', { name: /detail/i });
        expect(detail).toBeInTheDocument();
        expect(within(detail).getByText('Transcript')).toBeInTheDocument();
        expect(within(detail).getByText('Metrics')).toBeInTheDocument();
        expect(row).toHaveFocus();
        expect(detail).not.toHaveAttribute('tabindex');
    });

    it('does not put a Tabster Mover on the expanded detail', () => {
        renderCases(toolCallDataset);
        fireEvent.click(screen.getAllByRole('button', { name: CASE_ROW })[0]);

        const detail = screen.getByRole('region', { name: /detail/i });
        expect(detail).not.toHaveAttribute('data-tabster');
    });

    it('"Failing only" filters out passing cases', () => {
        renderCases(richDataset);
        const allRows = screen.getAllByRole('button', { name: CASE_ROW });
        const failingBefore = allRows.filter((r) => /\(failed\)/i.test(r.getAttribute('aria-label') ?? ''));
        expect(failingBefore.length).toBeGreaterThan(0);

        fireEvent.click(screen.getByRole('switch', { name: /show failed/i }));

        const afterRows = screen.getAllByRole('button', { name: CASE_ROW });
        expect(afterRows.length).toBe(failingBefore.length);
        for (const r of afterRows) {
            expect(r.getAttribute('aria-label')).toMatch(/\(failed\)/i);
        }
    });
});

describe('CasesView — responsive detail order', () => {
    it.each([
        [899, ['Metrics', 'Transcript']],
        [900, ['Metrics', 'Transcript']],
        [901, ['Metrics', 'Transcript']],
        [1199, ['Metrics', 'Transcript']],
        [1200, ['Metrics', 'Transcript']],
        [1201, ['Transcript', 'Metrics']],
        [1440, ['Transcript', 'Metrics']],
    ])('renders the expected DOM order at %ipx', (width, expected) => {
        detailQuery.setWidth(width);
        renderCases(toolCallDataset);
        expandFirstCase();

        expect(detailHeadings()).toEqual(expected);
        expect(screen.getAllByRole('heading', { name: 'Metrics' })).toHaveLength(1);
        expect(screen.getAllByRole('heading', { name: 'Transcript' })).toHaveLength(1);
    });

    it('registers one change listener and removes the same listener on unmount', () => {
        const view = renderCases(toolCallDataset);

        expect(window.matchMedia).toHaveBeenCalledOnce();
        expect(window.matchMedia).toHaveBeenCalledWith('(max-width: 1200px)');
        expect(detailQuery.addEventListener).toHaveBeenCalledOnce();
        const listener = detailQuery.addEventListener.mock.calls[0][1];

        view.unmount();

        expect(detailQuery.removeEventListener).toHaveBeenCalledOnce();
        expect(detailQuery.removeEventListener).toHaveBeenCalledWith('change', listener);
    });

    it('preserves expanded metric state, focus, and node identity across the breakpoint', () => {
        detailQuery.setWidth(1200);
        renderCases(toolCallDataset);
        expandFirstCase();
        const metric = screen.getByRole('button', { name: /weatherAccuracy/i });
        fireEvent.click(metric);
        metric.focus();

        expect(metric).toHaveAttribute('aria-expanded', 'true');
        expect(metric).toHaveFocus();

        act(() => detailQuery.setWidth(1201));
        expect(detailHeadings()).toEqual(['Transcript', 'Metrics']);
        expect(screen.getByRole('button', { name: /weatherAccuracy/i })).toBe(metric);
        expect(metric).toHaveAttribute('aria-expanded', 'true');
        expect(metric).toHaveFocus();

        act(() => detailQuery.setWidth(1200));
        expect(detailHeadings()).toEqual(['Metrics', 'Transcript']);
        expect(screen.getByRole('button', { name: /weatherAccuracy/i })).toBe(metric);
        expect(metric).toHaveAttribute('aria-expanded', 'true');
        expect(metric).toHaveFocus();
        expect(screen.getAllByRole('heading', { name: 'Metrics' })).toHaveLength(1);
        expect(screen.getAllByRole('heading', { name: 'Transcript' })).toHaveLength(1);
    });

    it('keeps keyboard-reachable transcript content in the same order as the DOM', () => {
        const dataset = structuredClone(toolCallDataset);
        dataset.scenarioRunResults[0].messages[0].contents = [{ $type: 'text', text: '```json\n{}\n```' }];
        detailQuery.setWidth(1200);
        renderCases(dataset);
        expandFirstCase();
        const metric = screen.getByRole('button', { name: /weatherAccuracy/i });
        const code = screen.getByRole('region', { name: 'Code block' });

        expect(metric.compareDocumentPosition(code) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

        act(() => detailQuery.setWidth(1201));
        expect(code.compareDocumentPosition(metric) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });
});
