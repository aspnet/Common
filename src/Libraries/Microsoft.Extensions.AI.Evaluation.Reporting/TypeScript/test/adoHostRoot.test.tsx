// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    createRoot: vi.fn(),
    render: vi.fn(),
    onBuildChanged: vi.fn(),
    getAttachments: vi.fn(),
}));

vi.mock('react-dom/client', () => ({
    createRoot: mocks.createRoot,
}));

vi.mock('../components', () => ({
    App: () => null,
    createScoreSummary: vi.fn(() => ({})),
    ReportContextProvider: ({ children }: { children: React.ReactNode }) => children,
}));

vi.mock('@fluentui/react-components', () => ({
    FluentProvider: ({ children }: { children: React.ReactNode }) => children,
    webLightTheme: {},
}));

vi.mock('azure-devops-extension-sdk', () => ({
    init: vi.fn().mockResolvedValue(undefined),
    ready: vi.fn().mockResolvedValue(undefined),
    getAccessToken: vi.fn().mockResolvedValue('token'),
    getConfiguration: vi.fn(() => ({ onBuildChanged: mocks.onBuildChanged })),
}));

vi.mock('../azure-devops-report/src/azure-devops-extension-api', () => ({
    getClient: vi.fn(() => ({ getAttachments: mocks.getAttachments })),
}));

vi.mock('../azure-devops-report/src/azure-devops-extension-api/Build', () => ({
    BuildRestClient: class BuildRestClient {},
}));

const dataset = {
    generatorVersion: '0.0.1',
    createdAt: '2026-06-30T10:00:00.000Z',
    scenarioRunResults: [],
} satisfies Dataset;

describe('Azure DevOps host root lifecycle', () => {
    beforeEach(() => {
        vi.resetModules();
        vi.stubGlobal('React', React);
        document.body.innerHTML = '<div id="root"></div>';
        mocks.createRoot.mockReset().mockReturnValue({ render: mocks.render });
        mocks.render.mockReset();
        mocks.onBuildChanged.mockReset();
        mocks.getAttachments.mockReset().mockResolvedValue([{
            name: 'ai-eval-report',
            _links: { self: { href: 'https://example.test/report.json' } },
        }]);
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
            ok: true,
            json: vi.fn().mockResolvedValue(dataset),
        }));
    });

    it('creates one root and renders successive build callbacks through it', async () => {
        await import('../azure-devops-report/src/main');
        await vi.waitFor(() => expect(mocks.onBuildChanged).toHaveBeenCalledOnce());
        const onBuildChanged = mocks.onBuildChanged.mock.calls[0][0];

        await onBuildChanged({ project: { name: 'project' }, id: 41 });
        await onBuildChanged({ project: { name: 'project' }, id: 42 });

        expect(mocks.createRoot).toHaveBeenCalledOnce();
        expect(mocks.createRoot).toHaveBeenCalledWith(document.getElementById('root'));
        expect(mocks.render).toHaveBeenCalledTimes(2);
    });
});
