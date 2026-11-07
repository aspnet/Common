// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.ServiceDiscovery.Internal;

/// <summary>
/// An <see cref="IChangeToken"/> which signals when any of the change tokens it is linked to signals.
/// </summary>
/// <remarks>
/// <para>
/// This serves the same purpose as <see cref="CompositeChangeToken"/>, but it holds callbacks on its sources
/// only for as long as a consumer is listening. <see cref="CompositeChangeToken"/> registers on its sources on
/// behalf of its consumers and releases those registrations only when it signals, so linking a token which
/// never signals roots the composite and everything it references for the lifetime of that source.
/// </para>
/// <para>
/// Here a consumer's registration is made directly on each source and owns those source registrations, so a
/// consumer releasing its own registration, which is what consumers already do, is all that is needed. Nothing
/// has to remember to release this token.
/// </para>
/// <para>
/// Registering directly on the sources also means the callback behaviour of this token is whatever its sources
/// provide, rather than being reshaped by an intermediate <see cref="CancellationTokenSource"/>. As
/// <see cref="IChangeToken"/> allows, callbacks are best effort; <see cref="HasChanged"/>, which polls the
/// sources, is the reliable way to observe a change.
/// </para>
/// </remarks>
internal sealed class LinkedChangeToken : IChangeToken
{
    private readonly IReadOnlyList<IChangeToken> _sources;
    private volatile bool _hasChanged;

    /// <summary>
    /// Initializes a new <see cref="LinkedChangeToken"/> instance.
    /// </summary>
    /// <param name="sources">The change tokens to link to.</param>
    public LinkedChangeToken(IReadOnlyList<IChangeToken> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;

        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i].ActiveChangeCallbacks)
            {
                ActiveChangeCallbacks = true;
                break;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Callbacks are raised only by sources which raise them. Changes to the other sources are observed only by
    /// polling <see cref="HasChanged"/>, which matches <see cref="CompositeChangeToken"/>.
    /// </remarks>
    public bool ActiveChangeCallbacks { get; }

    /// <inheritdoc/>
    public bool HasChanged
    {
        get
        {
            if (_hasChanged)
            {
                return true;
            }

            for (var i = 0; i < _sources.Count; i++)
            {
                if (_sources[i].HasChanged)
                {
                    _hasChanged = true;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets or sets a callout made by a signalling source once it has claimed a consumer's callback and before it
    /// reads the state that callback was registered with.
    /// </summary>
    /// <remarks>
    /// Only tests set this; it is null everywhere else. Those two steps are adjacent instructions, so setting this
    /// is the only way a test can decide how the race between Registration.Dispose and
    /// Registration.OnSourceSignaled comes out, and therefore the only way a test can show that this race is not a
    /// problem.
    /// </remarks>
    internal Action? OnCallbackClaimed { get; set; }

    /// <inheritdoc/>
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        // A registration holding no callback is how a claimed or disposed registration is represented, so a null
        // callback would silently produce a registration which is dead on arrival.
        ArgumentNullException.ThrowIfNull(callback);

        var registration = new Registration(this, callback, state);

        // Linked after construction rather than in the constructor, because a source which has already signaled
        // raises the callback during linking and must not observe a partially constructed registration.
        registration.LinkToSources();
        return registration;
    }

    /// <summary>
    /// A consumer's registration, which holds that consumer's registration on each of the sources and releases
    /// them when it is disposed or when one of the sources signals.
    /// </summary>
    private sealed class Registration : IDisposable
    {
        // Cached so that registering on a source does not allocate a delegate. The callback shape is dictated by
        // IChangeToken.RegisterChangeCallback; passing the registration as its state keeps it closure-free.
        private static readonly Action<object?> s_onSourceSignaled = static state => ((Registration)state!).OnSourceSignaled();

        private readonly LinkedChangeToken _token;
        private readonly IDisposable?[] _sourceRegistrations;
        private Action<object?>? _callback;
        private object? _state;

        public Registration(LinkedChangeToken token, Action<object?> callback, object? state)
        {
            _token = token;
            _callback = callback;
            _state = state;
            _sourceRegistrations = new IDisposable?[token._sources.Count];
        }

        /// <summary>
        /// Registers this consumer's callback on each source which raises callbacks.
        /// </summary>
        public void LinkToSources()
        {
            var sources = _token._sources;

            for (var i = 0; i < sources.Count; i++)
            {
                if (sources[i].ActiveChangeCallbacks)
                {
                    // A source which has already signaled may raise the callback here, synchronously. Sources
                    // backed by a CancellationToken do, but IChangeToken does not require it, so a change is
                    // only reliably observed by polling HasChanged.
                    _sourceRegistrations[i] = sources[i].RegisterChangeCallback(s_onSourceSignaled, this);
                }
            }

            // A null callback means a source signaled, or the consumer disposed, while this loop was still
            // running, so Release could not see every registration it was meant to release. Release the rest.
            if (Volatile.Read(ref _callback) is null)
            {
                Release();
            }
        }

        public void Dispose()
        {
            // Cleared before releasing, so that a concurrent LinkToSources sees that it has to release the
            // registrations it makes after this point.
            if (Interlocked.Exchange(ref _callback, null) is not null)
            {
                // Taking the callback is what claims the state, so the state is dropped here only when it was this
                // disposal which suppressed the callback. A source signalling concurrently may have taken the
                // callback instead, and it has to be able to hand the consumer the state it registered with.
                _state = null;
            }

            Release();
        }

        private void OnSourceSignaled()
        {
            // Only the first source to signal raises the consumer's callback, and a consumer which has disposed
            // its registration is not called at all.
            if (Interlocked.Exchange(ref _callback, null) is not { } callback)
            {
                return;
            }

            _token.OnCallbackClaimed?.Invoke();

            // Taking the callback above claimed the state, so a concurrent disposal cannot clear it from underneath
            // this callback, and that exchange orders this read after the write which registration made.
            var state = _state;
            _state = null;
            _token._hasChanged = true;

            try
            {
                callback(state);
            }
            finally
            {
                Release();
            }
        }

        private void Release()
        {
            for (var i = 0; i < _sourceRegistrations.Length; i++)
            {
                // Exchanged so that releasing more than once, which linking and a signaling source can both
                // cause, disposes each source registration exactly once.
                Interlocked.Exchange(ref _sourceRegistrations[i], null)?.Dispose();
            }
        }
    }
}
