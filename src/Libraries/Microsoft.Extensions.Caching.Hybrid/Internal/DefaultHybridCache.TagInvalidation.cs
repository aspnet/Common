// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    private static readonly Task<long> _zeroTimestamp = Task.FromResult<long>(0L);

    private readonly ConcurrentDictionary<string, Task<long>> _tagInvalidationTimes = [];

#if NET9_0_OR_GREATER
    private readonly ConcurrentDictionary<string, Task<long>>.AlternateLookup<ReadOnlySpan<char>> _tagInvalidationTimesBySpan;
    private readonly bool _tagInvalidationTimesUseAltLookup;
#endif

    private Task<long> _globalInvalidateTimestamp;

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return default; // nothing sensible to do
        }

        long now = CurrentTimestamp();
        InvalidateTagLocalCore(tag, now, isNow: true); // isNow to be 100% explicit
        return InvalidateL2TagAsync(tag, now, token);
    }

    public bool IsValid(CacheItem cacheItem)
    {
        long timestamp = cacheItem.CreationTimestamp;

        if (IsWildcardExpired(timestamp))
        {
            return false;
        }

        TagSet tags = cacheItem.Tags;
        switch (tags.Count)
        {
            case 0:
                return true;

            case 1:
                return !IsTagExpired(tags.GetSinglePrechecked(), timestamp, out _);

            default:
                bool allValid = true;
                foreach (string tag in tags.GetSpanPrechecked())
                {
                    if (IsTagExpired(tag, timestamp, out _))
                    {
                        allValid = false; // but check them all, to kick-off tag fetch
                    }
                }

                return allValid;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Completion-checked")]
    public bool IsWildcardExpired(long timestamp)
    {
        if (_globalInvalidateTimestamp.IsCompleted)
        {
            if (timestamp <= _globalInvalidateTimestamp.Result)
            {
                return true;
            }
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Completion-checked")]
    public bool IsTagExpired(ReadOnlySpan<char> tag, long timestamp, out bool isPending)
    {
        isPending = false;
#if NET9_0_OR_GREATER
        if (_tagInvalidationTimesUseAltLookup && _tagInvalidationTimesBySpan.TryGetValue(tag, out var pending))
        {
            if (pending.IsCompleted)
            {
                return timestamp <= pending.Result;
            }
            else
            {
                isPending = true;
                return true; // assume invalid until completed
            }
        }
        else if (!HasBackendCache)
        {
            // not invalidated, and no L2 to check
            return false;
        }
#endif

        // fallback to using a string
        return IsTagExpired(tag.ToString(), timestamp, out isPending);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Completion-checked")]
    public bool IsTagExpired(string tag, long timestamp, out bool isPending)
    {
        isPending = false;
        if (!_tagInvalidationTimes.TryGetValue(tag, out Task<long>? pending))
        {
            // not in the tag invalidation cache; if we have L2, need to check there
            if (HasBackendCache)
            {
                pending = SafeReadTagInvalidationAsync(tag);
                _ = _tagInvalidationTimes.TryAdd(tag, pending);
            }
            else
            {
                // not invalidated, and no L2 to check
                return false;
            }
        }

        if (pending.IsCompleted)
        {
            return timestamp <= pending.Result;
        }
        else
        {
            isPending = true;
            return true; // assume invalid until completed
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Ack")]
    public ValueTask<bool> IsAnyTagExpiredAsync(TagSet tags, long timestamp)
    {
        return tags.Count switch
        {
            0 => new(false),
            1 => IsTagExpiredAsync(tags.GetSinglePrechecked(), timestamp),
            _ => SlowAsync(this, tags, timestamp),
        };

        static async ValueTask<bool> SlowAsync(DefaultHybridCache @this, TagSet tags, long timestamp)
        {
            int count = tags.Count;
            for (int i = 0; i < count; i++)
            {
                if (await @this.IsTagExpiredAsync(tags[i], timestamp).ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Ack")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Completion-checked")]
    public ValueTask<bool> IsTagExpiredAsync(string tag, long timestamp)
    {
        if (!_tagInvalidationTimes.TryGetValue(tag, out Task<long>? pending))
        {
            // not in the tag invalidation cache; if we have L2, need to check there
            if (HasBackendCache)
            {
                pending = SafeReadTagInvalidationAsync(tag);
                _ = _tagInvalidationTimes.TryAdd(tag, pending);
            }
            else
            {
                // not invalidated, and no L2 to check
                return new(false);
            }
        }

        if (pending.IsCompleted)
        {
            return new(timestamp <= pending.Result);
        }
        else
        {
            return AwaitedAsync(pending, timestamp);
        }

        static async ValueTask<bool> AwaitedAsync(Task<long> pending, long timestamp) => timestamp <= await pending.ConfigureAwait(false);
    }

    internal static TimeSpan TicksToTimeSpan(long ticks) => TimeSpan.FromTicks(ticks);

    internal long CurrentTimestamp() => _clock.GetUtcNow().UtcTicks;

    internal void DebugInvalidateTag(string tag, Task<long> pending)
    {
        if (tag == TagSet.WildcardTag)
        {
            _globalInvalidateTimestamp = pending;
        }
        else
        {
            _tagInvalidationTimes[tag] = pending;
        }
    }

    internal void PrefetchTags(TagSet tags)
    {
        if (HasBackendCache && !tags.IsEmpty)
        {
            // only needed if L2 exists
            switch (tags.Count)
            {
                case 1:
                    PrefetchTagWithBackendCache(tags.GetSinglePrechecked());
                    break;
                default:
                    foreach (string tag in tags.GetSpanPrechecked())
                    {
                        PrefetchTagWithBackendCache(tag);
                    }

                    break;
            }
        }
    }

    private void PrefetchTagWithBackendCache(string tag)
    {
        if (!_tagInvalidationTimes.TryGetValue(tag, out Task<long>? pending))
        {
            _ = _tagInvalidationTimes.TryAdd(tag, SafeReadTagInvalidationAsync(tag));
        }
    }

    private void InvalidateTagLocalCore(string tag, long timestamp, bool isNow)
    {
        Task<long> timestampTask = Task.FromResult<long>(timestamp);
        if (tag == TagSet.WildcardTag)
        {
            _globalInvalidateTimestamp = timestampTask;
            if (isNow && !HasBackendCache)
            {
                // no L2, so we don't need any prior invalidated tags any more; can clear
                _tagInvalidationTimes.Clear();
            }
        }
        else
        {
            _tagInvalidationTimes[tag] = timestampTask;

            if (HybridCacheEventSource.Log.IsEnabled() || _options.ReportTagMetrics)
            {
                HybridCacheEventSource.Log.TagInvalidatedWithTags(tag, _options.ReportTagMetrics);
            }
        }
    }
}
