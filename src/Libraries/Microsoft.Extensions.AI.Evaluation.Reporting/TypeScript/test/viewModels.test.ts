// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { createScoreSummary } from '../components/core/Summary';
import {
    isLeafFailed,
    passRateByScenarioGroup,
    kpiCountsFromNode,
    ratingBucket,
    bucketMetrics,
    chronologicalExecutions,
} from '../components/core/viewModels';
import {
    multiGroupDataset,
    diagnosticsErrorDataset,
    twoExecutionDataset,
} from './fixtures/richDataset';

describe('passRateByScenarioGroup — group rows reconcile to the whole (multiGroupDataset)', () => {
    it('sum of per-group passing/total equals the tree iteration totals', () => {
        const summary = createScoreSummary(multiGroupDataset);
        const root = summary.primaryResult;

        const rows = passRateByScenarioGroup(multiGroupDataset);
        const sumPassing = rows.reduce((acc, r) => acc + r.passing, 0);
        const sumTotal = rows.reduce((acc, r) => acc + r.total, 0);

        const treePassing = root.numPassingIterations;
        const treeFailing = root.numFailingIterations;
        const treeTotal = treePassing + treeFailing;

        expect(sumTotal).toBe(treeTotal);
        expect(sumPassing).toBe(treePassing);
        expect(sumTotal - sumPassing).toBe(treeFailing);
    });

    it('produces one row per distinct scenario group with literal pass rates', () => {
        const rows = passRateByScenarioGroup(multiGroupDataset);

        expect(rows.map(r => r.group).sort()).toEqual(['GroupA', 'GroupB', 'GroupC']);

        const byGroup = (g: string) => rows.find(r => r.group === g)!;
        expect(byGroup('GroupA')).toMatchObject({ passing: 2, total: 2 });
        expect(byGroup('GroupA').passRate).toBeCloseTo(1, 10);
        expect(byGroup('GroupB')).toMatchObject({ passing: 2, total: 2 });
        expect(byGroup('GroupB').passRate).toBeCloseTo(1, 10);
        expect(byGroup('GroupC')).toMatchObject({ passing: 2, total: 3 });
        expect(byGroup('GroupC').passRate).toBeCloseTo(2 / 3, 10);
    });
});

describe('Overview derivations == Cases-tree counts (diagnosticsErrorDataset)', () => {
    it('KPI counts and group-sum counts equal the Cases-tree numPassing/numFailing', () => {
        const summary = createScoreSummary(diagnosticsErrorDataset);
        const tree = summary.primaryResult;

        expect(tree.numPassingIterations).toBe(2);
        expect(tree.numFailingIterations).toBe(1);

        const kpi = kpiCountsFromNode(tree);
        expect(kpi.passing).toBe(2);
        expect(kpi.failing).toBe(1);
        expect(kpi.total).toBe(3);
        expect(kpi.passRate).toBeCloseTo(2 / 3, 10);

        const rows = passRateByScenarioGroup(diagnosticsErrorDataset);
        const groupPassing = rows.reduce((acc, r) => acc + r.passing, 0);
        const groupTotal = rows.reduce((acc, r) => acc + r.total, 0);
        expect(groupPassing).toBe(2);
        expect(groupTotal).toBe(3);
    });

    it('isLeafFailed alone flags the diagnostics-only failure and clears the clean pass', () => {
        const failing = diagnosticsErrorDataset.scenarioRunResults.find(
            s => s.scenarioName === 'DiagTest.DiagnosticFailOnly',
        )!;
        const clean = diagnosticsErrorDataset.scenarioRunResults.find(
            s => s.scenarioName === 'DiagTest.CleanPass',
        )!;
        const info = diagnosticsErrorDataset.scenarioRunResults.find(
            s => s.scenarioName === 'DiagTest.InfoDiagnosticPass',
        )!;

        expect(failing.evaluationResult.metrics['mathematicalAccuracy'].interpretation?.failed).toBe(false);
        expect(isLeafFailed(failing)).toBe(true);
        expect(isLeafFailed(clean)).toBe(false);
        expect(isLeafFailed(info)).toBe(false);
    });
});

describe('isLeafFailed — empty-metrics semantics', () => {
    it('absent/empty metrics → false (uses .some, never .every)', () => {
        const noMetrics = {
            scenarioName: 'Empty.NoMetrics',
            iterationName: 'iteration1',
            executionName: 'exec-empty',
            creationTime: '2026-06-30T10:00:00.000Z',
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics: {} },
            formatVersion: 1,
        } as ScenarioRunResult;
        expect(isLeafFailed(noMetrics)).toBe(false);
    });
});

