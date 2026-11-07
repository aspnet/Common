// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useId, useMemo } from 'react';
import { Card, ProgressBar, makeStyles, mergeClasses } from '@fluentui/react-components';
import { useReportContext } from '../core/ReportContext';
import {
    trendTone,
    useReportStyles,
    srOnlyStyle,
} from '../styles/reportStyles';
import { StatusPill } from '../styles/StatusPill';
import {
    kpiCountsFromNode,
    bucketMetrics,
    ratingBucket,
    passRateByScenarioGroup,
    chronologicalExecutions,
    moversBetween,
    scenariosForExecution,
    isLeafFailed,
    type MoverRow,
    type ScenarioGroupPassRate,
    type KpiCounts,
} from '../core/viewModels';
import { formatNumber, isDisplayedZero } from '../core/metricModel';
import { judgmentWord, type DeltaJudgment } from '../core/metricDirection';

const pctInt = (r: number) => Math.round(r * 100);

const GROUP_COLS = '1.6fr 0.7fr 0.7fr 0.7fr 2.4fr 64px';

const useLocalStyles = makeStyles({
    mbL: { marginBottom: 'var(--spacing-l)' },
    cardInset: { margin: '-12px' },
    cardHeaderRow: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-m-nudge)',
        padding: 'var(--spacing-l) var(--spacing-xl) var(--spacing-m)',
        borderBottom: '1px solid var(--neutral-stroke-2)',
    },
    // SummaryCard
    heroCardBody: {
        margin: '-12px',
        overflow: 'hidden',
        borderRadius: 'var(--radius-card)',
    },
    heroPassNum: {
        fontSize: '3rem',
        fontWeight: 'var(--font-weight-semibold)',
        lineHeight: 1,
        color: 'var(--neutral-foreground-1)',
        fontVariantNumeric: 'tabular-nums',
    },
    heroPassPct: {
        fontSize: 'var(--font-size-500)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-3)',
    },
    kpiValue: {
        fontSize: 'var(--font-size-700)',
        fontWeight: 'var(--font-weight-semibold)',
        lineHeight: 1,
        whiteSpace: 'nowrap',
        color: 'var(--neutral-foreground-1)',
        fontVariantNumeric: 'tabular-nums',
    },
    kpiSub: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
    },

    // MoversCard
    iconBadgeSuccess: {
        flex: 'none',
        width: '26px',
        height: '26px',
        borderRadius: '50%',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'color-mix(in srgb, var(--positive-solid) 15%, transparent)',
    },
    headerTitleRow: {
        display: 'flex',
        alignItems: 'baseline',
        gap: 'var(--spacing-s-nudge)',
        minWidth: 0,
    },
    headerSub: { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' },
    moversGrid: {
        display: 'grid',
        gridTemplateColumns: 'minmax(0,1fr) auto auto',
        alignItems: 'center',
        columnGap: 'var(--spacing-m)',
        gridAutoRows: 'minmax(44px, auto)',
        padding: 'var(--spacing-s) var(--spacing-xl)',
    },
    listitemContents: { display: 'contents' },
    moverNameCell: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-m)',
        minWidth: 0,
    },
    moverNameText: {
        fontSize: 'var(--font-size-300)',
        lineHeight: 'calc(20 / 14)',
        minWidth: 0,
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        color: 'var(--neutral-foreground-1)',
    },
    moverValueCell: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-4)',
        fontVariantNumeric: 'tabular-nums',
        textAlign: 'right',
        justifySelf: 'end',
    },
    moverDeltaCell: { display: 'inline-flex', justifySelf: 'end' },
    moverDeltaDash: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
        fontVariantNumeric: 'tabular-nums',
        justifySelf: 'end',
    },

    // NeedsAttentionCard
    iconBadgeDanger: {
        flex: 'none',
        width: '26px',
        height: '26px',
        borderRadius: '50%',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'color-mix(in srgb, var(--negative-solid) 15%, transparent)',
    },
    attnList: { display: 'flex', flexDirection: 'column', padding: 'var(--spacing-s)' },
    attnRow: {
        display: 'flex',
        alignItems: 'center',
        flexWrap: 'wrap',
        gap: 'var(--spacing-m)',
        padding: 'var(--spacing-xxs) var(--spacing-m)',
        minHeight: '44px',
    },
    attnName: {
        flex: 1,
        fontSize: 'var(--font-size-300)',
        lineHeight: 'calc(20 / 14)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        color: 'var(--neutral-foreground-1)',
    },
    attnStat: {
        flex: 'none',
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
        fontVariantNumeric: 'tabular-nums',
        whiteSpace: 'nowrap',
    },
    attnViewWrap: { flex: 'none', display: 'inline-flex' },
    attnEmpty: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-s-nudge)',
        fontSize: 'var(--font-size-300)',
        color: 'var(--positive-solid)',
        padding: 'var(--spacing-l) var(--spacing-xl)',
    },

    // GroupTable
    groupTitle: {
        margin: 0,
        padding: 'var(--spacing-l) var(--spacing-xl) var(--spacing-m)',
        fontSize: 'var(--font-size-400)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-1)',
    },
    groupHeaderRow: {
        display: 'grid',
        gridTemplateColumns: GROUP_COLS,
        padding: 'var(--spacing-m-nudge) var(--spacing-xl)',
        borderBottom: '1px solid var(--neutral-stroke-2)',
    },
    textRight: { textAlign: 'right' },
    passRateHeaderCell: { textAlign: 'right', paddingRight: 'var(--spacing-l)' },
    groupRow: {
        display: 'grid',
        gridTemplateColumns: GROUP_COLS,
        alignItems: 'center',
        padding: 'var(--spacing-m) var(--spacing-xl)',
        borderBottom: '1px solid var(--neutral-stroke-3)',
    },
    groupNameCell: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
        fontSize: 'var(--font-size-300)',
        lineHeight: 'calc(20 / 14)',
        minWidth: 0,
    },
    groupNameText: { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
    groupNumCell: {
        textAlign: 'right',
        fontSize: 'var(--font-size-300)',
        color: 'var(--neutral-foreground-1)',
        fontVariantNumeric: 'tabular-nums',
    },
    passRateCell: {
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--spacing-s-nudge)',
        paddingLeft: 'var(--spacing-xxl)',
        paddingRight: 'var(--spacing-l)',
    },
    passRateText: {
        textAlign: 'right',
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-1)',
        fontVariantNumeric: 'tabular-nums',
        lineHeight: 1,
    },
    deltaCell: {
        textAlign: 'right',
        fontSize: 'var(--font-size-300)',
        fontWeight: 'var(--font-weight-semibold)',
        fontVariantNumeric: 'tabular-nums',
    },
    totalRow: {
        display: 'grid',
        gridTemplateColumns: GROUP_COLS,
        alignItems: 'center',
        padding: 'var(--spacing-m) var(--spacing-xl)',
        fontWeight: 'var(--font-weight-semibold)',
    },
    totalLabelCell: { fontSize: 'var(--font-size-300)' },
    totalGoodCell: {
        textAlign: 'right',
        fontSize: 'var(--font-size-300)',
        color: 'var(--neutral-foreground-1)',
    },
    totalPassWrap: {
        display: 'flex',
        alignItems: 'center',
        paddingRight: 'var(--spacing-l)',
        justifyContent: 'flex-end',
    },
    totalPassText: { fontSize: 'var(--font-size-300)', textAlign: 'right' },

    // OverviewView
    twoPaneGrid: {
        display: 'grid',
        gridTemplateColumns: '1.12fr 1fr',
        gap: 'var(--spacing-l)',
        alignItems: 'stretch',
        marginBottom: 'var(--spacing-l)',
    },
});

