// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

internal partial class DefaultHybridCache
{
    internal sealed class ImmutableCacheItem<T> : CacheItem<T> // used to hold types that do not require defensive copies
    {
        public ImmutableCacheItem(long creationTimestamp, TagSet tags)
            : base(creationTimestamp, tags)
        {
        }

        private T _value = default!; // deferred until SetValue

        public long Size { get; private set; } = -1;

        public override bool DebugIsImmutable => true;

        public void SetValue(T value, long size)
        {
            _value = value;
            Size = size;
        }

        public override bool TryGetValue(ILogger log, out T value)
        {
            value = _value;
            return true; // always available
        }

        public override bool TryGetSize(out long size)
        {
            size = Size;
            return size >= 0;
        }

        public override bool TryReserveBuffer(out BufferChunk buffer)
        {
            buffer = default;
            return false; // we don't have one to reserve!
        }
    }
}
