// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { ScoreNode, getScoreHistory, type ScoreSummary } from './Summary';
import { isLeafFailed } from './scoring';
import {
    inferBetterDirections,
    judgeValueDelta,
    ratingGoodness,
    type DeltaJudgment,
} from './metricDirection';

export { isLeafFailed };

type RatingBucket = 'good' | 'fair' | 'weak' | 'unknown';

type BucketCounts = {
    good: number;
    fair: number;
    weak: number;
    unknown: number;
};

export const ratingBucket = (rating: EvaluationRating | undefined): RatingBucket => {
    switch (rating) {
        case 'exceptional':
        case 'good':
            return 'good';
        case 'average':
            return 'fair';
        case 'poor':
        case 'unacceptable':
            return 'weak';
        case 'unknown':
        case 'inconclusive':
        default:
            return 'unknown';
    }
};

export const bucketMetrics = (scenarios: ScenarioRunResult[]): BucketCounts => {
    const counts: BucketCounts = { good: 0, fair: 0, weak: 0, unknown: 0 };
    for (const scenario of scenarios) {
        for (const metric of Object.values(scenario.evaluationResult?.metrics ?? {})) {
            counts[ratingBucket(metric?.interpretation?.rating)] += 1;
        }
    }
    return counts;
};

export type ScenarioGroupPassRate = {
    group: string;
    passing: number;
    total: number;
    passRate: number;
    deltaRun?: number;
};

const groupKey = (scenario: ScenarioRunResult): string => scenario.scenarioName.split('.')[0];

type CreationTime = {
    raw: string;
    instant?: number;
};

const creationTime = (raw: string): CreationTime => {
    const instant = Date.parse(raw);
    return Number.isFinite(instant) ? { raw, instant } : { raw };
};

const compareCreationTimes = (a: CreationTime, b: CreationTime): number => {
    if (a.instant !== undefined && b.instant !== undefined) {
        return a.instant - b.instant;
    }
    if (a.instant !== undefined) return -1;
    if (b.instant !== undefined) return 1;
    if (a.raw < b.raw) return -1;
    if (a.raw > b.raw) return 1;
    return 0;
};

export const chronologicalExecutions = (dataset: Dataset): string[] => {
    const results = dataset.scenarioRunResults ?? [];
    const minTime = new Map<string, CreationTime>();
    const insertionIndex = new Map<string, number>();
    for (const r of results) {
        if (!insertionIndex.has(r.executionName)) {
            insertionIndex.set(r.executionName, insertionIndex.size);
        }
        const candidate = creationTime(r.creationTime);
        const existing = minTime.get(r.executionName);
        if (existing === undefined || compareCreationTimes(candidate, existing) < 0) {
            minTime.set(r.executionName, candidate);
        }
    }
    return [...insertionIndex.keys()].sort((a, b) => {
        const ta = minTime.get(a);
        const tb = minTime.get(b);
        if (ta && tb) {
            const timeOrder = compareCreationTimes(ta, tb);
            if (timeOrder !== 0) return timeOrder;
        } else if (ta) {
            return -1;
        } else if (tb) {
            return 1;
        }
        return (insertionIndex.get(a) ?? 0) - (insertionIndex.get(b) ?? 0);
    });
};

const groupTallies = (results: ScenarioRunResult[]): Map<string, { passing: number; total: number }> => {
    const tallies = new Map<string, { passing: number; total: number }>();
    for (const scenario of results) {
        const key = groupKey(scenario);
        const entry = tallies.get(key) ?? { passing: 0, total: 0 };
        entry.total += 1;
        if (!isLeafFailed(scenario)) {
            entry.passing += 1;
        }
        tallies.set(key, entry);
    }
    return tallies;
};

export const passRateByScenarioGroup = (
    dataset: Dataset,
    execName?: string,
): ScenarioGroupPassRate[] => {
    const results = dataset.scenarioRunResults ?? [];
    const executions = chronologicalExecutions(dataset);
    const primaryExec = executions[0];

    const activeExec = execName ?? primaryExec;
    const activeIdx = executions.indexOf(activeExec);

    const previousExec = activeIdx > 0 ? executions[activeIdx - 1] : undefined;

    const activeResults = results.filter((r) => r.executionName === activeExec);
    const activeTallies = groupTallies(activeResults);

    const previousTallies = previousExec
        ? groupTallies(results.filter((r) => r.executionName === previousExec))
        : undefined;

    const rows: ScenarioGroupPassRate[] = [];
    for (const [group, { passing, total }] of activeTallies.entries()) {
        const passRate = total > 0 ? passing / total : 0;

        let deltaRun: number | undefined;
        if (previousTallies) {
            const prev = previousTallies.get(group);
            if (prev && prev.total > 0) {
                deltaRun = passRate - prev.passing / prev.total;
            }
        }

        rows.push({ group, passing, total, passRate, deltaRun });
    }
    return rows;
};

export const scenariosForExecution = (dataset: Dataset, execName?: string): ScenarioRunResult[] => {
    const results = dataset.scenarioRunResults ?? [];
    const activeExec = execName ?? chronologicalExecutions(dataset)[0];
    return results.filter((r) => r.executionName === activeExec);
};

export type KpiCounts = {
    passing: number;
    failing: number;
    total: number;
    passRate: number;
};

