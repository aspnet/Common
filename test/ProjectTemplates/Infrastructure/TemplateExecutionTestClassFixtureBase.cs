// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Shared.ProjectTemplates.Tests;

/// <summary>
/// Provides functionality scoped to the duration of all the tests in a single test class
/// extending <see cref="TemplateExecutionTestBase{TConfiguration}"/>.
/// </summary>
public abstract class TemplateExecutionTestClassFixtureBase : IAsyncLifetime
{
    private readonly string _templatePackageName;
    private readonly string _sandboxPackages;
    private readonly string _sandboxOutput;
    private readonly string _sandboxInstallPath;
    private readonly string _sandboxProjectsPath;

    private readonly MessageSinkTestOutputHelper _messageSinkTestOutputHelper;
    private ITestOutputHelper? _currentTestOutputHelper;

    /// <summary>
    /// Gets the current preferred output helper.
    /// If a test is underway, the output will be associated with that test.
    /// Otherwise, the output will appear as a diagnostic message via <see cref="IMessageSink"/>.
    /// </summary>
    private ITestOutputHelper OutputHelper => _currentTestOutputHelper ?? _messageSinkTestOutputHelper;

    protected TemplateExecutionTestClassFixtureBase(TemplateExecutionTestConfiguration configuration, IMessageSink messageSink)
    {
        _messageSinkTestOutputHelper = new(messageSink);

        _templatePackageName = configuration.TemplatePackageName;
        _sandboxPackages = configuration.TemplateSandboxPackages;
        _sandboxOutput = configuration.TemplateSandboxOutput;

        _sandboxInstallPath = Path.Combine(_sandboxOutput, "install");
        _sandboxProjectsPath = Path.Combine(_sandboxOutput, "projects");
    }

    public async ValueTask InitializeAsync()
    {
        // Here, we clear execution test output from the previous test run, if it exists.
        // It's critical that this clearing happens *before* the tests start, *not* after they complete.
        //
        // This is because:
        // 1. This enables debugging the previous test run by building/running generated projects manually.
        // 2. The existence of a project.assets.json file on disk is what allows template content to get discovered
        //    for component governance reporting.
        // Copy the template sandbox infrastructure to the output location for use during tests
        CopySandboxDirectory(WellKnownPaths.TemplateSandboxSource, _sandboxOutput);

        var installResult = await new DotNetNewCommand("install", _templatePackageName)
            .WithWorkingDirectory(_sandboxInstallPath)
            .WithEnvironmentVariable("LOCAL_SHIPPING_PATH", WellKnownPaths.LocalShippingPackagesPath)
            .WithEnvironmentVariable("NUGET_PACKAGES", _sandboxPackages)
            .WithCustomHive(_sandboxInstallPath)
            .ExecuteAsync(OutputHelper);

        installResult.AssertSucceeded($"dotnet new install {_templatePackageName}");

        // Create the sub-directory for the generated projects
        Directory.CreateDirectory(_sandboxProjectsPath);
    }

    private static void CopySandboxDirectory(string sandboxSource, string testSandbox)
    {
        if (Directory.Exists(testSandbox))
        {
            Directory.Delete(testSandbox, recursive: true);
        }

        Directory.CreateDirectory(testSandbox);

        var source = new DirectoryInfo(sandboxSource);

        foreach (FileInfo file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(testSandbox, file.Name));
        }

        foreach (DirectoryInfo subDir in source.GetDirectories())
        {
            CopySandboxDirectory(subDir.FullName, Path.Combine(testSandbox, subDir.Name));
        }
    }

    public async Task<Project> CreateProjectAsync(string templateName, string projectName, string? startupProjectRelativePath, params string[] args)
    {
        var outputFolderPath = Path.Combine(_sandboxProjectsPath, projectName);

        ReadOnlySpan<string> dotNetNewCommandArgs = [
            templateName,
            "-o", outputFolderPath,
            "-n", projectName,
            "--no-update-check",
            .. args
        ];

        var testDescription = string.Join(' ', dotNetNewCommandArgs);

        var newProjectResult = await new DotNetNewCommand(dotNetNewCommandArgs)
            .WithWorkingDirectory(_sandboxProjectsPath)
            .WithCustomHive(_sandboxInstallPath)
            .ExecuteAsync(OutputHelper);

        newProjectResult.AssertSucceeded(testDescription);

        return new Project(outputFolderPath, projectName) { StartupProjectRelativePath = startupProjectRelativePath };
    }

    public async Task RestoreProjectAsync(Project project)
    {
        var restoreResult = await new DotNetCommand("restore")
            .WithWorkingDirectory(project.StartupProjectFullPath)
            .WithEnvironmentVariable("LOCAL_SHIPPING_PATH", WellKnownPaths.LocalShippingPackagesPath)
            .WithEnvironmentVariable("NUGET_PACKAGES", _sandboxPackages)
            .ExecuteAsync(OutputHelper);

        restoreResult.AssertSucceeded($"""
            dotnet restore

            Working Directory: {project.StartupProjectFullPath}
            Local Shipping Path: {WellKnownPaths.LocalShippingPackagesPath}
            NuGet Packages Path: {_sandboxPackages}
            """);
    }

    public async Task BuildProjectAsync(Project project)
    {
        var buildResult = await new DotNetCommand("build", "--no-restore")
            .WithWorkingDirectory(project.StartupProjectFullPath)
            .ExecuteAsync(OutputHelper);

        buildResult.AssertSucceeded($"""
            dotnet build --no-restore

            Working Directory: {project.StartupProjectFullPath}
            """);
    }

    public void SetCurrentTestOutputHelper(ITestOutputHelper? outputHelper)
    {
        if (_currentTestOutputHelper is not null && outputHelper is not null)
        {
            throw new InvalidOperationException(
                "Cannot set the template execution test output helper when one is already present. " +
                "This might be a sign that template execution tests are running in parallel, " +
                "which is not currently supported.");
        }

        _currentTestOutputHelper = outputHelper;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        // Only here to implement IAsyncLifetime. Not currently used.
        return ValueTask.CompletedTask;
    }
}
