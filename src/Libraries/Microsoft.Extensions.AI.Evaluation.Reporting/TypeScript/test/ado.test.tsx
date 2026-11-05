// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React, { useEffect } from 'react';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, act, cleanup } from '@testing-library/react';
import { createScoreSummary, ReportContextProvider, useReportContext, detectHostDarkMode } from '../components';

const dataset: Dataset = {
    generatorVersion: '0.0.1',
    createdAt: '2026-06-30T10:00:00.000Z',
    scenarioRunResults: [
        {
            scenarioName: 'GroupA.Quality',
            iterationName: 'iteration1',
            executionName: 'exec-A',
            creationTime: '2026-06-30T10:00:00.000Z',
            messages: [{ role: 'user', contents: [{ $type: 'text', text: 'hi' } as TextContent] }],
            modelResponse: { messages: [{ role: 'assistant', contents: [{ $type: 'text', text: 'ok' } as TextContent] }] },
            evaluationResult: {
                metrics: {
                    coherence: {
                        $type: 'numeric', name: 'coherence', value: 5,
                        interpretation: { rating: 'good', failed: false },
                    } as NumericMetric,
                },
            },
            tags: ['GroupA'],
            formatVersion: 1,
        },
    ],
};

const captured: { setView?: (v: 'overview' | 'cases' | 'history' | 'comparison') => void; setDarkMode?: (v: boolean) => void; setExec?: (v: string | undefined) => void } = {};
const Probe = (): React.ReactElement => {
    const ctx = useReportContext();
    useEffect(() => {
        captured.setView = ctx.setView;
        captured.setDarkMode = ctx.setDarkMode;
        captured.setExec = ctx.setExec;
    });
    return (
        <div>
            <span data-testid="view">{ctx.view}</span>
            <span data-testid="dark">{String(ctx.darkMode)}</span>
            <span data-testid="exec">{ctx.exec ?? ''}</span>
            <span data-testid="execCount">{ctx.scoreSummary.executionHistory.size}</span>
        </div>
    );
};

const renderWith = (persistKey?: string) => {
    const scoreSummary = createScoreSummary(dataset);
    return render(
        <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary} persistKey={persistKey}>
            <Probe />
        </ReportContextProvider>,
    );
};

beforeEach(() => {
    sessionStorage.clear();
    document.documentElement.style.removeProperty('--background-color');
});

afterEach(() => {
    cleanup();
    sessionStorage.clear();
    document.documentElement.style.removeProperty('--background-color');
});

describe('detectHostDarkMode', () => {
    it('returns false when no host background var is injected (safe light default)', () => {
        expect(detectHostDarkMode()).toBe(false);
    });

    it('returns true for a dark host background (low luminance hex)', () => {
        document.documentElement.style.setProperty('--background-color', '#1e1e1e');
        expect(detectHostDarkMode()).toBe(true);
    });

    it('returns false for a light host background (high luminance hex)', () => {
        document.documentElement.style.setProperty('--background-color', '#ffffff');
        expect(detectHostDarkMode()).toBe(false);
    });

    it('parses rgb() host background values', () => {
        document.documentElement.style.setProperty('--background-color', 'rgb(32, 32, 32)');
        expect(detectHostDarkMode()).toBe(true);
        document.documentElement.style.setProperty('--background-color', 'rgb(245, 245, 245)');
        expect(detectHostDarkMode()).toBe(false);
    });

    it('falls back to false (light) on an unparseable value', () => {
        document.documentElement.style.setProperty('--background-color', 'not-a-color');
        expect(detectHostDarkMode()).toBe(false);
    });
});

describe('persistKey remount survival (ADO onBuildChanged)', () => {
    it('restores UI-intent across an unmount+remount with the same persistKey', () => {
        renderWith('42');
        expect(screen.getByTestId('view').textContent).toBe('overview');

        act(() => {
            captured.setDarkMode!(true);
            captured.setExec!('exec-A');
        });
        expect(screen.getByTestId('dark').textContent).toBe('true');
        expect(screen.getByTestId('exec').textContent).toBe('exec-A');

        cleanup();
        renderWith('42');

        expect(screen.getByTestId('dark').textContent).toBe('true');
        expect(screen.getByTestId('exec').textContent).toBe('exec-A');
        expect(screen.getByTestId('execCount').textContent).toBe('1');
    });

    it('does NOT leak UI-intent to a different persistKey (different build)', () => {
        renderWith('42');
        act(() => {
            captured.setDarkMode!(true);
        });
        expect(screen.getByTestId('dark').textContent).toBe('true');

        cleanup();
        renderWith('99');
        expect(screen.getByTestId('dark').textContent).toBe('false');
    });

    it('does not persist at all when persistKey is absent (standalone, zero regression)', () => {
        renderWith(undefined);
        act(() => {
            captured.setDarkMode!(true);
        });
        expect(
            Object.keys(sessionStorage).some((k) => k.startsWith('ai-eval-report:ui-intent:')),
        ).toBe(false);

        cleanup();
        renderWith(undefined);
        expect(screen.getByTestId('dark').textContent).toBe('false');
    });
});
