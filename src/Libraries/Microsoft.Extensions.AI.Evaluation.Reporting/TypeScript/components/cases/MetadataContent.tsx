// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell } from "@fluentui/react-components";
import { useStyles } from "../styles/Styles";

export const MetadataContent = ({ metadata }: { metadata: { [K: string]: string }; }) => {
    const classes = useStyles();
    const metadataEntries = Object.entries(metadata);

    if (metadataEntries.length === 0) {
        return null;
    }

    let tableCount = 1;
    if (metadataEntries.length > 10) {
        tableCount = 3;
    } else if (metadataEntries.length > 5) {
        tableCount = 2;
    }

    const tables: Array<Array<[string, string]>> = [];
    const itemsPerTable = Math.ceil(metadataEntries.length / tableCount);

    for (let i = 0; i < tableCount; i++) {
        const startIndex = i * itemsPerTable;
        const endIndex = Math.min(startIndex + itemsPerTable, metadataEntries.length);
        tables.push(metadataEntries.slice(startIndex, endIndex));
    }

    return (
        <div className={`${classes.tablesContainer} eval-meta-tables`}>
            {tables.map((tableData, tableIndex) => (
                <div key={`table-${tableIndex}`} className={classes.tableWrapper}>
                    <div
                        className={classes.tableContainer}
                        tabIndex={0}
                        role="region"
                        aria-label={tables.length > 1 ? `Metadata (${tableIndex + 1} of ${tables.length})` : 'Metadata'}
                    >
                        <Table>
                            <TableHeader>
                                <TableRow>
                                    <TableHeaderCell className={classes.tableHeaderCell}>Name</TableHeaderCell>
                                    <TableHeaderCell className={classes.tableHeaderCell}>Value</TableHeaderCell>
                                </TableRow>
                            </TableHeader>
                            <TableBody>
                                {tableData.map(([key, value], index) => (
                                    <tr key={`metadata-${tableIndex}-${index}`}>
                                        <TableCell>{key}</TableCell>
                                        <TableCell>{value}</TableCell>
                                    </tr>
                                ))}
                            </TableBody>
                        </Table>
                    </div>
                </div>
            ))}
        </div>
    );
};
