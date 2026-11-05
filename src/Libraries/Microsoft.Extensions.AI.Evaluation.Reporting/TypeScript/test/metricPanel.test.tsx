// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider } from '../components';
import { MetricPanel } from '../components/cases/MetricPanel';
import { multiGroupDataset } from './fixtures/richDataset';

const scenarioWith = (metrics: EvaluationResult['metrics']): ScenarioRunResult =>
    ({ evaluationResult: { metrics } }) as unknown as ScenarioRunResult;

const numeric = (
    name: string,
    value: number | undefined,
    rating: EvaluationRating,
    failed: boolean,
    diagnostics?: EvaluationDiagnostic[],
    metadata?: { [K: string]: string },
): NumericMetric => ({
    $type: 'numeric',
    name,
    ...(value === undefined ? {} : { value }),
    reason: `Reason for ${name}.`,
    interpretation: { rating, failed },
    diagnostics,
    metadata,
});

const boolean = (name: string, value: boolean, rating: EvaluationRating, failed: boolean): BooleanMetric => ({
    $type: 'boolean',
    name,
    value,
    reason: `Reason for ${name}.`,
    interpretation: { rating, failed },
});

const none = (name: string, rating: EvaluationRating): MetricWithNoValue => ({
    $type: 'none',
    name,
    reason: `Reason for ${name}.`,
    interpretation: { rating, failed: false },
});

const string = (name: string, value: string, rating: EvaluationRating): StringMetric => ({
    $type: 'string',
    name,
    value,
    reason: `Reason for ${name}.`,
    interpretation: { rating, failed: false },
});

// The score/severity track renders one <span> per scale segment inside a single track span;
// filled segments carry a non-empty boxShadow ("aura"), empty ones leave it unset.
const segmentTrack = (button: HTMLElement): HTMLElement => {
    const track = Array.from(button.querySelectorAll('span')).find(
        (el) => el.querySelectorAll(':scope > span').length >= 2,
    );
    if (!track) throw new Error('segment track not found');
    return track as HTMLElement;
};
const filledCount = (track: HTMLElement): number =>
    Array.from(track.querySelectorAll(':scope > span')).filter((s) => (s as HTMLElement).style.boxShadow !== '').length;

