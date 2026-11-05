// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

type ScoredMetric = NumericMetric | BooleanMetric | StringMetric | MetricWithNoValue;

export const formatNumber = (v: number): string => String(Number(v.toFixed(3)));

export const isDisplayedZero = (v: number): boolean => formatNumber(Math.abs(v)) === '0';

export const formatValue = (metric: ScoredMetric): string | undefined => {
    switch (metric.$type) {
        case 'numeric':
            return metric.value === undefined ? '—' : formatNumber(metric.value);
        case 'boolean':
            return metric.value === undefined ? undefined : metric.value ? 'Yes' : 'No';
        case 'string':
            return metric.value;
        default:
            return undefined;
    }
};
