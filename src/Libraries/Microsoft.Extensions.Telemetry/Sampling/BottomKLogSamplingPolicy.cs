// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Diagnostics.Sampling;

internal sealed class BottomKLogSamplingPolicy
{
    private static readonly BottomKLogSamplingFilterRule[] _defaultRules =
        [new()];

    private readonly BottomKLogSamplingFilterRule[] _rules;

    public BottomKLogSamplingPolicy(BottomKLogSamplingOptions options)
    {
        Options = options;
        _rules = options.Rules.Count == 0 ? _defaultRules : options.Rules.ToArray();
    }

    public BottomKLogSamplingOptions Options { get; }

    public bool ShouldBypass(string category, LogLevel logLevel, EventId eventId)
    {
        BottomKLogSamplingFilterRule? rule = Options.Enabled
            ? _rules.GetBestMatchFor(category, logLevel, eventId)
            : null;

        return rule is null;
    }
}
#endif
