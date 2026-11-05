// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.Logging.Testing.Test.Logging;

public partial class FakeLogCollectorTests
{
    private readonly ITestOutputHelper _outputHelper;

    public FakeLogCollectorTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetLogsAsync_EnumeratesNewLogsAsynchronouslyWithCancellationSupport(bool isWaitCancelled)
    {
        var fakeLogCollector = FakeLogCollector.Create(new FakeLogCollectorOptions());
        var logger = new FakeLogger(fakeLogCollector);
        var eventTracker = new ConcurrentQueue<string>();

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var awaitSequenceTask = AwaitSequence(
            new Queue<string>(["Log A", "Log B", "Sync"]), // Wait for event A and B followed by Sync
            fromIndex: 0,
            fakeLogCollector,
            eventTracker,
            cancellationToken: cancellationToken);

        EmitLogs(logger, ["Sync", "Log A", "Log C", "Sync", "Sync", "Log B", "Sync", "Sync"], eventTracker);

        await AssertAwaitingTaskCompleted(awaitSequenceTask);

        var res = await awaitSequenceTask;

        Assert.False(res.wasCancelled);
        Assert.Equal(6, res.index);

        awaitSequenceTask = AwaitSequence(
            new Queue<string>(["Log C", "Sync"]), // Wait for another Log C followed by Sync
            fromIndex: res.index + 1, // Starting from previously asserted state
            fakeLogCollector,
            eventTracker,
            cancellationToken: cancellationToken);

        if (isWaitCancelled)
        {
            cts.Cancel();
        }
        else
        {
            EmitLogs(logger, ["Log C", "Sync"], eventTracker);
        }

        await AssertAwaitingTaskCompleted(awaitSequenceTask);

        res = await awaitSequenceTask;
        Assert.Equal(isWaitCancelled, res.wasCancelled);
        Assert.Equal(isWaitCancelled ? -1 : 9, res.index);

        if (!isWaitCancelled)
        {
            // The user may want to await partial states, but then perform a sanity check on the whole expected history
            var snapshot = fakeLogCollector.GetSnapshot().Select(x => x.Message);
            var containsSequence = ContainsNonContinuousSequence(snapshot, new Queue<string>(["Log A", "Log B", "Sync", "Log C", "Sync"]));
            Assert.True(containsSequence);
        }

        OutputEventTracker(_outputHelper, eventTracker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetLogsAsync_RegardlessOfClearDuringWait_SuppliesNextLogWhenRecorded(bool clearIsCalledDuringWait)
    {
        var fakeLogCollector = FakeLogCollector.Create(new FakeLogCollectorOptions());
        var logger = new FakeLogger(fakeLogCollector);
        int moveNextCounter = 0;

        var abSequenceTask = AwaitSequence(
            new Queue<string>(["A", "B"]),
            fromIndex: 0,
            fakeLogCollector,
            null,
            cancellationToken: CancellationToken.None);

        var abcProcessedAB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var abcSequenceTask = AwaitSequence(
            new Queue<string>(["A", "B", "C"]),
            fromIndex: 0,
            fakeLogCollector,
            null,
            cancellationToken: CancellationToken.None,
            () =>
            {
                if (Interlocked.Increment(ref moveNextCounter) == 2)
                {
                    abcProcessedAB.TrySetResult(true);
                }
            });

        EmitLogs(logger, ["A", "B"], null);
        await AssertAwaitingTaskCompleted(abSequenceTask); // checkpoint to not clear, before A, B is processed
        await AssertAwaitingTaskCompleted(abcProcessedAB.Task); // ensure enumerator #2 has also consumed A and B

        if (clearIsCalledDuringWait)
        {
            fakeLogCollector.Clear();
        }

        EmitLogs(logger, ["C"], null);
        await AssertAwaitingTaskCompleted(abcSequenceTask);
        Assert.Equal(3, moveNextCounter);
    }

    private static async Task AssertAwaitingTaskCompleted(Task task)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
#pragma warning disable VSTHRD003
        var finishedTask = await Task.WhenAny(task, timeout);
#pragma warning restore VSTHRD003

        // Assert our tested task finished before the timeout
        Assert.Equal(finishedTask, task);
    }

    private static bool ContainsNonContinuousSequence(IEnumerable<string> orderedEnumeration, Queue<string> sequence)
    {
        foreach (var item in orderedEnumeration)
        {
            if (sequence.Count == 0)
            {
                break;
            }

            if (item == sequence.Peek())
            {
                sequence.Dequeue();
            }
        }

        return sequence.Count == 0;
    }

    private static async Task<(bool wasCancelled, int index)> AwaitSequence(
        Queue<string> sequence,
        int fromIndex,
        FakeLogCollector collector,
        ConcurrentQueue<string>? eventTracker,
        CancellationToken cancellationToken,
        Action? onMoveNextCalled = null)
    {
        eventTracker?.Enqueue("New sequence awaiter started at " + DateTime.Now + $", waiting for items: {string.Join(", ", sequence)} from index {fromIndex}.");

        try
        {
            int index = -1;
            var enumeration = collector.GetLogsAsync(cancellationToken: cancellationToken);
            await foreach (var log in enumeration)
            {
                onMoveNextCalled?.Invoke();
                index++;

                if (index < fromIndex)
                {
                    continue;
                }

                var msg = log.Message;
                var currentExpectation = sequence.Peek();

                eventTracker?.Enqueue($"Sequence awaiter checks log: \"{msg}\".");

                if (msg == currentExpectation)
                {
                    sequence.Dequeue();

                    if (sequence.Count != 0)
                    {
                        continue;
                    }

                    eventTracker?.Enqueue($"Sequence awaiter satisfied at {DateTime.Now}");
                    return (false, index);
                }
            }
        }
        catch (OperationCanceledException)
        {
            eventTracker?.Enqueue($"Sequence awaiter cancelled at {DateTime.Now}");
            return (true, -1);
        }

        throw new InvalidOperationException("Enumeration was supposed to be unbound.");
    }

    private static void OutputEventTracker(ITestOutputHelper testOutputHelper, ConcurrentQueue<string> eventTracker)
    {
        while (eventTracker.TryDequeue(out var item))
        {
            testOutputHelper.WriteLine(item);
        }
    }

    private static void EmitLogs(
        FakeLogger logger,
        IEnumerable<string> logsToEmit,
        ConcurrentQueue<string>? eventTracker)
    {
        foreach (var log in logsToEmit)
        {
            eventTracker?.Enqueue($"Emitting log: \"{log}\" at {DateTime.Now}, current log count: {logger.Collector.Count}");
            logger.Log(LogLevel.Debug, log);
        }
    }
}
