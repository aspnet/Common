// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { createScoreSummary } from '../components/core/Summary';

const emptyDataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: '2026-06-30T10:00:00.000Z',
    scenarioRunResults: [],
};

describe('createScoreSummary — empty-dataset guard', () => {
    it('returns a well-formed empty summary (no history, zero iterations)', () => {
        const summary = createScoreSummary(emptyDataset);

        expect(summary.includesReportHistory).toBe(false);
        expect(summary.executionHistory.size).toBe(0);
        expect(summary.nodesByKey.size).toBe(0);

        // A safe, empty root ScoreNode stands in for primaryResult.
        expect(summary.primaryResult).toBeDefined();
        expect(summary.primaryResult.numPassingIterations).toBe(0);
        expect(summary.primaryResult.numFailingIterations).toBe(0);
        expect(
            summary.primaryResult.numPassingIterations + summary.primaryResult.numFailingIterations,
        ).toBe(0);
    });

    it('does not throw when passed a bare empty array in place of a dataset', () => {
        // Reproduces the original crash surface (dataset.scenarioRunResults undefined).
        expect(() => createScoreSummary([] as unknown as Dataset)).not.toThrow();
        const summary = createScoreSummary([] as unknown as Dataset);
        expect(summary.includesReportHistory).toBe(false);
        expect(summary.executionHistory.size).toBe(0);
    });
});
