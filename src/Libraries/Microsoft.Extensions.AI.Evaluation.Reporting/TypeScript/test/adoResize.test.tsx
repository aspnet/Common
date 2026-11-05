// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, cleanup } from '@testing-library/react';
import { StrictMode } from 'react';
import { resize } from 'azure-devops-extension-sdk';
import { useAdoResize } from '../components/shell/useAdoResize';

// useAdoResize(enabled) only imports `resize` from the SDK. Stub the whole module so the
// hook's single dependency is a spy — no real ADO host is present in jsdom.
vi.mock('azure-devops-extension-sdk', () => ({
    resize: vi.fn(),
}));

const resizeMock = vi.mocked(resize);

// The hook schedules its work inside requestAnimationFrame. Rather than rely on fake-timer rAF
// support, drive rAF deterministically: capture scheduled callbacks and flush them on demand.
let rafCallbacks: Map<number, FrameRequestCallback>;
let nextRafId: number;

const flushRaf = () => {
    const pending = [...rafCallbacks.values()];
    rafCallbacks.clear();
    pending.forEach((cb) => cb(performance.now?.() ?? 0));
};

const Harness = ({ enabled }: { enabled: boolean }) => {
    useAdoResize(enabled);
    return <div>ado-harness</div>;
};

beforeEach(() => {
    resizeMock.mockReset();
    rafCallbacks = new Map();
    nextRafId = 0;
    vi.spyOn(globalThis, 'requestAnimationFrame').mockImplementation((cb: FrameRequestCallback) => {
        const id = ++nextRafId;
        rafCallbacks.set(id, cb);
        return id;
    });
    vi.spyOn(globalThis, 'cancelAnimationFrame').mockImplementation((id: number) => {
        rafCallbacks.delete(id);
    });
});

afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    if (Object.getOwnPropertyDescriptor(document.documentElement, 'scrollHeight')?.configurable) {
        delete (document.documentElement as unknown as { scrollHeight?: number }).scrollHeight;
    }
});

describe('useAdoResize', () => {
    it('calls resize with the document scroll height when enabled (after the rAF fires)', () => {
        Object.defineProperty(document.documentElement, 'scrollHeight', {
            configurable: true,
            get: () => 1234,
        });

        render(<Harness enabled={true} />);
        // rAF is scheduled but has not yet fired: resize must not be called synchronously.
        expect(resizeMock).not.toHaveBeenCalled();

        flushRaf();

        expect(resizeMock).toHaveBeenCalledTimes(1);
        expect(resizeMock).toHaveBeenCalledWith(undefined, 1234);
    });

    it('does NOT call resize when disabled', () => {
        render(<Harness enabled={false} />);
        flushRaf();
        expect(resizeMock).not.toHaveBeenCalled();
    });

    it('cancels the pending rAF on unmount so resize never fires', () => {
        const { unmount } = render(<Harness enabled={true} />);
        unmount();
        flushRaf();
        expect(resizeMock).not.toHaveBeenCalled();
    });

    it('cancels the previous frame before scheduling one for a rerender', () => {
        const { rerender } = render(<Harness enabled={true} />);
        const cancelSpy = vi.mocked(globalThis.cancelAnimationFrame);

        rerender(<Harness enabled={true} />);

        expect(cancelSpy).toHaveBeenCalledWith(1);
        expect(rafCallbacks.size).toBe(1);
        flushRaf();
        expect(resizeMock).toHaveBeenCalledTimes(1);
    });

    it('leaves only the current frame scheduled in StrictMode', () => {
        render(
            <StrictMode>
                <Harness enabled={true} />
            </StrictMode>,
        );

        expect(rafCallbacks.size).toBe(1);
        flushRaf();
        expect(resizeMock).toHaveBeenCalledTimes(1);
    });

    it('swallows errors thrown by resize (try/catch inside the rAF)', () => {
        resizeMock.mockImplementation(() => {
            throw new Error('ado host unavailable');
        });

        render(<Harness enabled={true} />);

        expect(() => flushRaf()).not.toThrow();
        expect(resizeMock).toHaveBeenCalledTimes(1);
    });
});
