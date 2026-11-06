// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Threading;

#if !NETCOREAPP3_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    internal abstract class StampedeState
#if NETCOREAPP3_0_OR_GREATER
        : IThreadPoolWorkItem
#endif
    {
        internal readonly CancellationToken SharedToken; // this might have a value even when _sharedCancellation is null

        // Because multiple callers can enlist, we need to track when the *last* caller cancels
        // (and keep going until then); that means we need to run with custom cancellation.
        private readonly CancellationTokenSource? _sharedCancellation;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Keep usage explicit")]
        private readonly DefaultHybridCache _cache;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Keep usage explicit")]
        private readonly CacheItem _cacheItem;

        // we expose the key as a by-ref readonly; this minimizes the stack work involved in passing the key around
        // (both in terms of width and copy-semantics)
        private readonly StampedeKey _key;
        public ref readonly StampedeKey Key => ref _key;
        protected CacheItem CacheItem => _cacheItem;

        /// <summary>
        /// Initializes a new instance of the <see cref="StampedeState"/> class optionally with shared cancellation support.
        /// </summary>
        protected StampedeState(DefaultHybridCache cache, in StampedeKey key, CacheItem cacheItem, bool canBeCanceled)
        {
            _cache = cache;
            _key = key;
            _cacheItem = cacheItem;
            if (canBeCanceled)
            {
                // If the first (or any) caller can't be cancelled;,we'll never get to zero: n point tracking.
                // (in reality, all callers usually use the same path, so cancellation is usually "all" or "none")
                _sharedCancellation = new();
                SharedToken = _sharedCancellation.Token;
            }
            else
            {
                SharedToken = CancellationToken.None;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StampedeState"/> class using a fixed cancellation token.
        /// </summary>
        protected StampedeState(DefaultHybridCache cache, in StampedeKey key, CacheItem cacheItem, CancellationToken token)
        {
            _cache = cache;
            _key = key;
            _cacheItem = cacheItem;
            SharedToken = token;
        }

#if !NETCOREAPP3_0_OR_GREATER
        protected static readonly WaitCallback SharedWaitCallback = static obj => Unsafe.As<StampedeState>(obj).Execute();
#endif

        protected DefaultHybridCache Cache => _cache;

        public abstract void Execute();

        public override string ToString() => Key.ToString();

        public abstract void SetCanceled();

        public int DebugCallerCount => _cacheItem.RefCount;

        public abstract Type Type { get; }

        public void CancelCaller()
        {
            // note that TryAddCaller has protections to avoid getting back from zero
            if (_cacheItem.Release())
            {
                // we're the last to leave; turn off the lights
                _sharedCancellation?.Cancel();
                SetCanceled();
            }
        }

        public bool TryAddCaller() => _cacheItem.TryReserve();
    }

    private void RemoveStampedeState(StampedeState expected)
    {
        ref readonly StampedeKey key = ref expected.Key;

        // see notes in SyncLock.cs
        lock (GetPartitionedSyncLock(in key))
        {
            if (_currentOperations.TryGetValue(key, out StampedeState? current)
                && ReferenceEquals(current, expected))
            {
                // Replacements take the same partitioned lock, and the unlocked TryAdd cannot replace an existing entry,
                // so this is safe.
                _ = _currentOperations.TryRemove(key, out StampedeState? removed);
                Debug.Assert(ReferenceEquals(removed, expected), "The stampede state changed while holding its partition lock.");
            }
        }
    }
}
