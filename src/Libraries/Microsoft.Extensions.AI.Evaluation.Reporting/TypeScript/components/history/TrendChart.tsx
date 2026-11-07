// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useEffect, useRef, useState } from 'react';
import { makeStyles } from '@fluentui/react-components';
import { useReportContext } from '../core/ReportContext';
import { formatNumber } from '../core/metricModel';
import type { AxisDomain } from './axisDomain';

export type BandPoint = {
    mean: number;
    median: number;
    lo: number;
    hi: number;
    n: number;
};

const W_FALLBACK = 760;
const H = 260;
const PAD_L = 34;
const PAD_R = 12;
const PAD_T = 12;
const PAD_B = 22;

export type TrendChartProps = {
    points: (BandPoint | undefined)[];
    domain: AxisDomain;
    ariaLabel: string;
    showLegend?: boolean;
};

const useLocalStyles = makeStyles({
    legend: {
        display: 'flex',
        gap: 'var(--spacing-xl)',
        alignItems: 'center',
        flexWrap: 'wrap',
        marginTop: 'var(--spacing-m)',
        paddingLeft: '34px',
    },
    legendItem: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
    },
    legendLabel: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
    },
    meanSwatch: {
        position: 'relative',
        display: 'inline-block',
        width: '22px',
        height: '2px',
        background: 'var(--chart-primary)',
        borderRadius: '2px',
    },
    medianSwatch: {
        position: 'relative',
        display: 'inline-block',
        width: '22px',
        height: '5.5px',
    },
    medianDash: {
        position: 'absolute',
        left: 0,
        right: 0,
        top: '50%',
        height: 0,
        borderTop: '1.5px dashed var(--chart-median-series)',
        transform: 'translateY(-50%)',
    },
    spreadSwatch: {
        display: 'inline-block',
        width: '22px',
        height: '13px',
        borderRadius: '3px',
        background: 'color-mix(in srgb, var(--chart-primary) 13%, transparent)',
        border: '1px solid color-mix(in srgb, var(--chart-primary) 32%, transparent)',
        boxSizing: 'border-box',
    },
    donutMean: {
        position: 'absolute',
        left: '50%',
        top: '50%',
        width: '8px',
        height: '8px',
        borderRadius: '50%',
        background: 'var(--chart-marker-fill)',
        border: '1.5px solid var(--chart-primary)',
        transform: 'translate(-50%,-50%)',
        boxSizing: 'border-box',
    },
    donutMedian: {
        position: 'absolute',
        left: '50%',
        top: '50%',
        width: '8px',
        height: '8px',
        borderRadius: '50%',
        background: 'var(--chart-marker-fill)',
        border: '1.5px solid var(--chart-median-series)',
        transform: 'translate(-50%,-50%)',
        boxSizing: 'border-box',
    },
});

