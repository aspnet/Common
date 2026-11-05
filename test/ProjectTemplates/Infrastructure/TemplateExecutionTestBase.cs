// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Shared.ProjectTemplates.Tests;

/// <summary>
/// Represents a test that executes a project template (create, restore, build, and run).
/// </summary>
/// <typeparam name="TConfiguration">A type defining global test execution settings.</typeparam>
[Collection(TemplateExecutionTestCollection.Name)]
public abstract class TemplateExecutionTestBase<TConfiguration> : IClassFixture<TemplateExecutionTestBase<TConfiguration>.TemplateExecutionTestFixture>, IDisposable
    where TConfiguration : ITemplateExecutionTestConfigurationProvider
{
    private bool _disposed;

    protected TemplateExecutionTestFixture Fixture { get; }

    protected ITestOutputHelper OutputHelper { get; }

    protected TemplateExecutionTestBase(TemplateExecutionTestFixture fixture, ITestOutputHelper outputHelper)
    {
        Fixture = fixture;
        Fixture.SetCurrentTestOutputHelper(outputHelper);

        OutputHelper = outputHelper;
    }

    protected async Task CreateRestoreAndBuild(string projectName, string[] args, string? startupProjectRelativePath = null)
    {
        var project = await Fixture.CreateProjectAsync(
            TConfiguration.Configuration.TemplateName,
            projectName,
            startupProjectRelativePath,
            args);

        await Fixture.RestoreProjectAsync(project);
        await Fixture.BuildProjectAsync(project);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            Fixture.SetCurrentTestOutputHelper(null);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// An implementation of <see cref="TemplateExecutionTestClassFixtureBase"/> that utilizes
    /// the configuration provided by <c>TConfiguration</c>.
    /// </summary>
    /// <remarks>
    /// The configuration has to be provided "statically" because the lifetime of the class fixture
    /// is longer than the lifetime of each test class instance. In other words, it's not possible for
    /// an instance of the test class to configure to the fixture directly, as the test class instance
    /// gets created after the fixture has a chance to perform global setup.
    /// </remarks>
    /// <param name="messageSink">The <see cref="IMessageSink"/>The <see cref="IMessageSink"/>.</param>
    public sealed class TemplateExecutionTestFixture(IMessageSink messageSink)
        : TemplateExecutionTestClassFixtureBase(TConfiguration.Configuration, messageSink);
}
