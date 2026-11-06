// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

[EventSource(Name = "Microsoft-Extensions-HybridCache")]
internal sealed class HybridCacheEventSource : EventSource
{
    public static readonly HybridCacheEventSource Log = new();

    // System.Diagnostics.Metrics instruments for tag-aware metrics
    private static readonly Meter _sMeter = new("Microsoft.Extensions.Caching.Hybrid");
    private static readonly Counter<long> _sLocalCacheHits = _sMeter.CreateCounter<long>("hybrid_cache.local.hits", description: "Total number of local cache hits");
    private static readonly Counter<long> _sLocalCacheMisses = _sMeter.CreateCounter<long>("hybrid_cache.local.misses", description: "Total number of local cache misses");
    private static readonly Counter<long> _sDistributedCacheHits = _sMeter.CreateCounter<long>("hybrid_cache.distributed.hits", description: "Total number of distributed cache hits");
    private static readonly Counter<long> _sDistributedCacheMisses = _sMeter.CreateCounter<long>("hybrid_cache.distributed.misses", description: "Total number of distributed cache misses");
    private static readonly Counter<long> _sLocalCacheWrites = _sMeter.CreateCounter<long>("hybrid_cache.local.writes", description: "Total number of local cache writes");
    private static readonly Counter<long> _sDistributedCacheWrites = _sMeter.CreateCounter<long>("hybrid_cache.distributed.writes", description: "Total number of distributed cache writes");
    private static readonly Counter<long> _sTagInvalidations = _sMeter.CreateCounter<long>("hybrid_cache.tag.invalidations", description: "Total number of tag invalidations");

    internal const int EventIdLocalCacheHit = 1;
    internal const int EventIdLocalCacheMiss = 2;
    internal const int EventIdDistributedCacheGet = 3;
    internal const int EventIdDistributedCacheHit = 4;
    internal const int EventIdDistributedCacheMiss = 5;
    internal const int EventIdDistributedCacheFailed = 6;
    internal const int EventIdUnderlyingDataQueryStart = 7;
    internal const int EventIdUnderlyingDataQueryComplete = 8;
    internal const int EventIdUnderlyingDataQueryFailed = 9;
    internal const int EventIdLocalCacheWrite = 10;
    internal const int EventIdDistributedCacheWrite = 11;
    internal const int EventIdStampedeJoin = 12;
    internal const int EventIdUnderlyingDataQueryCanceled = 13;
    internal const int EventIdDistributedCacheCanceled = 14;
    internal const int EventIdTagInvalidated = 15;

    // fast local counters
    private long _totalLocalCacheHit;
    private long _totalLocalCacheMiss;
    private long _totalDistributedCacheHit;
    private long _totalDistributedCacheMiss;
    private long _totalUnderlyingDataQuery;
    private long _currentUnderlyingDataQuery;
    private long _currentDistributedFetch;
    private long _totalLocalCacheWrite;
    private long _totalDistributedCacheWrite;
    private long _totalStampedeJoin;
    private long _totalTagInvalidations;

#if !(NETSTANDARD2_0 || NET462)
    // full Counter infrastructure
    private DiagnosticCounter[]? _counters;
#endif

    [NonEvent]
    public void ResetCounters()
    {
        Debug.WriteLine($"{nameof(HybridCacheEventSource)} counters reset!");

        Volatile.Write(ref _totalLocalCacheHit, 0);
        Volatile.Write(ref _totalLocalCacheMiss, 0);
        Volatile.Write(ref _totalDistributedCacheHit, 0);
        Volatile.Write(ref _totalDistributedCacheMiss, 0);
        Volatile.Write(ref _totalUnderlyingDataQuery, 0);
        Volatile.Write(ref _currentUnderlyingDataQuery, 0);
        Volatile.Write(ref _currentDistributedFetch, 0);
        Volatile.Write(ref _totalLocalCacheWrite, 0);
        Volatile.Write(ref _totalDistributedCacheWrite, 0);
        Volatile.Write(ref _totalStampedeJoin, 0);
        Volatile.Write(ref _totalTagInvalidations, 0);
    }

