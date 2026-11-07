// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { createScoreSummary, ReportContextProvider, useReportContext } from '../components';
import { AppShell } from '../components/shell/AppShell';
import { SidebarTree } from '../components/shell/SidebarTree';
import { richDataset, twoExecutionDataset } from './fixtures/richDataset';

class ResizeObserverMock {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const renderWithContext = (children: React.ReactNode, dataset: Dataset = twoExecutionDataset) => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
            {children}
        </ReportContextProvider>,
    );
};

const ProgrammaticCaseSwitch = () => {
    const { setView } = useReportContext();
    return <button onClick={() => setView('cases')}>open cases</button>;
};

const SidebarContextControls = () => {
    const { setExec, setSearchValue } = useReportContext();
    return (
        <>
            <button onClick={() => setSearchValue('aspirin')}>filter scenarios</button>
            <button onClick={() => setExec('exec-2026-06-29')}>change execution</button>
        </>
    );
};

const ControlledSidebarTree = ({ initialExpanded = [] }: { initialExpanded?: string[] }) => {
    const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set(initialExpanded));
    const onToggle = (key: string) => setExpanded((previous) => {
        const next = new Set(previous);
        if (next.has(key)) next.delete(key); else next.add(key);
        return next;
    });
    return <SidebarTree labelledBy="scenario-label" expanded={expanded} onToggle={onToggle} />;
};

beforeAll(() => {
    vi.stubGlobal('ResizeObserver', ResizeObserverMock);
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
    }));
});

afterAll(() => {
    vi.unstubAllGlobals();
});

describe('AppShell tab focus defaults', () => {
    it('tracks the selected view instead of memorizing a previously focused tab', () => {
        renderWithContext(
            <AppShell heightStrategy="fill-viewport" themeSource="toggle">
                <ProgrammaticCaseSwitch />
            </AppShell>,
        );

        const tablist = screen.getByRole('tablist', { name: 'Report views' });
        const mover = JSON.parse(tablist.getAttribute('data-tabster') ?? '{}').mover;
        const overview = screen.getByRole('tab', { name: 'Overview' });

        expect(mover).toMatchObject({ memorizeCurrent: false, hasDefault: true });

        overview.focus();
        fireEvent.click(screen.getByRole('button', { name: 'open cases' }));

        const cases = screen.getByRole('tab', { name: /Cases/ });
        expect(cases).toHaveAttribute('aria-selected', 'true');
        expect(JSON.parse(cases.getAttribute('data-tabster') ?? '{}').focusable?.isDefault).toBe(true);
        expect(overview).not.toHaveAttribute('data-tabster');
    });
});

describe('Sidebar tree boundaries', () => {
    it('does not configure vertical arrow navigation to wrap', () => {
        renderWithContext(<SidebarTree labelledBy="scenario-label" expanded={new Set()} onToggle={() => {}} />);

        const tree = screen.getByRole('tree');
        const mover = JSON.parse(tree.getAttribute('data-tabster') ?? '{}').mover;

        expect(mover.cyclic).toBe(false);
    });

    it('uses controlled expansion and preserves it across context rerenders', () => {
        renderWithContext(
            <>
                <ControlledSidebarTree initialExpanded={['root.GroupA']} />
                <SidebarContextControls />
            </>,
            richDataset,
        );

        let groupA = screen.getByRole('treeitem', { name: /^GroupA/ });
        expect(groupA).toHaveAttribute('aria-expanded', 'true');
        expect(groupA).toHaveAttribute('aria-level', '1');
        expect(groupA).toHaveAttribute('aria-posinset', '2');
        expect(groupA).toHaveAttribute('aria-setsize', '5');
        expect(screen.getByRole('treeitem', { name: /^FactualAccuracy/ })).toBeVisible();

        fireEvent.click(screen.getByRole('button', { name: 'Collapse GroupA' }));
        expect(groupA).toHaveAttribute('aria-expanded', 'false');
        expect(screen.queryByRole('treeitem', { name: /^FactualAccuracy/ })).not.toBeInTheDocument();

        fireEvent.keyDown(groupA, { key: 'ArrowRight' });
        expect(groupA).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByRole('treeitem', { name: /^FactualAccuracy/ })).toBeVisible();

        fireEvent.keyDown(groupA, { key: 'ArrowLeft' });
        expect(groupA).toHaveAttribute('aria-expanded', 'false');
        expect(screen.queryByRole('treeitem', { name: /^FactualAccuracy/ })).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('treeitem', { name: /^GroupB/ }));
        expect(groupA).toHaveAttribute('aria-expanded', 'false');

        fireEvent.click(screen.getByRole('button', { name: 'filter scenarios' }));
        expect(groupA).toHaveAttribute('aria-expanded', 'false');
        expect(screen.getByRole('treeitem', { name: /^GroupC/ })).toBeVisible();

        fireEvent.click(screen.getByRole('button', { name: 'change execution' }));
        groupA = screen.getByRole('treeitem', { name: /^GroupA/ });
        expect(groupA).toHaveAttribute('aria-expanded', 'false');
        expect(groupA).toHaveAttribute('aria-setsize', '3');
        expect(screen.queryByRole('treeitem', { name: /^GroupC/ })).not.toBeInTheDocument();

    });
});
