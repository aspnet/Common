// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.ProjectTemplates.Tests;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;
using Xunit;
using static Microsoft.Shared.ProjectTemplates.Tests.TemplateTestUtilities;

namespace Microsoft.Extensions.AI.Templates.Tests;

public class AIChatWebSnapshotTests : TemplateSnapshotTestBase
{
    // The wwwroot folder contains static content
    private static readonly string[] _verificationExcludePatterns = [
        "**/wwwroot/**"
    ];

    private readonly ILogger _log;

    public AIChatWebSnapshotTests(ITestOutputHelper log)
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        _log = new XunitLoggerProvider(log).CreateLogger("TestRun");
#pragma warning restore CA2000 // Dispose objects before losing scope
    }

    [Theory]
    [InlineData("--provider=openai")]
    [InlineData("--provider=ollama", "--vector-store=qdrant")]
    [InlineData("--provider=openai", "--vector-store=azureaisearch")]
    [InlineData("--aspire", "--provider=openai")]
    [InlineData("--aspire", "--provider=azureopenai", "--vector-store=azureaisearch")]
    public async Task RunSnapshotTests(params string[] templateArgs)
    {
        string projectNamePrefix = "AIChatWeb";
        string templatePackageName = "Microsoft.Extensions.AI.Templates";
        string templateName = "aichatweb";

        TemplateVerifierOptions options = PrepareSnapshotVerifier(
            projectNamePrefix,
            templatePackageName,
            templateName,
            templateArgs,
            _verificationExcludePatterns)
        .WithCustomScrubbers(ScrubbersDefinition.Empty
            .AddSolutionFileGuidScrubber()
            .AddUserSecretsScrubber()
            .AddPackageReferenceScrubber()
            .AddLocalhostPortScrubber());

        VerificationEngine engine = new VerificationEngine(_log);
        await engine.Execute(options);
    }
}
