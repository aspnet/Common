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

        private readonly TaskCompletionSource<CacheItem<T>>? _result;
        private TState? _state;
        private Func<TState, CancellationToken, ValueTask<T>>? _factory; // main data factory
        private HybridCacheEntryOptions? _options;
#if RUNTIME_PACKAGES_11_OR_GREATER
        private Func<TState, HybridCacheEntryContext, CancellationToken, ValueTask<T>>? _factoryWithContext; // context-aware data factory
        private HybridCacheEntryContext? _context; // mutable view handed to a context-aware factory
        private int _factoryContextRevision; // initial Revision of the context passed to the factory; used to detect mutations
#endif
        private Task<T>? _sharedUnwrap; // allows multiple non-cancellable callers to share a single task (when no defensive copy needed)

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

        public void QueueUserWorkItem(in TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_factory is null, "should not already have factory field");
            Debug.Assert(factory is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _factory = factory;
            _options = options;

#if NETCOREAPP3_0_OR_GREATER
            ThreadPool.UnsafeQueueUserWorkItem(this, false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(SharedWaitCallback, this);
#endif
        }

#if RUNTIME_PACKAGES_11_OR_GREATER
        public void QueueUserWorkItem(in TState state, Func<TState, HybridCacheEntryContext, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_factoryWithContext is null, "should not already have factory field");
            Debug.Assert(factory is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _factoryWithContext = factory;
            _options = options;
            _context = new HybridCacheEntryContext(options);
            _factoryContextRevision = _context.Revision;

#if NETCOREAPP3_0_OR_GREATER
            ThreadPool.UnsafeQueueUserWorkItem(this, false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(SharedWaitCallback, this);
#endif
        }
#endif

        [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Cancellation is handled separately via SharedToken")]
        public Task ExecuteDirectAsync(in TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_factory is null, "should not already have factory field");
            Debug.Assert(factory is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _factory = factory;
            _options = options;

            return BackgroundFetchAsync();
        }

#if RUNTIME_PACKAGES_11_OR_GREATER
        [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Cancellation is handled separately via SharedToken")]
        public Task ExecuteDirectAsync(in TState state, Func<TState, HybridCacheEntryContext, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options)
        {
            Debug.Assert(_factoryWithContext is null, "should not already have factory field");
            Debug.Assert(factory is not null, "factory argument should be meaningful");

            // initialize the callback state
            _state = state;
            _factoryWithContext = factory;
            _options = options;
            _context = new HybridCacheEntryContext(options);
            _factoryContextRevision = _context.Revision;

            return BackgroundFetchAsync();
        }
#endif

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

#if RUNTIME_PACKAGES_11_OR_GREATER
                // Track write-side flags that are required regardless of
                // anything the user-supplied options or the factory say.
                HybridCacheEntryFlags mandatoryWriteSideFlags = Cache._hardFlags & WriteSideFlags;
#endif

                if ((activeFlags & HybridCacheEntryFlags.DisableDistributedCache) != HybridCacheEntryFlags.DisableDistributedCache)
                {
                    // in order to use distributed cache, the tags and keys must be valid unicode, to avoid security complications
                    if (!ValidateUnicodeCorrectness(Cache._logger, Key.Key, CacheItem.Tags))
                    {
                        activeFlags |= HybridCacheEntryFlags.DisableDistributedCache;
#if RUNTIME_PACKAGES_11_OR_GREATER
                        mandatoryWriteSideFlags |= HybridCacheEntryFlags.DisableDistributedCacheWrite;
#endif
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
                                    SetResultAndRecycleIfAppropriate(ref result, parsed.RemainingTime, parsed.LocalCacheSize);
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

#if RUNTIME_PACKAGES_11_OR_GREATER
                        if (_factoryWithContext is not null)
                        {
                            newValue = await _factoryWithContext(_state!, _context!, SharedToken).ConfigureAwait(false);
                        }
                        else
#endif
#pragma warning disable S1199 // False positive with conditional compilation: https://github.com/SonarSource/sonar-dotnet/issues/9858
                        {
                            newValue = await _factory!(_state!, SharedToken).ConfigureAwait(false);
                        }
#pragma warning restore S1199

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

#if RUNTIME_PACKAGES_11_OR_GREATER
                    // honor any mutations the factory made to the context it received; we use Revision
                    // as a fast-path skip for the common case where the factory didn't touch it.
                    if (_factoryWithContext is not null && _context!.Revision != _factoryContextRevision)
                    {
                        ApplyFactoryContext(_context, mandatoryWriteSideFlags, ref activeFlags);
                    }
#endif

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

                    // Serialization is required in any of these cases:
                    // - The CacheItem is mutable (the serialized bytes are the L1 and stampede storage, since each
                    //   read deserializes a defensive copy, and they double as the L2 payload).
                    // - The CacheItem is immutable but we are writing to L2 (serialization produces
                    //   the payload).
                    // - The CacheItem is immutable, we are writing to L1, and the entry size is not
                    //   known up-front (MemoryCache may enforce SizeLimit, and we report the
                    //   serialized byte length as the size). When the caller or factory has supplied
                    //   LocalSize via the entry options the size is already known, so this last
                    //   case does not apply.

                    CacheItem cacheItem = CacheItem;
                    long? knownLocalSize = ResolveLocalSize();
                    bool skipSerialize = cacheItem is ImmutableCacheItem<T>
                        && (activeFlags & HybridCacheEntryFlags.DisableDistributedCacheWrite) != 0
                        && ((activeFlags & HybridCacheEntryFlags.DisableLocalCacheWrite) != 0 || knownLocalSize is not null);

                    if (skipSerialize)
                    {
                        SetImmutableResultWithoutSerialize(newValue, activeFlags);
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
                            SetResultPreSerialized(newValue, ref bufferToRelease, serializer, activeFlags);

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
                            SetResultPreSerialized(newValue, ref bufferToRelease, serializer, activeFlags);
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
                Cache.RemoveStampedeState(this);
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

                Cache.RemoveStampedeState(this);
                _ = _result.TrySetResult(cacheItem);
            }
        }

        private void SetResultAndRecycleIfAppropriate(ref BufferChunk value, TimeSpan remainingTime, long? payloadLocalSize = null)
        {
            // set a result from L2 cache
            Debug.Assert(value.OversizedArray is not null, "expected buffer");

            // precedence: size persisted in the payload > caller-provided options.LocalSize > buffer.Length
            long? localSizeOverride = payloadLocalSize ?? ResolveLocalSize();
            IHybridCacheSerializer<T> serializer = Cache.GetSerializer<T>();
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // deserialize; and store object; buffer can be recycled now
                    immutable.SetValue(serializer.Deserialize(new(value.OversizedArray!, value.Offset, value.Length)), localSizeOverride ?? value.Length);
                    value.RecycleIfAppropriate();
                    cacheItem = immutable;
                    break;
                case MutableCacheItem<T> mutable:
                    // use the buffer directly as the backing in the cache-item; do *not* recycle now
                    mutable.SetValue(ref value, serializer);
                    mutable.DebugOnlyTrackBuffer(Cache);
                    if (localSizeOverride is { } mutableSize)
                    {
                        mutable.SetLocalSizeOverride(mutableSize);
                    }

                    cacheItem = mutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem, Key.Flags, remainingTime);
        }

        private long? ResolveLocalSize()
        {
            // factory mutations are captured by rebuilding _options from the context (see ApplyFactoryContext).
            // null means "use implementation default", per the LocalSize API contract — which we honor by
            // falling back to HybridCacheOptions.DefaultEntryOptions.LocalSize (also nullable).
            return GetLocalSize(_options) ?? Cache._defaultLocalSize;
        }

        private void SetImmutableResultWithoutSerialize(T value, HybridCacheEntryFlags activeFlags)
        {
            Debug.Assert(
                (activeFlags & HybridCacheEntryFlags.DisableDistributedCacheWrite) != 0
                && ((activeFlags & HybridCacheEntryFlags.DisableLocalCacheWrite) != 0 || ResolveLocalSize() is not null),
                "Only expected if L2 is disabled and either L1 is disabled or LocalSize is known.");

            // set a result from a value we calculated directly
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // no serialize needed
                    immutable.SetValue(value, size: ResolveLocalSize() ?? -1);
                    cacheItem = immutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem, activeFlags);
        }

        private void SetResultPreSerialized(T value, ref BufferChunk buffer, IHybridCacheSerializer<T>? serializer, HybridCacheEntryFlags activeFlags)
        {
            // set a result from a value we calculated directly that
            // has ALREADY BEEN SERIALIZED (we can optionally consume this buffer)
            CacheItem<T> cacheItem;
            switch (CacheItem)
            {
                case ImmutableCacheItem<T> immutable:
                    // no serialize needed
                    immutable.SetValue(value, size: ResolveLocalSize() ?? buffer.Length);
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

                    if (ResolveLocalSize() is { } mutableSize)
                    {
                        mutable.SetLocalSizeOverride(mutableSize);
                    }

                    cacheItem = mutable;
                    break;
                default:
                    cacheItem = ThrowUnexpectedCacheItem();
                    break;
            }

            SetResult(cacheItem, activeFlags);
        }

        private void SetResult(CacheItem<T> value, HybridCacheEntryFlags activeFlags) => SetResult(value, activeFlags, TimeSpan.MaxValue);

        private void SetResult(CacheItem<T> value, HybridCacheEntryFlags activeFlags, TimeSpan maxRelativeTime)
        {
            if ((activeFlags & HybridCacheEntryFlags.DisableLocalCacheWrite) == 0)
            {
                Cache.SetL1(Key.Key, value, _options, maxRelativeTime); // we can do this without a TCS, for SetValue
            }

            if (_result is not null)
            {
                Cache.RemoveStampedeState(this);
                _ = _result.TrySetResult(value);
            }
        }

#if RUNTIME_PACKAGES_11_OR_GREATER
        private const HybridCacheEntryFlags WriteSideFlags =
            HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite | HybridCacheEntryFlags.DisableCompression;

        /// <summary>
        /// Applies factory mutations to the active flags and effective options after the factory callback
        /// has executed. Only write-side flags are honored; read-side flags are ignored (reads already
        /// happened). The factory's write-side flags fully replace the user-supplied write-side flags, but
        /// <paramref name="mandatoryWriteSideFlags"/> are always preserved. Expiration / LocalCacheExpiration /
        /// LocalSize mutations are captured by rebuilding <see cref="_options"/> from the context, which is
        /// then read by SetL1 / SetL2Async / ResolveLocalSize. LocalSize is validated here since this is the
        /// only point at which factory mutations are observed.
        /// </summary>
        private void ApplyFactoryContext(
            HybridCacheEntryContext context,
            HybridCacheEntryFlags mandatoryWriteSideFlags,
            ref HybridCacheEntryFlags activeFlags)
        {
            _options = CreateOptionsFromContext(context);
            ValidateOptions(_options);

            HybridCacheEntryFlags factoryFlags = context.Flags ?? HybridCacheEntryFlags.None;
            activeFlags = (activeFlags & ~WriteSideFlags) | (factoryFlags & WriteSideFlags) | mandatoryWriteSideFlags;
        }
#endif
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
