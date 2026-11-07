// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import type { ReactNode } from 'react';
import { makeStyles, mergeClasses } from '@fluentui/react-components';

const usePillStyles = makeStyles({
    base: {
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        boxSizing: 'border-box',
        width: 'fit-content',
        fontWeight: 'var(--font-weight-semibold)',
        lineHeight: '1',
        whiteSpace: 'nowrap',
    },
    medium: { height: '20px', minWidth: '20px', padding: '0 var(--spacing-s-nudge)', fontSize: 'var(--font-size-200)' },
    small: { height: '16px', minWidth: '16px', padding: '0 var(--spacing-xs)', fontSize: 'var(--font-size-100)' },
    rounded: { borderRadius: 'var(--radius-medium)' },
    circular: { borderRadius: 'var(--radius-circular)' },
});

export type StatusPillProps = {
    solid: string;
    textColor: string;
    appearance?: 'ghost' | 'tint';
    size?: 'small' | 'medium';
    shape?: 'rounded' | 'circular';
    className?: string;
    children: ReactNode;
};

export const StatusPill = ({
    solid,
    textColor,
    appearance = 'ghost',
    size = 'medium',
    shape = 'rounded',
    className,
    children,
}: StatusPillProps) => {
    const styles = usePillStyles();
    const tint = appearance === 'tint';
    return (
        <span
            className={mergeClasses(
                styles.base,
                size === 'small' ? styles.small : styles.medium,
                shape === 'circular' ? styles.circular : styles.rounded,
                className,
            )}
            style={{
                color: textColor,
                backgroundColor: tint ? `color-mix(in srgb, ${solid} 8%, transparent)` : 'transparent',
            }}
        >
            {children}
        </span>
    );
};
