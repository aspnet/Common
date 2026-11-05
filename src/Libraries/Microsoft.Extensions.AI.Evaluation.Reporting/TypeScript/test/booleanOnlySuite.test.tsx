// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, HistoryView } from '../components';
import { OverviewView } from '../components/overview/OverviewView';

const boolMetric = (name: string, value: boolean, rating: EvaluationRating, failed: boolean): BooleanMetric => ({
    $type: 'boolean',
    name,
    value,
    reason: `Reason for ${name}.`,
    interpretation: { rating, failed },
});

const run = (
    scenarioName: string,
    executionName: string,
    creationTime: string,
    metrics: Record<string, BooleanMetric>,
): ScenarioRunResult =>
    ({
        scenarioName,
        iterationName: 'iteration1',
        executionName,
        creationTime,
        messages: [],
        modelResponse: { messages: [] },
        evaluationResult: { metrics },
        formatVersion: 1,
    }) as ScenarioRunResult;

const booleanOnlyDataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: '2026-02-01T00:00:00.000Z',
    scenarioRunResults: [
        run('Bool.Suite', 'run-old', '2026-01-01T00:00:00.000Z', {
            passed: boolMetric('passed', true, 'exceptional', false),
            safe: boolMetric('safe', false, 'unacceptable', true),
        }),
        run('Bool.Suite', 'run-new', '2026-02-01T00:00:00.000Z', {
            passed: boolMetric('passed', true, 'exceptional', false),
            safe: boolMetric('safe', true, 'exceptional', false),
        }),
    ],
};

const renderWith = (ui: React.ReactElement) => {
    const scoreSummary = createScoreSummary(booleanOnlyDataset);
    return render(
        <ReportContextProvider dataset={booleanOnlyDataset} scoreSummary={scoreSummary}>
            {ui}
        </ReportContextProvider>,
    );
};

describe('Boolean-only suite — OverviewView', () => {
    it('renders without throwing, with movers empty (non-goal) but needs-attention intact', () => {
        renderWith(<OverviewView />);

        expect(screen.getByText('Overall pass rate')).toBeInTheDocument();
        expect(screen.queryByText('Biggest movers')).not.toBeInTheDocument();
        expect(screen.getByText('Needs attention')).toBeInTheDocument();
    });
});

describe('Boolean-only suite — HistoryView', () => {
    it('renders without throwing, with the metric trend empty (non-goal, no numeric series)', () => {
        renderWith(<HistoryView />);

        expect(screen.queryAllByRole('tab')).toHaveLength(0);
        expect(screen.queryAllByRole('img')).toHaveLength(0);
        expect(screen.getByText(/needs at least 2 executions/i)).toBeInTheDocument();
    });
});
