// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { makeStyles } from "@fluentui/react-components";

export const useStyles = makeStyles({
    tableContainer: {
        overflowX: 'auto',
        maxWidth: '75rem',
    },
    autoWidthTable: {
        tableLayout: 'auto',
        width: '100%',
    },
    tableHeaderCell: {
        fontWeight: '600',
        fontSize: 'var(--font-size-300)',
        borderBottom: '1px solid var(--neutral-stroke-2)',
    },
    tablesContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: '1rem',
    },
    tableWrapper: {
        flex: '1',
    },
    copyButton: {
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        padding: '2px',
        color: 'var(--neutral-foreground-3)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: '3px',
        '&:hover': {
            backgroundColor: 'var(--neutral-background-4)',
            color: 'var(--neutral-foreground-1)',
        }
    },
    diagnosticErrorCell: {
        display: 'flex',
        alignItems: 'center',
        gap: '0.25rem',
        color: 'var(--diagnostic-error-foreground)',
        whiteSpace: 'nowrap',
    },
    diagnosticWarningCell: {
        display: 'flex',
        alignItems: 'center',
        gap: '0.25rem',
        color: 'var(--diagnostic-warning-foreground)',
        whiteSpace: 'nowrap',
    },
    diagnosticInfoCell: {
        display: 'flex',
        alignItems: 'center',
        gap: '0.25rem',
        color: 'var(--diagnostic-info-foreground)',
        whiteSpace: 'nowrap',
    },
    diagnosticMessageText: {
        fontFamily: 'var(--diagnostic-message-font-family)',
        whiteSpace: 'pre-wrap',
        overflow: 'auto',
        margin: 0,
        padding: 0,
        display: 'block',
    },
    diagnosticSeverityCell: {
        width: '1%',
        height: 'auto',
        whiteSpace: 'nowrap',
        verticalAlign: 'top',
        padding: '1em',
    },
    diagnosticMessageCell: {
        width: '100%',
        height: 'auto',
        verticalAlign: 'top',
        padding: '1em',
    },
    diagnosticCopyButtonCell: {
        width: '1%',
        height: 'auto',
        whiteSpace: 'nowrap',
        verticalAlign: 'top',
        padding: '1em',
    },
});
