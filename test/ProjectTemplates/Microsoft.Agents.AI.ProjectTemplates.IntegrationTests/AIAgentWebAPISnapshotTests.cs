// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.ProjectTemplates.Tests;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;
using Xunit;
using static Microsoft.Shared.ProjectTemplates.Tests.TemplateTestUtilities;

namespace Microsoft.Agents.AI.ProjectTemplates.Tests;

public class AIAgentWebAPISnapshotTests : TemplateSnapshotTestBase
{
    private readonly ILogger _log;

    public AIAgentWebAPISnapshotTests(ITestOutputHelper log)
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        _log = new XunitLoggerProvider(log).CreateLogger("TestRun");
#pragma warning restore CA2000 // Dispose objects before losing scope
    }

    [Theory]
    [InlineData("--provider=openai")]
    [InlineData("--provider=ollama")]
    [InlineData("--provider=azureopenai", "--managed-identity=true")]
    [InlineData("--provider=azureopenai", "--managed-identity=false")]
    public async Task RunSnapshotTests(params string[] templateArgs)
    {
        string projectNamePrefix = "AIAgentWebApi";
        string templatePackageName = "Microsoft.Agents.AI.ProjectTemplates";
        string templateName = "aiagent-webapi";

        TemplateVerifierOptions options = PrepareSnapshotVerifier(
            projectNamePrefix,
            templatePackageName,
            templateName,
            templateArgs)
        .WithCustomScrubbers(ScrubbersDefinition.Empty
            .AddUserSecretsScrubber()
            .AddPackageReferenceScrubber()
            .AddLocalhostPortScrubber());

        VerificationEngine engine = new VerificationEngine(_log);
        await engine.Execute(options);
    }
}
