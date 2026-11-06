// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

/// <summary>
/// The inbuilt implementation of <see cref="HybridCache"/>, as registered via <see cref="HybridCacheServiceExtensions.AddHybridCache(IServiceCollection)"/>.
/// </summary>
[SkipLocalsInit]
internal sealed partial class DefaultHybridCache : HybridCache
{
    internal const int DefaultExpirationMinutes = 5;

    [SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Keep usage explicit")]
    private readonly IDistributedCache? _backendCache;
    [SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Keep usage explicit")]
    private readonly IMemoryCache _localCache;
    private readonly IServiceProvider _services; // we can't resolve per-type serializers until we see each T
    private readonly IHybridCacheSerializerFactory[] _serializerFactories;
    [SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Keep usage explicit")]
    private readonly HybridCacheOptions _options;
    private readonly ILogger _logger;
    private readonly CacheFeatures _features; // used to avoid constant type-testing
    private readonly TimeProvider _clock;

    private readonly HybridCacheEntryFlags _hardFlags; // *always* present (for example, because no L2)
    private readonly HybridCacheEntryFlags _defaultFlags; // note this already includes hardFlags
    private readonly TimeSpan _defaultExpiration;
    private readonly TimeSpan _defaultLocalCacheExpiration;
    private readonly long? _defaultLocalSize;
    private readonly int _maximumKeyLength;

    private readonly DistributedCacheEntryOptions _defaultDistributedCacheExpiration;

    [Flags]
    internal enum CacheFeatures
    {
        None = 0,
        BackendCache = 1 << 0,
        BackendBuffers = 1 << 1,
    }

    internal CacheFeatures GetFeatures() => _features;

    // used to restrict features in test suite
    internal void DebugRemoveFeatures(CacheFeatures features) => Unsafe.AsRef(in _features) &= ~features;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CacheFeatures GetFeatures(CacheFeatures mask) => _features & mask;

    internal bool HasBackendCache => (_features & CacheFeatures.BackendCache) != 0;

    public DefaultHybridCache(IOptions<HybridCacheOptions> options, IServiceProvider services)
        : this(Throw.IfNull(options).Value, services)
    {
    }

    public DefaultHybridCache(HybridCacheOptions options, IServiceProvider services)
    {
        _services = Throw.IfNull(services);
        _localCache = services.GetRequiredService<IMemoryCache>();
        _options = options;
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(HybridCache)) ?? NullLogger.Instance;
        _clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

        // The backend cache service is optional; if not provided, we operate as a pure L1 cache.
        // If a service key is provided, the service must be present.
        _backendCache = _options.DistributedCacheServiceKey is null
            ? services.GetService<IDistributedCache>()
            : services.GetRequiredKeyedService<IDistributedCache>(_options.DistributedCacheServiceKey);

        // ignore L2 if it is really just the same L1, wrapped
        // (note not just an "is" test; if someone has a custom subclass, who knows what it does?)
        if (_backendCache is not null
            && _backendCache.GetType() == typeof(MemoryDistributedCache)
            && _localCache.GetType() == typeof(MemoryCache))
        {
            _backendCache = null;
        }

        // perform type-tests on the backend once only
        _features |= _backendCache switch
        {
            IBufferDistributedCache => CacheFeatures.BackendCache | CacheFeatures.BackendBuffers,
            not null => CacheFeatures.BackendCache,
            _ => CacheFeatures.None
        };

        // When resolving serializers via the factory API, we will want the *last* instance,
        // i.e. "last added wins"; we can optimize by reversing the array ahead of time, and
        // taking the first match
        IHybridCacheSerializerFactory[] factories = services.GetServices<IHybridCacheSerializerFactory>().ToArray();
        Array.Reverse(factories);
        _serializerFactories = factories;

        MaximumPayloadBytes = checked((int)_options.MaximumPayloadBytes); // for now hard-limit to 2GiB
        _maximumKeyLength = _options.MaximumKeyLength;

        HybridCacheEntryOptions? defaultEntryOptions = _options.DefaultEntryOptions;
        ValidateOptions(defaultEntryOptions);

        if (_backendCache is null)
        {
            _hardFlags |= HybridCacheEntryFlags.DisableDistributedCache;
        }

        _defaultFlags = (defaultEntryOptions?.Flags ?? HybridCacheEntryFlags.None) | _hardFlags;
        _defaultExpiration = defaultEntryOptions?.Expiration ?? TimeSpan.FromMinutes(DefaultExpirationMinutes);
        _defaultLocalCacheExpiration = GetEffectiveLocalCacheExpiration(defaultEntryOptions) ?? _defaultExpiration;
        _defaultLocalSize = GetLocalSize(defaultEntryOptions);
        _defaultDistributedCacheExpiration = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _defaultExpiration };

#if NET9_0_OR_GREATER
        _tagInvalidationTimesUseAltLookup = _tagInvalidationTimes.TryGetAlternateLookup(out _tagInvalidationTimesBySpan);
#endif

        // do this last
        _globalInvalidateTimestamp = _backendCache is null ? _zeroTimestamp : SafeReadTagInvalidationAsync(TagSet.WildcardTag);
    }

    internal IDistributedCache? BackendCache => _backendCache;
    internal IMemoryCache LocalCache => _localCache;

    internal HybridCacheOptions Options => _options;

    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        GetOrCreateOutcome outcome = TryBeginGetOrCreate(key, options, tags, cancellationToken,
            out HybridCacheEntryFlags flags, out bool canBeCanceled, out StampedeState<TState, T>? stampede, out ValueTask<T> result);

        switch (outcome)
        {
            case GetOrCreateOutcome.NoCache:
                return (flags & HybridCacheEntryFlags.DisableUnderlyingData) == 0
                    ? factory(state, cancellationToken) : default;
            case GetOrCreateOutcome.L1Hit:
            case GetOrCreateOutcome.JoinedStampede:
                return result;
        }

        // GetOrCreateOutcome.NewStampede: we own this stampede and must dispatch the factory
        if (canBeCanceled)
        {
            // *we* might cancel, but someone else might be depending on the result; start the
            // work independently, then join the outcome
            stampede!.QueueUserWorkItem(in state, factory, options);
            return stampede.JoinAsync(_logger, cancellationToken);
        }

        // we're going to run to completion; no need to get complicated
        _ = stampede!.ExecuteDirectAsync(in state, factory, options); // this larger task includes L2 write etc

        return stampede.UnwrapReservedAsync(_logger);
    }

#if RUNTIME_PACKAGES_11_OR_GREATER
    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
        Func<TState, HybridCacheEntryContext, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        GetOrCreateOutcome outcome = TryBeginGetOrCreate(key, options, tags, cancellationToken,
            out HybridCacheEntryFlags flags, out bool canBeCanceled, out StampedeState<TState, T>? stampede, out ValueTask<T> result);

