// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Shared.ProjectTemplates.Tests;
using Xunit;
using static Microsoft.Shared.ProjectTemplates.Tests.TemplateTestUtilities;

namespace Microsoft.Agents.AI.ProjectTemplates.Tests;

/// <summary>
/// Contains execution tests for the "AI Agent Web API" template.
/// </summary>
/// <remarks>
/// In addition to validating that the templates build and restore correctly,
/// these tests are also responsible for template component governance reporting.
/// This is because the generated output is left on disk after tests complete,
/// most importantly the project.assets.json file that gets created during restore.
/// Therefore, it's *critical* that these tests remain in a working state,
/// as disabling them will also disable CG reporting.
/// </remarks>
public class AIAgentWebAPIExecutionTests : TemplateExecutionTestBase<AIAgentWebAPIExecutionTests>, ITemplateExecutionTestConfigurationProvider
{
    public AIAgentWebAPIExecutionTests(TemplateExecutionTestFixture fixture, ITestOutputHelper outputHelper)
        : base(fixture, outputHelper)
    {
    }

    public static TemplateExecutionTestConfiguration Configuration { get; } = new()
    {
        TemplatePackageName = "Microsoft.Agents.AI.ProjectTemplates",
        TemplateName = "aiagent-webapi"
    };

    public static IEnumerable<object[]> GetSupportedProjectConfigurations()
    {
        (string name, string[] values)[] allOptionValues = [
            ("--provider",          ["azureopenai", "ollama", "openai"]),
            ("--managed-identity",  ["true", "false"]),
            ("--framework",         ["net8.0", "net9.0", "net10.0", "net11.0"])
        ];

        foreach (var args in GetPossibleOptions(allOptionValues))
        {
            // Managed Identity only applies when using an Azure service
            if (HasOption(args, "--managed-identity") && !HasOption(args, "--provider", "azureopenai"))
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
        string projectName = GetProjectNameForArgs(args, prefix: "AIAgentWebApi");
        await CreateRestoreAndBuild(projectName, args);
    }
}