export const TrendChart = ({ points, domain, ariaLabel, showLegend = true }: TrendChartProps) => {
    const { darkMode } = useReportContext();
    const local = useLocalStyles();

    const wrapRef = useRef<HTMLDivElement | null>(null);
    const [measuredW, setMeasuredW] = useState<number | undefined>(undefined);

    useEffect(() => {
        const el = wrapRef.current;
        if (!el || typeof ResizeObserver === 'undefined') return;
        const ro = new ResizeObserver((entries) => {
            const w = entries[0]?.contentRect.width;
            if (w && w > 0) setMeasuredW(w);
        });
        ro.observe(el);
        return () => ro.disconnect();
    }, []);

    if (points.length === 0) return null;

    const W = measuredW && measuredW > 0 ? measuredW : W_FALLBACK;

    const color = 'var(--chart-primary)';
    const medColor = 'var(--chart-median-series)';
    const rangeColor = 'var(--chart-primary)';

    const dom = domain;
    const n = points.length;
    const xOf = (i: number) =>
        PAD_L + (n <= 1 ? (W - PAD_L - PAD_R) / 2 : (i * (W - PAD_L - PAD_R)) / (n - 1));
    const yOf = (v: number) => {
        const y = PAD_T + (1 - (v - dom.min) / (dom.max - dom.min)) * (H - PAD_T - PAD_B);
        return Math.max(PAD_T, Math.min(H - PAD_B, y));
    };

    const gridEls: React.ReactNode[] = [];
    for (let g = 0; g <= dom.ticks; g++) {
        const v = dom.min + ((dom.max - dom.min) * g) / dom.ticks;
        const y = yOf(v);
        gridEls.push(
            <line key={`g${g}`} x1={PAD_L} y1={y} x2={W - PAD_R} y2={y} strokeWidth={1} style={{ stroke: 'var(--chart-gridline)' }} />,
        );
        gridEls.push(
            <text key={`gl${g}`} x={PAD_L - 6} y={y + 3} textAnchor="end" fontSize={10} style={{ fill: 'var(--chart-tick-text)' }}>
                {dom.fmt(v)}
            </text>,
        );
    }

    const valid = points.map((p, i) => ({ p, i })).filter((o): o is { p: BandPoint; i: number } => !!o.p);

    const bandEls: React.ReactNode[] = [];
    if (valid.length > 1) {
        const up = valid.map((o) => `${xOf(o.i)},${yOf(o.p.hi)}`);
        const dn = valid.slice().reverse().map((o) => `${xOf(o.i)},${yOf(o.p.lo)}`);
        bandEls.push(
            <polygon
                key="band"
                points={up.concat(dn).join(' ')}
                style={{ fill: rangeColor, fillOpacity: darkMode ? 0.2 : 0.13, stroke: rangeColor, strokeOpacity: 0.32, strokeWidth: 1 }}
            />,
        );
    } else if (valid.length === 1) {
        const o = valid[0];
        bandEls.push(
            <line key="band1" x1={xOf(o.i)} y1={yOf(o.p.lo)} x2={xOf(o.i)} y2={yOf(o.p.hi)} strokeWidth={6} strokeLinecap="round" style={{ stroke: rangeColor, strokeOpacity: 0.3 }} />,
        );
    }

    const medEls: React.ReactNode[] = [];
    if (valid.length > 1) {
        medEls.push(
            <polyline key="median" points={valid.map((o) => `${xOf(o.i)},${yOf(o.p.median)}`).join(' ')} fill="none" strokeWidth={1.5} strokeDasharray="5 3" strokeLinejoin="round" strokeLinecap="round" style={{ stroke: medColor }} />,
        );
    } else if (valid.length === 1) {
        const o = valid[0];
        medEls.push(
            <line key="median1" x1={xOf(o.i) - 7} y1={yOf(o.p.median)} x2={xOf(o.i) + 7} y2={yOf(o.p.median)} strokeWidth={1.5} strokeDasharray="5 3" strokeLinecap="round" style={{ stroke: medColor }} />,
        );
    }
    for (const o of valid) {
        medEls.push(
            <circle key={`md${o.i}`} cx={xOf(o.i)} cy={yOf(o.p.median)} r={3.25} strokeWidth={1.5} style={{ fill: 'var(--chart-marker-fill)', stroke: medColor }} />,
        );
    }

    const meanEls: React.ReactNode[] = [];
    if (valid.length > 1) {
        meanEls.push(
            <polyline key="mean" points={valid.map((o) => `${xOf(o.i)},${yOf(o.p.mean)}`).join(' ')} fill="none" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" style={{ stroke: color }} />,
        );
    }
    for (const o of valid) {
        meanEls.push(
            <circle key={`d${o.i}`} cx={xOf(o.i)} cy={yOf(o.p.mean)} r={3.25} strokeWidth={1.5} style={{ fill: 'var(--chart-marker-fill)', stroke: color }}>
                <title>{`R${o.i + 1}: mean ${formatNumber(o.p.mean)}`}</title>
            </circle>,
        );
    }

    const xLabels = points.map((_p, i) => (
        <text key={`xl${i}`} x={xOf(i)} y={H - 6} textAnchor="middle" fontSize={10} style={{ fill: 'var(--chart-tick-text)' }}>
            {`R${i + 1}`}
        </text>
    ));

    return (
        <div ref={wrapRef} style={{ display: 'flex', flexDirection: 'column' }}>
            <svg
                role="img"
                aria-label={ariaLabel}
                width={W}
                height={H}
                viewBox={`0 0 ${W} ${H}`}
                style={{ width: '100%', height: 'auto', display: 'block' }}
            >
                {gridEls}
                {bandEls}
                {medEls}
                {meanEls}
                {xLabels}
            </svg>

            {showLegend && (
                <div className={local.legend}>
                    <span className={local.legendItem}>
                        <span className={local.meanSwatch}>
                            <span className={local.donutMean} />
                        </span>
                        <span className={local.legendLabel}>Mean per run</span>
                    </span>
                    <span className={local.legendItem}>
                        <span className={local.medianSwatch}>
                            <span className={local.medianDash} />
                            <span className={local.donutMedian} />
                        </span>
                        <span className={local.legendLabel}>Median per run</span>
                    </span>
                    <span className={local.legendItem}>
                        <span className={local.spreadSwatch} />
                        <span className={local.legendLabel}>Min–max spread across cases</span>
                    </span>
                </div>
            )}
        </div>
    );
};
