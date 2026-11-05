// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Http.Resilience;

/// <summary>
/// Arguments of the <c>OnRetryBudgetExhausted</c> resilience event.
/// </summary>
[Experimental(diagnosticId: DiagnosticIds.Experiments.Resilience, UrlFormat = DiagnosticIds.UrlFormat)]
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Comparing event arguments is not an expected scenario.")]
public readonly struct OnRetryBudgetExhaustedArguments
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OnRetryBudgetExhaustedArguments"/> struct.
    /// </summary>
    /// <param name="attemptNumber">The zero-based number of the attempt whose retry was denied.</param>
    public OnRetryBudgetExhaustedArguments(int attemptNumber)
    {
        AttemptNumber = attemptNumber;
    }

    /// <summary>
    /// Gets the zero-based number of the attempt whose retry was denied.
    /// </summary>
    public int AttemptNumber { get; }
}
