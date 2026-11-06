// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>
/// Specifies the level of reasoning effort that should be applied when generating chat responses.
/// </summary>
/// <remarks>
/// This value suggests how much computational effort the model should put into reasoning.
/// Higher values may result in more thoughtful responses but with increased latency and token usage.
/// The specific interpretation and support for each level may vary between providers or even between models from the same provider.
/// </remarks>
public enum ReasoningEffort
{
    /// <summary>
    /// No reasoning effort.
    /// </summary>
    None,

    /// <summary>
    /// Low reasoning effort. Minimal reasoning for faster responses.
    /// </summary>
    Low,

    /// <summary>
    /// Medium reasoning effort. Balanced reasoning for most use cases.
    /// </summary>
    Medium,

    /// <summary>
    /// High reasoning effort. Extensive reasoning for complex tasks.
    /// </summary>
    High,

    /// <summary>
    /// Extra high reasoning effort. More extensive reasoning than <see cref="High"/>.
    /// </summary>
    ExtraHigh,

    /// <summary>
    /// Maximum reasoning effort. The highest level available, for the most demanding tasks.
    /// </summary>
    Max,
}
