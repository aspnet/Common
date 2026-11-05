// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.Metrics.Testing;

/// <summary>
/// Collects the measurements published from an <see cref="Instrument{T}"/> or <see cref="ObservableInstrument{T}"/>.
/// </summary>
/// <typeparam name="T">The type of metric data being recorded.</typeparam>
[DebuggerDisplay("{_measurements.Count} measurements")]
public sealed class MetricCollector<T> : IDisposable
    where T : struct
{
    private static readonly HashSet<Type> _supportedTs =
    [
        typeof(int),
        typeof(byte),
        typeof(short),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
    ];

    internal int WaitersCount => _waiters.Count; // Internal for testing
    private readonly MeterListener _meterListener = new();
    private readonly List<CollectedMeasurement<T>> _measurements = [];
    private readonly List<Waiter> _waiters = [];
    private readonly TimeProvider _timeProvider;
    private bool _disposed;
    private Instrument? _instrument;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollector{T}"/> class.
    /// </summary>
    /// <param name="instrument">The <see cref="Instrument{T}" /> to record measurements from.</param>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use the system time provider.</param>
    public MetricCollector(Instrument<T> instrument, TimeProvider? timeProvider = null)
        : this(timeProvider)
    {
        _instrument = Throw.IfNull(instrument);
        _meterListener.SetMeasurementEventCallback<T>(OnMeasurementRecorded);
        _meterListener.EnableMeasurementEvents(instrument);
        _meterListener.Start();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollector{T}"/> class.
    /// </summary>
    /// <param name="instrument">The <see cref="ObservableInstrument{T}" /> to record measurements from.</param>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use the system time provider.</param>
    public MetricCollector(ObservableInstrument<T> instrument, TimeProvider? timeProvider = null)
        : this(timeProvider)
    {
        _instrument = Throw.IfNull(instrument);
        _meterListener.SetMeasurementEventCallback<T>(OnMeasurementRecorded);
        _meterListener.EnableMeasurementEvents(instrument);
        _meterListener.Start();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollector{T}"/> class.
    /// </summary>
    /// <param name="meterScope">The scope of the meter that publishes the instrument to record.
    /// Take caution when using Meters in the global scope (scope == null). This interacts with
    /// static mutable data and tests doing this should not be run in parallel with each other.
    /// </param>
    /// <param name="meterName">The name of the meter that publishes the instrument to record.</param>
    /// <param name="instrumentName">The name of the instrument to record.</param>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use the system time provider.</param>
    /// <remarks>
    /// Both the meter name and scope are used to identity the meter of interest.
    /// </remarks>
    public MetricCollector(object? meterScope, string meterName, string instrumentName, TimeProvider? timeProvider = null)
        : this(timeProvider)
    {
        _ = Throw.IfNullOrEmpty(meterName);
        _ = Throw.IfNullOrEmpty(instrumentName);

        Initialize(instrument => Equals(instrument.Meter.Scope, meterScope) && instrument.Meter.Name == meterName && instrument.Name == instrumentName);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollector{T}"/> class.
    /// </summary>
    /// <param name="meter">The meter that publishes the instrument to record.</param>
    /// <param name="instrumentName">The name of the instrument to record.</param>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use the system time provider.</param>
    public MetricCollector(Meter meter, string instrumentName, TimeProvider? timeProvider = null)
        : this(timeProvider)
    {
        _ = Throw.IfNull(meter);
        _ = Throw.IfNullOrEmpty(instrumentName);

        Initialize(instrument => ReferenceEquals(instrument.Meter, meter) && instrument.Name == instrumentName);
    }

    private MetricCollector(TimeProvider? timeProvider)
    {
        if (!_supportedTs.Contains(typeof(T)))
        {
            var str = string.Join(", ", _supportedTs.Select(t => t.Name).ToArray());
            throw new InvalidOperationException($"MetricCollector can only be created for the following types: {str}.");
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Disposes the <see cref="MetricCollector{T}"/> and stops recording measurements.
    /// </summary>
    public void Dispose()
    {
        lock (_measurements)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _meterListener.Dispose();
        _measurements.Clear();

        // wake up anybody still waiting and inform them of the bad news: their horse is dead...
        foreach (var w in _waiters)
        {
            // trigger the task from outside the lock
            _ = w.TaskSource.TrySetException(MakeObjectDisposedException());
        }

        _waiters.Clear();
    }

    /// <summary>
    /// Gets the <see cref="Instrument"/> that is being recorded.
    /// </summary>
    /// <remarks>
    /// This may be <see langword="null"/> until the instrument is published.
    /// </remarks>
    public Instrument? Instrument => _instrument;

    /// <summary>
    /// Removes all accumulated measurements from the collector.
    /// </summary>
    public void Clear()
    {
        lock (_measurements)
        {
            ThrowIfDisposed();
            _measurements.Clear();
        }
    }

    /// <summary>
    /// Gets a snapshot of measurements collected by this collector.
    /// </summary>
    /// <param name="clear">Setting this to <see langword="true"/> will atomically clear the set of accumulated measurements.</param>
    /// <returns>The measurements recorded by this collector, ordered by recording time.</returns>
    public IReadOnlyList<CollectedMeasurement<T>> GetMeasurementSnapshot(bool clear = false)
    {
        lock (_measurements)
        {
            ThrowIfDisposed();

            var measurements = _measurements.ToArray();
            if (clear)
            {
                _measurements.Clear();
            }

            return measurements;
        }
    }

    /// <summary>
    /// Gets the latest measurement collected, if any.
    /// </summary>
    public CollectedMeasurement<T>? LastMeasurement
    {
        get
        {
            lock (_measurements)
            {
                ThrowIfDisposed();
                return _measurements.Count > 0 ? _measurements[_measurements.Count - 1] : null;
            }
        }
    }

    /// <summary>
    /// Returns a task that completes when the collector has collected a minimum number of measurements.
    /// </summary>
    /// <param name="minCount">The minimum number of measurements to wait for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the collector has collected the requisite number of measurements.</returns>
    public Task WaitForMeasurementsAsync(int minCount, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfLessThan(minCount, 1);

        Waiter w;
        lock (_measurements)
        {
            ThrowIfDisposed();

            if (_measurements.Count >= minCount)
            {
                return Task.CompletedTask;
            }

            w = new Waiter(minCount);
            _waiters.Add(w);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = cancellationToken.Register(() =>
            {
                lock (_measurements)
                {
                    _ = _waiters.Remove(w);
                }

                // trigger the task from outside the lock
                _ = w.TaskSource.TrySetCanceled(cancellationToken);
            });
        }

        return w.TaskSource.Task;
    }

    /// <summary>
    /// Returns a task that completes when the collector has collected a minimum number of measurements.
    /// </summary>
    /// <param name="minCount">The minimum number of measurements to wait for.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>A task that completes when the collector has collected the requisite number of measurements.</returns>
    [SuppressMessage("Resilience", "EA0014:The async method doesn't support cancellation", Justification = "Not relevant in this case")]
    public async Task WaitForMeasurementsAsync(int minCount, TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
        using var cancellationTokenSource = new CancellationTokenSource(timeout, _timeProvider);
#else
        using var cancellationTokenSource = _timeProvider.CreateCancellationTokenSource(timeout);
#endif
        await WaitForMeasurementsAsync(minCount, cancellationTokenSource.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Records all registered observable instruments.
    /// </summary>
    public void RecordObservableInstruments()
    {
        ThrowIfDisposed();
        _meterListener.RecordObservableInstruments();
    }

    private void Initialize(Func<Instrument, bool> instrumentPredicate)
    {
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if ((instrument is ObservableInstrument<T> or Instrument<T>) && instrumentPredicate(instrument))
            {
                if (Interlocked.CompareExchange(ref _instrument, instrument, null) is null)
                {
                    // no need to hear about new instruments being published
                    listener.InstrumentPublished = null;

                    // get ready to listen to measurement events
                    listener.SetMeasurementEventCallback<T>(OnMeasurementRecorded);

                    // let the flood gates open
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        // start listening to stuff...
        _meterListener.Start();
    }

    private void OnMeasurementRecorded(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var m = new CollectedMeasurement<T>(measurement, tags, _timeProvider.GetUtcNow());

        List<Waiter>? toBeWoken = null;
        lock (_measurements)
        {
            if (!_disposed)
            {
                // record the measurement
                _measurements.Add(m);

                // wake up any waiters that need it
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_measurements.Count >= _waiters[i].MinCount)
                    {
                        toBeWoken ??= [];
                        toBeWoken.Add(_waiters[i]);
                        _waiters.RemoveAt(i);
                    }
                }
            }
        }

        if (toBeWoken != null)
        {
            // trigger the task from outside the lock
            foreach (var w in toBeWoken)
            {
                // we use TrySetResult since the task may already be in the Cancelled state due to a timeout.
                _ = w.TaskSource.TrySetResult(true);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw MakeObjectDisposedException();
        }
    }

    private ObjectDisposedException MakeObjectDisposedException()
        => _instrument != null
            ? new(nameof(MetricCollector<T>), $"The metric collector instance for instrument '{_instrument.Name}' of meter '{_instrument.Meter.Name}' has been disposed.")
            : new(nameof(MetricCollector<T>));

    private readonly struct Waiter
    {
        public Waiter(int minCount)
        {
            MinCount = minCount;
        }

        public int MinCount { get; }

        // NOTE: In order to avoid potential dead locks, this task should
        // be completed when the main lock is not being held. Otherwise,
        // application code being woken up by the task could potentially
        // call back into the MetricCollector code and thus trigger a deadlock.
        public TaskCompletionSource<bool> TaskSource { get; } = new();
    }
}
