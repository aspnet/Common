// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// Chao1 / Good-Turing estimators used to weight as-yet-unseen callsites so the tail of the
/// distribution is not systematically under-sampled. See Chao, <i>Scand. J. Statist.</i> (1984).
/// </summary>
internal static class ChaoEstimator
{
    /// <summary>
    /// Computes the Good-Turing-derived <c>unseen_weight</c> from a sample of per-callsite frequencies
    /// using Chao1's species-richness lower bound. The returned value is <c>1 / f_unseen</c> where
    /// <c>f_unseen</c> is the expected per-unseen-callsite frequency. Falls back to <c>1.0</c> (treat
    /// unseen callsites as singletons) in degenerate cases.
    /// </summary>
    /// <param name="frequencies">The per-callsite arrival counts observed in the period.</param>
    /// <returns>The weight to assign to callsites not seen in the frozen table.</returns>
    public static double Chao1UnseenWeight(IEnumerable<long> frequencies)
    {
        long n = 0;
        long seen = 0;
        long f1 = 0;
        long f2 = 0;
        foreach (var f in frequencies)
        {
            if (f == 0)
            {
                continue;
            }

            n += f;
            seen++;
            if (f == 1)
            {
                f1++;
            }
            else if (f == 2)
            {
                f2++;
            }
        }

        if (n == 0 || f1 == 0)
        {
            return 1.0;
        }

        double nf = n;
        double f1f = f1;
        double f2f = f2;
        double seenf = seen;

        // Chao1 richness (lower bound on total number of distinct callsites).
        double chao1 = f2 > 0
            ? seenf + (((nf - 1.0) / nf) * (f1f * f1f) / (2.0 * f2f))
            : seenf + (((nf - 1.0) / nf) * (f1f * (f1f - 1.0)) / 2.0);

        double unseenSpecies = chao1 - seenf;
        if (unseenSpecies <= 0.0)
        {
            return 1.0;
        }

        // Good-Turing missing-mass estimate: f1 / n.
        double missingMass = f1f / nf;
        if (missingMass <= 0.0)
        {
            return 1.0;
        }

        double perUnseenProbability = missingMass / unseenSpecies;
        double unseenFrequency = nf * perUnseenProbability;
        if (double.IsNaN(unseenFrequency) || double.IsInfinity(unseenFrequency) || unseenFrequency <= 0.0)
        {
            return 1.0;
        }

        // Cap at 1.0: an unseen callsite should never be weighted as if it had been seen more than
        // once on average.
        double weight = 1.0 / unseenFrequency;
        return weight < 1.0 ? weight : 1.0;
    }
}
