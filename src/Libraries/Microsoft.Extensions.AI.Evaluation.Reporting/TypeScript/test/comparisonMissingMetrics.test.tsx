// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ComparisonView, createScoreSummary, ReportContextProvider } from '../components';

const metric = (name: string, value: number): NumericMetric =>
    ({
        $type: 'numeric',
        name,
        value,
        interpretation: { rating: 'good', failed: false },
    }) as NumericMetric;

const scenario = (
    executionName: string,
    creationTime: string,
    metrics: Record<string, NumericMetric>,
): ScenarioRunResult =>
    ({
        scenarioName: 'Comparison.MissingMetrics',
        iterationName: 'iteration1',
        executionName,
        creationTime,
        messages: [],
        modelResponse: { messages: [] },
        evaluationResult: { metrics },
        formatVersion: 1,
    }) as ScenarioRunResult;

const missingMetricsDataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: '2026-02-01T00:00:00Z',
    scenarioRunResults: [
        scenario('baseline', '2026-01-01T00:00:00Z', {
            removed: metric('removed', 5),
            shared: metric('shared', 1),
        }),
        scenario('current', '2026-02-01T00:00:00Z', {
            added: metric('added', 7),
            shared: metric('shared', 3),
        }),
    ],
};

const renderComparison = () => {
    const scoreSummary = createScoreSummary(missingMetricsDataset);
    return render(
        <ReportContextProvider dataset={missingMetricsDataset} scoreSummary={scoreSummary}>
            <ComparisonView />
        </ReportContextProvider>,
    );
};

const metricRow = (name: string): HTMLElement =>
    screen.getByText(name, { selector: '[role="cell"]' }).closest('[role="row"]') as HTMLElement;

describe('ComparisonView missing-side metrics', () => {
    it('labels a current-only metric as added without fabricating a delta from zero', () => {
        renderComparison();
        const row = metricRow('added');

        expect(row).toHaveTextContent('—→7');
        expect(row).toHaveTextContent('Added');
        expect(row).toHaveTextContent('added in current execution');
        expect(row).not.toHaveTextContent(/[▲▼]/);
        expect(screen.queryByText(/increased by 7/i)).not.toBeInTheDocument();
    });

    it('labels a baseline-only metric as removed without fabricating a current zero', () => {
        renderComparison();
        const row = metricRow('removed');

        expect(row).toHaveTextContent('5→—');
        expect(row).toHaveTextContent('Removed');
        expect(row).toHaveTextContent('not present in current execution');
        expect(row).not.toHaveTextContent(/[▲▼]/);
        expect(screen.queryByText(/decreased by 5/i)).not.toBeInTheDocument();
    });

    it('keeps normal numeric delta behavior for metrics present in both executions', () => {
        renderComparison();
        const row = metricRow('shared');

        expect(row).toHaveTextContent('1→3');
        expect(row).toHaveTextContent('▲ 2');
        expect(row).toHaveTextContent('increased by 2');
    });
});
