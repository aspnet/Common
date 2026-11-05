// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Extensions.Logging;

internal sealed partial class ExtendedLogger : ILogger
{
    // A joiner is removed from its slot while in use, so a nested logging call on the same thread doesn't reuse it.
    [ThreadStatic]
    private static ModernTagJoiner? _modernJoiner;

    [ThreadStatic]
    private static LegacyTagJoiner? _legacyJoiner;

    private static ModernTagJoiner RentModernJoiner()
    {
        var joiner = _modernJoiner ?? new();
        _modernJoiner = null;
        return joiner;
    }

    private static void ReturnModernJoiner(ModernTagJoiner joiner)
    {
        joiner.Clear();
        _modernJoiner = joiner;
    }

    private static LegacyTagJoiner RentLegacyJoiner()
    {
        var joiner = _legacyJoiner ?? new();
        _legacyJoiner = null;
        return joiner;
    }

    private static void ReturnLegacyJoiner(LegacyTagJoiner joiner)
    {
        joiner.Clear();
        _legacyJoiner = joiner;
    }
}
