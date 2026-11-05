// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { formatNumber } from '../core/metricModel';

export type AxisDomain = {
    min: number;
    max: number;
    ticks: number;
    fmt: (v: number) => string;
};

const MAX_UNIT_TICKS = 10;

const niceAxis = (lo: number, hi: number): AxisDomain => {
    if (hi <= lo) {
        const pad = Math.max(Math.abs(hi), 1) * 0.5;
        lo -= pad;
        hi += pad;
    }
    const rawStep = (hi - lo) / 5;
    const magnitude = 10 ** Math.floor(Math.log10(rawStep));
    const normalized = rawStep / magnitude;
    const step = (normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10) * magnitude;
    const min = Math.floor(lo / step) * step;
    const max = Math.ceil(hi / step) * step;
    return { min, max, ticks: Math.max(1, Math.round((max - min) / step)), fmt: formatNumber };
};

export const axisDomain = (values: readonly number[]): AxisDomain => {
    let dataLo = Infinity;
    let dataHi = -Infinity;
    for (const v of values) {
        if (!Number.isFinite(v)) continue;
        if (v < dataLo) dataLo = v;
        if (v > dataHi) dataHi = v;
    }
    if (!Number.isFinite(dataLo)) {
        dataLo = 0;
        dataHi = 1;
    }

    if (dataHi <= 1 && dataLo >= 0) {
        return { min: 0, max: 1, ticks: 5, fmt: formatNumber };
    }

    const min = Math.min(1, Math.floor(dataLo));
    const max = Math.max(5, Math.ceil(dataHi));
    if (max - min <= MAX_UNIT_TICKS) {
        return { min, max, ticks: Math.max(1, Math.round(max - min)), fmt: formatNumber };
    }

    return niceAxis(dataLo, dataHi);
};
