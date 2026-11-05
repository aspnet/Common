// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DiagnosticsContent } from '../components/cases/DiagnosticsContent';
import { MetadataContent } from '../components/cases/MetadataContent';

describe('DiagnosticsContent — severity rendering', () => {
    const diagnostics: EvaluationDiagnostic[] = [
        { severity: 'error', message: 'Hard failure in the response.' },
        { severity: 'warning', message: 'Something looks off.' },
        { severity: 'informational', message: 'Just so you know.' },
    ];

    it('renders each severity branch (error / warning / info) distinctly', () => {
        render(<DiagnosticsContent diagnostics={diagnostics} metricName="accuracy" />);

        expect(screen.getByText('Error')).toBeInTheDocument();
        expect(screen.getByText('Warning')).toBeInTheDocument();
        expect(screen.getByText('Info')).toBeInTheDocument();

        expect(screen.getByText('Hard failure in the response.')).toBeInTheDocument();
        expect(screen.getByText('Something looks off.')).toBeInTheDocument();
        expect(screen.getByText('Just so you know.')).toBeInTheDocument();
    });

    it('exposes a labelled region and a copy button per diagnostic', () => {
        render(<DiagnosticsContent diagnostics={diagnostics} metricName="accuracy" />);

        expect(screen.getByRole('region', { name: /Diagnostics for accuracy/i })).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /copy diagnostic/i })).toHaveLength(3);
    });

    it('renders nothing when there are no diagnostics', () => {
        const { container } = render(<DiagnosticsContent diagnostics={[]} metricName="accuracy" />);
        expect(container).toBeEmptyDOMElement();
    });
});

describe('MetadataContent — table-split branching', () => {
    const buildMeta = (count: number): { [K: string]: string } =>
        Object.fromEntries(Array.from({ length: count }, (_, i) => [`key${i}`, `value${i}`]));

    const tableCount = (container: HTMLElement): number =>
        container.querySelectorAll('.eval-meta-tables > div').length;

    it('uses a single table for 5 or fewer entries', () => {
        const { container } = render(<MetadataContent metadata={buildMeta(3)} />);
        expect(tableCount(container)).toBe(1);
        expect(screen.getByText('key0')).toBeInTheDocument();
        expect(screen.getByText('value2')).toBeInTheDocument();
    });

    it('splits into two tables for 6-10 entries', () => {
        const { container } = render(<MetadataContent metadata={buildMeta(7)} />);
        expect(tableCount(container)).toBe(2);
        // ceil(7/2) = 4 per table; every key still renders across the split.
        expect(screen.getByText('key0')).toBeInTheDocument();
        expect(screen.getByText('key6')).toBeInTheDocument();
    });

    it('splits into three tables for more than 10 entries', () => {
        const { container } = render(<MetadataContent metadata={buildMeta(12)} />);
        expect(tableCount(container)).toBe(3);
        expect(screen.getByText('key0')).toBeInTheDocument();
        expect(screen.getByText('key11')).toBeInTheDocument();
    });

    it('renders nothing for empty metadata', () => {
        const { container } = render(<MetadataContent metadata={{}} />);
        expect(container).toBeEmptyDOMElement();
    });
});
