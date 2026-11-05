// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, useReportContext, HistoryView } from '../components';

const E1 = 'EvaluationRun-alpha';
const E2 = 'EvaluationRun-bravo';
const E3 = 'EvaluationRun-charlie';

const T1 = '2026-01-01T00:00:00.000Z';
const T2 = '2026-02-01T00:00:00.000Z';
const T3 = '2026-03-01T00:00:00.000Z';

const numeric = (name: string, value: number): NumericMetric =>
    ({
        $type: 'numeric',
        name,
        value,
        reason: 'test',
        interpretation: { rating: 'good', failed: false },
    }) as NumericMetric;

const row = (
    executionName: string,
    creationTime: string,
    quality: number,
): ScenarioRunResult =>
    ({
        scenarioName: 'Group.Scenario',
        iterationName: 'iteration1',
        executionName,
        creationTime,
        messages: [],
        modelResponse: { messages: [] },
        evaluationResult: { metrics: { quality: numeric('quality', quality) } },
        formatVersion: 1,
    }) as ScenarioRunResult;

const dataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: T3,
    scenarioRunResults: [
        row(E3, T3, 4),
        row(E2, T2, 3),
        row(E1, T1, 2),
    ],
};

const renderHistory = () => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
            <HistoryView />
        </ReportContextProvider>,
    );
};

const runExecColumn = (): string[] =>
    [...document.querySelectorAll('.eval-grid4')]
        .map((r) => (r.querySelector(':scope > span')?.textContent ?? '').trim())
        .filter((t) => t && t !== 'Execution');

const statValue = (label: string): string => {
    const card = [...document.querySelectorAll('.eval-hist-stats > div')].find(
        (d) => d.querySelector('div')?.textContent?.trim() === label,
    );
    return (card?.querySelectorAll('div')[1]?.textContent ?? '').trim();
};

describe('HistoryView — chronological ordering', () => {
    it('renders run-history rows oldest-first even when inserted newest-first', () => {
        renderHistory();
        expect(runExecColumn()).toEqual([E1, E2, E3]);
    });

    it('labels First run score = oldest run and Last run score = newest run', () => {
        renderHistory();
        expect(statValue('First run score')).toBe('2');
        expect(statValue('Last run score')).toBe('4');
    });

    it('reports Net change as newest − oldest (an improvement, not a decline)', () => {
        renderHistory();
        expect(statValue('Net change')).toBe('▲ 2');
    });

    it("the middle row's dumbbell baselines against the chronologically-earlier run", () => {
        renderHistory();
        const rows = [...document.querySelectorAll('.eval-grid4')].filter(
            (r) => (r.querySelector(':scope > span')?.textContent ?? '').trim() === E2,
        );
        expect(rows.length).toBe(1);
        const middle = rows[0];

        const changeSpan = [...middle.querySelectorAll('span')].find(
            (s) => /[▲▼]/.test(s.textContent ?? '') && s.children.length === 0,
        );
        expect(changeSpan?.textContent?.trim()).toBe('▲ 1');

        const track = middle.querySelector('span[style*="min-width"]');
        const lefts = track
            ? [...track.querySelectorAll('span')]
                  .map((s) => s.getAttribute('style') ?? '')
                  .map((st) => /width:\s*8px/.test(st) ? st.match(/left:\s*([\d.]+)%/)?.[1] : undefined)
                  .filter((v): v is string => v !== undefined)
                  .map(Number)
            : [];
        expect(lefts.length).toBe(2);
        const [prevPct, curPct] = lefts;
        expect(prevPct).toBeLessThan(curPct);
    });

    it('keeps insertion order for equal chronology keys in both the timeline and chart', () => {
        const tiedDataset: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T1,
            scenarioRunResults: [
                row(E2, T1, 3),
                row(E1, T1, 2),
                row(E3, T1, 4),
            ],
        };
        const scoreSummary = createScoreSummary(tiedDataset);
        render(
            <ReportContextProvider dataset={tiedDataset} scoreSummary={scoreSummary}>
                <HistoryView />
            </ReportContextProvider>,
        );

        expect(runExecColumn()).toEqual([E2, E1, E3]);
        const chart = screen.getByRole('img', { name: /quality trend across executions/i });
        expect([...chart.querySelectorAll('circle title')].map((title) => title.textContent)).toEqual([
            'R1: mean 3',
            'R2: mean 2',
            'R3: mean 4',
        ]);
    });
});

describe('HistoryView — switching scenarios does not violate the Rules of Hooks', () => {
    const multiRow = (executionName: string, creationTime: string, quality: number): ScenarioRunResult =>
        ({
            scenarioName: 'Group.Multi',
            iterationName: 'iteration1',
            executionName,
            creationTime,
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics: { quality: numeric('quality', quality) } },
            formatVersion: 1,
        }) as ScenarioRunResult;

    const soloRow: ScenarioRunResult = {
        scenarioName: 'Group.Solo',
        iterationName: 'iteration1',
        executionName: E1,
        creationTime: T1,
        messages: [],
        modelResponse: { messages: [] },
        evaluationResult: { metrics: { quality: numeric('quality', 3) } },
        formatVersion: 1,
    } as ScenarioRunResult;

    const mixedDataset: Dataset = {
        generatorVersion: '0.0.1',
        createdAt: T2,
        scenarioRunResults: [multiRow(E1, T1, 2), multiRow(E2, T2, 4), soloRow],
    };

    const ScenarioButtons = () => {
        const { activeNode, selectScenarioLevel } = useReportContext();
        const keyFor = (scenarioName: string): string | undefined =>
            activeNode.flattenedNodes.find((n) => n.isLeafNode && n.scenario?.scenarioName === scenarioName)?.nodeKey;
        return (
            <>
                <button onClick={() => { const k = keyFor('Group.Multi'); if (k) selectScenarioLevel(k); }}>
                    select-multi
                </button>
                <button onClick={() => { const k = keyFor('Group.Solo'); if (k) selectScenarioLevel(k); }}>
                    select-solo
                </button>
            </>
        );
    };

    it('renders the trend for a multi-execution scenario, then the empty state for a single-execution one, without crashing', () => {
        const scoreSummary = createScoreSummary(mixedDataset);
        render(
            <ReportContextProvider dataset={mixedDataset} scoreSummary={scoreSummary}>
                <ScenarioButtons />
                <HistoryView />
            </ReportContextProvider>,
        );

        fireEvent.click(screen.getByText('select-multi'));
        expect(screen.getAllByRole('tab').length).toBeGreaterThan(0);

        expect(() => fireEvent.click(screen.getByText('select-solo'))).not.toThrow();
        expect(screen.getByText(/needs at least 2 executions/i)).toBeInTheDocument();
        expect(screen.queryAllByRole('tab').length).toBe(0);
    });
});

