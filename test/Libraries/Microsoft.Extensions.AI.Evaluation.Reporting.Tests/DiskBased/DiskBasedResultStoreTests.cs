// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class DiskBasedResultStoreTests : ResultStoreTester, IAsyncLifetime
{
    private readonly List<string> _tempStorage = [];

    private string UseTempStoragePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "M.E.AI.Eval.ResultStoreTests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        _tempStorage.Add(path);
        return path;
    }

    public ValueTask InitializeAsync()
    {
#if NET
        return ValueTask.CompletedTask;
#else
        return new ValueTask(Task.CompletedTask);
#endif
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        foreach (string path in _tempStorage)
        {
            try
            {
                Directory.Delete(path, true);
            }
#pragma warning disable CA1031 // Do not catch general exception types.
            catch
#pragma warning restore CA1031
            {
                // Best effort delete, don't crash on exceptions.
            }
        }

#if NET
        return ValueTask.CompletedTask;
#else
        return new ValueTask(Task.CompletedTask);
#endif
    }

    public override bool IsConfigured => true;

    public override IEvaluationResultStore CreateResultStore()
        => new DiskBasedResultStore(UseTempStoragePath());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WriteAndReadResultsWithContext(int contentCount)
    {
        AIContent[] contents = [new TextContent("first"), new TextContent("second")];
        var context = new TestContext("reference", contents.Take(contentCount).ToArray());
        var metric = new NumericMetric("score", value: 1);
        metric.AddOrUpdateContext(context);

        var result = new ScenarioRunResult(
            scenarioName: "scenario",
            iterationName: "iteration",
            executionName: "execution",
            creationTime: DateTime.UtcNow,
            messages: [new ChatMessage(ChatRole.User, "prompt")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")),
            evaluationResult: new EvaluationResult(metric));

        IEvaluationResultStore store = CreateResultStore();
        await store.WriteResultsAsync([result]);

        List<ScenarioRunResult> restored = [];
        await foreach (var entry in store.ReadResultsAsync())
        {
            restored.Add(entry);
        }

        var restoredMetric = Assert.IsType<NumericMetric>(Assert.Single(restored).EvaluationResult.Metrics[metric.Name]);
        var restoredContext = Assert.Single(restoredMetric.Context!).Value;
        Assert.Equal(context.Name, restoredContext.Name);
        Assert.Equal(
            context.Contents.Select(c => Assert.IsType<TextContent>(c).Text),
            restoredContext.Contents.Select(c => Assert.IsType<TextContent>(c).Text));
    }

    private sealed class TestContext(string name, params AIContent[] contents)
        : EvaluationContext(name, contents);
}