type DeltaChip = {
    show: boolean;
    label: string;
    status: DeltaJudgment;
};

const deltaArrowSign = (delta: number): { arrow: string; sign: string } => ({
    arrow: delta > 0 ? '▲' : '▼',
    sign: delta > 0 ? '+' : '−',
});

const chip = (
    delta: number | undefined,
    opts: { unit?: string; lowerBetter?: boolean } = {},
): DeltaChip => {
    if (delta === undefined || delta === 0) {
        return { show: delta !== undefined, label: '—', status: 'unchanged' };
    }
    const unit = opts.unit ?? '';
    const good = opts.lowerBetter ? delta < 0 : delta > 0;
    const { arrow, sign } = deltaArrowSign(delta);
    return {
        show: true,
        label: `${arrow} ${sign}${Math.abs(delta)}${unit}`,
        status: good ? 'improved' : 'regressed',
    };
};

const numDeltaChip = (d: number, judgment: DeltaJudgment): DeltaChip => {
    if (isDisplayedZero(d)) return { show: true, label: '—', status: 'unchanged' };
    const { arrow, sign } = deltaArrowSign(d);
    return { show: true, label: `${arrow} ${sign}${formatNumber(Math.abs(d))}`, status: judgment };
};

const numDeltaAriaLabel = (d: number, judgment: DeltaJudgment): string => {
    if (isDisplayedZero(d)) return 'no change';
    const direction = d > 0 ? `increased by ${formatNumber(Math.abs(d))}` : `decreased by ${formatNumber(Math.abs(d))}`;
    const word = judgmentWord(judgment);
    return word ? `${direction}, ${word}` : direction;
};