describe('MetricPanel — boolean metrics', () => {
    it('renders a ✓ segment and a "Yes" hero for a passing boolean', () => {
        render(<MetricPanel scenario={scenarioWith({ ok: boolean('ok', true, 'exceptional', false) })} />);

        expect(screen.getByText('✓')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button'));
        expect(screen.getByText('Yes')).toBeInTheDocument();
        expect(screen.getByText('Exceptional')).toBeInTheDocument();
    });

    it('renders a ✗ segment and a "No" hero for a failing-value boolean', () => {
        render(<MetricPanel scenario={scenarioWith({ ok: boolean('ok', false, 'unacceptable', false) })} />);

        expect(screen.getByText('✗')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button'));
        expect(screen.getByText('No')).toBeInTheDocument();
        expect(screen.getByText('Weak')).toBeInTheDocument();
    });
});

describe('MetricPanel — numeric metrics (rating-ordinal meter)', () => {
    it('shows the raw value and fills the meter to the rating ordinal', () => {
        render(<MetricPanel scenario={scenarioWith({ quality: numeric('quality', 4, 'good', false) })} />);

        const button = screen.getByRole('button');
        const track = segmentTrack(button);
        expect(track.querySelectorAll(':scope > span')).toHaveLength(5);
        expect(filledCount(track)).toBe(4);

        fireEvent.click(button);
        expect(screen.getByText('4')).toBeInTheDocument();
    });

    it('renders a value whose interpretation has no ordinal rating inside a neutral pill', () => {
        render(<MetricPanel scenario={scenarioWith({ tokenCount: numeric('tokenCount', 842, 'unknown', false) })} />);

        const button = screen.getByRole('button');
        const preview = within(button).getByText('842');
        expect(preview.parentElement).toHaveStyle({ backgroundColor: 'var(--eval-seg-empty)' });
        expect(within(button).queryByText('?')).not.toBeInTheDocument();
        expect(button.getAttribute('aria-label')).toContain('842');

        fireEvent.click(button);
        expect(screen.getAllByText('842')).toHaveLength(2);
    });

    it('fills the meter to 1/5 and colours the dot danger for an unacceptable rating', () => {
        render(<MetricPanel scenario={scenarioWith({ toxicity: numeric('toxicity', 6, 'unacceptable', true) })} />);

        const button = screen.getByRole('button');
        const track = segmentTrack(button);
        expect(track.querySelectorAll(':scope > span')).toHaveLength(5);
        expect(filledCount(track)).toBe(1); // unacceptable -> 1/5

        const dotStyle = button.querySelector('span span')?.getAttribute('style') ?? '';
        expect(dotStyle).toContain('negative-solid');

        fireEvent.click(button);
        expect(screen.getByText('6')).toBeInTheDocument();
    });
});

describe('MetricPanel — string metrics', () => {
    it('renders a rated string value inside a neutral pill in the collapsed row', () => {
        render(<MetricPanel scenario={scenarioWith({ verdict: string('verdict', 'PASS', 'good') })} />);

        const button = screen.getByRole('button');
        const preview = within(button).getByText('PASS');
        expect(preview.parentElement).toHaveStyle({ backgroundColor: 'var(--eval-seg-empty)' });

        fireEvent.click(button);
        expect(screen.getAllByText('PASS')).toHaveLength(2);
    });

    it('renders a neutral string value instead of "?"', () => {
        render(<MetricPanel scenario={scenarioWith({ verdict: string('verdict', 'NEEDS_REVIEW', 'unknown') })} />);

        const button = screen.getByRole('button');
        expect(within(button).getByText('NEEDS_REVIEW')).toBeInTheDocument();
        expect(within(button).queryByText('?')).not.toBeInTheDocument();
    });

    it('ellipsizes a long collapsed preview while preserving the full accessible and expanded value', () => {
        const value = 'A'.repeat(200);
        render(<MetricPanel scenario={scenarioWith({ verdict: string('verdict', value, 'unknown') })} />);

        const button = screen.getByRole('button', { name: `verdict, Unknown, ${value}` });
        const preview = within(button).getByText(value);
        expect(preview.closest('[aria-hidden="true"]')).toBeInTheDocument();

        fireEvent.click(button);
        expect(screen.getAllByText(value)).toHaveLength(2);
    });
});

describe('MetricPanel — neutral (none / unknown) rating', () => {
    it('renders a neutral boolean value instead of a rating glyph', () => {
        render(<MetricPanel scenario={scenarioWith({ enabled: boolean('enabled', true, 'unknown', false) })} />);

        const button = screen.getByRole('button');
        expect(within(button).getByText('Yes')).toBeInTheDocument();
        expect(within(button).queryByText('?')).not.toBeInTheDocument();
        expect(within(button).queryByText('✓')).not.toBeInTheDocument();
        expect(within(button).queryByText('✗')).not.toBeInTheDocument();
    });

    it('renders a neutral "?" track for a missing numeric value', () => {
        render(<MetricPanel scenario={scenarioWith({ mystery: numeric('mystery', undefined, 'unknown', false) })} />);

        expect(screen.getByText('?')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button'));
        expect(screen.getByText('Unknown')).toBeInTheDocument();
    });

    it('renders a terminal "?" track for a rated no-value metric', () => {
        render(<MetricPanel scenario={scenarioWith({ creativity: none('creativity', 'good') })} />);

        expect(screen.getByText('?')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button'));
        expect(screen.getByText('Good')).toBeInTheDocument();
    });
});

describe('MetricPanel — metricFailed product rule', () => {
    it('flips status to failed when a diagnostic severity is "error" even though interpretation.failed === false', () => {
        const metric = numeric('accuracy', 4.5, 'good', false, [
            { severity: 'error', message: 'Missing required crystal-structure detail.' },
        ]);
        render(<MetricPanel scenario={scenarioWith({ accuracy: metric })} />);

        const button = screen.getByRole('button', { name: /accuracy, failed/i });
        expect(button).toBeInTheDocument();

        fireEvent.click(button);
        expect(screen.getByText('Why this failed?')).toBeInTheDocument();
        expect(screen.queryByText('Why this score?')).not.toBeInTheDocument();
        expect(screen.getByText('Diagnostics')).toBeInTheDocument();
        expect(screen.getByText('Error')).toBeInTheDocument();
    });

    it('does not flip a passing metric that has no error diagnostics', () => {
        render(<MetricPanel scenario={scenarioWith({ clarity: numeric('clarity', 4, 'good', false) })} />);

        expect(screen.queryByRole('button', { name: /failed/i })).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /clarity/i }));
        expect(screen.getByText('Why this score?')).toBeInTheDocument();
    });

    it('keeps failed good and unknown booleans danger-coloured with a cross glyph', () => {
        render(
            <MetricPanel
                scenario={scenarioWith({
                    goodFailed: boolean('goodFailed', true, 'good', true),
                    unknownFailed: boolean('unknownFailed', true, 'unknown', true),
                })}
            />,
        );

        for (const name of ['goodFailed', 'unknownFailed']) {
            const button = screen.getByRole('button', { name: new RegExp(`${name}, failed`) });
            expect(within(button).getByText('✗')).toBeInTheDocument();
            expect(button.querySelector('span span')?.getAttribute('style')).toContain('negative-solid');
        }
    });
});

describe('MetricPanel — evaluation context', () => {
    it('renders ordered context groups and content only inside the owning expanded metric', () => {
        const groundedness = numeric('groundedness', 4, 'good', false);
        groundedness.context = {
            groundTruth: {
                name: 'Ground Truth (Completeness)',
                contents: [
                    { $type: 'text', text: '**Expected:** Mention both causes.' },
                    { $type: 'text', text: 'Second expected detail.' },
                ],
            },
            reference: {
                name: 'Reference notes',
                contents: [{ $type: 'text', text: 'Supporting source.' }],
            },
            empty: { name: 'Empty context', contents: [] },
        };

        render(
            <ReportContextProvider dataset={multiGroupDataset} scoreSummary={createScoreSummary(multiGroupDataset)}>
                <MetricPanel
                    scenario={scenarioWith({
                        groundedness,
                        relevance: numeric('relevance', 3, 'average', false),
                    })}
                />
            </ReportContextProvider>,
        );

        expect(screen.queryByText('Evaluation context')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /groundedness/i }));
        const detail = screen.getByRole('region', { name: 'groundedness detail' });
        const expected = within(detail).getByText('Expected:');
        const secondDetail = within(detail).getByText('Second expected detail.');
        const groundTruth = within(detail).getByText('Ground Truth (Completeness)');
        const reference = within(detail).getByText('Reference notes');

        expect(within(detail).getByText('Evaluation context')).toBeInTheDocument();
        expect(expected.tagName).toBe('STRONG');
        expect(groundTruth.compareDocumentPosition(reference) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(expected.compareDocumentPosition(secondDetail) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(expected.parentElement).not.toBe(secondDetail.parentElement);
        expect(within(detail).queryByText('Empty context')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /groundedness/i }));
        expect(screen.queryByText('Evaluation context')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /relevance/i }));
        expect(screen.queryByText('Evaluation context')).not.toBeInTheDocument();
    });
});

