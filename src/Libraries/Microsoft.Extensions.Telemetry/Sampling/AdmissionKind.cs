// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// The category of a <see cref="BottomKSampler{TCallsite, TPayload}.Admit(TCallsite)"/> decision.
/// </summary>
internal enum AdmissionKind
{
    /// <summary>
    /// The event must be dropped. The caller must not format the log record; this is the fast path
    /// that yields the CPU and allocation savings.
    /// </summary>
    Skip,

    /// <summary>
    /// The event was admitted into the statistical (bottom-K) sample. The caller must format the
    /// payload and call <see cref="BottomKSampler{TCallsite, TPayload}.Insert"/> with the admission.
    /// </summary>
    Admit,

    /// <summary>
    /// The event was rejected by the statistical sample but accepted by the bounded novelty preserve
    /// as a weight-0 observational record. The caller must format the payload and call
    /// <see cref="BottomKSampler{TCallsite, TPayload}.Insert"/> with the admission.
    /// </summary>
    Preserve,
}
