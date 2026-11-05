// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { createScoreSummary } from '../components';

const makeScenario = (
    scenarioName: string,
    iterationName: string,
    failed: boolean = false,
): ScenarioRunResult => ({
    scenarioName,
    iterationName,
    executionName: 'exec1',
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

const asDataset = (scenarioRunResults: ScenarioRunResult[]): Dataset => ({
    scenarioRunResults,
    createdAt: new Date().toISOString(),
    generatorVersion: '0.0.0-test',
});

describe('createScoreSummary — scenario/iteration name collisions', () => {
    it('counts a run whose tree node is also a parent of another run', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('Coherence', 'Test1'),
            makeScenario('Coherence.Test1', 'X', true),
        ]));

        const root = summary.primaryResult;
        expect(root.numPassingIterations).toBe(1);
        expect(root.numFailingIterations).toBe(1);
        expect(root.failed).toBe(true);
    });

    it('counts both runs regardless of insertion order', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('Coherence.Test1', 'X', true),
            makeScenario('Coherence', 'Test1'),
        ]));

        const root = summary.primaryResult;
        expect(root.numPassingIterations).toBe(1);
        expect(root.numFailingIterations).toBe(1);
    });

    it('exposes both colliding runs as leaves with distinct node keys', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('Coherence', 'Test1'),
            makeScenario('Coherence.Test1', 'X', true),
        ]));

        const leaves = summary.primaryResult.flattenedNodes.filter((n) => n.isLeafNode);
        expect(leaves).toHaveLength(2);
        expect(new Set(leaves.map((n) => n.nodeKey)).size).toBe(2);
    });

    it('keeps both results when the same scenario and iteration is reported twice', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('GroupA.ScenarioX', 'iteration1'),
            makeScenario('GroupA.ScenarioX', 'iteration1', true),
        ]));

        const root = summary.primaryResult;
        expect(root.numPassingIterations).toBe(1);
        expect(root.numFailingIterations).toBe(1);

        const keys = [...summary.nodesByKey.get('exec1')!.keys()];
        expect(new Set(keys).size).toBe(keys.length);
    });

    it('indexes every duplicated node under its own key', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('GroupA.ScenarioX', 'iteration1'),
            makeScenario('GroupA.ScenarioX', 'iteration1', true),
        ]));

        const leaves = summary.primaryResult.flattenedNodes.filter((n) => n.isLeafNode);
        expect(leaves).toHaveLength(2);
        for (const leaf of leaves) {
            expect(summary.nodesByKey.get('exec1')!.get(leaf.nodeKey)).toBe(leaf);
        }
    });

    it('distinguishes scenario path segments from dotted iteration names', () => {
        const summary = createScoreSummary(asDataset([
            makeScenario('A.B', 'C'),
            makeScenario('A', 'B.C', true),
        ]));

        const leaves = summary.primaryResult.flattenedNodes.filter((node) => node.isLeafNode);
        expect(leaves).toHaveLength(2);
        expect(new Set(leaves.map((node) => node.nodeKey)).size).toBe(2);
        for (const leaf of leaves) {
            expect(summary.nodesByKey.get('exec1')!.get(leaf.nodeKey)).toBe(leaf);
        }
    });
});
