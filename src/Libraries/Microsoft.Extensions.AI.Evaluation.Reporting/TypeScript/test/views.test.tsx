// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React, { useEffect } from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, useReportContext, HistoryView, ComparisonView } from '../components';
import { twoExecutionDataset, singleExecutionDataset } from './fixtures/richDataset';

const renderWith = (dataset: Dataset, ui: React.ReactElement) => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
            {ui}
        </ReportContextProvider>,
    );
};

const ScenarioSelector = ({ scenarioName, children }: { scenarioName: string; children: React.ReactNode }) => {
    const { activeNode, selectedScenarioLevel, selectScenarioLevel } = useReportContext();
    const targetKey = activeNode.flattenedNodes.find(
        (n) => n.isLeafNode && n.scenario?.scenarioName === scenarioName,
    )?.nodeKey;
    useEffect(() => {
        if (targetKey && selectedScenarioLevel !== targetKey) {
            selectScenarioLevel(targetKey);
        }
    }, [targetKey, selectedScenarioLevel]);
    return <>{children}</>;
};

describe('HistoryView — twoExecutionDataset', () => {
    it('renders exactly one trend chart (role=img) labelled for the active metric + scenario', () => {
        renderWith(twoExecutionDataset, <HistoryView />);
        const charts = screen.getAllByRole('img');
        expect(charts.length).toBe(1);
        expect(charts[0]).toHaveAttribute(
            'aria-label',
            expect.stringMatching(/trend across executions for Comparison\./),
        );
    });

});

describe('HistoryView — singleExecutionDataset (empty state)', () => {
    it('renders the "Needs at least 2 executions" message', () => {
        renderWith(singleExecutionDataset, <HistoryView />);
        expect(screen.getByText(/needs at least 2 executions/i)).toBeInTheDocument();
    });

});

describe('ComparisonView — twoExecutionDataset', () => {
    it('renders execution dropdowns for A and B', () => {
        renderWith(twoExecutionDataset, <ComparisonView />);
        expect(screen.getByLabelText(/baseline execution/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/current execution/i)).toBeInTheDocument();
    });

    it('keeps one ordered, noninteractive headline summary', () => {
        renderWith(twoExecutionDataset, <ComparisonView />);

        const labels = ['Metrics increased', 'Metrics decreased', 'Biggest change'];
        const stats = labels.map((label) => screen.getByText(label).parentElement!);
        expect(stats.map((stat) => [...stat.children].map((child) => child.textContent))).toEqual([
            ['Metrics increased', '2', 'of 4 comparable metrics'],
            ['Metrics decreased', '1', 'of 4 comparable metrics'],
            ['Biggest change', '▼ 3', 'safety'],
        ]);
        expect(stats.every((stat) => stat.children.length === 3)).toBe(true);
        expect(new Set(stats.map((stat) => stat.parentElement)).size).toBe(1);
        expect(stats[0].parentElement?.querySelectorAll('button, [role="button"]')).toHaveLength(0);
        expect(screen.getAllByRole('combobox')).toHaveLength(2);
        expect(screen.getByRole('combobox', { name: 'Baseline execution' })).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Current execution' })).toBeInTheDocument();
    });

    const metricRowNames = (container: HTMLElement): string[] =>
        [...container.querySelectorAll('[role="rowgroup"] .eval-grid3[role="row"]')].map(
            (row) => row.firstElementChild?.textContent?.trim() ?? '',
        );

    it('renders all metric rows when no scenario is selected', () => {
        const { container } = renderWith(twoExecutionDataset, <ComparisonView />);
        expect(metricRowNames(container).sort()).toEqual(
            ['accuracy', 'coherence', 'fluency', 'safety'],
        );
    });

    it('hides other scenarios once a sidebar scenario is selected', async () => {
        const { container } = renderWith(
            twoExecutionDataset,
            <ScenarioSelector scenarioName="Comparison.TextSummary">
                <ComparisonView />
            </ScenarioSelector>,
        );
        await waitFor(() =>
            expect(metricRowNames(container).sort()).toEqual(['coherence', 'safety']),
        );
    });
});

describe('ComparisonView — value deltas with no inferable direction stay directional and unjudged', () => {
    const inv = (name: string, value: number): NumericMetric =>
        ({
            $type: 'numeric',
            name,
            value,
            reason: 'test',
            interpretation: { rating: 'good', failed: false },
        }) as NumericMetric;

    const invRow = (executionName: string, creationTime: string, metrics: Record<string, NumericMetric>): ScenarioRunResult =>
        ({
            scenarioName: 'Inv.Scenario',
            iterationName: 'iteration1',
            executionName,
            creationTime,
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics },
            formatVersion: 1,
        }) as ScenarioRunResult;

    // Every metric here holds a constant 'good' rating, so no better-direction can be inferred from
    // the data. With no direction signal the deltas stay purely directional: toxicity DROPS 5→2 and
    // flat RISES 3→5, and neither is judged "improved" nor "regressed".
    const inversionDataset: Dataset = {
        generatorVersion: '0.0.1',
        createdAt: '2026-04-01T00:00:00.000Z',
        scenarioRunResults: [
            invRow('exec-old', '2026-03-01T00:00:00.000Z', { toxicity: inv('toxicity', 5), flat: inv('flat', 3) }),
            invRow('exec-new', '2026-04-01T00:00:00.000Z', { toxicity: inv('toxicity', 2), flat: inv('flat', 5) }),
        ],
    };

    it('reports the raw direction of every value delta when no direction can be inferred', () => {
        renderWith(inversionDataset, <ComparisonView />);
        expect(screen.queryByText('Metrics improved')).not.toBeInTheDocument();
        expect(screen.queryByText('Metrics regressed')).not.toBeInTheDocument();
        expect(screen.getByText('Metrics increased').nextElementSibling?.textContent).toBe('1');
        expect(screen.getByText('Metrics decreased').nextElementSibling?.textContent).toBe('1');
    });

    it('surfaces the biggest raw delta by magnitude, not by a "better" judgment', () => {
        renderWith(inversionDataset, <ComparisonView />);
        const biggest = screen.getByText('Biggest change');
        expect(biggest.nextElementSibling?.textContent).toContain('▼');
        expect(biggest.nextElementSibling?.nextElementSibling?.textContent).toBe('toxicity');
    });

    it('never announces "improved"/"regressed" in the accessible per-metric delta text', () => {
        renderWith(inversionDataset, <ComparisonView />);
        expect(screen.getByText('decreased by 3')).toBeInTheDocument();
        expect(screen.getByText('increased by 2')).toBeInTheDocument();
        expect(screen.queryByText(/improved|regressed/i)).not.toBeInTheDocument();
    });
});

describe('ComparisonView — singleExecutionDataset (empty state)', () => {
    it('renders the "Needs at least 2 executions" message', () => {
        renderWith(singleExecutionDataset, <ComparisonView />);
        expect(screen.getByText(/needs at least 2 executions/i)).toBeInTheDocument();
    });

    it('does NOT render execution dropdowns', () => {
        renderWith(singleExecutionDataset, <ComparisonView />);
        expect(screen.queryByLabelText(/baseline execution/i)).not.toBeInTheDocument();
        expect(screen.queryByLabelText(/current execution/i)).not.toBeInTheDocument();
    });
});
