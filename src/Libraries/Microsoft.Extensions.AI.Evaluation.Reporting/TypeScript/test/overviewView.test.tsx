// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, useReportContext, type ReportContextType } from '../components';
import { OverviewView } from '../components/overview/OverviewView';

const numeric = (name: string, value: number, rating: EvaluationRating, failed: boolean): NumericMetric =>
    ({
        $type: 'numeric',
        name,
        value,
        reason: 'test',
        interpretation: { rating, failed },
    }) as NumericMetric;

const run = (
    scenarioName: string,
    executionName: string,
    creationTime: string,
    metrics: Record<string, NumericMetric>,
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

const T_OLD = '2026-01-01T00:00:00.000Z';
const T_NEW = '2026-02-01T00:00:00.000Z';
const NEW = 'run-new';
const OLD = 'run-old';

const moversDataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: T_NEW,
    scenarioRunResults: [
        run('Alpha.Retrieval', NEW, T_NEW, {
            accuracy: numeric('accuracy', 4, 'good', false),
            safety: numeric('safety', 2, 'poor', true), // weak + failed → needs attention
            coverage: numeric('coverage', 3, 'good', false), // unchanged → zero-delta mover
        }),
        run('Alpha.Retrieval', OLD, T_OLD, {
            accuracy: numeric('accuracy', 2, 'poor', false),
            safety: numeric('safety', 5, 'exceptional', false),
            coverage: numeric('coverage', 3, 'good', false),
        }),
    ],
};

const noMoversDataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: T_NEW,
    scenarioRunResults: [
        run('Beta.QA', 'only', T_NEW, {
            clarity: numeric('clarity', 3, 'average', false),
        }),
    ],
};

const renderOverview = (dataset: Dataset, extra?: React.ReactNode) => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
            <OverviewView />
            {extra}
        </ReportContextProvider>,
    );
};

describe('OverviewView — pass-rate hero', () => {
    it('renders the overall pass-rate KPI (eyebrow + progress bar with a percentage)', () => {
        renderOverview(moversDataset);
        expect(screen.getByText('Overall pass rate')).toBeInTheDocument();
        expect(screen.getByLabelText(/Overall pass rate \d+%/)).toBeInTheDocument();
        expect(screen.getByText('Cases failing')).toBeInTheDocument();
    });
});

describe('OverviewView — movers vs needs-attention layout switch', () => {
    it('uses the two-pane layout when there are movers (predecessor exists)', () => {
        const { container } = renderOverview(moversDataset);
        expect(container.querySelector('.eval-twopane')).not.toBeNull();
        expect(screen.getByText('Biggest movers')).toBeInTheDocument();
        expect(screen.getByText('Needs attention')).toBeInTheDocument();
    });

    it('uses the single-pane layout (no movers) when the active run has no predecessor', () => {
        const { container } = renderOverview(noMoversDataset);
        expect(container.querySelector('.eval-twopane')).toBeNull();
        expect(screen.queryByText('Biggest movers')).not.toBeInTheDocument();
        expect(screen.getByText('Needs attention')).toBeInTheDocument();
        expect(screen.getByText('Beta.QA · clarity')).toBeInTheDocument();
    });
});