const rateSolid = (rate: number): string =>
    rate >= 0.9 ? 'var(--positive-solid)'
        : rate >= 0.75 ? 'var(--caution-solid)'
            : rate >= 0.5 ? 'var(--warning-solid)'
                : 'var(--negative-solid)';

const auraDotStyle = (solid: string): React.CSSProperties => {
    return {
        width: '8px',
        height: '8px',
        borderRadius: '50%',
        flex: 'none',
        boxSizing: 'border-box',
        background: solid,
        boxShadow: `0 0 0 3px color-mix(in srgb, ${solid} 18%, transparent)`,
    };
};

const deltaTextColor = (status: DeltaChip['status']): string =>
    `var(--${trendTone[status]}-text)`;

type AttentionItem = {
    key: string;
    label: string;
    scenario: string;
    solid: string;
    statStr: string;
    weak: number;
    share: number;
};

const attentionKey = (scenarioName: string, metricName: string): string =>
    JSON.stringify([scenarioName, metricName]);

const attentionItems = (scenarios: ScenarioRunResult[], limit = 3): AttentionItem[] => {
    const agg = new Map<string, { scenario: string; metric: string; weak: number; fair: number; total: number }>();
    for (const s of scenarios) {
        for (const [metricName, metric] of Object.entries(s.evaluationResult?.metrics ?? {})) {
            const bucket = ratingBucket(metric?.interpretation?.rating);
            const id = attentionKey(s.scenarioName, metricName);
            const entry = agg.get(id) ?? { scenario: s.scenarioName, metric: metricName, weak: 0, fair: 0, total: 0 };
            entry.total += 1;
            if (bucket === 'weak') entry.weak += 1;
            else if (bucket === 'fair') entry.fair += 1;
            agg.set(id, entry);
        }
    }

    return [...agg.entries()]
        .map(([key, a]) => {
            const bad = a.weak + a.fair;
            const solid = a.weak >= a.fair ? 'var(--negative-solid)' : 'var(--caution-solid)';
            return {
                key,
                label: `${a.scenario} · ${a.metric}`,
                scenario: a.scenario,
                solid,
                statStr: `${a.weak} weak · ${a.fair} fair`,
                weak: a.weak,
                share: a.total > 0 ? bad / a.total : 0,
            };
        })
        .filter((w) => w.weak > 0 || w.share > 0)
        .sort((x, y) => y.share - x.share)
        .slice(0, limit);
};

const DeltaBadge = ({ chip, size = 'medium', shape = 'rounded', appearance = 'ghost' }: { chip: DeltaChip; size?: 'small' | 'medium'; shape?: 'rounded' | 'circular'; appearance?: 'ghost' | 'tint' }) =>
    chip.show ? (
        <StatusPill
            solid={`var(--${trendTone[chip.status]}-solid)`}
            textColor={`var(--${trendTone[chip.status]}-text)`}
            appearance={appearance}
            size={size}
            shape={shape}
        >
            {chip.label}
        </StatusPill>
    ) : null;

