// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Storage.Files.DataLake;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

/// <summary>
/// Verifies that the Azure Storage backed <see cref="IEvaluationResultStore"/> and
/// <see cref="IDistributedCache"/> implementations reject caller-supplied names that are not valid single
/// path segments (mirroring the validation performed by the disk based implementations). These tests do not
/// require a configured Azure Storage account because validation occurs before any network request is issued.
/// </summary>
public class AzureStoragePathValidationTests
{
    // A DataLakeDirectoryClient constructed from a bare Uri is anonymous and never contacts the network in
    // these tests, since validation throws before any request is built.
    private static DataLakeDirectoryClient CreateDummyClient()
        => new(new Uri("https://account.dfs.core.windows.net/container/root"));

    public static IEnumerable<object[]> InvalidSegments()
    {
        yield return new object[] { ".." };
        yield return new object[] { "." };
        yield return new object[] { "../x" };
        yield return new object[] { "../../x" };
        yield return new object[] { "foo/bar" };
        yield return new object[] { " leading" };
        yield return new object[] { "trailing " };
    }

    private static ScenarioRunResult CreateResult(string scenarioName, string iterationName, string executionName)
        => new(
            scenarioName: scenarioName,
            iterationName: iterationName,
            executionName: executionName,
            creationTime: DateTime.UtcNow,
            messages: [new ChatMessage(ChatRole.User, "User prompt")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "LLM response")),
            evaluationResult: new EvaluationResult(new BooleanMetric("boolean", value: true)));

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task WriteResultsAsync_InvalidExecutionName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());
        ScenarioRunResult result = CreateResult("scenario", "iteration", segment);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.WriteResultsAsync([result]));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task WriteResultsAsync_InvalidScenarioName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());
        ScenarioRunResult result = CreateResult(segment, "iteration", "execution");

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.WriteResultsAsync([result]));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task WriteResultsAsync_InvalidIterationName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());
        ScenarioRunResult result = CreateResult("scenario", segment, "execution");

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.WriteResultsAsync([result]));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task ReadResultsAsync_InvalidExecutionName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (ScenarioRunResult result in store.ReadResultsAsync(segment))
            {
                _ = result;
            }
        });
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task DeleteResultsAsync_InvalidExecutionName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.DeleteResultsAsync(segment));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task GetScenarioNamesAsync_InvalidExecutionName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (string scenarioName in store.GetScenarioNamesAsync(segment))
            {
                _ = scenarioName;
            }
        });
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task GetIterationNamesAsync_InvalidScenarioName_Throws(string segment)
    {
        var store = new AzureStorageResultStore(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (string iterationName in store.GetIterationNamesAsync("execution", segment))
            {
                _ = iterationName;
            }
        });
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task GetCacheAsync_InvalidScenarioName_Throws(string segment)
    {
        var provider = new AzureStorageResponseCacheProvider(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await provider.GetCacheAsync(segment, "iteration"));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task GetCacheAsync_InvalidIterationName_Throws(string segment)
    {
        var provider = new AzureStorageResponseCacheProvider(CreateDummyClient());

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await provider.GetCacheAsync("scenario", segment));
    }

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public async Task Cache_InvalidKey_Throws(string segment)
    {
        var provider = new AzureStorageResponseCacheProvider(CreateDummyClient());
        IDistributedCache cache = await provider.GetCacheAsync("scenario", "iteration");

        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.GetAsync(segment));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetAsync(segment, [1, 2, 3], new DistributedCacheEntryOptions()));
        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.RemoveAsync(segment));
        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.RefreshAsync(segment));

        Assert.Throws<ArgumentException>(() => cache.Get(segment));
        Assert.Throws<ArgumentException>(
            () => cache.Set(segment, [1, 2, 3], new DistributedCacheEntryOptions()));
        Assert.Throws<ArgumentException>(() => cache.Remove(segment));
        Assert.Throws<ArgumentException>(() => cache.Refresh(segment));
    }
}
