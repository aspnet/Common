// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { DismissCircle16Regular, Warning16Regular, Info16Regular, Copy16Regular } from "@fluentui/react-icons";
import { Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell, mergeClasses } from "@fluentui/react-components";
import { useStyles } from "../styles/Styles";
import { srOnlyStyle } from "../styles/reportStyles";
import { useAnnounce } from "../core/Announcer";

export const DiagnosticsContent = ({ diagnostics, metricName }: { diagnostics: EvaluationDiagnostic[]; metricName: string; }) => {
    const classes = useStyles();
    const announce = useAnnounce();

    if (diagnostics.length === 0) {
        return null;
    }

    const renderSeverityCell = (diagnostic: EvaluationDiagnostic) => {
        if (diagnostic.severity === "error") {
            return (
                <span className={classes.diagnosticErrorCell}>
                    <DismissCircle16Regular /> Error
                </span>
            );
        } else if (diagnostic.severity === "warning") {
            return (
                <span className={classes.diagnosticWarningCell}>
                    <Warning16Regular /> Warning
                </span>
            );
        } else {
            return (
                <span className={classes.diagnosticInfoCell}>
                    <Info16Regular /> Info
                </span>
            );
        }
    };

    const copyToClipboard = (text: string) => {
        if (!navigator.clipboard) {
            announce('Copy failed');
            return;
        }
        navigator.clipboard.writeText(text).then(
            () => announce('Diagnostic copied to clipboard'),
            () => announce('Copy failed'),
        );
    };

    return (
        <div
            className={classes.tableContainer}
            tabIndex={0}
            role="region"
            aria-label={`Diagnostics for ${metricName}`}
        >
            <Table className={classes.autoWidthTable}>
                <TableHeader>
                    <TableRow>
                        <TableHeaderCell className={mergeClasses(classes.tableHeaderCell, classes.diagnosticSeverityCell)}>Severity</TableHeaderCell>
                        <TableHeaderCell className={mergeClasses(classes.tableHeaderCell, classes.diagnosticMessageCell)}>Message</TableHeaderCell>
                        <TableHeaderCell className={mergeClasses(classes.tableHeaderCell, classes.diagnosticCopyButtonCell)}>
                            <span style={srOnlyStyle}>Actions</span>
                        </TableHeaderCell>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    {diagnostics.map((diag, index) => (
                        <tr key={`diag-${index}`}>
                            <TableCell className={classes.diagnosticSeverityCell}>
                                {renderSeverityCell(diag)}
                            </TableCell>
                            <TableCell className={classes.diagnosticMessageCell}>
                                <pre className={classes.diagnosticMessageText}>
                                    {diag.message}
                                </pre>
                            </TableCell>
                            <TableCell className={classes.diagnosticCopyButtonCell}>
                                <button
                                    className={classes.copyButton}
                                    onClick={() => copyToClipboard(`${diag.severity}: ${diag.message}`)}
                                    title="Copy Diagnostic"
                                    aria-label="Copy diagnostic"
                                >
                                    <Copy16Regular />
                                </button>
                            </TableCell>
                        </tr>
                    ))}
                </TableBody>
            </Table>
        </div>
    );
};
