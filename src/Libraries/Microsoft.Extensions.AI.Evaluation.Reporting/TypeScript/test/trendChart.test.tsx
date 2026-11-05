// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider } from '../components';
import { TrendChart, type BandPoint } from '../components/history/TrendChart';
import { axisDomain } from '../components/history/axisDomain';
import { singleExecutionDataset } from './fixtures/richDataset';

const renderChart = (ui: React.ReactElement) => {
    const scoreSummary = createScoreSummary(singleExecutionDataset);
    return render(
        <ReportContextProvider dataset={singleExecutionDataset} scoreSummary={scoreSummary}>
            {ui}
        </ReportContextProvider>,
    );
};

const bp = (mean: number, extra: Partial<BandPoint> = {}): BandPoint => ({
    mean,
    median: mean,
    lo: mean,
    hi: mean,
    n: 1,
    ...extra,
});

const meanDots = (c: HTMLElement): SVGCircleElement[] =>
    [...c.querySelectorAll('circle')].filter((el) => el.querySelector('title')) as SVGCircleElement[];

const plotBounds = (container: HTMLElement) => {
    const gridLines = [...container.querySelectorAll('svg > line')]
        .filter((line) => line.getAttribute('x1') !== line.getAttribute('x2'));
    const x = gridLines.flatMap((line) => [Number(line.getAttribute('x1')), Number(line.getAttribute('x2'))]);
    const y = gridLines.map((line) => Number(line.getAttribute('y1')));
    return { left: Math.min(...x), right: Math.max(...x), top: Math.min(...y), bottom: Math.max(...y) };
};

const scoreDomain = axisDomain([1, 5]);

describe('TrendChart — point geometry', () => {
    it('orders two points left-to-right and low-to-high within the chart bounds', () => {
        const { container } = renderChart(
            <TrendChart points={[bp(1), bp(5)]} domain={scoreDomain} ariaLabel="Accuracy trend" />,
        );
        const dots = meanDots(container);
        const { left, right, top, bottom } = plotBounds(container);
        expect(dots).toHaveLength(2);
        const [firstX, secondX] = dots.map((dot) => Number(dot.getAttribute('cx')));
        const [firstY, secondY] = dots.map((dot) => Number(dot.getAttribute('cy')));
        expect(firstX).toBeGreaterThanOrEqual(left);
        expect(secondX).toBeLessThanOrEqual(right);
        expect(firstX).toBeLessThan(secondX);
        expect(firstY).toBeLessThanOrEqual(bottom);
        expect(secondY).toBeGreaterThanOrEqual(top);
        expect(firstY).toBeGreaterThan(secondY);
    });

    it('centres a single point horizontally', () => {
        const { container } = renderChart(
            <TrendChart points={[bp(3, { lo: 2, hi: 4 })]} domain={scoreDomain} ariaLabel="Single" />,
        );
        const dots = meanDots(container);
        const { left, right, top, bottom } = plotBounds(container);
        expect(dots).toHaveLength(1);
        expect(Number(dots[0].getAttribute('cx'))).toBe((left + right) / 2);
        expect(Number(dots[0].getAttribute('cy'))).toBeGreaterThan(top);
        expect(Number(dots[0].getAttribute('cy'))).toBeLessThan(bottom);
    });

    it('draws the min–max spread as a filled band polygon for a multi-point series', () => {
        const { container } = renderChart(
            <TrendChart points={[bp(2, { lo: 1, hi: 3 }), bp(4, { lo: 3, hi: 5 })]} domain={scoreDomain} ariaLabel="Band" />,
        );
        expect(container.querySelector('polygon')).not.toBeNull();
    });
});

describe('TrendChart — accessibility + legend toggle', () => {
    it('exposes the SVG as role=img with the supplied aria-label', () => {
        renderChart(<TrendChart points={[bp(3)]} domain={scoreDomain} ariaLabel="Latency trend" />);
        expect(screen.getByRole('img', { name: 'Latency trend' })).toBeInTheDocument();
    });

    it('renders nothing when there are no points', () => {
        const { container } = renderChart(<TrendChart points={[]} domain={scoreDomain} ariaLabel="Empty" />);
        expect(container.querySelector('svg')).toBeNull();
    });

    it('shows the three-item legend by default', () => {
        renderChart(<TrendChart points={[bp(3)]} domain={scoreDomain} ariaLabel="Legend" />);
        expect(screen.getByText('Mean per run')).toBeInTheDocument();
        expect(screen.getByText('Median per run')).toBeInTheDocument();
        expect(screen.getByText(/spread across cases/)).toBeInTheDocument();
    });

    it('omits the legend when showLegend is false', () => {
        renderChart(<TrendChart points={[bp(3)]} domain={scoreDomain} ariaLabel="NoLegend" showLegend={false} />);
        expect(screen.queryByText('Mean per run')).not.toBeInTheDocument();
        expect(screen.queryByText('Median per run')).not.toBeInTheDocument();
    });
});

describe('TrendChart — axisDomain framing (anchored domain, no squashing, no clipping)', () => {
    it('keeps all points within the plot rect for an out-of-range series, and the axis max grows to fit it', () => {
        const values = [2, 6, 4];
        const domain = axisDomain(values);
        expect(domain.max).toBeGreaterThanOrEqual(6);

        const { container } = renderChart(
            <TrendChart points={values.map((v) => bp(v))} domain={domain} ariaLabel="Outlier" />,
        );
        const dots = meanDots(container);
        const { top, bottom } = plotBounds(container);
        expect(dots).toHaveLength(3);
        for (const dot of dots) {
            const cy = Number(dot.getAttribute('cy'));
            expect(cy).toBeGreaterThanOrEqual(top);
            expect(cy).toBeLessThanOrEqual(bottom);
        }
    });

    it('frames a genuine [0,1] fraction series to the unit interval without squashing it to the plot floor', () => {
        const values = [0.2, 0.5, 0.8];
        const domain = axisDomain(values);
        expect(domain.min).toBe(0);
        expect(domain.max).toBe(1);

        const { container } = renderChart(
            <TrendChart points={values.map((v) => bp(v))} domain={domain} ariaLabel="Fraction" />,
        );
        const ys = meanDots(container).map((d) => Number(d.getAttribute('cy')));
        const span = Math.max(...ys) - Math.min(...ys);
        expect(span).toBeGreaterThan(120);
    });

    it('clamps a point outside the supplied domain into the plot rect instead of rendering off-canvas', () => {
        const narrowDomain = { min: 1, max: 5, ticks: 4, fmt: (v: number) => String(v) };
        const { container } = renderChart(
            <TrendChart points={[bp(9)]} domain={narrowDomain} ariaLabel="Clamped" />,
        );
        const dots = meanDots(container);
        const { top, bottom } = plotBounds(container);
        expect(dots).toHaveLength(1);
        const cy = Number(dots[0].getAttribute('cy'));
        expect(cy).toBeGreaterThanOrEqual(top);
        expect(cy).toBeLessThanOrEqual(bottom);
    });
});
