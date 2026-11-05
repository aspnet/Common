// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { createScoreSummary } from '../components';
import { singleExecutionDataset, twoExecutionDataset } from './fixtures/richDataset';


const makeScenario = (
    scenarioName: string,
    iterationName: string,
    executionName: string,
    failed: boolean,
): ScenarioRunResult => ({
    scenarioName,
    iterationName,
    executionName,
    creationTime: new Date().toISOString(),
    messages: [],
    modelResponse: { messages: [] },
    evaluationResult: {
        metrics: {
            coherence: {
                $type: 'numeric',
                name: 'coherence',
                value: failed ? 1 : 5,
                interpretation: { rating: failed ? 'unacceptable' : 'exceptional', failed },
            } as NumericMetric,
        },
    },
    formatVersion: 1,
});

const dataset: Dataset = {
    scenarioRunResults: [
        makeScenario('GroupA.ScenarioX', 'iteration1', 'exec1', false),
        makeScenario('GroupA.ScenarioY', 'iteration1', 'exec1', true),
        makeScenario('GroupB.ScenarioZ', 'iteration1', 'exec1', false),
    ],
    createdAt: new Date().toISOString(),
    generatorVersion: '0.0.0-test',
};

describe('createScoreSummary — aggregate pass/fail counts', () => {
    it('returns the correct passing and failing iteration counts at the root', () => {
        const summary = createScoreSummary(dataset);
        const root = summary.primaryResult;

        expect(root.numPassingIterations).toBe(2);
        expect(root.numFailingIterations).toBe(1);
    });

    it('marks the root as failed when any child fails', () => {
        const summary = createScoreSummary(dataset);
        expect(summary.primaryResult.failed).toBe(true);
    });
});

describe('createScoreSummary — report history flag', () => {
    it('a single-execution report has one execution and no report history', () => {
        const summary = createScoreSummary(singleExecutionDataset);
        expect(summary.executionHistory.size).toBe(1);
        expect(summary.includesReportHistory).toBe(false);
    });

    it('a multi-execution report exposes its execution history', () => {
        const summary = createScoreSummary(twoExecutionDataset);
        expect(summary.executionHistory.size).toBe(2);
        expect(summary.includesReportHistory).toBe(true);
    });
});
