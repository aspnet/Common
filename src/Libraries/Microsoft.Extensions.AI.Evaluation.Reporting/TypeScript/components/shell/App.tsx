// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { AppShell, type HeightStrategy, type ThemeSource } from './AppShell';
import { ViewRouter } from './ViewRouter';

export const App = ({
    heightStrategy = 'fill-viewport',
    themeSource = 'toggle',
}: {
    heightStrategy?: HeightStrategy;
    themeSource?: ThemeSource;
} = {}) => {
    return (
        <AppShell heightStrategy={heightStrategy} themeSource={themeSource}>
            <ViewRouter />
        </AppShell>
    );
};