const SummaryCard = ({
    passRate,
    passChip,
    kpis,
}: {
    passRate: number;
    passChip: DeltaChip;
    kpis: { label: string; value: string; sub: string; chip: DeltaChip }[];
}) => {
    const s = useReportStyles();
    const local = useLocalStyles();
    const passNow = pctInt(passRate);
    const fillSolid = rateSolid(passRate);

    const idPrefix = useId();

    return (
        <div className={mergeClasses('eval-hero-acrylic', local.mbL)}>
            <Card appearance="outline">
                <div className={mergeClasses('eval-hero-body', local.heroCardBody)}>
                    <div role="group" aria-labelledby={`${idPrefix}-passrate`} className="eval-hero-primary">
                        <div className={s.eyebrow} id={`${idPrefix}-passrate`}>Overall pass rate</div>
                        <div className="eval-hero-primary-value">
                            <span className={local.heroPassNum}>
                                {passNow}
                                <span className={local.heroPassPct}>%</span>
                            </span>
                            <DeltaBadge chip={passChip} size="medium" shape="circular" />
                        </div>
                    </div>
                    <div className="eval-hero-kpis">
                        {kpis.map((k, i) => (
                            <div key={k.label} role="group" aria-labelledby={`${idPrefix}-kpi-${i}`} className="eval-hero-kpi">
                                <div className={mergeClasses('eval-hero-kpi-label', s.eyebrow)} id={`${idPrefix}-kpi-${i}`}>{k.label}</div>
                                <div className="eval-hero-kpi-value">
                                    <span className={local.kpiValue}>
                                        {k.value}
                                    </span>
                                    <DeltaBadge chip={k.chip} />
                                </div>
                                <span className={mergeClasses('eval-hero-kpi-sub', local.kpiSub)}>{k.sub}</span>
                            </div>
                        ))}
                    </div>
                    <ProgressBar
                        value={passNow / 100}
                        thickness="medium"
                        className="eval-grprate"
                        aria-label={`Overall pass rate ${passNow}%`}
                        style={{ ['--eval-bar']: fillSolid } as React.CSSProperties}
                    />
                </div>
            </Card>
        </div>
    );
};

const MoversCard = ({ movers, compareLabel }: { movers: MoverRow[]; compareLabel?: string }) => {
    const s = useReportStyles();
    const local = useLocalStyles();
    return (
        <Card appearance="outline">
            <div className={local.cardInset}>
                <div className={local.cardHeaderRow}>
                    <span
                        aria-hidden="true"
                        className={local.iconBadgeSuccess}
                    >
                        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="var(--positive-solid)" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M5 11 L11 5 M6.5 5 H11 V9.5" />
                        </svg>
                    </span>
                    <div className={local.headerTitleRow}>
                        <h2 className={s.sectionHeaderTitle}>Biggest movers</h2>
                        {compareLabel && (
                            <span
                                className={mergeClasses(s.sectionHeaderSub, local.headerSub)}
                                title={`vs ${compareLabel}`}
                            >
                                vs {compareLabel}
                            </span>
                        )}
                    </div>
                </div>
                <div role="list" className={local.moversGrid}>
                    {movers.map((m) => {
                        const dc = numDeltaChip(m.delta, m.status);
                        const valStr = formatNumber(m.value);
                        const isZeroDelta = dc.label === '—';
                        return (
                            // display: contents keeps the 3 cells participating directly in the
                            // parent CSS grid (unchanged layout) while grouping them as one listitem.
                            <div key={`${m.scenarioName}-${m.metricName}`} role="listitem" className={local.listitemContents}>
                                <span className={local.moverNameCell}>
                                    <span aria-hidden="true" style={auraDotStyle(`var(--${trendTone[dc.status]}-solid)`)} />
                                    <span
                                        title={`${m.scenarioName} · ${m.metricName}`}
                                        className={local.moverNameText}
                                    >
                                        {m.scenarioName} · {m.metricName}
                                    </span>
                                </span>
                                <span className={local.moverValueCell}>
                                    {valStr}
                                </span>
                                <span className={local.moverDeltaCell}>
                                    {isZeroDelta ? (
                                        <span className={local.moverDeltaDash}>—</span>
                                    ) : (
                                        <>
                                            <span aria-hidden="true"><DeltaBadge chip={dc} shape="circular" appearance="tint" /></span>
                                            <span style={srOnlyStyle}>{numDeltaAriaLabel(m.delta, m.status)}</span>
                                        </>
                                    )}
                                </span>
                            </div>
                        );
                    })}
                </div>
            </div>
        </Card>
    );
};

