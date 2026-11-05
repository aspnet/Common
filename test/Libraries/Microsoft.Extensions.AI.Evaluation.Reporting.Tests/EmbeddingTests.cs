// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Html;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class EmbeddingTests
{
    [Fact]
    public void CIBuildsMustIncludeEmbeddedHTML()
    {
        // TF_BUILD should be set in our CI pipeline
        Assert.SkipUnless(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")), "Skipping test because it is not running in CI");

        Assert.NotEmpty(HtmlReportWriter.HtmlTemplateBefore);
        Assert.NotEmpty(HtmlReportWriter.HtmlTemplateAfter);
    }
}
