// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Provides ordered failover across a sequence of chat clients.</summary>
/// <remarks>
/// <para>
/// The clients are tried in order. An invocation failure before streaming output is exposed advances to the next
/// client. Cancellation and failures after streaming output is exposed are propagated without failover.
/// </para>
/// <para>
/// The configured clients are snapshotted by the constructor. The same client may appear more than once, in which
/// case it is invoked once per position. When every client has failed, the final failure is rethrown.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class OrderedFailoverChatClient : FailoverChatClient
{
    private readonly bool _leaveOpen;
    private readonly IChatClient[] _clients;

    // Holds the next client index for a request that has a failed attempt. A nonterminal update is always followed
    // by another selection, so a stored index is always in range.
    private readonly ConcurrentDictionary<RoutingContext, int> _requestStates = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="OrderedFailoverChatClient"/> class.</summary>
    /// <param name="clients">The clients to invoke, in fallback order.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave inner clients open when this instance is disposed;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="clients"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="clients"/> is empty or contains <see langword="null"/>.</exception>
    public OrderedFailoverChatClient(IReadOnlyList<IChatClient> clients, bool leaveOpen = false)
    {
        _ = Throw.IfNull(clients);

        IChatClient[] clientsSnapshot = [.. clients];
        if (clientsSnapshot.Length == 0)
        {
            Throw.ArgumentException(nameof(clients), "At least one client must be provided.");
        }

        foreach (IChatClient client in clientsSnapshot)
        {
            if (client is null)
            {
                Throw.ArgumentException(nameof(clients), "Clients must not contain null.");
            }
        }

        _leaveOpen = leaveOpen;
        _clients = clientsSnapshot;
    }

    /// <inheritdoc/>
    protected override ValueTask<IChatClient> SelectClientAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        _ = Throw.IfNull(context);
        _ = cancellationToken;

        int clientIndex = _requestStates.TryGetValue(context, out int nextClientIndex) ? nextClientIndex : 0;

        return new(_clients[clientIndex]);
    }

    /// <inheritdoc/>
    protected override ValueTask OnRoutingUpdateAsync(
        RoutingContext context,
        FailoverChatClientAttempt attempt,
        bool isTerminal,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (isTerminal)
        {
            _ = _requestStates.TryRemove(context, out _);
            return default;
        }

        Exception? exception = attempt.Exception;
        Debug.Assert(exception is not null, "A nonterminal update always reports a failed invocation.");

        int nextClientIndex = (_requestStates.TryGetValue(context, out int attemptedIndex) ? attemptedIndex : 0) + 1;
        if (nextClientIndex < _clients.Length)
        {
            _requestStates[context] = nextClientIndex;
            return default;
        }

        // Every client has failed. Release the state before the final failure ends routing.
        _ = _requestStates.TryRemove(context, out _);
        ExceptionDispatchInfo.Capture(exception!).Throw();
        throw exception!;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requestStates.Clear();

        if (disposing && !_leaveOpen)
        {
            foreach (IChatClient client in _clients)
            {
                client.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
