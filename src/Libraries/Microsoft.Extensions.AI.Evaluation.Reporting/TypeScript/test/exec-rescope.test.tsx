// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React, { useEffect } from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
    createScoreSummary,
    ReportContextProvider,
    useReportContext,
    CasesView,
    passRateByScenarioGroup,
    bucketMetrics,
    scenariosForExecution,
} from '../components';
import { twoExecutionDataset } from './fixtures/richDataset';

const PRIMARY = 'exec-v1';
const OTHER = 'exec-v2';

describe('exec re-scope — Overview derivations follow the selected execution', () => {
    it('passRateByScenarioGroup differs between executions and default == primary', () => {
        const primaryRows = passRateByScenarioGroup(twoExecutionDataset, PRIMARY);
        const otherRows = passRateByScenarioGroup(twoExecutionDataset, OTHER);
        const defaultRows = passRateByScenarioGroup(twoExecutionDataset);

        const rate = (rows: typeof primaryRows, group: string) =>
            rows.find((r) => r.group === group)?.passRate;

        expect(rate(primaryRows, 'Comparison')).not.toBe(rate(otherRows, 'Comparison'));

        expect(defaultRows).toEqual(primaryRows);
    });

    it('bucketMetrics scopes to the active execution; default == primary', () => {
        const primaryScenarios = scenariosForExecution(twoExecutionDataset, PRIMARY);
        const otherScenarios = scenariosForExecution(twoExecutionDataset, OTHER);
        const defaultScenarios = scenariosForExecution(twoExecutionDataset);

        expect(bucketMetrics(primaryScenarios)).not.toEqual(bucketMetrics(otherScenarios));

        expect(defaultScenarios).toEqual(primaryScenarios);
    });
});

describe('exec re-scope — per-execution search index', () => {
    it('exposes one index per execution and the primary entry equals reverseTextIndex', () => {
        const summary = createScoreSummary(twoExecutionDataset);
        expect([...summary.reverseTextIndexByExecution.keys()].sort()).toEqual(
            [PRIMARY, OTHER].sort(),
        );
        expect(summary.reverseTextIndexByExecution.get(PRIMARY)).toBe(summary.reverseTextIndex);
    });

    it('a query unique to one execution matches only that execution index', () => {
        const summary = createScoreSummary(twoExecutionDataset);
        const v1Index = summary.reverseTextIndexByExecution.get(PRIMARY)!;
        const v2Index = summary.reverseTextIndexByExecution.get(OTHER)!;

        const v1Hits = v1Index.search('broadband');
        const v2Hits = v2Index.search('broadband');
        expect(v1Hits.size).toBe(0);
        expect(v2Hits.size).toBeGreaterThan(0);
    });
});

const renderCasesWith = (exec?: string) => {
    const summary = createScoreSummary(twoExecutionDataset);
    return render(
        <ReportContextProvider dataset={twoExecutionDataset} scoreSummary={summary}>
            <ExecSetter exec={exec}>
                <CasesView />
            </ExecSetter>
        </ReportContextProvider>,
    );
};

const ExecSetter = ({ exec, children }: { exec?: string; children: React.ReactNode }) => {
    const { setExec } = useReportContext();
    useEffect(() => {
        if (exec !== undefined) setExec(exec);
    }, [exec, setExec]);
    return <>{children}</>;
};

describe('exec re-scope — CasesView follows the selected execution', () => {
    it('failing-case count matches the active execution (default == primary)', async () => {
        const { unmount } = renderCasesWith(undefined);
        const defaultFailing = screen
            .getAllByRole('button', { name: /passed|failed/i })
            .filter((r) => /failed/i.test(r.getAttribute('aria-label') ?? ''));
        expect(defaultFailing.length).toBe(0);
        unmount();

        renderCasesWith(OTHER);
        const failingV2 = await screen.findAllByRole('button', { name: /failed/i });
        expect(failingV2.length).toBe(1);
    });
});
