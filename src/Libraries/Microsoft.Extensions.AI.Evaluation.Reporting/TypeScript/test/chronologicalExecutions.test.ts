// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, expect, it } from 'vitest';
import { chronologicalExecutions } from '../components/core/viewModels';

const scenario = (executionName: string, creationTime: string, iterationName = 'iteration1'): ScenarioRunResult =>
    ({
        scenarioName: 'Ordering.Scenario',
        iterationName,
        executionName,
        creationTime,
        messages: [],
        modelResponse: { messages: [] },
        evaluationResult: { metrics: {} },
        formatVersion: 1,
    }) as ScenarioRunResult;

const dataset = (...scenarioRunResults: ScenarioRunResult[]): Dataset => ({
    generatorVersion: '0.0.1',
    createdAt: '2026-01-01T00:00:00.000Z',
    scenarioRunResults,
});

describe('chronologicalExecutions', () => {
    it('orders timezone-offset timestamps by their actual instants', () => {
        const result = chronologicalExecutions(dataset(
            scenario('later', '2026-01-01T00:30:00Z'),
            scenario('earlier', '2026-01-01T01:00:00+02:00'),
        ));

        expect(result).toEqual(['earlier', 'later']);
    });

    it('uses the earliest actual instant across every result in an execution', () => {
        const result = chronologicalExecutions(dataset(
            scenario('multi', '2026-01-02T00:00:00Z', 'iteration1'),
            scenario('single', '2026-01-01T00:00:00Z'),
            scenario('multi', '2026-01-01T01:00:00+02:00', 'iteration2'),
        ));

        expect(result).toEqual(['multi', 'single']);
    });

    it('falls back deterministically for invalid timestamps and equal instants', () => {
        const invalid = chronologicalExecutions(dataset(
            scenario('invalid-z', 'not-a-time-z'),
            scenario('invalid-a-first', 'not-a-time-a'),
            scenario('invalid-a-second', 'not-a-time-a'),
        ));
        const equalInstants = chronologicalExecutions(dataset(
            scenario('offset-first', '2025-12-31T19:00:00-05:00'),
            scenario('utc-second', '2026-01-01T00:00:00Z'),
        ));

        expect(invalid).toEqual(['invalid-a-first', 'invalid-a-second', 'invalid-z']);
        expect(equalInstants).toEqual(['offset-first', 'utc-second']);
    });
});