describe('ratingBucket + bucketMetrics', () => {
    it('maps ratings into good/fair/weak/unknown buckets', () => {
        expect(ratingBucket('exceptional')).toBe('good');
        expect(ratingBucket('good')).toBe('good');
        expect(ratingBucket('average')).toBe('fair');
        expect(ratingBucket('poor')).toBe('weak');
        expect(ratingBucket('unacceptable')).toBe('weak');
        expect(ratingBucket('unknown')).toBe('unknown');
        expect(ratingBucket('inconclusive')).toBe('unknown');
        expect(ratingBucket(undefined)).toBe('unknown');
    });

    it('buckets every metric into the exact good/fair/weak/unknown distribution', () => {
        const primary = multiGroupDataset.scenarioRunResults;
        const counts = bucketMetrics(primary);

        expect(counts).toEqual({ good: 3, fair: 3, weak: 2, unknown: 2 });

        // Conservation: all 10 metrics counted exactly once.
        const totalMetrics = primary.reduce(
            (acc, s) => acc + Object.values(s.evaluationResult.metrics).length,
            0,
        );
        expect(totalMetrics).toBe(10);
        expect(counts.good + counts.fair + counts.weak + counts.unknown).toBe(totalMetrics);
    });
});

describe('passRateByScenarioGroup — deltaRun compares against the chronologically previous run', () => {
    it('deltaRun is NEGATIVE (−0.5) when the active exec underperforms the previous run', () => {
        const rows = passRateByScenarioGroup(twoExecutionDataset, 'exec-v2');
        const comparison = rows.find(r => r.group === 'Comparison')!;
        expect(comparison).toMatchObject({ passing: 1, total: 2 });
        expect(comparison.passRate).toBeCloseTo(0.5, 10);
        expect(comparison.deltaRun).toBeCloseTo(-0.5, 10);
    });

    it('deltaRun is POSITIVE (+0.5) when a later exec outperforms the previous run', () => {
        // Both executions share a creationTime, so chronological order falls back to insertion
        // order; reversing it makes exec-v1 (2/2 passing) the later run, after exec-v2 (1/2).
        const reversed: Dataset = {
            ...twoExecutionDataset,
            scenarioRunResults: [...twoExecutionDataset.scenarioRunResults].reverse(),
        };
        const rows = passRateByScenarioGroup(reversed, 'exec-v1');
        const comparison = rows.find(r => r.group === 'Comparison')!;
        expect(comparison.passRate).toBeCloseTo(1, 10);
        expect(comparison.deltaRun).toBeCloseTo(0.5, 10);
    });

    it('deltaRun is undefined for the earliest execution (no previous run to compare)', () => {
        const rows = passRateByScenarioGroup(twoExecutionDataset); // default active = earliest exec-v1
        const comparison = rows.find(r => r.group === 'Comparison')!;
        expect(comparison).toMatchObject({ passing: 2, total: 2 });
        expect(comparison.deltaRun).toBeUndefined();
    });

    it('deltaRun is undefined for a single-execution dataset', () => {
        const single: Dataset = {
            ...twoExecutionDataset,
            scenarioRunResults: twoExecutionDataset.scenarioRunResults.filter(
                s => s.executionName === 'exec-v1',
            ),
        };
        for (const r of passRateByScenarioGroup(single)) {
            expect(r.deltaRun).toBeUndefined();
        }
    });
});

describe('passRateByScenarioGroup — out-of-order insertion', () => {
    const G1 = 'exec-earliest';
    const G2 = 'exec-middle';
    const G3 = 'exec-latest';

    const OT1 = '2026-04-01T00:00:00.000Z';
    const OT2 = '2026-05-01T00:00:00.000Z';
    const OT3 = '2026-06-01T00:00:00.000Z';

    const orderingMetric = (value: number, failed: boolean): NumericMetric =>
        ({
            $type: 'numeric',
            name: 'accuracy',
            value,
            interpretation: { rating: failed ? 'poor' : 'good', failed },
        }) as NumericMetric;

    const orderingScenario = (executionName: string, creationTime: string, value: number, failed: boolean): ScenarioRunResult =>
        ({
            scenarioName: 'Ordering.Case',
            iterationName: 'iteration1',
            executionName,
            creationTime,
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics: { accuracy: orderingMetric(value, failed) } },
            formatVersion: 1,
        }) as ScenarioRunResult;

    const outOfOrderDataset: Dataset = {
        generatorVersion: '0.0.1',
        createdAt: OT3,
        scenarioRunResults: [
            orderingScenario(G3, OT3, 5, false),
            orderingScenario(G1, OT1, 1, true),
            orderingScenario(G2, OT2, 3, false),
        ],
    };

    it('passRateByScenarioGroup resolves the chronological predecessor, not the insertion predecessor', () => {
        const chrono = chronologicalExecutions(outOfOrderDataset);
        expect(chrono).toEqual([G1, G2, G3]);

        const previous = chrono[chrono.indexOf(G3) - 1];
        expect(previous).toBe(G2);

        const rows = passRateByScenarioGroup(outOfOrderDataset, G3);
        const group = rows.find(r => r.group === 'Ordering')!;
        expect(group.deltaRun).toBeCloseTo(0, 10);
    });
});