describe('OverviewView — needs-attention row + View action', () => {
    it('lists a weak/failing metric as a needs-attention row', () => {
        renderOverview(moversDataset);
        const attnNames = [...document.querySelectorAll('.eval-attn-name')].map((e) => e.textContent);
        expect(attnNames).toContain('Alpha.Retrieval · safety');
        expect(screen.getByRole('button', { name: 'View cases for Alpha.Retrieval · safety' })).toBeInTheDocument();
    });

    it('clicking "View" opens cases for that scenario (setView(cases) + selectScenarioLevel)', () => {
        let ctx: ReportContextType | undefined;
        const Probe = () => {
            ctx = useReportContext();
            return null;
        };
        renderOverview(moversDataset, <Probe />);

        expect(ctx?.view).toBe('overview');
        expect(ctx?.selectedScenarioLevel).toBeUndefined();

        fireEvent.click(screen.getByRole('button', { name: 'View cases for Alpha.Retrieval · safety' }));

        expect(ctx?.view).toBe('cases');
        expect(ctx?.selectedScenarioLevel).toBeTruthy();
        expect(ctx?.selectedScenarioLevel).toContain('Alpha');
    });

    it('labels weak ratings as weak instead of reporting them as case failures', () => {
        const weakButPassing: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Ratings.Weak', NEW, T_NEW, {
                    quality: numeric('quality', 1, 'poor', false),
                }),
            ],
        };

        renderOverview(weakButPassing);

        expect(screen.getByText('1 weak · 0 fair')).toBeInTheDocument();
        expect(screen.queryByText(/1 failing/i)).not.toBeInTheDocument();
        expect(screen.getByText('Cases failing').parentElement?.textContent).toContain('0');
    });

    it('uses direct semantic rating tokens for weak- and fair-dominant markers', () => {
        const weakRender = renderOverview(moversDataset);
        const weakRow = weakRender.container.querySelector<HTMLElement>('.eval-attn-name[title="Alpha.Retrieval · safety"]')?.parentElement;
        expect((weakRow?.firstElementChild as HTMLElement | null)?.style.background).toBe('var(--negative-solid)');
        weakRender.unmount();

        const fairRender = renderOverview(noMoversDataset);
        const fairRow = fairRender.container.querySelector<HTMLElement>('.eval-attn-name[title="Beta.QA · clarity"]')?.parentElement;
        expect((fairRow?.firstElementChild as HTMLElement | null)?.style.background).toBe('var(--caution-solid)');
        expect(screen.getByText('0 weak · 1 fair')).toBeInTheDocument();
    });
});

describe('OverviewView — pass-rate color thresholds', () => {
    it('preserves the existing success, caution, warning, and danger boundaries', () => {
        const groupRuns = (group: string, total: number, passing: number): ScenarioRunResult[] =>
            Array.from({ length: total }, (_, index) => run(
                `${group}.Case${index}`,
                NEW,
                T_NEW,
                { score: numeric('score', index < passing ? 4 : 1, index < passing ? 'good' : 'poor', index >= passing) },
            ));
        const dataset: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                ...groupRuns('Ninety', 10, 9),
                ...groupRuns('SeventyFive', 4, 3),
                ...groupRuns('Fifty', 2, 1),
                ...groupRuns('Below', 2, 0),
            ],
        };
        const { container } = renderOverview(dataset);
        const groupDotBackground = (group: string): string | undefined => {
            const row = [...container.querySelectorAll<HTMLElement>('.eval-grid6')]
                .find((candidate) => candidate.firstElementChild?.textContent?.trim() === group);
            return row?.firstElementChild?.querySelector<HTMLElement>('span[aria-hidden="true"]')?.style.background;
        };

        expect(groupDotBackground('Ninety')).toBe('var(--positive-solid)');
        expect(groupDotBackground('SeventyFive')).toBe('var(--caution-solid)');
        expect(groupDotBackground('Fifty')).toBe('var(--warning-solid)');
        expect(groupDotBackground('Below')).toBe('var(--negative-solid)');
    });
});

