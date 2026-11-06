// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    private const int MaxCacheDays = 1000;
    private const string TagKeyPrefix = "__MSFT_HCT__";
    private static readonly DistributedCacheEntryOptions _tagInvalidationEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(MaxCacheDays) };

    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Manual sync check")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Deliberate recycle only on success")]
    internal ValueTask<BufferChunk> GetFromL2DirectAsync(string key, CancellationToken token)
    {
        switch (GetFeatures(CacheFeatures.BackendCache | CacheFeatures.BackendBuffers))
        {
            case CacheFeatures.BackendCache: // legacy byte[]-based

                Task<byte[]?> pendingLegacy = _backendCache!.GetAsync(key, token);

#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                if (!pendingLegacy.IsCompletedSuccessfully)
#else
                if (pendingLegacy.Status != TaskStatus.RanToCompletion)
#endif
                {
                    return new(AwaitedLegacyAsync(pendingLegacy, this));
                }

                return new(GetValidPayloadSegment(pendingLegacy.Result)); // already complete

            case CacheFeatures.BackendCache | CacheFeatures.BackendBuffers: // IBufferWriter<byte>-based
                RecyclableArrayBufferWriter<byte> writer = RecyclableArrayBufferWriter<byte>.Create(MaximumPayloadBytes);
                IBufferDistributedCache cache = Unsafe.As<IBufferDistributedCache>(_backendCache!); // type-checked already

                ValueTask<bool> pendingBuffers = cache.TryGetAsync(key, writer, token);
                if (!pendingBuffers.IsCompletedSuccessfully)
                {
                    return new(AwaitedBuffersAsync(pendingBuffers, writer));
                }

                BufferChunk result = pendingBuffers.GetAwaiter().GetResult()
                    ? new(writer.DetachCommitted(out int length), 0, length, returnToPool: true)
                    : default;
                writer.Dispose(); // it is not accidental that this isn't "using"; avoid recycling if not 100% sure what happened
                return new(result);
        }

        return default; // treat as a "miss"

        static async Task<BufferChunk> AwaitedLegacyAsync(Task<byte[]?> pending, DefaultHybridCache @this)
        {
            byte[]? bytes = await pending.ConfigureAwait(false);
            return @this.GetValidPayloadSegment(bytes);
        }

        static async Task<BufferChunk> AwaitedBuffersAsync(ValueTask<bool> pending, RecyclableArrayBufferWriter<byte> writer)
        {
            BufferChunk result = await pending.ConfigureAwait(false)
                    ? new(writer.DetachCommitted(out int length), 0, length, returnToPool: true)
                    : default;
            writer.Dispose(); // it is not accidental that this isn't "using"; avoid recycling if not 100% sure what happened
            return result;
        }
    }

    internal ValueTask SetL2Async(string key, CacheItem cacheItem, in BufferChunk buffer, HybridCacheEntryOptions? options, CancellationToken token)
        => HasBackendCache ? WritePayloadAsync(key, cacheItem, buffer, options, token) : default;

    internal ValueTask SetDirectL2Async(string key, in BufferChunk buffer, DistributedCacheEntryOptions options, CancellationToken token)
    {
        Debug.Assert(buffer.OversizedArray is not null, "array should be non-null");
        switch (GetFeatures(CacheFeatures.BackendCache | CacheFeatures.BackendBuffers))
        {
            case CacheFeatures.BackendCache: // legacy byte[]-based
                byte[] arr = buffer.OversizedArray!;
                if (buffer.Offset != 0 || arr.Length != buffer.Length)
                {
                    // we'll need a right-sized snapshot
                    arr = buffer.ToArray();
                }

                return new(_backendCache!.SetAsync(key, arr, options, token));
            case CacheFeatures.BackendCache | CacheFeatures.BackendBuffers: // ReadOnlySequence<byte>-based
                IBufferDistributedCache cache = Unsafe.As<IBufferDistributedCache>(_backendCache!); // type-checked already
                return cache.SetAsync(key, buffer.AsSequence(), options, token);
        }

        return default;
    }

    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Manual async core implementation")]
    internal ValueTask InvalidateL2TagAsync(string tag, long timestamp, CancellationToken token)
    {
        if (!HasBackendCache)
        {
            return default; // no L2
        }

        byte[] oversized = ArrayPool<byte>.Shared.Rent(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(oversized, timestamp);
        ValueTask pending = SetDirectL2Async(TagKeyPrefix + tag, new BufferChunk(oversized, 0, sizeof(long), false), _tagInvalidationEntryOptions, token);

        if (pending.IsCompletedSuccessfully)
        {
            pending.GetAwaiter().GetResult(); // ensure observed (IVTS etc)
            ArrayPool<byte>.Shared.Return(oversized);
            return default;
        }
        else
        {
            return AwaitedAsync(pending, oversized);
        }

        static async ValueTask AwaitedAsync(ValueTask pending, byte[] oversized)
        {
            await pending.ConfigureAwait(false);
            ArrayPool<byte>.Shared.Return(oversized);
        }
    }

    [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Cancellation handled internally")]
    internal async Task<long> SafeReadTagInvalidationAsync(string tag)
    {
        Debug.Assert(HasBackendCache, "shouldn't be here without L2");

        const int READ_TIMEOUT = 4000;

        try
        {
            using var cts = new CancellationTokenSource(millisecondsDelay: READ_TIMEOUT);
            BufferChunk buffer = await GetFromL2DirectAsync(TagKeyPrefix + tag, cts.Token).ConfigureAwait(false);

            long timestamp;
            if (buffer.OversizedArray is not null)
            {
                if (buffer.Length == sizeof(long))
                {
                    timestamp = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan());
                }
                else
                {
                    // not what we expected! assume invalid
                    timestamp = CurrentTimestamp();
                }

                buffer.RecycleIfAppropriate();
            }
            else
            {
                timestamp = 0; // never invalidated
            }

            buffer.RecycleIfAppropriate();
            return timestamp;
        }
        catch (Exception ex)
        {
            // ^^^ this catch is the "Safe" in "SafeReadTagInvalidationAsync"
            Debug.WriteLine(ex.Message);

            // if anything goes wrong reading tag invalidations; we have to assume the tag is invalid
            return CurrentTimestamp();
        }
    }

    internal void SetL1<T>(string key, CacheItem<T> value, HybridCacheEntryOptions? options, TimeSpan maxRelativeTime)
    {
        // incr ref-count for the the cache itself; this *may* be released via the NeedsEvictionCallback path
        if (value.TryReserve())
        {
            // based on CacheExtensions.Set<TItem>, but with post-eviction recycling

            // intentionally use manual Dispose rather than "using"; confusingly, it is Dispose()
            // that actually commits the add - so: if we fault, we don't want to try
            // committing a partially configured cache entry
            ICacheEntry cacheEntry = _localCache.CreateEntry(key);

            var expiry = GetL1AbsoluteExpirationRelativeToNow(options);
            if (expiry > maxRelativeTime)
            {
                // enforce "not exceeding the remaining overall cache lifetime" that we got from L2
                expiry = maxRelativeTime;
            }

            cacheEntry.AbsoluteExpirationRelativeToNow = expiry;
            cacheEntry.Value = value;

            if (value.TryGetSize(out long size))
            {
                cacheEntry = cacheEntry.SetSize(size);
            }

            if (value.NeedsEvictionCallback)
            {
                cacheEntry = cacheEntry.RegisterPostEvictionCallback(CacheItem.SharedOnEviction);
            }

            // commit
            cacheEntry.Dispose();

            if (HybridCacheEventSource.Log.IsEnabled() || _options.ReportTagMetrics)
            {
                HybridCacheEventSource.Log.LocalCacheWriteWithTags(value.Tags, _options.ReportTagMetrics);
            }
        }
    }

    private async ValueTask WritePayloadAsync(string key, CacheItem cacheItem, BufferChunk payload, HybridCacheEntryOptions? options, CancellationToken token)
    {
        // bundle a serialized payload inside the wrapper used at the DC layer
        int maxLength = HybridCachePayload.GetMaxBytes(key, cacheItem.Tags, payload.Length);
        byte[] oversized = ArrayPool<byte>.Shared.Rent(maxLength);

        int length = HybridCachePayload.Write(oversized, key, cacheItem.CreationTimestamp, GetL2AbsoluteExpirationRelativeToNow(options),
            HybridCachePayload.PayloadFlags.None, cacheItem.Tags, payload.AsSequence());

        await SetDirectL2Async(key, new(oversized, 0, length, true), GetL2DistributedCacheOptions(options), token).ConfigureAwait(false);

        ArrayPool<byte>.Shared.Return(oversized);
    }

    private BufferChunk GetValidPayloadSegment(byte[]? payload)
    {
        if (payload is not null)
        {
            if (payload.Length > MaximumPayloadBytes)
            {
                ThrowPayloadLengthExceeded(payload.Length);
            }

            return new(payload);
        }

        return default;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowPayloadLengthExceeded(int size) // splitting the exception bits out to a different method
    {
        // also add via logger when possible
        throw new InvalidOperationException($"Maximum cache length ({MaximumPayloadBytes} bytes) exceeded");
    }

    private DistributedCacheEntryOptions GetL2DistributedCacheOptions(HybridCacheEntryOptions? options)
    {
        DistributedCacheEntryOptions? result = null;
        if (options is not null)
        {
            TimeSpan expiration = GetL2AbsoluteExpirationRelativeToNow(options);
            if (expiration != _defaultExpiration)
            {
                // ^^^ avoid creating unnecessary DC options objects if the expiration still matches the default
                result = ToDistributedCacheEntryOptions(options);
            }
        }

        return result ?? _defaultDistributedCacheExpiration;

#if NET8_0_OR_GREATER
        // internal method memoizes this allocation; since it is "init", it is immutable (outside reflection)
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = nameof(ToDistributedCacheEntryOptions))]
        extern static DistributedCacheEntryOptions? ToDistributedCacheEntryOptions(HybridCacheEntryOptions options);
#else
        // without that helper method, we'll just eat the alloc (down-level TFMs)
        static DistributedCacheEntryOptions ToDistributedCacheEntryOptions(HybridCacheEntryOptions options)
            => new() { AbsoluteExpirationRelativeToNow = options.Expiration };
#endif
    }
}
