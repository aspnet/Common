// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience.Internal;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Microsoft.Extensions.Http.Resilience.Test.Internal;

public class RetryBudgetTests
{
    private static readonly TimeSpan _window = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _bucket = TimeSpan.FromTicks(_window.Ticks / RetryBudget.BucketCount);

    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public void BelowMinimumThroughput_AllowsAllRetries()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 9);

        CountAllowedRetries(budget, 100).Should().Be(100);
    }

    [Fact]
    public void NoRequests_AllowsRetries()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 1);

        budget.TryAcquireRetry().Should().BeTrue();
    }

    [Theory]
    [InlineData(0.1, 100, 10)]
    [InlineData(0.1, 1000, 100)]
    [InlineData(0.5, 100, 50)]
    [InlineData(1.0, 100, 100)]
    [InlineData(0.15, 99, 14)]
    public void AboveMinimumThroughput_EnforcesRatio(double retryRatio, int requests, int expectedRetries)
    {
        var budget = CreateBudget(retryRatio, minimumThroughput: 10);
        RecordRequests(budget, requests);

        CountAllowedRetries(budget, requests * 2).Should().Be(expectedRetries);
    }

    [Theory]
    [InlineData(0.01, 10)]
    [InlineData(0.01, 99)]
    [InlineData(0.001, 500)]
    public void SmallRatio_AllowsAtLeastOneRetry(double retryRatio, int requests)
    {
        var budget = CreateBudget(retryRatio, minimumThroughput: 10);
        RecordRequests(budget, requests);

        CountAllowedRetries(budget, 10).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public void ZeroRatio_DeniesAllRetries_RegardlessOfThroughput(int requests)
    {
        var budget = CreateBudget(retryRatio: 0, minimumThroughput: 100);
        RecordRequests(budget, requests);

        budget.TryAcquireRetry().Should().BeFalse();
    }

    [Fact]
    public void RetriesAddedBelowMinimumThroughput_CountOnceThroughputIsReached()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 5);
        CountAllowedRetries(budget, 5).Should().Be(5);

        RecordRequests(budget, 45);

        // 50 requests allow 5 retries, all of which were already used below the minimum throughput.
        budget.TryAcquireRetry().Should().BeFalse();
    }

    [Fact]
    public void SlidingWindow_ExpiresOldBuckets()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        // Still within the window: requests and retries from the first bucket are counted.
        _timeProvider.Advance(_window - _bucket);
        budget.TryAcquireRetry().Should().BeFalse();

        // The first bucket ages out; no requests remain, so the budget isn't enforced.
        _timeProvider.Advance(_bucket);
        budget.TryAcquireRetry().Should().BeTrue();
    }

    [Fact]
    public void SlidingWindow_CountsRequestsAcrossBuckets()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);

        for (int i = 0; i < RetryBudget.BucketCount; i++)
        {
            RecordRequests(budget, 10);
            _timeProvider.Advance(_bucket);
        }

        // The first bucket has aged out, 90 requests remain.
        CountAllowedRetries(budget, 100).Should().Be(9);
    }

    [Fact]
    public void SlidingWindow_ReusesBucketAfterFullRotation()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        // Same bucket index, one full window later: the stale counts must be reset, not added to.
        _timeProvider.Advance(_window);
        RecordRequests(budget, 20);

        CountAllowedRetries(budget, 20).Should().Be(2);
    }

    [Fact]
    public void SlidingWindow_HandlesLongIdlePeriods()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        _timeProvider.Advance(TimeSpan.FromDays(400));
        RecordRequests(budget, 10);

        CountAllowedRetries(budget, 20).Should().Be(1);
    }

    [Fact]
    public void SlidingWindow_HandlesEpochWrapAround()
    {
        // With a 1 second window a bucket is 100 ms, so the 32-bit epoch wraps after roughly 13.6 years.
        var window = TimeSpan.FromSeconds(1);
        var bucket = TimeSpan.FromTicks(window.Ticks / RetryBudget.BucketCount);
        var budget = new RetryBudget(0.1, window, 10, _timeProvider);

        _timeProvider.Advance(TimeSpan.FromTicks(bucket.Ticks * (((long)uint.MaxValue) - 1)));
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        // Crosses the wrap-around while the previous buckets are still in the window.
        _timeProvider.Advance(TimeSpan.FromTicks(bucket.Ticks * 3));
        budget.TryAcquireRetry().Should().BeFalse();

        _timeProvider.Advance(window);
        RecordRequests(budget, 10);
        CountAllowedRetries(budget, 20).Should().Be(1);
    }

    [Fact]
    public void Increment_SaturatesInsteadOfOverflowingIntoEpoch()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        _timeProvider.Advance(TimeSpan.FromTicks(_bucket.Ticks * 3));

        var counters = (long[])typeof(RetryBudget).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!;
        const long CountMask = 0xFFFF_FFFF;
        const long Epoch = 3;
        var index = (int)(Epoch % RetryBudget.BucketCount) * 2;

        foreach (var start in new[] { CountMask - 1, (CountMask >> 1) - 1 })
        {
            counters[index] = (Epoch << 32) | start;

            RecordRequests(budget, 3);

            var expected = Math.Min(start + 3, CountMask);
            counters[index].Should().Be((Epoch << 32) | expected);
        }
    }

    [Fact]
    public void Increment_DelayedWriter_DoesNotResetNewerEpoch()
    {
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);

        // A writer reads epoch 0, then pauses for a full window while other writers reuse the bucket for epoch 10.
        _timeProvider.Advance(_window);
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        budget.Increment(0, RetryBudget.RequestsOffset);
        budget.Increment(0, RetryBudget.RetriesOffset);

        // The exhausted budget stays exhausted, and the delayed samples count toward the newer epoch.
        budget.TryAcquireRetry().Should().BeFalse();
        var counters = (long[])typeof(RetryBudget).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!;
        counters[RetryBudget.RequestsOffset].Should().Be((10L << 32) | 101);
        counters[RetryBudget.RetriesOffset].Should().Be((10L << 32) | 11);
    }

    [Fact]
    public void Increment_DelayedWriter_DoesNotResetNewerEpochAcrossWrapAround()
    {
        var window = TimeSpan.FromSeconds(1);
        var bucket = TimeSpan.FromTicks(window.Ticks / RetryBudget.BucketCount);
        var budget = new RetryBudget(0.1, window, 10, _timeProvider);

        // The delayed writer read epoch uint.MaxValue - 1 (bucket 4); the bucket was then reused for epoch 14 after the wrap.
        _timeProvider.Advance(TimeSpan.FromTicks(bucket.Ticks * (((long)uint.MaxValue) + 15)));
        RecordRequests(budget, 100);
        CountAllowedRetries(budget, 20).Should().Be(10);

        budget.Increment(uint.MaxValue - 1, RetryBudget.RequestsOffset);

        budget.TryAcquireRetry().Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentRecordRequest_DoesNotLoseUpdates()
    {
        const int Threads = 8;
        const int RequestsPerThread = 10_000;
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);

        using var start = new Barrier(Threads);
        await RunOnDedicatedThreadsAsync(Threads, () =>
        {
            start.SignalAndWait();
            RecordRequests(budget, RequestsPerThread);
        });

        CountAllowedRetries(budget, Threads * RequestsPerThread).Should().Be(Threads * RequestsPerThread / 10);
    }

    [Fact]
    public async Task ConcurrentTryAcquireRetry_StaysCloseToRatio()
    {
        const int Threads = 8;
        const int AttemptsPerThread = 1_000;
        var budget = CreateBudget(retryRatio: 0.1, minimumThroughput: 10);
        RecordRequests(budget, 1000);

        var allowed = 0;
        using var start = new Barrier(Threads);
        await RunOnDedicatedThreadsAsync(Threads, () =>
        {
            start.SignalAndWait();
            Interlocked.Add(ref allowed, CountAllowedRetries(budget, AttemptsPerThread));
        });

        // The check and the increment aren't atomic, so each thread can overshoot by at most one retry.
        allowed.Should().BeInRange(100, 100 + Threads);
    }

    // Blocking thread-pool threads on a barrier starves tests that run in parallel, so each participant gets its own thread.
    private static Task RunOnDedicatedThreadsAsync(int threads, Action action)
        => Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Factory.StartNew(
            action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));

    private static void RecordRequests(RetryBudget budget, int count)
    {
        for (int i = 0; i < count; i++)
        {
            budget.RecordRequest();
        }
    }

    private static int CountAllowedRetries(RetryBudget budget, int attempts)
    {
        var allowed = 0;

        for (int i = 0; i < attempts; i++)
        {
            if (budget.TryAcquireRetry())
            {
                allowed++;
            }
        }

        return allowed;
    }

    private RetryBudget CreateBudget(double retryRatio, int minimumThroughput)
        => new(retryRatio, _window, minimumThroughput, _timeProvider);
}
