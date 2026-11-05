// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useEffect } from 'react';
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import {
    ReportContextProvider,
    useReportContext,
    createScoreSummary,
    getConversationDisplay,
    TranscriptBlock,
} from '../components';
import { toolCallDataset } from './fixtures/richDataset';

// prettifyJson defaults to true in ReportContext and there is no prop override, so this
// helper flips it from inside the provider to exercise the "off" branch.
const SetPretty = ({ value }: { value: boolean }) => {
    const { prettifyJson, setPrettifyJson } = useReportContext();
    useEffect(() => {
        if (prettifyJson !== value) setPrettifyJson(value);
    }, [value, prettifyJson, setPrettifyJson]);
    return null;
};

const renderMessages = (messages: ChatMessage[], prettify: boolean) => {
    const scoreSummary = createScoreSummary(toolCallDataset);
    const { messages: display } = getConversationDisplay(messages);
    return render(
        <ReportContextProvider dataset={toolCallDataset} scoreSummary={scoreSummary}>
            <SetPretty value={prettify} />
            <TranscriptBlock messages={display} />
        </ReportContextProvider>,
    );
};

const text = (value: string): AIContent => ({ $type: 'text', text: value }) as unknown as AIContent;
const functionCall = (callId: string, name: string, args: unknown): AIContent =>
    ({ $type: 'functionCall', callId, name, arguments: args, informationalOnly: false }) as unknown as AIContent;
const functionResult = (callId: string, result: unknown): AIContent =>
    ({ $type: 'functionResult', callId, result }) as unknown as AIContent;

const preTexts = (container: HTMLElement): string[] =>
    Array.from(container.querySelectorAll('pre')).map((p) => p.textContent ?? '');

describe('TranscriptBlock — JSON-in-text branch (TextNode)', () => {
    const jsonMessage: ChatMessage[] = [
        { role: 'assistant', authorName: 'gpt-4o', contents: [text('{"city":"Seattle","temp":14}')] },
    ];

    it('pretty-prints JSON-parseable text when prettifyJson is on', () => {
        const { container } = renderMessages(jsonMessage, true);
        const pre = container.querySelector('pre');
        expect(pre?.textContent).toContain('\n');
        expect(pre?.textContent).toContain('  "city": "Seattle"');
    });

    it('renders JSON-parseable text compactly when prettifyJson is off', () => {
        const { container } = renderMessages(jsonMessage, false);
        const pre = container.querySelector('pre');
        expect(pre?.textContent).toBe('{"city":"Seattle","temp":14}');
        expect(pre?.textContent).not.toContain('\n');
    });
});

describe('TranscriptBlock — tool call/result JSON (safeJson / safeJsonMaybeString)', () => {
    const toolMessages: ChatMessage[] = [
        { role: 'assistant', authorName: 'gpt-4o', contents: [functionCall('c1', 'lookup', { q: 'x' })] },
        { role: 'tool', contents: [functionResult('c1', { ok: true, n: 5 })] },
    ];

    it('pretty-prints call arguments and results when prettifyJson is on', () => {
        const { container } = renderMessages(toolMessages, true);
        const texts = preTexts(container);
        expect(texts.some((t) => /\n {2}"q": "x"/.test(t))).toBe(true);
        expect(texts.some((t) => /\n {2}"ok": true/.test(t))).toBe(true);
    });

    it('compacts call arguments and results when prettifyJson is off', () => {
        const { container } = renderMessages(toolMessages, false);
        const texts = preTexts(container);
        expect(texts).toContain('{"q":"x"}');
        expect(texts).toContain('{"ok":true,"n":5}');
    });
});
