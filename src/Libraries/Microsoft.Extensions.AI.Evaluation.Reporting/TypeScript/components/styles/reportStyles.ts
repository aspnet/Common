// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import type { CSSProperties } from 'react';
import { makeStyles } from '@fluentui/react-components';

export const trendTone = { improved: 'positive', regressed: 'negative', unchanged: 'neutral' } as const;

export const srOnlyStyle: CSSProperties = {
    position: 'absolute',
    width: '1px',
    height: '1px',
    padding: 0,
    margin: '-1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
    border: 0,
};

export const useReportStyles = makeStyles({
    card: {
        backgroundColor: 'var(--report-surface-card)',
        border: '1px solid var(--neutral-stroke-1)',
        borderRadius: 'var(--radius-card)',
        overflow: 'hidden',
    },
    cardNested: {
        backgroundColor: 'var(--report-surface-nested-card)',
        border: '1px solid var(--neutral-stroke-1)',
        borderRadius: 'var(--radius-large)',
        overflow: 'hidden',
    },

    sectionHeaderTitle: {
        margin: 0,
        fontSize: 'var(--font-size-400)',
        lineHeight: 'calc(22 / 16)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-1)',
    },
    sectionHeaderSub: {
        fontSize: 'var(--font-size-200)',
        lineHeight: 'calc(20 / 12)',
        color: 'var(--neutral-foreground-4)',
        whiteSpace: 'nowrap',
    },
    eyebrow: {
        fontSize: 'var(--font-size-100)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-3)',
        textTransform: 'uppercase',
        letterSpacing: '0.4px',
    },
    compactTableHeader: {
        fontSize: 'var(--font-size-100)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-4)',
        textTransform: 'uppercase',
        letterSpacing: '.5px',
    },
    badge: {
        display: 'inline-flex',
        alignItems: 'center',
        height: '20px',
        padding: '0 var(--spacing-s)',
        borderRadius: 'var(--radius-circular)',
        backgroundColor: 'var(--neutral-background-3)',
        color: 'var(--neutral-foreground-2)',
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        letterSpacing: '0.2px',
        whiteSpace: 'nowrap',
        flex: 'none',
    },

    sidebarItem: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-m)',
        width: '100%',
        boxSizing: 'border-box',
        border: 'none',
        cursor: 'pointer',
        backgroundColor: 'transparent',
        color: 'var(--neutral-foreground-1)',
        fontFamily: 'inherit',
        textAlign: 'left',
        borderRadius: 'var(--radius-medium)',
        minHeight: 'var(--interactive-size-compact)',
        padding: 'var(--spacing-s-nudge) var(--spacing-m-nudge)',
        '@media (max-width: 900px), (pointer: coarse)': {
            minHeight: 'var(--interactive-size-touch)',
        },
    },
    sidebarItemLabel: {
        flex: '1 1 auto',
        minWidth: 0,
        fontSize: 'var(--font-size-300)',
        lineHeight: 'calc(20 / 14)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
    },
    sidebarCaret: {
        width: '16px',
        flex: 'none',
        display: 'inline-flex',
        justifyContent: 'center',
        alignItems: 'center',
        color: 'var(--neutral-foreground-3)',
        transition: 'transform var(--duration-fast) var(--curve-easy-ease)',
    },

    segmentedPill: {
        position: 'relative',
        zIndex: 1,
        flex: 'none',
        border: 'none',
        background: 'transparent',
        color: 'var(--neutral-foreground-2)',
        fontWeight: 'var(--font-weight-regular)',
        fontFamily: 'inherit',
        fontSize: 'var(--font-size-300)',
        lineHeight: 1,
        padding: 'var(--spacing-s) var(--spacing-l)',
        borderRadius: 'var(--radius-medium)',
        cursor: 'pointer',
        whiteSpace: 'nowrap',
        transition: 'color var(--duration-faster) var(--curve-easy-ease)',
    },
    segmentedPillActive: {
        color: 'var(--neutral-foreground-1)',
        fontWeight: 'var(--font-weight-semibold)',
    },
    slideIndicatorPill: {
        position: 'absolute',
        top: 0,
        left: 0,
        zIndex: 0,
        backgroundColor: 'var(--neutral-background-1)',
        boxShadow: 'var(--shadow-2)',
        borderRadius: 'var(--radius-medium)',
        opacity: 0,
        transition:
            'transform var(--duration-normal) var(--curve-decelerate-max), ' +
            'width var(--duration-normal) var(--curve-decelerate-max), ' +
            'height var(--duration-normal) var(--curve-decelerate-max)',
    },

    viewLink: {
        appearance: 'none',
        border: 'none',
        cursor: 'pointer',
        fontFamily: 'var(--font-family-base)',
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        lineHeight: 'calc(16 / 12)',
        color: 'var(--brand-foreground-1)',
        backgroundColor: 'transparent',
        padding: 'var(--spacing-xs) var(--spacing-s)',
        borderRadius: 'var(--radius-medium)',
        transition: 'background-color var(--duration-faster) var(--curve-easy-ease)',
        '&:hover': {
            backgroundColor: 'var(--brand-background-2)',
            color: 'var(--brand-foreground-2)',
        },
        '&:active': { backgroundColor: 'var(--brand-background-2-hover)' },
    },

    tscroll: {
        '@media (max-width: 720px)': {
            overflowX: 'auto',
            overflowY: 'hidden',
            WebkitOverflowScrolling: 'touch',
        },
    },
});