export const kpiCountsFromNode = (node: ScoreNode): KpiCounts => {
    const passing = node.numPassingIterations;
    const failing = node.numFailingIterations;
    const total = passing + failing;
    return { passing, failing, total, passRate: total > 0 ? passing / total : 0 };
};

type MetricHistoryPoint = {
    executionName: string;
    value: number;
};

type MetricHistorySeries = {
    metricName: string;
    points: MetricHistoryPoint[];
};

const isNumericMetric = (metric: BaseEvaluationMetric): metric is NumericMetric =>
    metric.$type === 'numeric' && typeof (metric as NumericMetric).value === 'number';

export const metricHistoryForScenario = (
    scoreSummary: ScoreSummary,
    scenario: ScenarioRunResult,
): MetricHistorySeries[] => {
    const history = getScoreHistory(scoreSummary, scenario);
    if (history.size < 2) {
        return [];
    }

    const series = new Map<string, MetricHistoryPoint[]>();
    for (const [executionName, run] of history.entries()) {
        for (const metric of Object.values(run.evaluationResult?.metrics ?? {})) {
            if (isNumericMetric(metric)) {
                const points = series.get(metric.name) ?? [];
                points.push({ executionName, value: metric.value! });
                series.set(metric.name, points);
            }
        }
    }

    return [...series.entries()].map(([metricName, points]) => ({ metricName, points }));
};

export type MoverRow = {
    scenarioName: string;
    metricName: string;
    value: number;
    delta: number;
    status: DeltaJudgment;
};

type MoverAgg = {
    scenarioName: string;
    metricName: string;
    sum: number;
    n: number;
    goodnessSum: number;
    goodnessN: number;
};

const pairKey = (scenarioName: string, metricName: string): string =>
    JSON.stringify([scenarioName, metricName]);

const meanByScenarioMetric = (results: ScenarioRunResult[]): Map<string, MoverAgg> => {
    const agg = new Map<string, MoverAgg>();
    for (const r of results) {
        for (const metric of Object.values(r.evaluationResult?.metrics ?? {})) {
            if (!isNumericMetric(metric)) continue;
            const key = pairKey(r.scenarioName, metric.name);
            const entry =
                agg.get(key) ??
                { scenarioName: r.scenarioName, metricName: metric.name, sum: 0, n: 0, goodnessSum: 0, goodnessN: 0 };
            entry.sum += metric.value!;
            entry.n += 1;
            const goodness = ratingGoodness(metric.interpretation?.rating);
            if (goodness !== undefined) {
                entry.goodnessSum += goodness;
                entry.goodnessN += 1;
            }
            agg.set(key, entry);
        }
    }
    return agg;
};

const goodnessMean = (agg: MoverAgg): number | undefined =>
    agg.goodnessN > 0 ? agg.goodnessSum / agg.goodnessN : undefined;

const metricValueSpans = (results: ScenarioRunResult[]): Map<string, number> => {
    const lo = new Map<string, number>();
    const hi = new Map<string, number>();
    for (const r of results) {
        for (const metric of Object.values(r.evaluationResult?.metrics ?? {})) {
            if (!isNumericMetric(metric)) continue;
            const value = metric.value!;
            const l = lo.get(metric.name);
            const h = hi.get(metric.name);
            if (l === undefined || value < l) lo.set(metric.name, value);
            if (h === undefined || value > h) hi.set(metric.name, value);
        }
    }
    const spans = new Map<string, number>();
    for (const [name, l] of lo) {
        const span = hi.get(name)! - l;
        spans.set(name, span > 0 ? span : 1);
    }
    return spans;
};

export const moversBetween = (
    results: ScenarioRunResult[],
    selectedExec: string | undefined,
    prevExec: string | undefined,
    limit = 5,
): MoverRow[] => {
    if (!selectedExec || !prevExec) return [];

    const directions = inferBetterDirections(results);
    const spans = metricValueSpans(results);
    const selectedAgg = meanByScenarioMetric(results.filter((r) => r.executionName === selectedExec));
    const prevAgg = meanByScenarioMetric(results.filter((r) => r.executionName === prevExec));

    const rows: MoverRow[] = [];
    for (const [key, sel] of selectedAgg.entries()) {
        const prev = prevAgg.get(key);
        if (!prev || prev.n === 0 || sel.n === 0) continue;
        const selMean = sel.sum / sel.n;
        const prevMean = prev.sum / prev.n;
        const delta = selMean - prevMean;
        const selGoodness = goodnessMean(sel);
        const prevGoodness = goodnessMean(prev);
        const goodnessDelta =
            selGoodness !== undefined && prevGoodness !== undefined ? selGoodness - prevGoodness : undefined;
        rows.push({
            scenarioName: sel.scenarioName,
            metricName: sel.metricName,
            value: selMean,
            delta,
            status: judgeValueDelta(directions.get(sel.metricName) ?? 'none', delta, goodnessDelta),
        });
    }

    const magnitude = (row: MoverRow): number => Math.abs(row.delta) / (spans.get(row.metricName) ?? 1);
    rows.sort((a, b) => magnitude(b) - magnitude(a));
    return Number.isFinite(limit) ? rows.slice(0, limit) : rows;
};
