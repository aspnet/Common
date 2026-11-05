// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { posOn, dumbbellStyles } from '../components/history/dumbbellGeometry';
import { axisDomain } from '../components/history/axisDomain';

// Pure geometry used by both HistoryView and ComparisonView dumbbell rows.
// These are the exact numeric/string contracts the rendered rows depend on.

describe('axisDomain — presentation-only chart/dumbbell framing', () => {
    it.each<[string, number[], { min: number; max: number; ticks: number }]>([
        ['frames a genuine fraction-shaped series to the unit interval', [0.2, 0.5, 0.8], { min: 0, max: 1, ticks: 5 }],
        ['anchors a conforming 1..5 series to the stable frame', [1, 2, 3, 4, 5], { min: 1, max: 5, ticks: 4 }],
        ['expands the anchored frame for an outlier above 5', [2, 6, 4], { min: 1, max: 6, ticks: 5 }],
        ['expands the anchored frame for an outlier below 1', [-2, 3], { min: -2, max: 5, ticks: 7 }],
        ['falls back to the unit frame without finite values', [], { min: 0, max: 1, ticks: 5 }],
    ])('%s', (_name, values, expected) => {
        expect(axisDomain(values)).toMatchObject(expected);
    });

    it('bounds the gridline count for wide numeric ranges instead of one line per integer', () => {
        const pct = axisDomain([0, 40, 100]);
        expect(pct.ticks).toBeLessThanOrEqual(12);
        expect(pct.min).toBeLessThanOrEqual(0);
        expect(pct.max).toBeGreaterThanOrEqual(100);

        const tokens = axisDomain([1200, 3000, 4800]);
        expect(tokens.ticks).toBeLessThanOrEqual(12);
        expect(tokens.min).toBeLessThanOrEqual(1200);
        expect(tokens.max).toBeGreaterThanOrEqual(4800);
    });

    it('routes fmt through the pinned formatNumber policy (settles tick decimals, no denominator)', () => {
        const dom = axisDomain([0.2, 0.5, 0.8]);
        expect(dom.fmt(0.2)).toBe('0.2');
        expect(dom.fmt(1)).toBe('1');
        expect(dom.fmt(0.1 + 0.2)).toBe('0.3');
    });
});

describe('posOn — value → 0..100 with clamping', () => {
    it.each<[number, number, number, number]>([
        [0.5, 0, 1, 50],
        [0, 0, 1, 0],
        [1, 0, 1, 100],
        // below min clamps to 0
        [-0.5, 0, 1, 0],
        // above max clamps to 100
        [2, 0, 1, 100],
        [3, 1, 5, 50],
        [1, 1, 5, 0],
        [5, 1, 5, 100],
        // degenerate domain (max not > min) falls back to the 50% midpoint
        [5, 5, 5, 50],
    ])('posOn(%d, %d, %d) === %d', (v, min, max, expected) => {
        expect(posOn(v, min, max)).toBe(expected);
    });
});

describe('dumbbellStyles — connector/dot geometry', () => {
    const UNCHANGED_SOLID = 'var(--neutral-solid)';

    it('renders the full connector + both dots when there is a prev, a delta, and a gap > connEpsilon', () => {
        const db = dumbbellStyles(20, 80, true);
        expect(db).toMatchObject({
            sk: 'unchanged',
            connector: { left: '20%', width: '60%' },
            dotB: { left: '20%' },
            dotA: { left: '80%' },
        });
        expect(db.connector.display).toBeUndefined();
        expect(db.dotB.display).toBeUndefined();
        expect(db.dotA.display).toBeUndefined();
    });

    it('hides the connector AND dotB when prevPos is null (first point / no baseline)', () => {
        const db = dumbbellStyles(null, 60, true);
        expect(db.sk).toBe('unchanged');
        expect(db.connector).toStrictEqual({ display: 'none' });
        expect(db.dotB).toStrictEqual({ display: 'none' });
        // dotA still renders at the current position with the unchanged color.
        expect(db.dotA.display).toBeUndefined();
        expect(db.dotA.left).toBe('60%');
        expect(db.dotA.background).toBe(UNCHANGED_SOLID);
    });

    it('hides the connector when hasDelta is false, but keeps dotB visible (prev exists)', () => {
        const db = dumbbellStyles(20, 80, false);
        expect(db.sk).toBe('unchanged');
        expect(db.connector).toStrictEqual({ display: 'none' });
        expect(db.dotB.display).toBeUndefined();
        expect(db.dotB.left).toBe('20%');
        expect(db.dotA.left).toBe('80%');
        expect(db.dotA.background).toBe(UNCHANGED_SOLID);
    });

    it('hides the connector when the gap is within connEpsilon (dots still shown)', () => {
        // gap 0.005 <= default epsilon 0.01 → connector suppressed, both dots kept.
        const db = dumbbellStyles(50, 50.005, true);
        expect(db.connector).toStrictEqual({ display: 'none' });
        expect(db.dotB.left).toBe('50%');
        expect(db.dotA.left).toBe('50.005%');
    });

    it('honours a connEpsilon of 0 (ComparisonView passes 0): equal positions hide, any gap shows', () => {
        expect(dumbbellStyles(50, 50, true, 'unchanged', 0).connector).toStrictEqual({ display: 'none' });
        const shown = dumbbellStyles(50, 51, true, 'unchanged', 0).connector;
        expect(shown.display).toBeUndefined();
        expect(shown.left).toBe('50%');
        expect(shown.width).toBe('1%');
    });

    it('clamps out-of-range positions into 0..100 before emitting left offsets', () => {
        const db = dumbbellStyles(-10, 150, true);
        // prv clamps to 0, cur clamps to 100
        expect(db.dotB.left).toBe('0%');
        expect(db.dotA.left).toBe('100%');
        expect(db.connector.left).toBe('0%');
        expect(db.connector.width).toBe('100%');
    });
});
