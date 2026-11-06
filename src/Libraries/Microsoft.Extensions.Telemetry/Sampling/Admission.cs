// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// The result of an admission attempt. For an <see cref="AdmissionKind.Admit"/> outcome it also
/// carries the EXP rank that must be handed back verbatim to
/// <see cref="BottomKSampler{TCallsite, TPayload}.Insert"/> so the sampler can order its heap.
/// </summary>
internal readonly struct Admission : IEquatable<Admission>
{
    private Admission(AdmissionKind kind, double key)
    {
        Kind = kind;
        Key = key;
    }

    /// <summary>
    /// Gets a shared <see cref="AdmissionKind.Skip"/> admission.
    /// </summary>
    public static Admission Skip { get; } = new(AdmissionKind.Skip, double.NaN);

    /// <summary>
    /// Gets a shared <see cref="AdmissionKind.Preserve"/> admission.
    /// </summary>
    public static Admission Preserve { get; } = new(AdmissionKind.Preserve, double.NaN);

    /// <summary>
    /// Gets the admission category.
    /// </summary>
    public AdmissionKind Kind { get; }

    /// <summary>
    /// Gets the EXP rank <c>-ln(u) / w_c</c> used for bottom-K heap ordering. Only meaningful when
    /// <see cref="Kind"/> is <see cref="AdmissionKind.Admit"/>; otherwise <see cref="double.NaN"/>.
    /// </summary>
    public double Key { get; }

    public static bool operator ==(Admission left, Admission right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Admission left, Admission right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Creates an <see cref="AdmissionKind.Admit"/> admission carrying its EXP rank.
    /// </summary>
    /// <param name="key">The EXP rank <c>-ln(u) / w_c</c> for heap ordering.</param>
    /// <returns>An admit admission.</returns>
    public static Admission Admit(double key) => new(AdmissionKind.Admit, key);

    /// <inheritdoc/>
    public bool Equals(Admission other) => Kind == other.Kind && Key.Equals(other.Key);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Admission other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (Kind, Key).GetHashCode();
}
