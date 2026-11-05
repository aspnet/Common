// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Represents one client invocation attempt performed by a <see cref="FailoverChatClient"/>.</summary>
/// <remarks>
/// <para>
/// A completed response has <see cref="ResponseCompleted"/> set to <see langword="true"/> and
/// <see cref="Exception"/> set to <see langword="null"/>.
/// </para>
/// <para>
/// A failed invocation has <see cref="ResponseCompleted"/> set to <see langword="false"/> and
/// <see cref="Exception"/> set to the observed exception. If a streaming caller stops enumerating before the
/// response ends and disposal completes successfully, both <see cref="ResponseCompleted"/> and
/// <see cref="Exception"/> are unset.
/// </para>
/// <para>
/// <see cref="OutputCommitted"/> is independent of the outcome and indicates whether any streaming update was
/// exposed to the caller.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class FailoverChatClientAttempt
{
    internal FailoverChatClientAttempt(
        IChatClient client,
        Exception? exception,
        TimeSpan duration,
        TimeSpan? timeToFirstUpdate,
        bool responseCompleted,
        bool outputCommitted)
    {
        Debug.Assert(duration >= TimeSpan.Zero, "Expected a non-negative duration.");
        Debug.Assert(
            timeToFirstUpdate is not { } ttfu || (ttfu >= TimeSpan.Zero && ttfu <= duration),
            "Expected time to first update to be within the active duration.");
        Debug.Assert(
            !responseCompleted || exception is null,
            "A completed response should not have an exception.");
        Debug.Assert(
            outputCommitted == timeToFirstUpdate.HasValue,
            "Output commitment and time to first update should agree.");

        Client = Throw.IfNull(client);
        Exception = exception;
        Duration = duration;
        TimeToFirstUpdate = timeToFirstUpdate;
        ResponseCompleted = responseCompleted;
        OutputCommitted = outputCommitted;
    }

    /// <summary>Gets the client that was invoked.</summary>
    public IChatClient Client { get; }

    /// <summary>Gets the time spent actively invoking the client.</summary>
    /// <remarks>For streaming, time spent by the caller processing yielded updates is excluded.</remarks>
    public TimeSpan Duration { get; }

    /// <summary>Gets the exception observed while invoking or disposing the client, if any.</summary>
    /// <remarks>
    /// <para>If invocation and disposal both throw, this contains the disposal exception.</para>
    /// <para>
    /// A <see langword="null"/> value does not necessarily indicate success; inspect <see cref="ResponseCompleted"/>
    /// to distinguish a completed response from a streaming response that the caller stopped consuming.
    /// </para>
    /// </remarks>
    public Exception? Exception { get; }

    /// <summary>Gets a value indicating whether any streaming update was exposed to the caller.</summary>
    /// <remarks>This is always <see langword="false"/> for non-streaming invocations.</remarks>
    public bool OutputCommitted { get; }

    /// <summary>Gets a value indicating whether the response completed successfully.</summary>
    /// <remarks>
    /// For streaming responses, this is <see langword="false"/> when the caller stops enumeration before the
    /// response stream ends.
    /// </remarks>
    public bool ResponseCompleted { get; }

    /// <summary>Gets the time until the first streaming update, if this was a non-empty streaming invocation.</summary>
    public TimeSpan? TimeToFirstUpdate { get; }
}