        // The factory receives a mutable HybridCacheEntryContext (seeded from the effective options) so it
        // can influence the cache entry options without affecting the caller's HybridCacheEntryOptions instance.
        // In the NoCache case there is no cache to honor the mutations against, but the factory may still rely
        // on being able to inspect and mutate the context.

        switch (outcome)
        {
            case GetOrCreateOutcome.NoCache:
                return (flags & HybridCacheEntryFlags.DisableUnderlyingData) == 0
                    ? factory(state, new HybridCacheEntryContext(options), cancellationToken) : default;
            case GetOrCreateOutcome.L1Hit:
            case GetOrCreateOutcome.JoinedStampede:
                return result;
        }

        if (canBeCanceled)
        {
            stampede!.QueueUserWorkItem(in state, factory, options);
            return stampede.JoinAsync(_logger, cancellationToken);
        }

        _ = stampede!.ExecuteDirectAsync(in state, factory, options);

        return stampede.UnwrapReservedAsync(_logger);
    }
#endif

    private enum GetOrCreateOutcome
    {
        NoCache,
        L1Hit,
        JoinedStampede,
        NewStampede
    }

    // Performs the shared pre-factory work for both GetOrCreateAsync overloads.
    private GetOrCreateOutcome TryBeginGetOrCreate<TState, T>(
        string key,
        HybridCacheEntryOptions? options,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken,
        out HybridCacheEntryFlags flags,
        out bool canBeCanceled,
        out StampedeState<TState, T>? stampede,
        out ValueTask<T> result)
    {
        ValidateOptions(options);

        canBeCanceled = cancellationToken.CanBeCanceled;
        if (canBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        flags = GetEffectiveFlags(options);
        stampede = null;
        result = default;

        if (!ValidateKey(key))
        {
            return GetOrCreateOutcome.NoCache;
        }

        bool eventSourceEnabled = HybridCacheEventSource.Log.IsEnabled();

        if ((flags & HybridCacheEntryFlags.DisableLocalCacheRead) == 0)
        {
            if (TryGetExisting(key, out CacheItem<T>? typed)
                && typed.TryGetValue(_logger, out T? value))
            {
                if (eventSourceEnabled)
                {
                    HybridCacheEventSource.Log.LocalCacheHit();
                }

                result = new(value);
                return GetOrCreateOutcome.L1Hit;
            }

            if (eventSourceEnabled)
            {
                HybridCacheEventSource.Log.LocalCacheMiss();
            }
        }

        if (GetOrCreateStampedeState(key, flags, out stampede, canBeCanceled, tags))
        {
            return GetOrCreateOutcome.NewStampede;
        }

        // joined a pre-existing stampede
        if (eventSourceEnabled)
        {
            HybridCacheEventSource.Log.StampedeJoin();
        }

        result = stampede.JoinAsync(_logger, cancellationToken);
        return GetOrCreateOutcome.JoinedStampede;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _localCache.Remove(key);
        return _backendCache is null ? default : new(_backendCache.RemoveAsync(key, cancellationToken));
    }

    private static void ValidateOptions(HybridCacheEntryOptions? options)
    {
#if RUNTIME_PACKAGES_11_OR_GREATER
        if (options?.LocalSize is { } size && size < 0)
        {
            Throw.ArgumentException(nameof(options),
                $"{nameof(HybridCacheEntryOptions)}.{nameof(HybridCacheEntryOptions.LocalSize)} must be non-negative.");
        }
#endif
    }

    private static long? GetLocalSize(HybridCacheEntryOptions? options)
    {
#if RUNTIME_PACKAGES_11_OR_GREATER
        return options?.LocalSize;
#else
        return null;
#endif
    }

#if RUNTIME_PACKAGES_11_OR_GREATER
    // Projects the (possibly factory-mutated) context back into an options instance that the write paths
    // (SetL1 / SetL2Async / ResolveLocalSize) read. Keep in sync when HybridCacheEntryContext gains a property.
    internal static HybridCacheEntryOptions CreateOptionsFromContext(HybridCacheEntryContext context)
        => new HybridCacheEntryOptions
        {
            Expiration = context.Expiration,
            LocalCacheExpiration = context.LocalCacheExpiration,
            Flags = context.Flags,
            LocalSize = context.LocalSize,
        };
#endif

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        // since we're forcing a write: disable L1+L2 read; we'll use a direct pass-thru of the value as the callback, to reuse all the code
        // note also that stampede token is not shared with anyone else
        HybridCacheEntryFlags flags = GetEffectiveFlags(options) | (HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableDistributedCacheRead);
        var state = new StampedeState<T, T>(this, new StampedeKey(key, flags), TagSet.Create(tags), cancellationToken);
        return new(state.ExecuteDirectAsync(value, static (state, _) => new(state), options)); // note this spans L2 write etc
    }

    // exposed as internal for testability
    internal TimeSpan GetL1AbsoluteExpirationRelativeToNow(HybridCacheEntryOptions? options)
        => GetEffectiveLocalCacheExpiration(options) ?? _defaultLocalCacheExpiration;

    internal TimeSpan GetL2AbsoluteExpirationRelativeToNow(HybridCacheEntryOptions? options) => options?.Expiration ?? _defaultExpiration;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HybridCacheEntryFlags GetEffectiveFlags(HybridCacheEntryOptions? options)
        => (options?.Flags | _hardFlags) ?? _defaultFlags;

    private static TimeSpan? GetEffectiveLocalCacheExpiration(HybridCacheEntryOptions? options)
    {
        // If LocalCacheExpiration is not specified, then use option's Expiration, to keep in sync by default.
        // Or in other words: the inheritance of "LocalCacheExpiration : Expiration" in a single object takes
        // precedence between the inheritance between per-entry options and global options, and if a caller
        // provides a per-entry option with *just* the Expiration specified, then that is assumed to also
        // specify the LocalCacheExpiration.
        if (options is not null)
        {
            if (options.LocalCacheExpiration is { } local)
            {
                if (options.Expiration is { } overall)
                {
                    // enforce "not exceeding the remaining overall cache lifetime"
                    return local < overall ? local : overall;
                }

                return local;
            }

            return options.Expiration;
        }

        return null;
    }

    // reserve non-printable characters from keys, to prevent potential L2 abuse
    private static bool ContainsReservedCharacters(ReadOnlySpan<char> key)
    {
        const char MaxControlChar = (char)31;

#if NET8_0_OR_GREATER
        return key.IndexOfAnyInRange((char)0, MaxControlChar) >= 0;
#else
        foreach (char c in key)
        {
            if (c <= MaxControlChar)
            {
                return true;
            }
        }

        return false;
#endif
    }

    private bool ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.KeyEmptyOrWhitespace();
            return false;
        }

        if (key.Length > _maximumKeyLength)
        {
            _logger.MaximumKeyLengthExceeded(_maximumKeyLength, key.Length);
            return false;
        }

        if (ContainsReservedCharacters(key.AsSpan()))
        {
            _logger.KeyInvalidContent();
            return false;
        }

        // nothing to complain about
        return true;
    }

    private bool TryGetExisting<T>(string key, [NotNullWhen(true)] out CacheItem<T>? value)
    {
        if (_localCache.TryGetValue(key, out object? untyped) && untyped is CacheItem<T> typed)
        {
            // check tag-based and global invalidation
            if (IsValid(typed))
            {
                value = typed;
                return true;
            }

            // remove from L1; note there's a little unavoidable race here; worst case is that
            // a fresher value gets dropped - we'll have to accept it
            _localCache.Remove(key);
        }

        // failure
        value = null;
        return false;
    }
}
