// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Whether a larger raw value is better, worse, or carries no learnable direction for a metric.
// Inferred only from the library's own `interpretation.rating` — never from a metric name, a
// fabricated scale, or mock metadata — so it degrades to 'none' (neutral) when the data gives
// no signal rather than guessing.
export type BetterDirection = 'higher' | 'lower' | 'none';

export type DeltaJudgment = 'improved' | 'regressed' | 'unchanged';

const RATING_GOODNESS: Partial<Record<EvaluationRating, number>> = {
    unacceptable: 1,
    poor: 2,
    average: 3,
    good: 4,
    exceptional: 5,
};

// 'unknown'/'inconclusive' carry no ordinal signal and are excluded from inference.
export const ratingGoodness = (rating: EvaluationRating | undefined): number | undefined =>
    rating === undefined ? undefined : RATING_GOODNESS[rating];

type DirAcc = { n: number; sumV: number; sumG: number; sumVG: number; goodness: Set<number> };

export const inferBetterDirections = (
    results: ScenarioRunResult[],
): Map<string, BetterDirection> => {
    const acc = new Map<string, DirAcc>();
    for (const r of results) {
        for (const metric of Object.values(r.evaluationResult?.metrics ?? {})) {
            if (metric?.$type !== 'numeric') continue;
            const value = (metric as NumericMetric).value;
            if (typeof value !== 'number') continue;
            const goodness = ratingGoodness(metric.interpretation?.rating);
            if (goodness === undefined) continue;
            let entry = acc.get(metric.name);
            if (!entry) {
                entry = { n: 0, sumV: 0, sumG: 0, sumVG: 0, goodness: new Set() };
                acc.set(metric.name, entry);
            }
            entry.n += 1;
            entry.sumV += value;
            entry.sumG += goodness;
            entry.sumVG += value * goodness;
            entry.goodness.add(goodness);
        }
    }

    const directions = new Map<string, BetterDirection>();
    for (const [name, e] of acc) {
        if (e.n < 3 || e.goodness.size < 2) {
            directions.set(name, 'none');
            continue;
        }
        const covariance = e.n * e.sumVG - e.sumV * e.sumG;
        directions.set(name, covariance > 0 ? 'higher' : covariance < 0 ? 'lower' : 'none');
    }
    return directions;
};

export const judgeValueDelta = (
    direction: BetterDirection,
    valueDelta: number,
    goodnessDelta?: number,
): DeltaJudgment => {
    if (direction !== 'none' && Math.abs(valueDelta) > 1e-9) {
        const good = direction === 'higher' ? valueDelta > 0 : valueDelta < 0;
        return good ? 'improved' : 'regressed';
    }
    if (goodnessDelta !== undefined && Math.abs(goodnessDelta) > 1e-9) {
        return goodnessDelta > 0 ? 'improved' : 'regressed';
    }
    return 'unchanged';
};

export const judgmentWord = (judgment: DeltaJudgment): 'improved' | 'regressed' | undefined =>
    judgment === 'unchanged' ? undefined : judgment;