describe('MetricPanel — rating vocabulary and semantic tokens', () => {
    it('maps EvaluationRating values to their display words', () => {
        render(
            <MetricPanel
                scenario={scenarioWith({
                    excM: numeric('excM', 5, 'exceptional', false),
                    goodM: numeric('goodM', 4, 'good', false),
                    avgM: numeric('avgM', 3, 'average', false),
                    poorM: numeric('poorM', 2, 'poor', false),
                    weakM: numeric('weakM', 1, 'unacceptable', false),
                    incM: none('incM', 'inconclusive'),
                })}
            />,
        );

        // ratingWord() mapping is exposed on each row's accessible name.
        expect(screen.getByRole('button', { name: /excM, Exceptional/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /goodM, Good/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /avgM, Fair/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /poorM, Poor/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /weakM, Weak/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /incM, Inconclusive/ })).toBeInTheDocument();
    });

    it('maps rating buckets directly to their solid and text tokens', () => {
        render(
            <MetricPanel
                scenario={scenarioWith({
                    goodM: numeric('goodM', 4, 'good', false),
                    avgM: numeric('avgM', 3, 'average', false),
                    weakM: numeric('weakM', 1, 'unacceptable', false),
                    unkM: numeric('unkM', undefined, 'unknown', false),
                    failedM: numeric('failedM', 4, 'good', true),
                })}
            />,
        );

        const dotStyleOf = (name: string): string => {
            const btn = screen.getByRole('button', { name: new RegExp(name) });
            return btn.querySelector('span span')?.getAttribute('style') ?? '';
        };

        expect(dotStyleOf('goodM')).toContain('positive-solid');
        expect(dotStyleOf('avgM')).toContain('caution-solid');
        expect(dotStyleOf('weakM')).toContain('negative-solid');
        expect(dotStyleOf('unkM')).toContain('neutral-solid');
        expect(dotStyleOf('failedM')).toContain('negative-solid');

        for (const [name, word, token] of [
            ['goodM', 'Good', 'positive-text'],
            ['avgM', 'Fair', 'caution-text'],
            ['weakM', 'Weak', 'negative-text'],
            ['unkM', 'Unknown', 'neutral-text'],
        ]) {
            fireEvent.click(screen.getByRole('button', { name: new RegExp(name) }));
            expect(screen.getByText(word).getAttribute('style')).toContain(token);
        }
    });
});

describe('MetricPanel — empty state', () => {
    it('shows a placeholder when the scenario has no metrics', () => {
        render(<MetricPanel scenario={scenarioWith({})} />);
        expect(screen.getByText('No metrics for this case.')).toBeInTheDocument();
    });
});
