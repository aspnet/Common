// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// A single record produced when a sampling period is flushed. The <see cref="SamplingCount"/> is the
/// Horvitz-Thompson weight: summed across all records of a callsite it is an unbiased estimate of that
/// callsite's true arrival count for the period.
/// </summary>
/// <typeparam name="TCallsite">The callsite identifier type (in production, the durable ID).</typeparam>
/// <typeparam name="TPayload">The formatted log payload type.</typeparam>
internal readonly struct SampledRecord<TCallsite, TPayload> : IEquatable<SampledRecord<TCallsite, TPayload>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SampledRecord{TCallsite, TPayload}"/> struct.
    /// </summary>
    /// <param name="callsite">The callsite identifier.</param>
    /// <param name="payload">The formatted payload.</param>
    /// <param name="samplingCount">
    /// The Horvitz-Thompson weight. A value of <c>0</c> marks an observational novelty-preserve record
    /// that does not contribute to count estimates.
    /// </param>
    public SampledRecord(TCallsite callsite, TPayload payload, double samplingCount)
    {
        Callsite = callsite;
        Payload = payload;
        SamplingCount = samplingCount;
    }

    /// <summary>
    /// Gets the callsite identifier.
    /// </summary>
    public TCallsite Callsite { get; }

    /// <summary>
    /// Gets the formatted payload.
    /// </summary>
    public TPayload Payload { get; }

    /// <summary>
    /// Gets the Horvitz-Thompson sampling weight. A value greater than or equal to <c>1</c> means the
    /// record stands in for that many events; a value of <c>0</c> is an observational novelty record
    /// that does not contribute to estimates.
    /// </summary>
    public double SamplingCount { get; }

    public static bool operator ==(SampledRecord<TCallsite, TPayload> left, SampledRecord<TCallsite, TPayload> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SampledRecord<TCallsite, TPayload> left, SampledRecord<TCallsite, TPayload> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc/>
    public bool Equals(SampledRecord<TCallsite, TPayload> other)
    {
        return EqualityComparer<TCallsite>.Default.Equals(Callsite, other.Callsite)
            && EqualityComparer<TPayload>.Default.Equals(Payload, other.Payload)
            && SamplingCount.Equals(other.SamplingCount);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SampledRecord<TCallsite, TPayload> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (Callsite, Payload, SamplingCount).GetHashCode();
}
