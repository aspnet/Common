// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Diagnostics.Sampling;

/// <summary>
/// A <see cref="LoggingSampler"/> that makes the bottom-K admission decision at the sampling seam &#8212;
/// dropping records the reservoir rejects before they are buffered &#8212; and shares its reservoir
/// with the paired <see cref="BottomKLogBuffer"/>, which holds the admitted records and emits them,
/// weighted, at each flush.
/// </summary>
internal sealed class BottomKLoggingSampler : LoggingSampler
{
    private readonly BottomKLogBuffer _buffer;

    public BottomKLoggingSampler(BottomKLogBuffer buffer)
    {
        _buffer = Throw.IfNull(buffer);
    }

    /// <inheritdoc/>
    public override bool ShouldSample<TState>(in LogEntry<TState> logEntry)
        => _buffer.Admit(logEntry.Category, logEntry.LogLevel, logEntry.EventId);
}
#endif