const NeedsAttentionCard = ({ items, onView }: { items: AttentionItem[]; onView: (item: AttentionItem) => void }) => {
    const s = useReportStyles();
    const local = useLocalStyles();
    return (
        <Card appearance="outline">
            <div className={local.cardInset}>
                <div className={local.cardHeaderRow}>
                    <span
                        aria-hidden="true"
                        className={local.iconBadgeDanger}
                    >
                        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="var(--negative-solid)" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M5 5 L11 11 M11 6.5 V11 H6.5" />
                        </svg>
                    </span>
                    <h2 className={s.sectionHeaderTitle}>Needs attention</h2>
                </div>
                {items.length > 0 ? (
                    <div role="list" className={local.attnList}>
                        {items.map((a) => (
                            <div key={a.key} role="listitem" className={local.attnRow}>
                                <span aria-hidden="true" style={auraDotStyle(a.solid)} />
                                <span title={a.label} className={mergeClasses('eval-attn-name', local.attnName)}>
                                    {a.label}
                                </span>
                                <span className={local.attnStat}>
                                    {a.statStr}
                                </span>
                                <span className={local.attnViewWrap}>
                                    <button className={s.viewLink} onClick={() => onView(a)} aria-label={`View cases for ${a.label}`}>
                                        View
                                    </button>
                                </span>
                            </div>
                        ))}
                    </div>
                ) : (
                    <div className={local.attnEmpty}>
                        No fair or weak ratings in scope
                    </div>
                )}
            </div>
        </Card>
    );
};

const GroupTable = ({
    rows,
    activeScenarios,
    totalKpi,
    totalGoodPct,
    passChip,
}: {
    rows: ScenarioGroupPassRate[];
    activeScenarios: ScenarioRunResult[];
    totalKpi: KpiCounts;
    totalGoodPct: number;
    passChip: DeltaChip;
}) => {
    const s = useReportStyles();
    const local = useLocalStyles();
    const titleId = useId();
    return (
        <Card appearance="outline">
            <div className={local.cardInset}>
                <h2 id={titleId} className={local.groupTitle}>
                    Pass rate by scenario group
                </h2>
                <div className={s.tscroll} role="table" aria-labelledby={titleId} tabIndex={0}>
                    <div className={mergeClasses('eval-grid6', s.compactTableHeader, local.groupHeaderRow)} role="row">
                        <span role="columnheader">Scenario group</span>
                        <span role="columnheader" className={local.textRight}>Good</span>
                        <span role="columnheader" className={local.textRight}>Fair</span>
                        <span role="columnheader" className={local.textRight}>Weak</span>
                        <span role="columnheader" className={local.passRateHeaderCell}>Pass rate</span>
                        <span role="columnheader" className={local.textRight}>Δ run</span>
                    </div>
                    {rows.map((row) => {
                        const groupScenarios = activeScenarios.filter((sc) => sc.scenarioName.split('.')[0] === row.group);
                        const gb = bucketMetrics(groupScenarios);
                        const solid = rateSolid(row.passRate);
                        const ratePct = pctInt(row.passRate);
                        const deltaBadgeChip = row.deltaRun !== undefined ? chip(ratePct - pctInt(row.passRate - row.deltaRun), { unit: '%' }) : { show: false, label: '', status: 'unchanged' as const };
                        return (
                            <div key={row.group} className={mergeClasses('eval-grid6', local.groupRow)} role="row">
                                <span role="cell" className={local.groupNameCell}>
                                    <span aria-hidden="true" style={auraDotStyle(solid)} />
                                    <span className={local.groupNameText}>{row.group}</span>
                                </span>
                                <span role="cell" className={local.groupNumCell}>{gb.good}</span>
                                <span role="cell" className={local.groupNumCell}>{gb.fair}</span>
                                <span role="cell" className={local.groupNumCell}>{gb.weak}</span>
                                <span role="cell" className={local.passRateCell}>
                                    <span className={local.passRateText}>{ratePct}%</span>
                                    <ProgressBar
                                        value={ratePct / 100}
                                        thickness="medium"
                                        className="eval-grprate"
                                        aria-label={`Pass rate ${ratePct}%`}
                                        style={{ ['--eval-bar']: solid } as React.CSSProperties}
                                    />
                                </span>
                                <span
                                    role="cell"
                                    className={local.deltaCell}
                                    style={{ color: deltaTextColor(deltaBadgeChip.status) }}
                                >
                                    {deltaBadgeChip.show ? deltaBadgeChip.label : '—'}
                                </span>
                            </div>
                        );
                    })}
                    <div className={mergeClasses('eval-grid6', local.totalRow)} role="row">
                        <span role="cell" className={local.totalLabelCell}>All scenarios</span>
                        <span role="cell" className={local.totalGoodCell}>{totalGoodPct}%</span>
                        <span role="cell" />
                        <span role="cell" />
                        <span role="cell" className={local.totalPassWrap}>
                            <span className={local.totalPassText}>{pctInt(totalKpi.passRate)}%</span>
                        </span>
                        <span
                            role="cell"
                            className={local.deltaCell}
                            style={{ color: deltaTextColor(passChip.status) }}
                        >
                            {passChip.show ? passChip.label : '—'}
                        </span>
                    </div>
                </div>
            </div>
        </Card>
    );
};

