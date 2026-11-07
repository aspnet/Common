// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace Microsoft.Extensions.Http.Resilience.Internal;

/// <summary>
/// Tracks requests and retries over a sliding window and decides whether a retry is allowed.
/// </summary>
/// <remarks>
/// The window is split into <see cref="BucketCount"/> buckets. Each bucket has a request counter and a retry counter,
/// stored next to each other so that a decision reads them in a single pass. Each counter packs its epoch (upper 32 bits)
/// and its count (lower 32 bits) into a single <see cref="long"/>. While the epoch is current, the counter is updated with
/// a single atomic increment; a compare-and-swap is only needed to reset a counter for a new epoch.
/// A counter whose epoch is older than the window is lazily reset on the next write and ignored on reads. A delayed writer
/// never resets a counter that was already reused for a newer epoch; its increment lands in that newer epoch instead.
/// The check and the increment in <see cref="TryAcquireRetry"/> are not atomic, so concurrent retry decisions
/// can slightly overshoot the configured ratio. This is acceptable for a load-shedding heuristic.
/// </remarks>
internal sealed class RetryBudget
{
    internal const int BucketCount = 10;

    private const int EpochShift = 32;
    private const long CountMask = 0xFFFF_FFFF;

    // Below this count, a plain atomic increment can't carry into the epoch bits, even with many concurrent writers.
    private const long FastPathCountLimit = CountMask >> 1;

    internal const int RequestsOffset = 0;
    internal const int RetriesOffset = 1;
    private const int CountersPerBucket = 2;

    private readonly long[] _counters = new long[BucketCount * CountersPerBucket];
    private readonly TimeProvider _timeProvider;
    private readonly long _startTimestamp;
    private readonly long _bucketTicks;
    private readonly double _retryRatio;
    private readonly int _minimumThroughput;

    public RetryBudget(double retryRatio, TimeSpan samplingDuration, int minimumThroughput, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _retryRatio = retryRatio;
        _minimumThroughput = minimumThroughput;
        _startTimestamp = timeProvider.GetTimestamp();
        _bucketTicks = Math.Max(1, (long)(samplingDuration.TotalSeconds * timeProvider.TimestampFrequency / BucketCount));
    }

    public void RecordRequest() => Increment(GetCurrentEpoch(), RequestsOffset);

    public bool TryAcquireRetry()
    {
        // Checked before the minimum throughput so that a zero ratio reliably suppresses every retry,
        // including when traffic to a failing dependency has collapsed below the minimum.
        if (_retryRatio <= 0)
        {
            return false;
        }

        var epoch = GetCurrentEpoch();
        Sum(epoch, out var requests, out var retries);

        // The allowance is floored at one retry so that a small ratio doesn't turn the budget into a total block
        // for traffic just above the minimum throughput, where the ratio multiplied by the requests is below 1.
        if (requests >= _minimumThroughput && retries >= Math.Max(1, (long)(_retryRatio * requests)))
        {
            return false;
        }

        Increment(epoch, RetriesOffset);
        return true;
    }

    private static uint GetEpoch(long value) => (uint)((ulong)value >> EpochShift);

    private static long GetCount(long value) => value & CountMask;

    // Unsigned subtraction handles epoch wrap-around; future epochs (from a racing writer) wrap to large values and are skipped.
    private static long GetLiveCount(long value, uint epoch) => unchecked(epoch - GetEpoch(value)) < BucketCount ? GetCount(value) : 0;

    internal void Increment(uint epoch, int offset)
    {
        ref long counter = ref _counters[((int)(epoch % BucketCount) * CountersPerBucket) + offset];

        while (true)
        {
            var current = Volatile.Read(ref counter);
            var currentEpoch = GetEpoch(current);
            long count = 1;

            if (currentEpoch != epoch && IsNewer(currentEpoch, epoch))
            {
                // This writer was delayed long enough for another writer to reuse the bucket for a newer epoch.
                // Resetting it would discard that newer data, so the increment lands in the newer epoch instead.
                epoch = currentEpoch;
            }

            if (currentEpoch == epoch)
            {
                count = GetCount(current);

                if (count < FastPathCountLimit)
                {
                    // If a racing writer resets the counter for a newer epoch in between, this increment lands in that
                    // newer epoch, which is at least as recent, so the sample is still counted.
                    _ = Interlocked.Increment(ref counter);
                    return;
                }

                // Saturate instead of overflowing into the epoch bits.
                if (count == CountMask)
                {
                    return;
                }

                count++;
            }

            var updated = ((long)epoch << EpochShift) | count;

            if (Interlocked.CompareExchange(ref counter, updated, current) == current)
            {
                return;
            }
        }
    }

    // Distances are measured back from the current epoch with unsigned subtraction, so this handles wrap-around and
    // treats a counter that was never written (epoch 0) as older, however much time has passed.
    private bool IsNewer(uint epoch, uint than)
    {
        var now = GetCurrentEpoch();
        return unchecked(now - epoch) < unchecked(now - than);
    }

    private void Sum(uint epoch, out long requests, out long retries)
    {
        long requestSum = 0;
        long retrySum = 0;

        for (int i = 0; i < _counters.Length; i += CountersPerBucket)
        {
            requestSum += GetLiveCount(Volatile.Read(ref _counters[i + RequestsOffset]), epoch);
            retrySum += GetLiveCount(Volatile.Read(ref _counters[i + RetriesOffset]), epoch);
        }

        requests = requestSum;
        retries = retrySum;
    }

    private uint GetCurrentEpoch() => unchecked((uint)((_timeProvider.GetTimestamp() - _startTimestamp) / _bucketTicks));
}
