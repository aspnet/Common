// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act, cleanup } from '@testing-library/react';
import { AnnouncerProvider, useAnnounce } from '../components/core/Announcer';

const BLANK_MS = 60;
const HOLD_MS = 250;

const region = (): HTMLElement | null => document.querySelector('[role="status"]');
const regionText = (): string => region()?.textContent ?? '';

const captured: { announce?: (msg: string) => void } = {};
const Probe = () => {
    captured.announce = useAnnounce();
    return null;
};

const renderProvider = () =>
    render(
        <AnnouncerProvider>
            <Probe />
        </AnnouncerProvider>,
    );

beforeEach(() => {
    vi.useFakeTimers();
    captured.announce = undefined;
});

afterEach(() => {
    cleanup();
    vi.useRealTimers();
});

describe('AnnouncerProvider — live region mount contract', () => {
    it('mounts the region with role=status/aria-live=polite/aria-atomic=true and empty text before any announce()', () => {
        renderProvider();
        const el = region();
        expect(el).not.toBeNull();
        expect(el).toHaveAttribute('role', 'status');
        expect(el).toHaveAttribute('aria-live', 'polite');
        // aria-atomic ensures the whole (single-string) message is re-read on each swap.
        expect(el).toHaveAttribute('aria-atomic', 'true');
        expect(el?.textContent).toBe('');
    });

    it('hides the region via the clip sr-only technique — never display:none/visibility:hidden', () => {
        // A display:none / visibility:hidden "optimization" would drop the node from the
        // a11y tree and silently mute every announcement while all other tests still pass.
        renderProvider();
        const el = region()!;
        expect(el.style.display).not.toBe('none');
        expect(el.style.visibility).not.toBe('hidden');
        // The chosen technique is the 1px position:absolute + overflow:hidden clip, which
        // keeps the node rendered (and announced) while pushing it visually offscreen.
        expect(el.style.position).toBe('absolute');
        expect(el.style.width).toBe('1px');
        expect(el.style.height).toBe('1px');
        expect(el.style.overflow).toBe('hidden');
    });

    it('portals the region as a direct child of document.body carrying data-tabster-never-hide', () => {
        renderProvider();
        const el = region();
        expect(el?.parentElement).toBe(document.body);
        expect(el).toHaveAttribute('data-tabster-never-hide');
    });
});

describe('AnnouncerProvider — announce() queueing', () => {
    it('queues two announce() calls fired <60ms apart so BOTH reach the region (the original drop bug)', () => {
        renderProvider();
        act(() => {
            captured.announce!('first');
            captured.announce!('second');
        });
        expect(regionText()).toBe('');

        act(() => vi.advanceTimersByTime(BLANK_MS));
        expect(regionText()).toBe('first');

        act(() => vi.advanceTimersByTime(HOLD_MS));
        expect(regionText()).toBe('');

        act(() => vi.advanceTimersByTime(BLANK_MS));
        expect(regionText()).toBe('second');
    });

    it('delivers three rapid announce() calls in FIFO order with no reorder or duplication', () => {
        renderProvider();
        act(() => {
            captured.announce!('one');
            captured.announce!('two');
            captured.announce!('three');
        });

        const seen: string[] = [];
        for (let i = 0; i < 3; i++) {
            act(() => vi.advanceTimersByTime(BLANK_MS));
            seen.push(regionText());
            act(() => vi.advanceTimersByTime(HOLD_MS));
        }
        expect(seen).toEqual(['one', 'two', 'three']);
    });

    it('re-announces an identical string twice (blanks the region between the two)', () => {
        renderProvider();
        act(() => captured.announce!('Copied'));
        act(() => vi.advanceTimersByTime(BLANK_MS));
        expect(regionText()).toBe('Copied');

        act(() => captured.announce!('Copied'));
        act(() => vi.advanceTimersByTime(HOLD_MS));
        expect(regionText()).toBe('');

        act(() => vi.advanceTimersByTime(BLANK_MS));
        expect(regionText()).toBe('Copied');
    });
});

describe('AnnouncerProvider — teardown', () => {
    it('clears the pending timer on unmount (no leak, no post-unmount state update)', () => {
        const { unmount } = renderProvider();
        act(() => captured.announce!('leaving'));

        const clearSpy = vi.spyOn(window, 'clearTimeout');
        unmount();
        expect(clearSpy).toHaveBeenCalled();

        expect(() => act(() => vi.advanceTimersByTime(10_000))).not.toThrow();
        clearSpy.mockRestore();
    });
});
