// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Polly;

namespace Microsoft.Extensions.Http.Resilience.Internal;

/// <summary>
/// Options of the <see cref="RetryBudgetStrategy"/>. Only used to name the strategy for telemetry.
/// </summary>
internal sealed class RetryBudgetStrategyOptions : ResilienceStrategyOptions
{
}
