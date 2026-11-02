// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static Microsoft.Extensions.Caching.Hybrid.Internal.DefaultHybridCache;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    internal sealed class StampedeState<TState, T> : StampedeState
    {
        // note on terminology: L1 and L2 are, for brevity, used interchangeably with "local" and "distributed" cache, i.e. `IMemoryCache` and `IDistributedCache`
        private const HybridCacheEntryFlags FlagsDisableL1AndL2Write = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite;

        private readonly TaskCompletionSource<CacheItem<T>>? _result;
        private TState? _state;
        private Func<TState, CancellationToken, ValueTask<T>>? _underlying; // main data factory
        private HybridCacheEntryOptions? _options;
        private Task<T>? _sharedUnwrap; // allows multiple non-cancellable callers to share a single task (when no defensive copy needed)

        // true when neither L2 nor the underlying data store provided a value; shared by all joined callers,
        // and assigned before the result is published, so it is safe to read once the result has been observed
        internal bool IsDefaultResult { get; private set; }

        // ONLY set the result, without any other side-effects
        internal void SetResultDirect(CacheItem<T> value)
            => _result?.TrySetResult(value);

        public StampedeState(DefaultHybridCache cache, in StampedeKey key, TagSet tags, bool canBeCanceled)
            : base(cache, key, CacheItem<T>.Create(cache.CurrentTimestamp(), tags), canBeCanceled)
        {
            _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public StampedeState(DefaultHybridCache cache, in StampedeKey key, TagSet tags, CancellationToken token)
            : base(cache, key, CacheItem<T>.Create(cache.CurrentTimestamp(), tags), token)
        {
            // no TCS in this case - this is for SetValue only
        }

        public override Type Type => typeof(T);

        public void QueueUserWorkItem(in TState state, Func<TState, CancellationToken, ValueTask<T>> underlying, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_underlying is null, "should not already have factory field");
            Debug.Assert(underlying is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _underlying = underlying;
            _options = options;

#if NETCOREAPP3_0_OR_GREATER
            ThreadPool.UnsafeQueueUserWorkItem(this, false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(SharedWaitCallback, this);
#endif
        }

        [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Cancellation is handled separately via SharedToken")]
        public Task ExecuteDirectAsync(in TState state, Func<TState, CancellationToken, ValueTask<T>> underlying, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_underlying is null, "should not already have factory field");
            Debug.Assert(underlying is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _underlying = underlying;
            _options = options;

            return BackgroundFetchAsync();
        }

        public override void Execute() => _ = BackgroundFetchAsync();

        public override void SetCanceled() => _result?.TrySetCanceled(SharedToken);

        public ValueTask<T> JoinAsync(ILogger log, CancellationToken token)
        {
            // If the underlying has already completed, and/or our local token can't cancel: we
            // can simply wrap the shared task; otherwise, we need our own cancellation state.
            return token.CanBeCanceled && !Task.IsCompleted ? WithCancellationAsync(log, this, token) : UnwrapReservedAsync(log);

            static async ValueTask<T> WithCancellationAsync(ILogger log, StampedeState<TState, T> stampede, CancellationToken token)
            {
                var cancelStub = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration reg = token.Register(static obj =>
                {
                    _ = ((TaskCompletionSource<bool>)obj!).TrySetResult(true);
                }, cancelStub);

                CacheItem<T> result;
                try
                {
                    Task first = await System.Threading.Tasks.Task.WhenAny(stampede.Task, cancelStub.Task).ConfigureAwait(false);
                    if (ReferenceEquals(first, cancelStub.Task))
                    {
                        // we expect this to throw, because otherwise we wouldn't have gotten here
                        token.ThrowIfCancellationRequested(); // get an appropriate exception
                    }

                    Debug.Assert(ReferenceEquals(first, stampede.Task), "should not be cancelled");

                    // this has already completed, but we'll get the stack nicely
                    result = await stampede.Task.ConfigureAwait(false);
                }
                catch
                {
                    stampede.CancelCaller();
                    throw;
                }

                // outside the catch, so we know we only decrement one way or the other
                return result.GetReservedValue(log);
            }
        }

        public Task<CacheItem<T>> Task
        {
            get
            {
                Debug.Assert(_result is not null, "result should be assigned");
                return _result is null ? InvalidAsync() : _result.Task;

                static Task<CacheItem<T>> InvalidAsync() => System.Threading.Tasks.Task.FromException<CacheItem<T>>(
                    new InvalidOperationException("Task should not be accessed for non-shared instances"));
            }
        }

        [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "No cancellable operation")]
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Checked manual unwrap")]
        internal ValueTask<T> UnwrapReservedAsync(ILogger log)
        {
            Task<CacheItem<T>> task = Task;
#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            if (task.IsCompletedSuccessfully)
#else
            if (task.Status == TaskStatus.RanToCompletion)
#endif
            {
                return new(task.Result.GetReservedValue(log));
            }

            // if the type is immutable, callers can share the final step too (this may leave dangling
            // reservation counters, but that's OK)
            Task<T> result = ImmutableTypeCache<T>.IsImmutable ? (_sharedUnwrap ??= AwaitedAsync(log, Task)) : AwaitedAsync(log, Task);
            return new(result);

            static async Task<T> AwaitedAsync(ILogger log, Task<CacheItem<T>> task)
                => (await task.ConfigureAwait(false)).GetReservedValue(log);
        }

        [DoesNotReturn]
        private static CacheItem<T> ThrowUnexpectedCacheItem() => throw new InvalidOperationException("Unexpected cache item");

        [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "In this case the cancellation token is provided internally via SharedToken")]
        private async Task BackgroundFetchAsync()
        {
            bool eventSourceEnabled = HybridCacheEventSource.Log.IsEnabled();
            try
            {
                HybridCacheEntryFlags activeFlags = Key.Flags;
                if ((activeFlags & HybridCacheEntryFlags.DisableDistributedCache) != HybridCacheEntryFlags.DisableDistributedCache)
                {
                    // in order to use distributed cache, the tags and keys must be valid unicode, to avoid security complications
                    if (!ValidateUnicodeCorrectness(Cache._logger, Key.Key, CacheItem.Tags))
                    {
                        activeFlags |= HybridCacheEntryFlags.DisableDistributedCache;
                    }
                }

                // read from L2 if appropriate
                if ((activeFlags & HybridCacheEntryFlags.DisableDistributedCacheRead) == 0)
                {
                    // kick off any necessary tag invalidation fetches
                    Cache.PrefetchTags(CacheItem.Tags);

                    BufferChunk result;
                    try
                    {
                        if (eventSourceEnabled)
                        {
                            HybridCacheEventSource.Log.DistributedCacheGet();
                        }

                        result = await Cache.GetFromL2DirectAsync(Key.Key, SharedToken).ConfigureAwait(false);
                        if (eventSourceEnabled)
                        {
                            if (result.HasValue)
                            {
                                HybridCacheEventSource.Log.DistributedCacheHit();
                            }
                            else
                            {
                                HybridCacheEventSource.Log.DistributedCacheMiss();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (SharedToken.IsCancellationRequested)
                    {
                        if (eventSourceEnabled)
                        {
                            HybridCacheEventSource.Log.DistributedCacheCanceled();
                        }

                        throw; // don't just treat as miss - exit ASAP
                    }
                    catch (Exception ex)
                    {
                        if (eventSourceEnabled)
                        {
                            HybridCacheEventSource.Log.DistributedCacheFailed();
                        }

                        Cache._logger.CacheUnderlyingDataQueryFailure(ex);
                        result = default; // treat as "miss"
                    }

                    if (result.HasValue)
                    {
                        // result is the wider payload including HC headers; unwrap it:
                        HybridCachePayload.HybridCachePayloadParseResult parseResult = HybridCachePayload.TryParse(
                            result.AsArraySegment(), Key.Key, CacheItem.Tags, Cache, out HybridCachePayload.ParsedPayload parsed, out Exception? fault);
                        switch (parseResult)
                        {
                            case HybridCachePayload.HybridCachePayloadParseResult.Success:
                                // check any pending expirations, if necessary
                                if (parsed.PendingTags.IsEmpty
                                    || !await Cache.IsAnyTagExpiredAsync(parsed.PendingTags, parsed.CreationTime).ConfigureAwait(false))
                                {
                                    // move into the payload segment (minus any framing/header/etc data)
                                    result = new(parsed.Payload.Array!, parsed.Payload.Offset, parsed.Payload.Count, result.ReturnToPool);
                                    SetResultAndRecycleIfAppropriate(ref result, parsed.RemainingTime);
                                    return;
                                }

                                break;
                            case HybridCachePayload.HybridCachePayloadParseResult.ExpiredByEntry:
                            case HybridCachePayload.HybridCachePayloadParseResult.ExpiredByWildcard:
                            case HybridCachePayload.HybridCachePayloadParseResult.ExpiredByTag:
                                // we don't need to log anything in the case of expiration
                                break;
                            default:
                                Cache._logger.CacheBackendDataRejected(parseResult, fault);
                                break;
                        }

                        result.RecycleIfAppropriate();
                    }
                }

                // nothing from L2; invoke the underlying data store
                if ((activeFlags & HybridCacheEntryFlags.DisableUnderlyingData) == 0)
                {
                    // invoke the callback supplied by the caller
                    T newValue;
                    try
                    {
                        if (eventSourceEnabled)
                        {
                            HybridCacheEventSource.Log.UnderlyingDataQueryStart();
                        }

                        newValue = await _underlying!(_state!, SharedToken).ConfigureAwait(false);

                        if (eventSourceEnabled)
                        {
                            HybridCacheEventSource.Log.UnderlyingDataQueryComplete();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (eventSourceEnabled)
                        {
                            if (ex is OperationCanceledException && SharedToken.IsCancellationRequested)
                            {
                                HybridCacheEventSource.Log.UnderlyingDataQueryCanceled();
                            }
                            else
                            {
                                HybridCacheEventSource.Log.UnderlyingDataQueryFailed();
                            }
                        }

                        throw;
                    }

                    // check whether we're going to hit a timing problem with tag invalidation
                    if (!Cache.IsValid(CacheItem))
                    {
                        // When writing to L1, we need to avoid a problem where either "*" or one of
                        // the active tags matches "now" - we get into a problem whereby it is
                        // ambiguous whether the data is invalidated; consider all of the following happen
                        // *in the same measured instance*:
                        // - write with value A
                        // - invalidate by tag (or wildcard)
                        // - write with value B
                        // Both A and B have the same timestamp as the invalidated one; to avoid this problem,
                        // we need to detect this (very rare) scenario, and inject an artificial delay, such that
                        // B effectively gets written at a later time.
                        long time = Cache.CurrentTimestamp();
                        if (time <= CacheItem.CreationTimestamp)
                        {
                            // Clock hasn't changed; this is *very rare*, and honestly mostly applies to
                            // tests with dummy fetch calls; inject an artificial delay and re-fetch
                            // the time.
                            await System.Threading.Tasks.Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
                            time = Cache.CurrentTimestamp();
                        }

                        // We can safely update the timestamp without fear of torn values etc; no competing code
                        // will access this until we set it into L1, which happens towards the *end* of this method,
                        // and we (the current thread/path) are the only execution for this instance.
                        CacheItem.UnsafeSetCreationTimestamp(time);
                    }

                    // If we're writing this value *anywhere*, we're going to need to serialize; this is obvious
                    // in the case of L2, but we also need it for L1, because MemoryCache might be enforcing
                    // SizeLimit (we can't know - it is an abstraction), and for *that* we need to know the item size.
                    // Likewise, if we're writing to a MutableCacheItem, we'll be serializing *anyway* for the payload.
                    //
                    // Rephrasing that: the only scenario in which we *do not* need to serialize is if:
                    // - it is an ImmutableCacheItem (so we don't need bytes for the CacheItem, L1)
                    // - we're not writing to L2
                    CacheItem cacheItem = CacheItem;
                    bool skipSerialize = cacheItem is ImmutableCacheItem<T> && (activeFlags & FlagsDisableL1AndL2Write) == FlagsDisableL1AndL2Write;

                    if (skipSerialize)
                    {
                        SetImmutableResultWithoutSerialize(newValue);
                    }
                    else if (cacheItem.TryReserve())
                    {
                        // ^^^ The first thing we need to do is make sure we're not getting into a thread race over buffer disposal.
                        // In particular, if this cache item is somehow so short-lived that the buffers would be released *before* we're
                        // done writing them to L2, which happens *after* we've provided the value to consumers.
                        BufferChunk bufferToRelease = default;
                        if (Cache.TrySerialize(newValue, out BufferChunk buffer, out IHybridCacheSerializer<T>? serializer))
                        {
                            // note we also capture the resolved serializer ^^^ - we'll need it again later

                            // protect "buffer" (this is why we "reserved") for writing to L2 if needed; SetResultPreSerialized
                            // *may* (depending on context) claim this buffer, in which case "bufferToRelease" gets reset, and
                            // the final RecycleIfAppropriate() is a no-op; however, the buffer is valid in either event,
                            // (with TryReserve above guaranteeing that we aren't in a race condition).
                            bufferToRelease = buffer;

                            // and since "bufferToRelease" is the thing that will be returned at some point, we can make it explicit
                            // that we do not need or want "buffer" to do any recycling (they're the same memory)
                            buffer = buffer.DoNotReturnToPool();

                            // set the underlying result for this operation (includes L1 write if appropriate)
                            SetResultPreSerialized(newValue, ref bufferToRelease, serializer);

                            // Note that at this point we've already released most or all of the waiting callers. Everything
                            // from this point onwards happens in the background, from the perspective of the calling code.

                            // Write to L2 if appropriate.
                            if ((activeFlags & HybridCacheEntryFlags.DisableDistributedCacheWrite) == 0)
                            {
                                // We already have the payload serialized, so this is trivial to do.
                                try
                                {
                                    await Cache.SetL2Async(Key.Key, cacheItem, in buffer, _options, SharedToken).ConfigureAwait(false);

                                    if (eventSourceEnabled)
                                    {
                                        HybridCacheEventSource.Log.DistributedCacheWrite();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // log the L2 write failure, but that doesn't need to interrupt the app flow (so:
                                    // don't rethrow); L1 will still reduce impact, and L1 without L2 is better than
                                    // hard failure every time
                                    Cache._logger.CacheBackendWriteFailure(ex);
                                }
                            }
                        }
                        else
                        {
                            // unable to serialize (or quota exceeded); try to at least store the onwards value; this is
                            // especially useful for immutable data types
                            SetResultPreSerialized(newValue, ref bufferToRelease, serializer);
                        }

                        // Release our hook on the CacheItem (only really important for "mutable").
                        _ = cacheItem.Release();

                        // Finally, recycle whatever was left over from SetResultPreSerialized; using "bufferToRelease"
                        // here is NOT a typo; if SetResultPreSerialized left this value alone (immutable), then
                        // this is our recycle step; if SetResultPreSerialized transferred ownership to the (mutable)
                        // CacheItem, then this becomes a no-op, and the buffer only gets fully recycled when the
                        // CacheItem itself is fully clear.
                        bufferToRelease.RecycleIfAppropriate();
                    }
                    else
                    {
                        throw new InvalidOperationException("Internal HybridCache failure: unable to reserve cache item to assign result");
                    }
                }
                else
                {
                    // can't read from data store; implies we shouldn't write
                    // back to anywhere else, either
                    SetDefaultResult();
                }
            }
            catch (Exception ex)
            {
                SetException(ex);
            }
        }

        private void SetException(Exception ex)
        {
            if (_result is not null)
            {
                Cache.RemoveStampedeState(in Key);
                _ = _result.TrySetException(ex);
            }
        }

        private void SetDefaultResult()
        {
            // note we don't store this dummy result in L1 or L2; we complete with our own cache item, because
            // it already holds one reservation per joined caller, and each caller releases one when unwrapping
            if (_result is not null)
            {
                CacheItem<T> cacheItem;
                switch (CacheItem)
                {
                    case ImmutableCacheItem<T> immutable:
                        immutable.SetValue(default!, size: -1);
                        cacheItem = immutable;
                        break;
                    case MutableCacheItem<T> mutable:
                        mutable.SetFallbackValue(default!);
                        cacheItem = mutable;
                        break;
                    default:
                        cacheItem = ThrowUnexpectedCacheItem();
                        break;
                }

                IsDefaultResult = true;
                Cache.RemoveStampedeState(in Key);
                _ = _result.TrySetResult(cacheItem);
            }
        }

        private void SetResultAndRecycleIfAppropriate(ref BufferChunk value, TimeSpan remainingTime)
        {
            // set a result from L2 cache
            Debug.Assert(value.OversizedArray is not null, "expected buffer");

            IHybridCacheSerializer<T> serializer = Cache.GetSerializer<T>();
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // deserialize; and store object; buffer can be recycled now
                    immutable.SetValue(serializer.Deserialize(new(value.OversizedArray!, value.Offset, value.Length)), value.Length);
                    value.RecycleIfAppropriate();
                    cacheItem = immutable;
                    break;
                case MutableCacheItem<T> mutable:
                    // use the buffer directly as the backing in the cache-item; do *not* recycle now
                    mutable.SetValue(ref value, serializer);
                    mutable.DebugOnlyTrackBuffer(Cache);
                    cacheItem = mutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem, remainingTime);
        }

        private void SetImmutableResultWithoutSerialize(T value)
        {
            Debug.Assert((Key.Flags & FlagsDisableL1AndL2Write) == FlagsDisableL1AndL2Write, "Only expected if L1+L2 disabled");

            // set a result from a value we calculated directly
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // no serialize needed
                    immutable.SetValue(value, size: -1);
                    cacheItem = immutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem);
        }

        private void SetResultPreSerialized(T value, ref BufferChunk buffer, IHybridCacheSerializer<T>? serializer)
        {
            // set a result from a value we calculated directly that
            // has ALREADY BEEN SERIALIZED (we can optionally consume this buffer)
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // no serialize needed
                    immutable.SetValue(value, size: buffer.Length);
                    cacheItem = immutable;

                    // (but leave the buffer alone)
                    break;
                case MutableCacheItem<T> mutable:
                    if (serializer is null)
                    {
                        // serialization is failing; set fallback value
                        mutable.SetFallbackValue(value);
                    }
                    else
                    {
                        mutable.SetValue(ref buffer, serializer);
                        mutable.DebugOnlyTrackBuffer(Cache);
                    }

                    cacheItem = mutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem);
        }

        private void SetResult(CacheItem<T> value) => SetResult(value, TimeSpan.MaxValue);

        private void SetResult(CacheItem<T> value, TimeSpan maxRelativeTime)
        {
            if ((Key.Flags & HybridCacheEntryFlags.DisableLocalCacheWrite) == 0)
            {
                Cache.SetL1(Key.Key, value, _options, maxRelativeTime); // we can do this without a TCS, for SetValue
            }

            if (_result is not null)
            {
                Cache.RemoveStampedeState(in Key);
                _ = _result.TrySetResult(value);
            }
        }
    }

    private static bool ValidateUnicodeCorrectness(ILogger logger, string key, TagSet tags)
    {
        int maxChars = Math.Max(key.Length, tags.MaxLength());
        int maxBytes = HybridCachePayload.Encoding.GetMaxByteCount(maxChars);

        byte[] leasedBytes = [];
        char[] leasedChars = [];

        Span<byte> byteBuffer = maxBytes <= 128 ? stackalloc byte[128] : (leasedBytes = ArrayPool<byte>.Shared.Rent(maxBytes));
        Span<char> charBuffer = maxChars <= 128 ? stackalloc char[128] : (leasedChars = ArrayPool<char>.Shared.Rent(maxChars));

        bool isValid = true;

        if (!Test(key, byteBuffer, charBuffer))
        {
            Log.KeyInvalidUnicode(logger);
            isValid = false;
        }

        if (isValid)
        {
            switch (tags.Count)
            {
                case 0:
                    break;
                case 1:
                    if (!Test(tags.GetSinglePrechecked(), byteBuffer, charBuffer))
                    {
                        Log.TagInvalidUnicode(logger);
                        isValid = false;
                        break;
                    }

                    break;
                default:
                    foreach (string tag in tags.GetSpanPrechecked())
                    {
                        if (!Test(tag, byteBuffer, charBuffer))
                        {
                            Log.TagInvalidUnicode(logger);
                            isValid = false;
                            break;
                        }
                    }

                    break;
            }
        }

        ArrayPool<char>.Shared.Return(leasedChars);
        ArrayPool<byte>.Shared.Return(leasedBytes);
        return isValid;

        static unsafe bool Test(string value, Span<byte> byteBuffer, Span<char> charBuffer)
        {
            // for reliable confidence of unicode correctness: encode and decode, and verify equality
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            byteBuffer = byteBuffer.Slice(0, HybridCachePayload.Encoding.GetBytes(value.AsSpan(), byteBuffer));
            charBuffer = charBuffer.Slice(0, HybridCachePayload.Encoding.GetChars(byteBuffer, charBuffer));
#else
            unsafe
            {
                int bytes;
                fixed (byte* bPtr = byteBuffer)
                {
                    fixed (char* cPtr = value)
                    {
                        bytes = HybridCachePayload.Encoding.GetBytes(cPtr, value.Length, bPtr, byteBuffer.Length);
                    }

                    fixed (char* cPtr = charBuffer)
                    {
                        charBuffer = charBuffer.Slice(0, HybridCachePayload.Encoding.GetChars(bPtr, bytes, cPtr, charBuffer.Length));
                    }
                }
            }
#endif
            return charBuffer.SequenceEqual(value.AsSpan());
        }
    }
}