export const OverviewView = () => {
    const local = useLocalStyles();
    const { dataset, scoreSummary, activeExecution, activeNode, scopedNode, selectedScenarioLevel, selectScenarioLevel, setView } = useReportContext();

    const activeScenarios = useMemo(() => {
        const all = scenariosForExecution(dataset, activeExecution);
        if (!selectedScenarioLevel) return all;
        const scopedNames = new Set(
            scopedNode.flattenedNodes
                .filter((n) => n.isLeafNode && n.scenario)
                .map((n) => n.scenario!.scenarioName),
        );
        return all.filter((s) => scopedNames.has(s.scenarioName));
    }, [dataset, activeExecution, scopedNode, selectedScenarioLevel]);

    const kpi = useMemo(() => kpiCountsFromNode(scopedNode), [scopedNode]);
    const buckets = useMemo(() => bucketMetrics(activeScenarios), [activeScenarios]);

    const groupRows = useMemo(() => {
        const all = passRateByScenarioGroup(dataset, activeExecution);
        if (!selectedScenarioLevel) return all;
        const scopedGroups = new Set(activeScenarios.map((s) => s.scenarioName.split('.')[0]));
        return all.filter((r) => scopedGroups.has(r.group));
    }, [dataset, activeExecution, activeScenarios, selectedScenarioLevel]);

    const compareLabel = useMemo(() => {
        const chrono = chronologicalExecutions(dataset);
        const idx = chrono.indexOf(activeExecution);
        return idx > 0 ? chrono[idx - 1] : undefined;
    }, [dataset, activeExecution]);

    const totalDeltaRun = useMemo(() => {
        const rowsWithDelta = groupRows.filter((r) => r.deltaRun !== undefined);
        if (rowsWithDelta.length === 0) return undefined;
        const prevGroupsByName = compareLabel
            ? new Map(passRateByScenarioGroup(dataset, compareLabel).map((r) => [r.group, r]))
            : undefined;
        let prevPassing = 0;
        let prevTotal = 0;
        for (const r of rowsWithDelta) {
            const prevGroup = prevGroupsByName?.get(r.group);
            if (!prevGroup) continue;
            prevPassing += prevGroup.passing;
            prevTotal += prevGroup.total;
        }
        if (prevTotal === 0) return undefined;
        return kpi.passRate - prevPassing / prevTotal;
    }, [groupRows, kpi.passRate, dataset, compareLabel]);

    const movers = useMemo(() => {
        const rows = moversBetween(
            dataset.scenarioRunResults,
            activeExecution,
            compareLabel,
            selectedScenarioLevel ? Infinity : 5,
        );
        if (!selectedScenarioLevel) return rows;
        const scopedNames = new Set(activeScenarios.map((s) => s.scenarioName));
        return rows.filter((m) => scopedNames.has(m.scenarioName)).slice(0, 5);
    }, [dataset, activeExecution, compareLabel, activeScenarios, selectedScenarioLevel]);

    const attention = useMemo(() => attentionItems(activeScenarios), [activeScenarios]);

    const groupsFullyPassing = groupRows.filter((r) => r.passRate >= 1 && r.total > 0).length;
    const totalGroups = groupRows.length;

    const totalEvals = buckets.good + buckets.fair + buckets.weak + buckets.unknown;
    const goodPct = Math.round((buckets.good / Math.max(1, totalEvals)) * 100);

    const prev = useMemo(() => {
        if (!compareLabel) return undefined;
        const prevNode = scoreSummary.executionHistory.get(compareLabel);
        if (!prevNode) return undefined;

        const scopedNames = selectedScenarioLevel
            ? new Set(activeScenarios.map((s) => s.scenarioName))
            : undefined;
        const prevScenarios = scopedNames
            ? scenariosForExecution(dataset, compareLabel).filter((s) => scopedNames.has(s.scenarioName))
            : scenariosForExecution(dataset, compareLabel);

        const prevFailing = scopedNames
            ? prevScenarios.filter((s) => isLeafFailed(s)).length
            : kpiCountsFromNode(prevNode).failing;
        const prevBuckets = bucketMetrics(prevScenarios);
        const prevEvals = prevBuckets.good + prevBuckets.fair + prevBuckets.weak + prevBuckets.unknown;
        const prevGroups = passRateByScenarioGroup(dataset, compareLabel).filter(
            (r) => !scopedNames || activeScenarios.some((s) => s.scenarioName.split('.')[0] === r.group),
        );
        const prevScenPass = prevGroups.filter((r) => r.passRate >= 1 && r.total > 0).length;
        return {
            failing: prevFailing,
            scenPass: prevScenPass,
            goodPct: Math.round((prevBuckets.good / Math.max(1, prevEvals)) * 100),
        };
    }, [compareLabel, dataset, scoreSummary, activeScenarios, selectedScenarioLevel]);

    const passChip = chip(totalDeltaRun !== undefined ? Math.round(totalDeltaRun * 100) : undefined, { unit: '%' });
    const failChip = chip(prev ? kpi.failing - prev.failing : undefined, { lowerBetter: true });
    const scenChip = chip(prev ? groupsFullyPassing - prev.scenPass : undefined);
    const goodChip = chip(prev ? goodPct - prev.goodPct : undefined, { unit: '%' });

    const hasMovers = movers.length > 0;

    const openCasesForScenario = (item: AttentionItem) => {
        const leaf = activeNode.flattenedNodes.find(
            (n) => n.isLeafNode && n.scenario?.scenarioName === item.scenario,
        );
        const nodeKey = leaf ? leaf.nodeKey.slice(0, leaf.nodeKey.lastIndexOf('.')) : `root.${item.scenario}`;
        if (nodeKey !== selectedScenarioLevel) selectScenarioLevel(nodeKey);
        setView('cases');
    };

    const kpis = [
        { label: 'Cases failing', value: String(kpi.failing), sub: `of ${kpi.total} cases`, chip: failChip },
        { label: 'Scenarios fully passing', value: `${groupsFullyPassing} / ${totalGroups}`, sub: groupsFullyPassing === totalGroups ? 'no failing case' : `${totalGroups - groupsFullyPassing} with failures`, chip: scenChip },
        { label: 'Good ratings', value: `${goodPct}%`, sub: `${buckets.good} of ${totalEvals} evals`, chip: goodChip },
    ];

    return (
        <div>
            <SummaryCard passRate={kpi.passRate} passChip={passChip} kpis={kpis} />

            {hasMovers ? (
                <div
                    className={mergeClasses('eval-twopane', local.twoPaneGrid)}
                >
                    <MoversCard movers={movers} compareLabel={compareLabel} />
                    <NeedsAttentionCard items={attention} onView={openCasesForScenario} />
                </div>
            ) : (
                <div className={local.mbL}>
                    <NeedsAttentionCard items={attention} onView={openCasesForScenario} />
                </div>
            )}

            {groupRows.length > 0 && (
                <GroupTable
                    rows={groupRows}
                    activeScenarios={activeScenarios}
                    totalKpi={kpi}
                    totalGoodPct={goodPct}
                    passChip={passChip}
                />
            )}
        </div>
    );
};
