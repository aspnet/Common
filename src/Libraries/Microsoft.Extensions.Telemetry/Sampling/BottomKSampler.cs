// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.Sampling;

#pragma warning disable CA5394 // Do not use insecure randomness - acceptable for the purposes of sampling

/// <summary>
/// Adaptive bottom-K log sampler.
/// </summary>
/// <remarks>
/// <para>
/// Bottom-K uses a bottom-(K+1) weighted reservoir sketch with exponential ranks (a WS-sketch, Cohen &amp;
/// Kaplan 2007, built on the priority sampling of Duffield, Lund &amp; Thorup 2007). Across periods it
/// feeds the previous period's per-callsite arrival counts back as inverse-frequency weights, so
/// chatty callsites are sampled hard while rare ones are kept. A Chao1 / Good-Turing estimate (Chao
/// 1984) weights as-yet-unseen callsites, and a bounded novelty preserve keeps one example of each
/// first-rejected callsite as a weight-0 observational record for tail coverage.
/// </para>
/// <para>
/// The type is single-threaded by design: use one instance per thread. The <see cref="Admit"/> fast
/// path avoids formatting the payload for dropped events, which is the source of the CPU and
/// allocation savings.
/// </para>
/// </remarks>
/// <typeparam name="TCallsite">The callsite identifier type (in production, the durable ID).</typeparam>
/// <typeparam name="TPayload">The formatted log payload type.</typeparam>
internal sealed class BottomKSampler<TCallsite, TPayload>
    where TCallsite : notnull
{
    private const long DefaultMinPeriodCount = 32;

    private readonly int _reservoirCapacity;
    private readonly int _preserveCapacity;
    private readonly long _minPeriodCount;
    private readonly BottomKUnseenWeightMode _unseenWeightMode;
    private readonly Action<TPayload>? _releasePayload;
    private readonly Random _rng;
    private readonly List<HeapEntry> _heap;
    private readonly Dictionary<TCallsite, CallsiteState> _states;

    private Dictionary<TCallsite, long> _freqPrev;
    private Dictionary<TCallsite, long> _freqCurr;
    private long _seqCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="BottomKSampler{TCallsite, TPayload}"/> class using the
    /// default preserve capacity (equal to <paramref name="reservoirCapacity"/>), the default Chao1
    /// stability threshold, <see cref="BottomKUnseenWeightMode.Chao1"/>, and an OS-derived seed.
    /// </summary>
    /// <param name="reservoirCapacity">The sample size <c>T</c> per period. Must be at least 1.</param>
    public BottomKSampler(int reservoirCapacity)
        : this(reservoirCapacity, reservoirCapacity, DefaultMinPeriodCount, BottomKUnseenWeightMode.Chao1, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BottomKSampler{TCallsite, TPayload}"/> class with explicit
    /// configuration.
    /// </summary>
    /// <param name="reservoirCapacity">The sample size <c>T</c> per period. Must be at least 1.</param>
    /// <param name="preserveCapacity">The novelty-preserve capacity <c>R</c> (<c>0</c> disables the preserve). Must not be negative.</param>
    /// <param name="minPeriodCount">The minimum prior-period arrival count below which the frozen table is discarded and the next period is treated as warmup. Must not be negative.</param>
    /// <param name="unseenWeightMode">The strategy used to weight unseen callsites.</param>
    /// <param name="seed">An optional RNG seed for deterministic behavior; <see langword="null"/> uses an OS-derived seed.</param>
    /// <param name="releasePayload">An optional callback invoked for payloads excluded from the final sample.</param>
    public BottomKSampler(
        int reservoirCapacity,
        int preserveCapacity,
        long minPeriodCount,
        BottomKUnseenWeightMode unseenWeightMode,
        int? seed,
        Action<TPayload>? releasePayload = null)
    {
        _reservoirCapacity = Throw.IfLessThan(reservoirCapacity, 1);
        _preserveCapacity = Throw.IfLessThan(preserveCapacity, 0);
        _minPeriodCount = Throw.IfLessThan(minPeriodCount, 0);
        _unseenWeightMode = unseenWeightMode;
        _releasePayload = releasePayload;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _heap = new List<HeapEntry>(reservoirCapacity + 2);
        _states = new Dictionary<TCallsite, CallsiteState>(reservoirCapacity + preserveCapacity);
        _freqPrev = new Dictionary<TCallsite, long>(reservoirCapacity);
        _freqCurr = new Dictionary<TCallsite, long>(reservoirCapacity);
        _seqCounter = 0;
        ReserveLength = 0;
        Tau = double.PositiveInfinity;

        // Until the first flush every callsite is "unseen" with weight 1.0, i.e. we behave as a
        // uniform reservoir.
        UnseenWeight = 1.0;
    }

    /// <summary>
    /// Gets the current threshold <c>tau</c>. Decreases monotonically within a period and resets to
    /// <see cref="double.PositiveInfinity"/> at flush. Exposed for tests and metrics.
    /// </summary>
    public double Tau { get; private set; }

    /// <summary>
    /// Gets the current unseen-callsite weight, frozen at the last flush. Exposed for tests and metrics.
    /// </summary>
    public double UnseenWeight { get; private set; }

    /// <summary>
    /// Gets the number of callsites in the prior-period frozen table.
    /// </summary>
    public int FrozenCallsites => _freqPrev.Count;

    /// <summary>
    /// Gets the current novelty-preserve occupancy.
    /// </summary>
    public int ReserveLength { get; private set; }

    /// <inheritdoc/>
    public Admission Admit(TCallsite callsite)
    {
        // Always count arrivals for the next period's frozen table, even when skipped.
        _freqCurr[callsite] = _freqCurr.TryGetValue(callsite, out var current) ? current + 1 : 1;

        double wC = WeightFor(_freqPrev, callsite, UnseenWeight);
        double u = _rng.NextDouble();
        if (u <= 0.0)
        {
            u = 1e-300;
        }

        // EXP rank: -ln(U(0,1]) / w ~ Exp(w). See Cohen & Kaplan for the rank-function family.
        double k = -Math.Log(u) / wC;

        if (k < Tau)
        {
            return Admission.Admit(k);
        }

        // K-rejected. Consider the novelty preserve. Skip when the callsite is already represented
        // anywhere in the state map (heap presence would violate disjointness; preserve presence is
        // the first-rejection-wins rule).
        if (ReserveLength < _preserveCapacity && !_states.ContainsKey(callsite))
        {
            return Admission.Preserve;
        }

        return Admission.Skip;
    }

    /// <inheritdoc/>
    public void Insert(TCallsite callsite, Admission admission, TPayload payload)
    {
        switch (admission.Kind)
        {
            case AdmissionKind.Admit:
                InsertAdmit(callsite, admission.Key, payload);
                break;

            case AdmissionKind.Preserve:
                InsertPreserve(callsite, payload);
                break;

            default:
                // Skip admissions must never reach Insert.
                Throw.ArgumentException(nameof(admission), "Insert must not be called for a Skip admission.");
                break;
        }
    }

    /// <inheritdoc/>
    public void FlushInto(ICollection<SampledRecord<TCallsite, TPayload>> output)
    {
        _ = Throw.IfNull(output);

        double finalTau = double.PositiveInfinity;
        if (_heap.Count > _reservoirCapacity)
        {
            HeapEntry excluded = HeapPopMax();
            finalTau = excluded.Key;
            ReleasePayload(excluded.Payload);
        }

        // (1) Drain the bottom-K heap with Horvitz-Thompson weights. When K+1 ranks were retained,
        // the removed maximum is the first excluded rank and therefore the independent threshold.
        foreach (var entry in _heap)
        {
            double wC = WeightFor(_freqPrev, entry.Callsite, UnseenWeight);
            double samplingCount;
            if (double.IsInfinity(finalTau))
            {
                samplingCount = 1.0;
            }
            else
            {
                // EXP-rank inclusion probability: pi = 1 - exp(-tau * w_c).
                double pi = -Expm1(-finalTau * wC);
                samplingCount = pi > 0.0 ? Math.Max(1.0, 1.0 / pi) : 1.0;
            }

            output.Add(new SampledRecord<TCallsite, TPayload>(entry.Callsite, entry.Payload, samplingCount));
        }

        _heap.Clear();

        // (2) Drain the preserve slots as weight-0 observational novelty records. Heap entries were
        // already emitted above, and the heap/preserve disjointness invariant guarantees these
        // callsites are not double-counted.
        foreach (var pair in _states)
        {
            if (pair.Value.Preserve is { } preserve)
            {
                output.Add(new SampledRecord<TCallsite, TPayload>(pair.Key, preserve.Payload, 0.0));
            }
        }

        _states.Clear();
        ReserveLength = 0;

        // (3) Period-boundary bookkeeping.
        long observed = 0;
        foreach (var value in _freqCurr.Values)
        {
            observed += value;
        }

        if (observed < _minPeriodCount)
        {
            _freqPrev.Clear();
            UnseenWeight = 1.0;
        }
        else
        {
            UnseenWeight = ComputeUnseenWeight();
            (_freqPrev, _freqCurr) = (_freqCurr, _freqPrev);
        }

        _freqCurr.Clear();
        Tau = double.PositiveInfinity;
    }

    /// <inheritdoc/>
    public List<SampledRecord<TCallsite, TPayload>> Flush()
    {
        var output = new List<SampledRecord<TCallsite, TPayload>>(_heap.Count + ReserveLength);
        FlushInto(output);
        return output;
    }

    private static double WeightFor(Dictionary<TCallsite, long> freqPrev, TCallsite callsite, double unseenWeight)
        => freqPrev.TryGetValue(callsite, out var frequency) ? 1.0 / frequency : unseenWeight;

    /// <summary>
    /// A netstandard2.0-safe <c>exp(x) - 1</c> that stays accurate near zero, where the inclusion
    /// probability would otherwise suffer catastrophic cancellation.
    /// </summary>
    /// <param name="x">The exponent.</param>
    /// <returns><c>exp(x) - 1</c>.</returns>
    private static double Expm1(double x)
    {
        if (Math.Abs(x) < 1e-5)
        {
            // Two-term Taylor series; the truncation error is O(x^3) which is negligible here.
            return x + (0.5 * x * x);
        }

        return Math.Exp(x) - 1.0;
    }

    private void InsertAdmit(TCallsite callsite, double key, TPayload payload)
    {
        if (!_states.TryGetValue(callsite, out var state))
        {
            state = new CallsiteState();
            _states[callsite] = state;
        }

        // A heap admission supplants any pre-existing preserve slot for this callsite.
        if (state.Preserve is { } preserve)
        {
            ReleasePayload(preserve.Payload);
            state.Preserve = null;
            ReserveLength--;
        }

        state.HeapCount++;
        HeapPush(new HeapEntry(key, callsite, payload));

        // Retain K+1 ranks. The maximum is the first item excluded from the bottom-K sample and is
        // kept until flush so its rank remains independent of every emitted record's own rank.
        if (_heap.Count > _reservoirCapacity + 1)
        {
            HeapEntry evicted = HeapPopMax();
            ReleasePayload(evicted.Payload);

            // The evicted entry may be the one just pushed (when its key is the new maximum), in which
            // case the increment and decrement cancel.
            if (_states.TryGetValue(evicted.Callsite, out var evictedState))
            {
                evictedState.HeapCount--;
                if (evictedState.IsEmpty)
                {
                    _ = _states.Remove(evicted.Callsite);
                }
            }
        }

        Tau = _heap.Count > _reservoirCapacity
            ? _heap[0].Key
            : double.PositiveInfinity;
    }

    private void InsertPreserve(TCallsite callsite, TPayload payload)
    {
        long seq = _seqCounter;
        _seqCounter++;

        // Only create the preserve slot when the callsite has no current heap or preserve entry: heap
        // presence wins by disjointness, preserve presence by first-rejection-wins.
        if (!_states.ContainsKey(callsite))
        {
            _states[callsite] = new CallsiteState { Preserve = (payload, seq) };
            ReserveLength++;
            return;
        }

        // Ownership transfers to the sampler on Insert. A stale preserve admission can lose a race
        // to another insertion for the same callsite, so release the payload it cannot retain.
        ReleasePayload(payload);
    }

    private double ComputeUnseenWeight()
    {
        if (_unseenWeightMode == BottomKUnseenWeightMode.RarestSeen)
        {
            // The rarest seen callsite has the smallest frequency, hence the largest weight.
            long minFrequency = 0;
            foreach (var value in _freqCurr.Values)
            {
                if (value > 0 && (minFrequency == 0 || value < minFrequency))
                {
                    minFrequency = value;
                }
            }

            if (minFrequency == 0)
            {
                return 1.0;
            }

            double weight = 1.0 / minFrequency;
            return weight < 1.0 ? weight : 1.0;
        }

        return ChaoEstimator.Chao1UnseenWeight(_freqCurr.Values);
    }

    private void HeapPush(HeapEntry entry)
    {
        _heap.Add(entry);
        int i = _heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_heap[parent].Key >= _heap[i].Key)
            {
                break;
            }

            (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
            i = parent;
        }
    }

    private HeapEntry HeapPopMax()
    {
        HeapEntry root = _heap[0];
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);
        if (_heap.Count > 0)
        {
            SiftDown(0);
        }

        return root;
    }

    private void ReleasePayload(TPayload payload) => _releasePayload?.Invoke(payload);

    private void SiftDown(int start)
    {
        int i = start;
        int count = _heap.Count;
        while (true)
        {
            int left = (2 * i) + 1;
            int right = (2 * i) + 2;
            int largest = i;

            if (left < count && _heap[left].Key > _heap[largest].Key)
            {
                largest = left;
            }

            if (right < count && _heap[right].Key > _heap[largest].Key)
            {
                largest = right;
            }

            if (largest == i)
            {
                break;
            }

            (_heap[i], _heap[largest]) = (_heap[largest], _heap[i]);
            i = largest;
        }
    }

    /// <summary>
    /// One heap entry. <see cref="Key"/> is the EXP rank; the containing list is maintained as a
    /// max-heap so its root is the current threshold <c>tau</c>.
    /// </summary>
    private readonly struct HeapEntry
    {
        public HeapEntry(double key, TCallsite callsite, TPayload payload)
        {
            Key = key;
            Callsite = callsite;
            Payload = payload;
        }

        public double Key { get; }

        public TCallsite Callsite { get; }

        public TPayload Payload { get; }
    }

    /// <summary>
    /// Per-callsite live state: reservoir multiplicity plus an optional novelty-preserve slot. The two
    /// are mutually exclusive.
    /// </summary>
    private sealed class CallsiteState
    {
        public uint HeapCount { get; set; }

        public (TPayload Payload, long Seq)? Preserve { get; set; }

        public bool IsEmpty => HeapCount == 0 && !Preserve.HasValue;
    }
}
#endif