describe('OverviewView — mover delta rendering', () => {
    const moverDeltaCell = (label: string): Element | null | undefined => {
        const nameCell = document.querySelector(`[title="${label}"]:not(.eval-attn-name)`)?.parentElement;
        return nameCell?.nextElementSibling?.nextElementSibling;
    };

    it('renders a zero-delta mover as a plain em dash (no ▲/▼ badge)', () => {
        renderOverview(moversDataset);
        const coverage = screen.getByText('Alpha.Retrieval · coverage').closest('span');
        const rowRoot = coverage?.parentElement?.parentElement; // grid emits 3 sibling cells per mover
        expect(rowRoot?.textContent).toContain('—');
    });

    it('judges a lower-is-better metric from the library ratings — a drop reads as improved, not backwards', () => {
        const inverted: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Gamma.Toxicity', NEW, T_NEW, {
                    toxicity: numeric('toxicity', 1, 'exceptional', false),
                }),
                run('Gamma.Toxicity', OLD, T_OLD, {
                    toxicity: numeric('toxicity', 5, 'unacceptable', true),
                }),
            ],
        };
        renderOverview(inverted);
        const nameCell = screen.getByText('Gamma.Toxicity · toxicity').parentElement;
        const deltaCell = nameCell?.nextElementSibling?.nextElementSibling;
        expect(deltaCell?.textContent).toContain('▼');
        expect(deltaCell?.textContent).not.toContain('▲');
        expect(deltaCell?.textContent).toContain('decreased by 4, improved');
        expect(deltaCell?.textContent).not.toContain('regressed');
    });

    it('judges by the inferred direction — a higher-is-better metric that rises reads as improved', () => {
        const higher: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Quality.Alpha', NEW, T_NEW, { quality: numeric('quality', 5, 'exceptional', false) }),
                run('Quality.Beta', NEW, T_NEW, { quality: numeric('quality', 3, 'average', false) }),
                run('Quality.Gamma', NEW, T_NEW, { quality: numeric('quality', 1, 'unacceptable', true) }),
                run('Quality.Alpha', OLD, T_OLD, { quality: numeric('quality', 4, 'good', false) }),
                run('Quality.Beta', OLD, T_OLD, { quality: numeric('quality', 3, 'average', false) }),
                run('Quality.Gamma', OLD, T_OLD, { quality: numeric('quality', 1, 'unacceptable', true) }),
            ],
        };
        renderOverview(higher);
        const nameCell = screen.getByText('Quality.Alpha · quality').parentElement;
        const deltaCell = nameCell?.nextElementSibling?.nextElementSibling;
        expect(deltaCell?.textContent).toContain('▲');
        expect(deltaCell?.textContent).toContain('increased by 1, improved');
        expect(deltaCell?.textContent).not.toContain('regressed');
    });

    it('leaves a metric with no rating signal unjudged (neutral, raw direction only)', () => {
        const noInterpretation: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Delta.Unrated', NEW, T_NEW, { latency: numeric('latency', 120, 'unknown', false) }),
                run('Delta.Unrated', OLD, T_OLD, { latency: numeric('latency', 100, 'unknown', false) }),
            ],
        };
        renderOverview(noInterpretation);
        const nameCell = screen.getByText('Delta.Unrated · latency').parentElement;
        const deltaCell = nameCell?.nextElementSibling?.nextElementSibling;
        expect(deltaCell?.textContent).toContain('▲');
        expect(deltaCell?.textContent).toContain('increased by 20');
        expect(deltaCell?.textContent).not.toMatch(/improved|regressed/i);
    });

    it('shows a non-zero delta when it is visible at the report precision', () => {
        const precise: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Precision.Small', NEW, T_NEW, { quality: numeric('quality', 1.004, 'good', false) }),
                run('Precision.Small', OLD, T_OLD, { quality: numeric('quality', 1, 'poor', false) }),
            ],
        };

        renderOverview(precise);

        const nameCell = screen.getByText('Precision.Small · quality').parentElement;
        const deltaCell = nameCell?.nextElementSibling?.nextElementSibling;
        expect(deltaCell?.textContent).toContain('▲');
        expect(deltaCell?.textContent).toContain('increased by 0.004, improved');
    });

    it('uses improved, regressed, and unchanged trend colors with borderless pills', () => {
        const { unmount } = renderOverview(moversDataset);

        const improvedPill = moverDeltaCell('Alpha.Retrieval · accuracy')?.querySelector<HTMLElement>('span[style]');
        const regressedPill = moverDeltaCell('Alpha.Retrieval · safety')?.querySelector<HTMLElement>('span[style]');
        expect(improvedPill?.style.color).toBe('var(--positive-text)');
        expect(improvedPill?.style.border).toBe('');
        expect(regressedPill?.style.color).toBe('var(--negative-text)');
        expect(regressedPill?.style.border).toBe('');

        unmount();
        const unknownRatings: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Delta.Unrated', NEW, T_NEW, { latency: numeric('latency', 120, 'unknown', false) }),
                run('Delta.Unrated', OLD, T_OLD, { latency: numeric('latency', 100, 'unknown', false) }),
            ],
        };
        renderOverview(unknownRatings);

        const unchangedCell = moverDeltaCell('Delta.Unrated · latency');
        const unchangedPill = unchangedCell?.querySelector<HTMLElement>('span[style]');
        expect(unchangedCell?.textContent).toContain('increased by 20');
        expect(unchangedCell?.textContent).not.toMatch(/improved|regressed/i);
        expect(unchangedPill?.style.color).toBe('var(--neutral-text)');
        expect(unchangedPill?.style.border).toBe('');
    });
});

