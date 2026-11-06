// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    internal sealed partial class MutableCacheItem<T> : CacheItem<T> // used to hold types that require defensive copies
    {
        private IHybridCacheSerializer<T>? _serializer;
        private BufferChunk _buffer;
        private T? _fallbackValue; // only used in the case of serialization failures
        private long? _localSizeOverride;

        public MutableCacheItem(long creationTimestamp, TagSet tags)
            : base(creationTimestamp, tags)
        {
        }

        public override bool NeedsEvictionCallback => _buffer.ReturnToPool;

        public override bool DebugIsImmutable => false;

        public void SetValue(ref BufferChunk buffer, IHybridCacheSerializer<T> serializer)
        {
            _serializer = serializer;
            _buffer = buffer;
            buffer = default; // we're taking over the lifetime; the caller no longer has it!
        }

        public void SetFallbackValue(T fallbackValue)
        {
            _fallbackValue = fallbackValue;
        }

        public override bool TryGetValue(ILogger log, out T value)
        {
            // only if we haven't already burned
            if (TryReserve())
            {
                try
                {
                    IHybridCacheSerializer<T>? serializer = _serializer;
                    value = serializer is null ? _fallbackValue! : serializer.Deserialize(_buffer.AsSequence());
                    return true;
                }
                catch (Exception ex)
                {
                    log.DeserializationFailure(ex);
                    throw;
                }
                finally
                {
                    _ = Release();
                }
            }

            value = default!;
            return false;
        }

        public override bool TryGetSize(out long size)
        {
            // only if we haven't already burned
            if (TryReserve())
            {
                size = _localSizeOverride ?? _buffer.Length;
                _ = Release();
                return true;
            }

            size = 0;
            return false;
        }

        public void SetLocalSizeOverride(long size)
        {
            _localSizeOverride = size;
        }

        public override bool TryReserveBuffer(out BufferChunk buffer)
        {
            // only if we haven't already burned
            if (TryReserve())
            {
                buffer = _buffer.DoNotReturnToPool(); // not up to them!
                return true;
            }

            buffer = default;
            return false;
        }

        protected override void OnFinalRelease()
        {
            DebugOnlyDecrementOutstandingBuffers();
            _buffer.RecycleIfAppropriate();
        }
    }
}
