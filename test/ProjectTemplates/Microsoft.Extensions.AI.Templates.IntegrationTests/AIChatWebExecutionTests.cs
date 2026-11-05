// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Shared.ProjectTemplates.Tests;
using Xunit;
using static Microsoft.Shared.ProjectTemplates.Tests.TemplateTestUtilities;

namespace Microsoft.Extensions.AI.Templates.Tests;

/// <summary>
/// Contains execution tests for the "AI Chat Web" template.
/// </summary>
/// <remarks>
/// In addition to validating that the templates build and restore correctly,
/// these tests are also responsible for template component governance reporting.
/// This is because the generated output is left on disk after tests complete,
/// most importantly the project.assets.json file that gets created during restore.
/// Therefore, it's *critical* that these tests remain in a working state,
/// as disabling them will also disable CG reporting.
/// </remarks>
public class AIChatWebExecutionTests : TemplateExecutionTestBase<AIChatWebExecutionTests>, ITemplateExecutionTestConfigurationProvider
{
    public AIChatWebExecutionTests(TemplateExecutionTestFixture fixture, ITestOutputHelper outputHelper)
        : base(fixture, outputHelper)
    {
    }

    public static TemplateExecutionTestConfiguration Configuration { get; } = new()
    {
        TemplatePackageName = "Microsoft.Extensions.AI.Templates",
        TemplateName = "aichatweb"
    };

    public static IEnumerable<object[]> GetSupportedProjectConfigurations()
    {
        (string name, string[] values)[] allOptionValues = [
            ("--provider",          ["azureopenai", "ollama", "openai" /*, "azureaifoundry" */]),
            ("--vector-store",      ["azureaisearch", "local", "qdrant"]),
            ("--aspire",            ["true", "false"]),
            ("--managed-identity",  ["true", "false"]),
            ("--Framework",         ["net9.0", "net10.0", "net11.0"])
        ];

        foreach (var args in GetPossibleOptions(allOptionValues))
        {
            // Managed Identity only applies when using an Azure service and not using aspire
            if (HasOption(args, "--managed-identity"))
            {
                if (HasOption(args, "--aspire"))
                {
                    continue;
                }

                if (!HasOption(args, "--provider", "azureopenai") &&
                    !HasOption(args, "--provider", "azureaifoundry") &&
                    !HasOption(args, "--vector-store", "azureaisearch"))
                {
                    continue;
                }
            }

            // Qdrant requires using Aspire orchestration
            if (HasOption(args, "--vector-store", "qdrant") && !HasOption(args, "--aspire"))
            {
                continue;
            }

            yield return args;
        }
    }

    // Do not skip. See XML docs for this test class.
    [Theory]
    [MemberData(nameof(GetSupportedProjectConfigurations))]
    public async Task TestAllSupportedConfigurations(params string[] args)
    {
        string projectName = GetProjectNameForArgs(args, prefix: "AIChatWeb");
        string? startupProjectRelativePath = HasOption(args, "--aspire") ? $"{projectName}.AppHost" : null;

        await CreateRestoreAndBuild(projectName, args, startupProjectRelativePath);
    }

    /// <summary>
    /// Runs a single test with --aspire and a project name that will trigger the class name
    /// normalization bug reported in https://github.com/dotnet/extensions/issues/6811.
    /// </summary>
    [Fact]
    public async Task CreateRestoreAndBuild_AspireProjectName()
    {
        await CreateRestoreAndBuild("mix.ed-dash_name 123", ["--aspire", "--provider", "azureopenai"]);
    }

    /// <summary>
    /// Tests build for various project name formats, including dots and other
    /// separators, to trigger the class name normalization bug described
    /// in https://github.com/dotnet/extensions/issues/6811
    /// </summary>
    /// <remarks>
    /// Because this test takes a few minutes to run and is only needed for regression
    /// testing of project name handing integration with Aspire, it is skipped by default.
    /// Set the environment variable <c>AI_TEMPLATES_TEST_PROJECT_NAMES</c> to "true" or "1"
    /// to enable it.
    /// </remarks>
    [Theory]
    [InlineData("dot.name")]
    [InlineData("project.123")]
    [InlineData("space name")]
    [InlineData(".1My.Projec-")]
    [InlineData("1Project123")]
    [InlineData("11double")]
    [InlineData("1")]
    [InlineData("nomatch")]
    public async Task CreateRestoreAndBuild_AspireProjectName_Variants(string projectName)
    {
        string? envValue = System.Environment.GetEnvironmentVariable("AI_TEMPLATES_TEST_PROJECT_NAMES");
        Assert.SkipUnless(
            string.Equals(envValue, "true", System.StringComparison.OrdinalIgnoreCase) || envValue == "1",
            "Set the environment variable AI_TEMPLATES_TEST_PROJECT_NAMES to 'true' or '1' to enable this test.");

        await CreateRestoreAndBuild(projectName, ["--aspire", "--provider", "azureopenai"]);
    }
}
