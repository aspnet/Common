// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useLayoutEffect } from 'react';
import { resize } from 'azure-devops-extension-sdk';

export function useAdoResize(enabled: boolean): void {
    useLayoutEffect(() => {
        if (!enabled) return;

        const raf = requestAnimationFrame(() => {
            try {
                const height = document.documentElement.scrollHeight;
                resize(undefined, height);
            } catch {
                // resize is best-effort; the SDK throws when not hosted in an ADO iframe.
            }
        });

        return () => cancelAnimationFrame(raf);
    });
}
