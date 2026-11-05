// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App, createScoreSummary, ReportContextProvider } from '../../components';
import './index.css'
import { init, ready, getAccessToken, getConfiguration } from "azure-devops-extension-sdk";
import { getClient } from "./azure-devops-extension-api";
import { Build, Attachment, BuildRestClient } from "./azure-devops-extension-api/Build";
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const ErrorHtml = ({ message }: { message: string }) =>
  <div className="error">{message}</div>;

const ATTACHMENT_NAME = 'ai-eval-report';
const ATTACHMENT_TYPE = 'ai-eval-report-json';

const findReportAttachment = async (client: BuildRestClient, project: string, buildId: number) => {
  const attachments = await client.getAttachments(project, buildId, ATTACHMENT_TYPE);
  const att = attachments.find((attachment: Attachment) => attachment.name === ATTACHMENT_NAME);
  if (att && att._links && att._links.self && att._links.self.href) {
    return att._links.self.href;
  }
  return null;
};

const getReportData = async (client: BuildRestClient, project: string, buildId: number) => {
  const att = await findReportAttachment(client, project, buildId);
  if (att) {
    const accessToken = await getAccessToken();
    const encodedToken = btoa(":" + accessToken);
    const headers = { headers: { Authorization: `Basic ${encodedToken}` } };
    const response = await fetch(att, headers);
    if (!response.ok) {
      throw new Error(`Failed to fetch attachment: ${response.statusText}`);
    }

    const dataset = await response.json() as Dataset;
    if (!dataset) {
      throw new Error('No data was returned from AI evaluation data pipeline attachment.');
    }
    return dataset;
  }

};

const run = async () => {

  await init({ applyTheme: true });
  await ready();

  const config = getConfiguration();
  const root = createRoot(document.getElementById('root')!);
  config.onBuildChanged(async (build: Build) => {

    try {
      const client = getClient(BuildRestClient);
      const dataset = await getReportData(client, build.project.name, build.id);
      if (!dataset) {
        throw new Error('No data was available to load.');
      }

      const scoreSummary = createScoreSummary(dataset);

      root.render(
        <StrictMode>
          <ReportContextProvider dataset={dataset} scoreSummary={scoreSummary} persistKey={String(build.id)}>
            <App heightStrategy="auto-grow" themeSource="host" />
          </ReportContextProvider>
        </StrictMode>
      );

    } catch (e) {

      const message = e instanceof Error ? e.message : 'An unknown error occurred while loading the report.';
      root.render(
        <FluentProvider theme={webLightTheme}>
          <StrictMode>
            <ErrorHtml message={message} />
          </StrictMode>
        </FluentProvider>
      );
    }
  });
};

run();
