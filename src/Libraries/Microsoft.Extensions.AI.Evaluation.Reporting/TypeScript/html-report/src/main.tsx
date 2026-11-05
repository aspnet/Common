// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App, createScoreSummary, ReportContextProvider } from '../../components';

let dataset: Dataset = { scenarioRunResults: [], createdAt: '' };

const rootElement = document.getElementById('root')!;

if (!import.meta.env.PROD) {
  // This only runs in development. In production the data is read from the data-dataset attribute on the root element.
  // run `node init-devdata.js` to populate the data file from the most recent execution.
  const imported = await import("../devdata.json");
  dataset = imported.default as unknown as Dataset;
} else {
  // In production, the data is HTML-encoded and placed in a data-dataset attribute.
  // This pattern avoids XSS vulnerabilities that can occur when embedding JSON in script blocks.
  const datasetJson = rootElement.getAttribute('data-dataset');
  if (datasetJson) {
    try {
      dataset = JSON.parse(datasetJson) as Dataset;
    } catch {
      // Keep the empty fallback dataset when the embedded payload is malformed.
    }
  }
}

const scoreSummary = createScoreSummary(dataset);

createRoot(rootElement).render(
  <StrictMode>
    <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary}>
      <App heightStrategy="fill-viewport" themeSource="toggle" />
    </ReportContextProvider>
  </StrictMode>,
)
