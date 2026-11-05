// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useReportContext } from '../core/ReportContext';
import { OverviewView } from '../overview/OverviewView';
import { CasesView } from '../cases/CasesView';
import { HistoryView } from '../history/HistoryView';
import { ComparisonView } from '../history/ComparisonView';

export const ViewRouter = () => {
    const { view } = useReportContext();

    switch (view) {
        case 'cases':
            return <CasesView />;
        case 'history':
            return <HistoryView />;
        case 'comparison':
            return <ComparisonView />;
        case 'overview':
        default:
            return <OverviewView />;
    }
};
