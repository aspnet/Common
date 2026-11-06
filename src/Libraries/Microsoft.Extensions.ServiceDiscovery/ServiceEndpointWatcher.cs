// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery.Internal;

namespace Microsoft.Extensions.ServiceDiscovery;

/// <summary>
/// Watches for updates to the collection of resolved endpoints for a specified service.
/// </summary>
internal sealed partial class ServiceEndpointWatcher(
    IServiceEndpointProvider[] providers,
    ILogger logger,
    string serviceName,
    TimeProvider timeProvider,
    IOptions<ServiceDiscoveryOptions> options) : IDisposable, IAsyncDisposable
{
    private static readonly TimerCallback s_pollingAction = static state => _ = ((ServiceEndpointWatcher)state!).RefreshAsync(force: true);

    private readonly object _lock = new();
    private readonly ILogger _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ServiceDiscoveryOptions _options = options.Value;
    private readonly IServiceEndpointProvider[] _providers = providers;
    private readonly CancellationTokenSource _disposalCancellation = new();
    private ITimer? _pollingTimer;
    private ServiceEndpointSource? _cachedEndpoints;
    private Task _refreshTask = Task.CompletedTask;
    private volatile CacheStatus _cacheState;
    private IDisposable? _changeTokenRegistration;

    /// <summary>
    /// Gets the service name.
    /// </summary>
    public string ServiceName { get; } = serviceName;

    /// <summary>
    /// Gets or sets the action called when endpoints are updated.
    /// </summary>
    public Action<ServiceEndpointResolverResult>? OnEndpointsUpdated { get; set; }

    /// <summary>
    /// Starts the endpoint resolver.
    /// </summary>
    public void Start()
    {
        ThrowIfNoProviders();
        _ = RefreshAsync(force: false);
    }

    /// <summary>
    /// Returns a collection of resolved endpoints for the service.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A collection of resolved endpoints for the service.</returns>
    public ValueTask<ServiceEndpointSource> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNoProviders();
        ObjectDisposedException.ThrowIf(_disposalCancellation.IsCancellationRequested, this);
        cancellationToken.ThrowIfCancellationRequested();

        // If the cache is valid, return the cached value.
        if (_cachedEndpoints is { ChangeToken.HasChanged: false } cached)
        {
            return new ValueTask<ServiceEndpointSource>(cached);
        }

        // Otherwise, ensure the cache is being refreshed
        // Wait for the cache refresh to complete and return the cached value.
        return GetEndpointsInternal(cancellationToken);

        async ValueTask<ServiceEndpointSource> GetEndpointsInternal(CancellationToken cancellationToken)
        {
            ServiceEndpointSource? result;
            var disposalToken = _disposalCancellation.Token;
            do
            {
                disposalToken.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshAsync(force: false).WaitAsync(cancellationToken).ConfigureAwait(false);
                result = _cachedEndpoints;
            } while (result is null);

            return result;
        }
    }

    // Ensures that there is a refresh operation running, if needed, and returns the task which represents the completion of the operation
    private Task RefreshAsync(bool force)
    {
        lock (_lock)
        {
            // If the cache is invalid or needs invalidation, refresh the cache.
            if (!_disposalCancellation.IsCancellationRequested && _refreshTask.IsCompleted && (_cacheState == CacheStatus.Invalid || _cachedEndpoints is null or { ChangeToken.HasChanged: true } || force))
            {
                // Indicate that the cache is being updated and start a new refresh task.
                _cacheState = CacheStatus.Refreshing;

                // Don't capture the current ExecutionContext and its AsyncLocals onto the callback.
                var restoreFlow = false;
                try
                {
                    if (!ExecutionContext.IsFlowSuppressed())
                    {
                        ExecutionContext.SuppressFlow();
                        restoreFlow = true;
                    }

                    _refreshTask = RefreshAsyncInternal();
                }
                finally
                {
                    if (restoreFlow)
                    {
                        ExecutionContext.RestoreFlow();
                    }
                }
            }

            return _refreshTask;
        }
    }

    private async Task RefreshAsyncInternal()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var cancellationToken = _disposalCancellation.Token;
        Exception? error = null;
        ServiceEndpointSource? newEndpoints = null;
        CacheStatus newCacheState;
        try
        {
            IDisposable? changeTokenRegistration;
            lock (_lock)
            {
                // Capture the existing change token registration, if any, so we can dispose it outside the lock.
                // Disposing inside the lock can cause a deadlock since Dispose waits for any in-flight callback to complete,
                // but the callback may be waiting to acquire _lock.
                changeTokenRegistration = _changeTokenRegistration;
                _changeTokenRegistration = null;
            }

            // Dispose the existing change token registration, if any, outside the lock to avoid deadlock.
            changeTokenRegistration?.Dispose();

            Log.ResolvingEndpoints(_logger, ServiceName);
            var builder = new ServiceEndpointBuilder();
            foreach (var provider in _providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await provider.PopulateAsync(builder, cancellationToken).ConfigureAwait(false);
            }

            var endpoints = builder.Build();
            newCacheState = CacheStatus.Valid;

            ITimer? pollingTimerToDispose = null;
            lock (_lock)
            {
                if (_disposalCancellation.IsCancellationRequested)
                {
                    pollingTimerToDispose = _pollingTimer;
                    _pollingTimer = null;
                }

                // Check if we need to poll for updates or if we can register for change notification callbacks.
                else if (endpoints.ChangeToken.ActiveChangeCallbacks)
                {
                    // Initiate a background refresh when the change token fires.
                    _changeTokenRegistration = endpoints.ChangeToken.RegisterChangeCallback(static state => _ = ((ServiceEndpointWatcher)state!).RefreshAsync(force: false), this);

                    // Dispose the existing timer, if any, since we are reliant on change tokens for updates.
                    pollingTimerToDispose = _pollingTimer;
                    _pollingTimer = null;
                }
                else
                {
                    SchedulePollingTimer();
                }

                // The cache is valid
                newEndpoints = endpoints;
                newCacheState = CacheStatus.Valid;
            }

            pollingTimerToDispose?.Dispose();
        }
        catch (Exception exception)
        {
            error = exception;
            newCacheState = CacheStatus.Invalid;
            SchedulePollingTimer();
        }

        // If there was an error, the cache must be invalid.
        Debug.Assert(error is null || newCacheState is CacheStatus.Invalid);

        // To ensure coherence between the value returned by calls made to GetEndpointsAsync and value passed to the callback,
        // we invalidate the cache before invoking the callback. This causes callers to wait on the refresh task
        // before receiving the updated value. An alternative approach is to lock access to _cachedEndpoints, but
        // that will have more overhead in the common case.
        if (newCacheState is CacheStatus.Valid)
        {
            Interlocked.Exchange(ref _cachedEndpoints, null);
        }

        if (OnEndpointsUpdated is { } callback)
        {
            try
            {
                callback(new(newEndpoints, error));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error notifying observers of updated endpoints.");
            }
        }

        lock (_lock)
        {
            if (newCacheState is CacheStatus.Valid)
            {
                Debug.Assert(newEndpoints is not null);
                _cachedEndpoints = newEndpoints;
            }

            _cacheState = newCacheState;
        }

        if (error is not null)
        {
            Log.ResolutionFailed(_logger, error, ServiceName);
            ExceptionDispatchInfo.Throw(error);
        }
        else if (newEndpoints is not null)
        {
            Log.ResolutionSucceeded(_logger, ServiceName, newEndpoints);
        }
    }

    private void SchedulePollingTimer()
    {
        ITimer? pollingTimerToDispose = null;
        lock (_lock)
        {
            if (_disposalCancellation.IsCancellationRequested)
            {
                pollingTimerToDispose = _pollingTimer;
                _pollingTimer = null;
            }
            else if (_pollingTimer is null)
            {
                _pollingTimer = _timeProvider.CreateTimer(s_pollingAction, this, _options.RefreshPeriod, TimeSpan.Zero);
            }
            else
            {
                _pollingTimer.Change(_options.RefreshPeriod, TimeSpan.Zero);
            }
        }

        pollingTimerToDispose?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => DisposeCore();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        DisposeCore();

        if (_refreshTask is { } task)
        {
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void DisposeCore()
    {
        try
        {
            _disposalCancellation.Cancel();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error cancelling disposal cancellation token.");
        }

        IDisposable? changeTokenRegistration;
        ITimer? pollingTimer;
        lock (_lock)
        {
            // Capture the existing change token registration and polling timer so we can dispose them outside the lock.
            // Disposing _changeTokenRegistration inside the lock can cause a deadlock since Dispose waits for any in-flight
            // callback to complete, but the callback may be waiting to acquire _lock.
            changeTokenRegistration = _changeTokenRegistration;
            _changeTokenRegistration = null;

            pollingTimer = _pollingTimer;
            _pollingTimer = null;
        }

        changeTokenRegistration?.Dispose();
        pollingTimer?.Dispose();
    }

    private enum CacheStatus
    {
        Invalid,
        Refreshing,
        Valid
    }

    private void ThrowIfNoProviders()
    {
        if (_providers.Length == 0)
        {
            ThrowNoProvidersConfigured();
        }
    }

    [DoesNotReturn]
    private static void ThrowNoProvidersConfigured() => throw new InvalidOperationException("No service endpoint providers are configured.");
}
