// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { formatNumber, formatValue, isDisplayedZero } from '../components/core/metricModel';

const numericMetric = (value?: number): NumericMetric =>
    ({
        $type: 'numeric',
        name: 'm',
        ...(value === undefined ? {} : { value }),
    }) as NumericMetric;

const booleanMetric = (value?: boolean): BooleanMetric =>
    ({
        $type: 'boolean',
        name: 'm',
        ...(value === undefined ? {} : { value }),
    }) as BooleanMetric;

const stringMetric = (value?: string): StringMetric =>
    ({
        $type: 'string',
        name: 'm',
        ...(value === undefined ? {} : { value }),
    }) as StringMetric;

const noneMetric = (): MetricWithNoValue =>
    ({
        $type: 'none',
        name: 'm',
    }) as MetricWithNoValue;

describe('formatNumber — pinned precision policy (round to <=3dp, strip trailing zeros)', () => {
    it('pins the worked examples from the plan', () => {
        expect(formatNumber(0.87)).toBe('0.87');
        expect(formatNumber(0.5)).toBe('0.5');
        expect(formatNumber(842)).toBe('842');
        expect(formatNumber(4)).toBe('4');
        expect(formatNumber(4.5)).toBe('4.5');
        expect(formatNumber(6)).toBe('6');
        expect(formatNumber(4.333333333)).toBe('4.333');
    });

    it('normalizes -0 to "0"', () => {
        expect(formatNumber(-0)).toBe('0');
    });
});

describe('isDisplayedZero — follows the pinned display precision', () => {
    it('only treats values that render as zero as unchanged', () => {
        expect(isDisplayedZero(0.0004)).toBe(true);
        expect(isDisplayedZero(-0.0004)).toBe(true);
        expect(isDisplayedZero(0.0005)).toBe(false);
        expect(isDisplayedZero(0.004)).toBe(false);
    });
});

describe('formatValue — classification by $type', () => {
    it.each([
        ['numeric values use formatNumber', [numericMetric(0.87), numericMetric(842)], ['0.87', '842']],
        ['an absent numeric value uses the em-dash placeholder', [numericMetric()], ['—']],
        ['boolean values render as Yes or No', [booleanMetric(true), booleanMetric(false)], ['Yes', 'No']],
        ['an absent boolean value stays absent', [booleanMetric()], [undefined]],
        ['string values pass through unchanged', [stringMetric('PASS')], ['PASS']],
        ['a metric with no value stays absent', [noneMetric()], [undefined]],
    ] as const)('%s', (_name, metrics, expected) => {
        expect(metrics.map(formatValue)).toEqual(expected);
    });
});