    [Event(EventIdLocalCacheHit, Level = EventLevel.Verbose)]
    public void LocalCacheHit()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalLocalCacheHit);
        WriteEvent(EventIdLocalCacheHit);
    }

    [Event(EventIdLocalCacheMiss, Level = EventLevel.Verbose)]
    public void LocalCacheMiss()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalLocalCacheMiss);
        WriteEvent(EventIdLocalCacheMiss);
    }

    [Event(EventIdDistributedCacheGet, Level = EventLevel.Verbose)]
    public void DistributedCacheGet()
    {
        // should be followed by DistributedCacheHit, DistributedCacheMiss or DistributedCacheFailed
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _currentDistributedFetch);
        WriteEvent(EventIdDistributedCacheGet);
    }

    [Event(EventIdDistributedCacheHit, Level = EventLevel.Verbose)]
    public void DistributedCacheHit()
    {
        DebugAssertEnabled();

        // note: not concerned about off-by-one here, i.e. don't panic
        // about these two being atomic ref each-other - just the overall shape
        _ = Interlocked.Increment(ref _totalDistributedCacheHit);
        _ = Interlocked.Decrement(ref _currentDistributedFetch);
        WriteEvent(EventIdDistributedCacheHit);
    }

    [Event(EventIdDistributedCacheMiss, Level = EventLevel.Verbose)]
    public void DistributedCacheMiss()
    {
        DebugAssertEnabled();

        // note: not concerned about off-by-one here, i.e. don't panic
        // about these two being atomic ref each-other - just the overall shape
        _ = Interlocked.Increment(ref _totalDistributedCacheMiss);
        _ = Interlocked.Decrement(ref _currentDistributedFetch);
        WriteEvent(EventIdDistributedCacheMiss);
    }

    [Event(EventIdDistributedCacheFailed, Level = EventLevel.Error)]
    public void DistributedCacheFailed()
    {
        DebugAssertEnabled();
        _ = Interlocked.Decrement(ref _currentDistributedFetch);
        WriteEvent(EventIdDistributedCacheFailed);
    }

    [Event(EventIdDistributedCacheCanceled, Level = EventLevel.Verbose)]
    public void DistributedCacheCanceled()
    {
        DebugAssertEnabled();
        _ = Interlocked.Decrement(ref _currentDistributedFetch);
        WriteEvent(EventIdDistributedCacheCanceled);
    }

    [Event(EventIdUnderlyingDataQueryStart, Level = EventLevel.Verbose)]
    public void UnderlyingDataQueryStart()
    {
        // should be followed by UnderlyingDataQueryComplete or UnderlyingDataQueryFailed
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalUnderlyingDataQuery);
        _ = Interlocked.Increment(ref _currentUnderlyingDataQuery);
        WriteEvent(EventIdUnderlyingDataQueryStart);
    }

    [Event(EventIdUnderlyingDataQueryComplete, Level = EventLevel.Verbose)]
    public void UnderlyingDataQueryComplete()
    {
        DebugAssertEnabled();
        _ = Interlocked.Decrement(ref _currentUnderlyingDataQuery);
        WriteEvent(EventIdUnderlyingDataQueryComplete);
    }

    [Event(EventIdUnderlyingDataQueryFailed, Level = EventLevel.Error)]
    public void UnderlyingDataQueryFailed()
    {
        DebugAssertEnabled();
        _ = Interlocked.Decrement(ref _currentUnderlyingDataQuery);
        WriteEvent(EventIdUnderlyingDataQueryFailed);
    }

    [Event(EventIdUnderlyingDataQueryCanceled, Level = EventLevel.Verbose)]
    public void UnderlyingDataQueryCanceled()
    {
        DebugAssertEnabled();
        _ = Interlocked.Decrement(ref _currentUnderlyingDataQuery);
        WriteEvent(EventIdUnderlyingDataQueryCanceled);
    }

    [Event(EventIdLocalCacheWrite, Level = EventLevel.Verbose)]
    public void LocalCacheWrite()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalLocalCacheWrite);
        WriteEvent(EventIdLocalCacheWrite);
    }

    [Event(EventIdDistributedCacheWrite, Level = EventLevel.Verbose)]
    public void DistributedCacheWrite()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalDistributedCacheWrite);
        WriteEvent(EventIdDistributedCacheWrite);
    }

    [Event(EventIdStampedeJoin, Level = EventLevel.Verbose)]
    internal void StampedeJoin()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalStampedeJoin);
        WriteEvent(EventIdStampedeJoin);
    }

    [Event(EventIdTagInvalidated, Level = EventLevel.Verbose)]
    internal void TagInvalidated()
    {
        DebugAssertEnabled();
        _ = Interlocked.Increment(ref _totalTagInvalidations);
        WriteEvent(EventIdTagInvalidated);
    }

    /// <summary>
    /// Reports a local cache hit with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void LocalCacheHitWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            LocalCacheHit();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitLocalCacheHitMetric(tags);
            }
            else
            {
                _sLocalCacheHits.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a local cache miss with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void LocalCacheMissWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            LocalCacheMiss();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitLocalCacheMissMetric(tags);
            }
            else
            {
                _sLocalCacheMisses.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a distributed cache hit with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void DistributedCacheHitWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            DistributedCacheHit();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitDistributedCacheHitMetric(tags);
            }
            else
            {
                _sDistributedCacheHits.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a distributed cache miss with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void DistributedCacheMissWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            DistributedCacheMiss();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitDistributedCacheMissMetric(tags);
            }
            else
            {
                _sDistributedCacheMisses.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a local cache write with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void LocalCacheWriteWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            LocalCacheWrite();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitLocalCacheWriteMetric(tags);
            }
            else
            {
                _sLocalCacheWrites.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a distributed cache write with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void DistributedCacheWriteWithTags(TagSet tags, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            DistributedCacheWrite();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            if (tags.Count > 0)
            {
                EmitDistributedCacheWriteMetric(tags);
            }
            else
            {
                _sDistributedCacheWrites.Add(1);
            }
        }
    }

    /// <summary>
    /// Reports a tag invalidation with optional tag dimensions for System.Diagnostics.Metrics.
    /// </summary>
    /// <param name="tag">The specific tag that was invalidated.</param>
    /// <param name="reportTagMetrics">Whether to emit tag dimensions in System.Diagnostics.Metrics.</param>
    [NonEvent]
    public void TagInvalidatedWithTags(string tag, bool reportTagMetrics)
    {
        if (IsEnabled())
        {
            TagInvalidated();// Emit EventSource event
        }

        // Also emit metrics when requested
        if (reportTagMetrics)
        {
            _sTagInvalidations.Add(1, new KeyValuePair<string, object?>("tag", tag));
        }
    }

    /// <summary>
    /// Emits a local cache hit metric with tag dimensions.
    /// </summary>
    /// <param name="tags">The tags to include as metric dimensions.</param>
    [NonEvent]
    private static void EmitLocalCacheHitMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sLocalCacheHits.Add(1, tagList);
    }

    [NonEvent]
    private static void EmitLocalCacheMissMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sLocalCacheMisses.Add(1, tagList);
    }

    [NonEvent]
    private static void EmitDistributedCacheHitMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sDistributedCacheHits.Add(1, tagList);
    }

    [NonEvent]
    private static void EmitDistributedCacheMissMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sDistributedCacheMisses.Add(1, tagList);
    }

    [NonEvent]
    private static void EmitLocalCacheWriteMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sLocalCacheWrites.Add(1, tagList);
    }

    [NonEvent]
    private static void EmitDistributedCacheWriteMetric(TagSet tags)
    {
        var tagList = CreateTagList(tags);
        _sDistributedCacheWrites.Add(1, tagList);
    }

    /// <summary>
    /// Converts a TagSet to a TagList for use with System.Diagnostics.Metrics instruments.
    /// Tags are added with keys "tag_0", "tag_1", etc. to maintain order and avoid conflicts.
    /// </summary>
    /// <param name="tags">The TagSet to convert.</param>
    /// <returns>A TagList containing the tag values as dimensions.</returns>
    [NonEvent]
    private static TagList CreateTagList(TagSet tags)
    {
        var tagList = default(TagList);
        switch (tags.Count)
        {
            case 0:
                break; // no tags to add
            case 1:
                tagList.Add("tag_0", tags.GetSinglePrechecked());
                break;
            default:
                var span = tags.GetSpanPrechecked();
                for (int i = 0; i < span.Length; i++)
                {
                    tagList.Add($"tag_{i}", span[i]);
                }

                break;
        }

        return tagList;
    }

