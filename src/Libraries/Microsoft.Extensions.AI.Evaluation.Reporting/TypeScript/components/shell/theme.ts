// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { webDarkTheme, webLightTheme, type Theme } from '@fluentui/react-components';
import './theme.css';

const DARK_ROOT_CLASS = 'fluent-dark';

type ResolvedTheme = {
    fluentTheme: Theme;
    rootClass: string | undefined;
};

export const resolveTheme = (darkMode: boolean): ResolvedTheme => ({
    fluentTheme: darkMode ? webDarkTheme : webLightTheme,
    rootClass: darkMode ? DARK_ROOT_CLASS : undefined,
});

const HOST_BACKGROUND_VAR = '--background-color';

const parseRgb = (raw: string): [number, number, number] | null => {
    const value = raw.trim();
    if (value === '') return null;

    if (value.startsWith('#')) {
        const hex = value.slice(1);
        if (hex.length === 3) {
            const r = parseInt(hex[0] + hex[0], 16);
            const g = parseInt(hex[1] + hex[1], 16);
            const b = parseInt(hex[2] + hex[2], 16);
            if ([r, g, b].some(Number.isNaN)) return null;
            return [r, g, b];
        }
        if (hex.length === 6 || hex.length === 8) {
            const r = parseInt(hex.slice(0, 2), 16);
            const g = parseInt(hex.slice(2, 4), 16);
            const b = parseInt(hex.slice(4, 6), 16);
            if ([r, g, b].some(Number.isNaN)) return null;
            return [r, g, b];
        }
        return null;
    }

    const m = value.match(/rgba?\(([^)]+)\)/i);
    if (m) {
        const parts = m[1].split(',').map((p) => parseFloat(p.trim()));
        if (parts.length >= 3 && parts.slice(0, 3).every((n) => !Number.isNaN(n))) {
            return [parts[0], parts[1], parts[2]];
        }
    }
    return null;
};

const luminance = ([r, g, b]: [number, number, number]): number =>
    (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;

export const detectHostDarkMode = (): boolean => {
    if (typeof document === 'undefined' || typeof getComputedStyle !== 'function') {
        return false;
    }
    const raw = getComputedStyle(document.documentElement)
        .getPropertyValue(HOST_BACKGROUND_VAR);
    const rgb = parseRgb(raw);
    if (!rgb) return false;
    return luminance(rgb) < 0.5;
};
