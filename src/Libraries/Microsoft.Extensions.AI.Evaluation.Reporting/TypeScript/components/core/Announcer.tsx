// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import * as React from 'react';
import { createPortal } from 'react-dom';
import { srOnlyStyle } from '../styles/reportStyles';

type AnnounceFn = (message: string) => void;

const AnnouncerContext = React.createContext<AnnounceFn>(() => {});

export const useAnnounce = (): AnnounceFn => React.useContext(AnnouncerContext);

const blankMs = 60;
const holdMs = 250;

export const AnnouncerProvider = ({ children }: { children: React.ReactNode }) => {
    const [message, setMessage] = React.useState('');
    const queue = React.useRef<string[]>([]);
    const timer = React.useRef<number | undefined>(undefined);

    const announce = React.useCallback((msg: string) => {
        function drain() {
            const next = queue.current.shift();
            if (next === undefined) {
                timer.current = undefined;
                return;
            }
            setMessage('');
            timer.current = window.setTimeout(() => {
                setMessage(next);
                timer.current = window.setTimeout(drain, holdMs);
            }, blankMs);
        }

        queue.current.push(msg);
        if (timer.current === undefined) {
            drain();
        }
    }, []);

    React.useEffect(
        () => () => { if (timer.current !== undefined) window.clearTimeout(timer.current); },
        [],
    );

    return (
        <AnnouncerContext.Provider value={announce}>
            {children}
            {createPortal(
                // Must be portalled to <body> with data-tabster-never-hide, or Fluent's
                // Popover/Drawer Modalizer aria-hides this region, swallowing announcements.
                <div
                    role="status"
                    aria-live="polite"
                    aria-atomic="true"
                    data-tabster-never-hide=""
                    style={srOnlyStyle}
                >
                    {message}
                </div>,
                document.body,
            )}
        </AnnouncerContext.Provider>
    );
};
