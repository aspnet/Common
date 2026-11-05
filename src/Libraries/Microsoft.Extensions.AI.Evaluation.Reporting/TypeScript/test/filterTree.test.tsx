// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect, afterEach } from 'vitest';
import { render, cleanup, act } from '@testing-library/react';
import {
    createScoreSummary,
    ReportContextProvider,
    useReportContext,
    ScoreNode,
    type ReportContextType,
    type ScoreSummary,
} from '../components';
import { richDataset } from './fixtures/richDataset';

// ReportContext.filterTree closes over the live `selectedTags` / `searchValue` state, so
// the only faithful way to exercise it is to render a real provider, drive the UI state
// setters (handleTagClick / setSearchValue), and re-read the freshly rebuilt context value.
let ctx: ReportContextType | undefined;

const Capture = () => {
    ctx = useReportContext();
    return null;
};

const renderWithContext = (): ScoreSummary => {
    const summary = createScoreSummary(richDataset);
    render(
        <ReportContextProvider dataset={richDataset} scoreSummary={summary}>
            <Capture />
        </ReportContextProvider>,
    );
    return summary;
};

const leafNames = (node: ScoreNode | null): string[] =>
    node === null
        ? []
        : node.flattenedNodes
              .filter(n => n.isLeafNode)
              .map(n => n.scenario!.scenarioName);

afterEach(() => {
    cleanup();
    ctx = undefined;
});

describe('ReportContext.filterTree', () => {
    it('(a) no filter active → returns the tree unchanged (same node, same counts)', () => {
        const summary = renderWithContext();
        const root = summary.primaryResult;

        const filtered = ctx!.filterTree(root);

        // With no tags and no search the function returns the input node itself.
        expect(filtered).toBe(root);
        expect(filtered!.numPassingIterations).toBe(root.numPassingIterations);
        expect(filtered!.numFailingIterations).toBe(root.numFailingIterations);
    });

    it('(b) tag-only filter prunes to tagged leaves and re-aggregates the rebuilt group', () => {
        const summary = renderWithContext();

        act(() => ctx!.handleTagClick('GroupB'));

        const filtered = ctx!.filterTree(summary.primaryResult)!;
        const names = leafNames(filtered);

        // Only the three 'GroupB'-tagged leaves in the primary execution survive.
        expect(names.length).toBe(3);
        expect(names.every(n => n.startsWith('GroupB'))).toBe(true);

        // The rebuilt subtree is re-aggregated from the kept leaves:
        // GroupB.CodeGeneration iter2 fails (errorHandling=unacceptable); the other two pass.
        expect(filtered.numPassingIterations).toBe(2);
        expect(filtered.numFailingIterations).toBe(1);
    });

    it('(c) search-only filter keeps only the leaves whose indexed text matches', () => {
        const summary = renderWithContext();

        act(() => ctx!.setSearchValue('aspirin'));

        const filtered = ctx!.filterTree(summary.primaryResult)!;
        // 'aspirin' appears only in GroupC.SafetyCheck (prompt + model response).
        expect(leafNames(filtered)).toEqual(['GroupC.SafetyCheck']);
    });

    it('(d) tag + search combine with OR (union), not AND', () => {
        const summary = renderWithContext();

        act(() => ctx!.handleTagClick('python'));
        act(() => ctx!.setSearchValue('aspirin'));

        const filtered = ctx!.filterTree(summary.primaryResult)!;
        const names = leafNames(filtered).sort();

        // 'python' tag matches GroupB.CodeGeneration iter1; 'aspirin' search matches
        // GroupC.SafetyCheck. filterTree evaluates `tagMatches || searchMatches`, so the
        // result is the UNION of both — an AND would have yielded zero leaves.
        expect(names).toEqual(['GroupB.CodeGeneration', 'GroupC.SafetyCheck']);
    });
});