#if !(NETSTANDARD2_0 || NET462)
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Lifetime exceeds obvious scope; handed to event source")]
    [NonEvent]
    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command == EventCommand.Enable)
        {
            // lazily create counters on first Enable
            _counters ??= [
                new PollingCounter("total-local-cache-hits", this, () => Volatile.Read(ref _totalLocalCacheHit)) { DisplayName = "Total Local Cache Hits" },
                new PollingCounter("total-local-cache-misses", this, () => Volatile.Read(ref _totalLocalCacheMiss)) { DisplayName = "Total Local Cache Misses" },
                new PollingCounter("total-distributed-cache-hits", this, () => Volatile.Read(ref _totalDistributedCacheHit)) { DisplayName = "Total Distributed Cache Hits" },
                new PollingCounter("total-distributed-cache-misses", this, () => Volatile.Read(ref _totalDistributedCacheMiss)) { DisplayName = "Total Distributed Cache Misses" },
                new PollingCounter("total-data-query", this, () => Volatile.Read(ref _totalUnderlyingDataQuery)) { DisplayName = "Total Data Queries" },
                new PollingCounter("current-data-query", this, () => Volatile.Read(ref _currentUnderlyingDataQuery)) { DisplayName = "Current Data Queries" },
                new PollingCounter("current-distributed-cache-fetches", this, () => Volatile.Read(ref _currentDistributedFetch)) { DisplayName = "Current Distributed Cache Fetches" },
                new PollingCounter("total-local-cache-writes", this, () => Volatile.Read(ref _totalLocalCacheWrite)) { DisplayName = "Total Local Cache Writes" },
                new PollingCounter("total-distributed-cache-writes", this, () => Volatile.Read(ref _totalDistributedCacheWrite)) { DisplayName = "Total Distributed Cache Writes" },
                new PollingCounter("total-stampede-joins", this, () => Volatile.Read(ref _totalStampedeJoin)) { DisplayName = "Total Stampede Joins" },
                new PollingCounter("total-tag-invalidations", this, () => Volatile.Read(ref _totalTagInvalidations)) { DisplayName = "Total Tag Invalidations" },
            ];
        }

        base.OnEventCommand(command);
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for local cache hit when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitLocalCacheHitMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitLocalCacheHitMetric(tags);
        }
        else
        {
            _sLocalCacheHits.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for local cache miss when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitLocalCacheMissMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitLocalCacheMissMetric(tags);
        }
        else
        {
            _sLocalCacheMisses.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for distributed cache hit when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitDistributedCacheHitMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitDistributedCacheHitMetric(tags);
        }
        else
        {
            _sDistributedCacheHits.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for distributed cache miss when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitDistributedCacheMissMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitDistributedCacheMissMetric(tags);
        }
        else
        {
            _sDistributedCacheMisses.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for local cache write when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitLocalCacheWriteMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitLocalCacheWriteMetric(tags);
        }
        else
        {
            _sLocalCacheWrites.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for distributed cache write when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tags">The cache entry tags to include as metric dimensions.</param>
    [NonEvent]
    public static void EmitDistributedCacheWriteMetrics(TagSet tags)
    {
        if (tags.Count > 0)
        {
            EmitDistributedCacheWriteMetric(tags);
        }
        else
        {
            _sDistributedCacheWrites.Add(1);
        }
    }

    /// <summary>
    /// Emits only System.Diagnostics.Metrics for tag invalidation when ReportTagMetrics is enabled.
    /// </summary>
    /// <param name="tag">The specific tag that was invalidated.</param>
    [NonEvent]
    public static void EmitTagInvalidationMetrics(string tag)
    {
        _sTagInvalidations.Add(1, new KeyValuePair<string, object?>("tag", tag));
    }
#endif

    [NonEvent]
    [Conditional("DEBUG")]
    private void DebugAssertEnabled([CallerMemberName] string caller = "")
    {
        Debug.Assert(IsEnabled(), $"Missing check to {nameof(HybridCacheEventSource)}.{nameof(Log)}.{nameof(IsEnabled)} from {caller}");
        Debug.WriteLine($"{nameof(HybridCacheEventSource)}: {caller}"); // also log all event calls, for visibility
    }
}