describe('HistoryView — scenario and iteration scope', () => {
    const iterationRow = (
        scenarioName: string,
        iterationName: string,
        executionName: string,
        creationTime: string,
        quality: number,
    ): ScenarioRunResult =>
        ({
            scenarioName,
            iterationName,
            executionName,
            creationTime,
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics: { quality: numeric('quality', quality) } },
            formatVersion: 1,
        }) as ScenarioRunResult;

    it('aggregates all scenario iterations into mean, median, and min-max values per execution', () => {
        const scopedDataset: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T2,
            scenarioRunResults: [
                iterationRow('Group.Multi', 'iteration1', E1, T1, 1),
                iterationRow('Group.Multi', 'iteration2', E1, T1, 2),
                iterationRow('Group.Multi', 'iteration3', E1, T1, 9),
                iterationRow('Group.Multi', 'iteration1', E2, T2, 2),
                iterationRow('Group.Multi', 'iteration2', E2, T2, 4),
                iterationRow('Group.Multi', 'iteration3', E2, T2, 12),
            ],
        };
        const scoreSummary = createScoreSummary(scopedDataset);
        render(
            <ReportContextProvider dataset={scopedDataset} scoreSummary={scoreSummary}>
                <HistoryView />
            </ReportContextProvider>,
        );

        expect(statValue('First run score')).toBe('4');
        expect(statValue('Last run score')).toBe('6');
        expect(statValue('Net change')).toBe('▲ 2');

        const chart = screen.getByRole('img', { name: /quality trend across executions/i });
        const medianLine = chart.querySelector('polyline[stroke-dasharray]');
        const meanLine = [...chart.querySelectorAll('polyline')].find(
            (line) => !line.hasAttribute('stroke-dasharray'),
        );

        expect(chart.querySelector('polygon')).not.toBeNull();
        expect(medianLine).not.toBeNull();
        expect(meanLine).toBeDefined();
        expect(medianLine?.getAttribute('points')).not.toBe(meanLine?.getAttribute('points'));
    });

    it('resolves a selected scenario that exists only in older executions', () => {
        const archivedDataset: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T3,
            scenarioRunResults: [
                iterationRow('Current.Only', 'iteration1', E3, T3, 5),
                iterationRow('Archived.Scenario', 'iteration1', E2, T2, 4),
                iterationRow('Archived.Scenario', 'iteration1', E1, T1, 2),
            ],
        };
        const scoreSummary = createScoreSummary(archivedDataset);
        const archivedKey = scoreSummary.executionHistory
            .get(E2)!
            .flattenedNodes.find((node) => node.scenario?.scenarioName === 'Archived.Scenario')!
            .nodeKey;
        const SelectArchived = () => {
            const { selectScenarioLevel } = useReportContext();
            return <button onClick={() => selectScenarioLevel(archivedKey)}>select-archived</button>;
        };
        render(
            <ReportContextProvider dataset={archivedDataset} scoreSummary={scoreSummary}>
                <SelectArchived />
                <HistoryView />
            </ReportContextProvider>,
        );

        fireEvent.click(screen.getByText('select-archived'));

        expect(screen.getByRole('tab', { name: 'quality' })).toBeInTheDocument();
        expect(statValue('First run score')).toBe('2');
        expect(statValue('Last run score')).toBe('4');
    });

    it('shows an unavailable state instead of falling back to an unrelated scenario', () => {
        const scoreSummary = createScoreSummary(dataset);
        const SelectMissing = () => {
            const { selectedScenarioLevel, selectScenarioLevel } = useReportContext();
            return (
                <>
                    <output>{selectedScenarioLevel ?? 'none'}</output>
                    <button onClick={() => selectScenarioLevel('root.Missing')}>select-missing</button>
                    <button onClick={() => selectScenarioLevel('root.Other')}>select-other</button>
                </>
            );
        };
        render(
            <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
                <SelectMissing />
                <HistoryView />
            </ReportContextProvider>,
        );

        fireEvent.click(screen.getByText('select-missing'));

        expect(screen.getByText('root.Missing')).toBeInTheDocument();
        expect(screen.getByText('Selected scenario unavailable')).toBeInTheDocument();
        expect(screen.queryByRole('tab')).not.toBeInTheDocument();

        fireEvent.click(screen.getByText('select-other'));
        expect(screen.getByText('root.Other')).toBeInTheDocument();

        fireEvent.click(screen.getByText('select-other'));
        expect(screen.getByText('none')).toBeInTheDocument();
    });
});