describe('OverviewView — overall pass-rate delta when the suite grows between runs', () => {
    const runIter = (
        scenarioName: string,
        iterationName: string,
        executionName: string,
        creationTime: string,
        metrics: Record<string, NumericMetric>,
    ): ScenarioRunResult =>
        ({
            scenarioName,
            iterationName,
            executionName,
            creationTime,
            messages: [],
            modelResponse: { messages: [] },
            evaluationResult: { metrics },
            formatVersion: 1,
        }) as ScenarioRunResult;

    const passing = () => ({ score: numeric('score', 5, 'good', false) });
    const failing = () => ({ score: numeric('score', 1, 'poor', true) });

    const grownSuite: Dataset = {
        generatorVersion: '0.0.1',
        createdAt: T_NEW,
        scenarioRunResults: [
            runIter('Gamma.Suite', 'iteration1', NEW, T_NEW, passing()),
            runIter('Gamma.Suite', 'iteration2', NEW, T_NEW, passing()),
            runIter('Gamma.Suite', 'iteration3', NEW, T_NEW, passing()),
            runIter('Gamma.Suite', 'iteration4', NEW, T_NEW, failing()),
            runIter('Gamma.Suite', 'iteration1', OLD, T_OLD, passing()),
            runIter('Gamma.Suite', 'iteration2', OLD, T_OLD, passing()),
        ],
    };

    it('compares against the previous run\'s own total, not the current run\'s', () => {
        const { container } = renderOverview(grownSuite);

        expect(container.textContent).toContain('▼ −25%');
        expect(container.textContent).not.toContain('▲ +25%');
    });

    it('uses improved, regressed, and unchanged group-delta text colors', () => {
        const statusDataset: Dataset = {
            generatorVersion: '0.0.1',
            createdAt: T_NEW,
            scenarioRunResults: [
                run('Improved.One', NEW, T_NEW, { score: numeric('score', 5, 'exceptional', false) }),
                run('Regressed.One', NEW, T_NEW, { score: numeric('score', 1, 'poor', true) }),
                run('Stable.One', NEW, T_NEW, { score: numeric('score', 3, 'unknown', false) }),
                run('Improved.One', OLD, T_OLD, { score: numeric('score', 1, 'poor', true) }),
                run('Regressed.One', OLD, T_OLD, { score: numeric('score', 5, 'exceptional', false) }),
                run('Stable.One', OLD, T_OLD, { score: numeric('score', 3, 'unknown', false) }),
            ],
        };
        const { container } = renderOverview(statusDataset);
        const deltaCell = (group: string): HTMLElement | undefined => {
            const row = [...container.querySelectorAll<HTMLElement>('.eval-grid6')]
                .find((candidate) => candidate.firstElementChild?.textContent?.trim() === group);
            return row?.lastElementChild as HTMLElement | undefined;
        };

        expect(deltaCell('Improved')?.textContent).toBe('▲ +100%');
        expect(deltaCell('Improved')?.style.color).toBe('var(--positive-text)');
        expect(deltaCell('Regressed')?.textContent).toBe('▼ −100%');
        expect(deltaCell('Regressed')?.style.color).toBe('var(--negative-text)');
        expect(deltaCell('Stable')?.textContent).toBe('—');
        expect(deltaCell('Stable')?.style.color).toBe('var(--neutral-text)');
    });
});
