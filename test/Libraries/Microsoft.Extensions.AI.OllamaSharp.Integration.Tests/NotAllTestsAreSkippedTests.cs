// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.Extensions.AI;

/// <summary>
/// We need this test to ensure that not all tests in this project are skipped.
/// When all tests are skipped, Microsoft.Testing.Platform exits with code 8 ("zero tests ran")
/// which is treated as a failure in CI. This test guarantees at least one test always executes.
/// </summary>
public class NotAllTestsAreSkippedTests
{
    [Fact]
    public void NotAllTestsAreSkipped()
    {
        if (TestRunnerConfiguration.Instance["Ollama:Endpoint"] is string endpoint)
        {
            Assert.NotNull(IntegrationTestHelpers.GetOllamaUri());
        }
    }
}
