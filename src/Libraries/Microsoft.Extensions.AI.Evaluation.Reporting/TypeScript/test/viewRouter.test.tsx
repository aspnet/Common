// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React, { useEffect } from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, useReportContext, type ReportView } from '../components';
import { ViewRouter } from '../components/shell/ViewRouter';
import { twoExecutionDataset } from './fixtures/richDataset';

// ViewRouter reads `view` from ReportContext and returns exactly one view component:
//   'cases' -> CasesView, 'history' -> HistoryView, 'comparison' -> ComparisonView,
//   'overview' | default -> OverviewView.
// Each view exposes a unique text marker, so asserting that marker is enough to prove routing.
const MARKER: Record<ReportView, RegExp> = {
    overview: /overall pass rate/i,   // OverviewView SummaryCard eyebrow
    cases: /show failed/i,            // CasesView failed-only switch label
    history: /run history/i,          // HistoryView run-history section
    comparison: /per-metric change/i, // ComparisonView per-metric section
};

// Drives the context to `view` after mount (mirrors the useEffect setter pattern in views.test.tsx).
// `view` is typed loosely so the "unexpected value" fallback case can force a non-ReportView string.
const RouteAt = ({ view }: { view?: string }) => {
    const { view: current, setView } = useReportContext();
    useEffect(() => {
        if (view !== undefined && current !== view) {
            setView(view as ReportView);
        }
    }, [view, current, setView]);
    return <ViewRouter />;
};

const renderRouter = (ui: React.ReactElement) => {
    const scoreSummary = createScoreSummary(twoExecutionDataset);
    return render(
        <ReportContextProvider dataset={twoExecutionDataset} scoreSummary={scoreSummary}>
            {ui}
        </ReportContextProvider>,
    );
};

afterEach(() => {
    cleanup();
});

describe('ViewRouter — routing per ReportView', () => {
    it.each([
        ['overview', undefined],
        ['cases', 'cases'],
        ['history', 'history'],
        ['comparison', 'comparison'],
    ] satisfies [ReportView, ReportView | undefined][])('renders %s for its valid route', async (expected, view) => {
        renderRouter(view === undefined ? <ViewRouter /> : <RouteAt view={view} />);
        expect(await screen.findByText(MARKER[expected])).toBeInTheDocument();
        for (const [candidate, marker] of Object.entries(MARKER)) {
            if (candidate !== expected) expect(screen.queryByText(marker)).not.toBeInTheDocument();
        }
    });

    it('renders OverviewView (default branch) for an unexpected view value', async () => {
        // The switch default falls through to OverviewView for any value outside the union.
        renderRouter(<RouteAt view="__not-a-real-view__" />);
        expect(await screen.findByText(MARKER.overview)).toBeInTheDocument();
        expect(screen.queryByText(MARKER.cases)).not.toBeInTheDocument();
    });
});
