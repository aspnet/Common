// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides a template for an <see cref="IChatClient"/> that selects and invokes another chat client.
/// </summary>
/// <remarks>
/// <para>
/// Derived classes implement <see cref="SelectClientAsync"/> to supply one client for each request. The selected
/// client is invoked once, and its response or failure is propagated to the caller.
/// The exact client instance represents the selected routing identity. Caller-supplied options may vary per
/// invocation, but they are ephemeral and do not participate in that identity. Applications can use distinct
/// configured client wrappers when configurations require distinct routing identities, while custom policies may
/// maintain separate application-specific grouping keys.
/// </para>
/// <para>
/// <see cref="RoutingContext.ChatOptions"/> holds the options for the request and is what the selected client
/// receives. Options that belong to a particular route are configured on the client instead, typically with a
/// <c>ConfigureOptionsChatClient</c> wrapper, which clones the request options and applies the route's own values
/// on top of them.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public abstract class RoutingChatClient : IChatClient
{
    /// <summary>Creates a routing client that selects one client for each request.</summary>
    /// <param name="clientSelector">The callback that selects the client to invoke.</param>
    /// <returns>A routing client that uses <paramref name="clientSelector"/>.</returns>
    /// <remarks>The selected clients are caller-owned and are not disposed by the returned routing client.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="clientSelector"/> is <see langword="null"/>.</exception>
    public static RoutingChatClient Create(
        Func<RoutingContext, CancellationToken, ValueTask<IChatClient>> clientSelector)
    {
        _ = Throw.IfNull(clientSelector);

        return new CallbackRoutingChatClient(clientSelector);
    }

    /// <summary>Selects the client to invoke for the request.</summary>
    /// <param name="context">The request-specific inputs.</param>
    /// <param name="cancellationToken">The cancellation token supplied for the request.</param>
    /// <returns>The client to invoke.</returns>
    /// <remarks>
    /// Client-specific behavior should generally be attached to the returned client. Modifying
    /// <see cref="RoutingContext.ChatOptions"/> changes the options for the request itself, including any later
    /// invocation of a different client for the same request. Exceptions from this method propagate to the caller.
    /// </remarks>
    protected abstract ValueTask<IChatClient> SelectClientAsync(
        RoutingContext context,
        CancellationToken cancellationToken);

    /// <inheritdoc/>
    public virtual async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var context = new RoutingContext(messages, options);
        IChatClient client = await SelectClientAsync(context, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException($"{nameof(SelectClientAsync)} returned null.");

        return await client.GetResponseAsync(
            context.Messages,
            context.ChatOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var context = new RoutingContext(messages, options);
        IChatClient client = await SelectClientAsync(context, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException($"{nameof(SelectClientAsync)} returned null.");

        await foreach (ChatResponseUpdate update in
            client.GetStreamingResponseAsync(context.Messages, context.ChatOptions, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc/>
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Provides a mechanism for releasing resources owned by the derived instance.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    /// <remarks>The default implementation performs no operation.</remarks>
    protected virtual void Dispose(bool disposing)
    {
    }

    private sealed class CallbackRoutingChatClient : RoutingChatClient
    {
        private readonly Func<RoutingContext, CancellationToken, ValueTask<IChatClient>> _clientSelector;

        public CallbackRoutingChatClient(
            Func<RoutingContext, CancellationToken, ValueTask<IChatClient>> clientSelector)
        {
            _clientSelector = clientSelector;
        }

        protected override ValueTask<IChatClient> SelectClientAsync(
            RoutingContext context,
            CancellationToken cancellationToken) =>
            _clientSelector(context, cancellationToken);
    }
}
