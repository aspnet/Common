// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid.Internal;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Caching.Hybrid;

/// <summary>
/// Extension methods for <see cref="HybridCache"/>.
/// </summary>
public static class HybridCacheExtensions
{
    /// <summary>
    /// Gets the value stored for the specified key, without creating it if it is missing.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache to read from.</param>
    /// <param name="key">The key of the entry to look for.</param>
    /// <param name="options">Additional options for this cache entry, such as flags that skip the local or distributed cache.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
    /// <returns>
    /// A tuple whose <c>Found</c> item is <see langword="true"/> when a value was found in the local or distributed cache,
    /// in which case <c>Value</c> holds it; on a miss, <c>Found</c> is <see langword="false"/> and <c>Value</c> is the default for <typeparamref name="T"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A miss is never written to the local or distributed cache. A value found in the distributed cache can be copied into
    /// the local cache, the same way as with <see cref="HybridCache.GetOrCreateAsync{T}(string, System.Func{CancellationToken, ValueTask{T}}, HybridCacheEntryOptions?, System.Collections.Generic.IEnumerable{string}?, CancellationToken)"/>;
    /// pass <see cref="HybridCacheEntryFlags.DisableLocalCacheWrite"/> to prevent that.
    /// </para>
    /// <para>
    /// The <see cref="HybridCache"/> implementation registered by <c>AddHybridCache</c> tells a miss apart from a cached
    /// default value. Other implementations are queried through
    /// <see cref="HybridCache.GetOrCreateAsync{TState, T}(string, TState, System.Func{TState, CancellationToken, ValueTask{T}}, HybridCacheEntryOptions?, System.Collections.Generic.IEnumerable{string}?, CancellationToken)"/>
    /// with writes disabled; an implementation that merges concurrent requests for the same key may then report
    /// a concurrent miss as a found default value.
    /// </para>
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.HybridCache, UrlFormat = DiagnosticIds.UrlFormat)]
    public static ValueTask<(bool Found, T? Value)> GetAsync<T>(this HybridCache cache, string key,
        HybridCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(cache);

        return cache is DefaultHybridCache defaultCache
            ? defaultCache.GetAsync<T>(key, options, cancellationToken)
            : GetViaFactoryAsync(cache, key, options, cancellationToken);

        static async ValueTask<(bool Found, T? Value)> GetViaFactoryAsync(HybridCache cache, string key,
            HybridCacheEntryOptions? options, CancellationToken cancellationToken)
        {
            // the factory only records the miss; with both writes disabled, its default result is never stored
            const HybridCacheEntryFlags DisableWrites = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite;
            var readOptions = new HybridCacheEntryOptions
            {
                Expiration = options?.Expiration,
                LocalCacheExpiration = options?.LocalCacheExpiration,
                Flags = ((options?.Flags ?? HybridCacheEntryFlags.None) | DisableWrites) & ~HybridCacheEntryFlags.DisableUnderlyingData,
            };

            var miss = new StrongBox<bool>();
            T value = await cache.GetOrCreateAsync(key, miss, static (state, _) =>
            {
                state.Value = true;
                return default(ValueTask<T>);
            }, readOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            return miss.Value ? (false, default) : (true, value);
        }
    }
}
