// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides a template for a <see cref="RoutingChatClient"/> that can select another client after an invocation fails.
/// </summary>
/// <remarks>
/// <para>
/// The client for each attempt is supplied by <see cref="RoutingChatClient.SelectClientAsync"/>. After an invocation,
/// <see cref="OnRoutingUpdateAsync"/> reports the concrete attempt and whether another selection will follow. An
/// uncanceled failure causes another selection only when it happened before any streaming output was exposed and the
/// attempt limit permits it.
/// </para>
/// <para>
/// The base class owns invocation, streaming commitment, attempt limits, and attempt reporting. Derived classes own
/// client selection, policy state, selection-failure cleanup, and the lifetime of clients they retain.
/// </para>
/// <para>
/// Once streaming enumeration begins, callers must dispose the enumerator. Abandoning an active enumerator without
/// disposing it prevents both inner enumerator disposal and the terminal routing update.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public abstract class FailoverChatClient : RoutingChatClient
{
    /// <summary>Gets or sets the maximum number of client invocations permitted for one request.</summary>
    /// <value>
    /// A positive attempt limit, or <see langword="null"/> to leave termination to client selection and request
    /// cancellation. The default is <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// The value is captured when a non-streaming request begins or when a streaming response begins enumeration.
    /// Changing it does not affect requests or enumerations already in progress.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not <see langword="null"/> or positive.</exception>
    public int? MaximumAttemptsPerRequest
    {
        get;
        set
        {
            if (value is <= 0)
            {
                Throw.ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Invoked after a client invocation completes, fails, or is abandoned.</summary>
    /// <param name="context">The request-specific inputs.</param>
    /// <param name="attempt">The attempted client invocation.</param>
    /// <param name="isTerminal">
    /// <see langword="true"/> if the base will not select another client after this method returns successfully;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token supplied for the request.</param>
    /// <returns>A task representing the update operation.</returns>
    /// <remarks>
    /// <para>
    /// The default implementation performs no operation. A nonterminal update always contains an uncanceled,
    /// pre-output failed attempt. State changes made by the override are visible to the next call to
    /// <see cref="RoutingChatClient.SelectClientAsync"/>.
    /// </para>
    /// <para>
    /// This method is invoked once after each selected-client invocation, whether it completes, fails, or is abandoned.
    /// Selection failures are not reported. A selector that retains request-scoped state must release it before
    /// throwing; a request may therefore end without a terminal update when selection fails.
    /// </para>
    /// <para>
    /// Exceptions from this method propagate to the caller. A terminal update exception replaces the response or
    /// exception already produced by the request. A nonterminal update exception stops routing without another update.
    /// An override that retains per-request state must release that state before throwing because no later update is
    /// made after an update exception.
    /// </para>
    /// </remarks>
    protected virtual ValueTask OnRoutingUpdateAsync(
        RoutingContext context,
        FailoverChatClientAttempt attempt,
        bool isTerminal,
        CancellationToken cancellationToken) => default;

    /// <inheritdoc/>
    public sealed override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var context = new RoutingContext(messages, options);
        int? maximumAttempts = MaximumAttemptsPerRequest;
        int attemptCount = 0;

        while (true)
        {
            IChatClient selectedClient =
                await SelectClientAsync(context, cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException($"{nameof(SelectClientAsync)} returned null.");

            attemptCount++;
            ChatResponse? response = null;
            Exception? exception = null;
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                response = await selectedClient.GetResponseAsync(
                    context.Messages,
                    context.ChatOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            stopwatch.Stop();
            var attempt = new FailoverChatClientAttempt(
                selectedClient,
                exception,
                stopwatch.Elapsed,
                timeToFirstUpdate: null,
                responseCompleted: exception is null,
                outputCommitted: false);
            bool cancellationRequested =
                exception is not null &&
                cancellationToken.IsCancellationRequested;
            bool isTerminal =
                exception is null ||
                cancellationRequested ||
                (maximumAttempts is int limit && attemptCount >= limit);

            await OnRoutingUpdateAsync(context, attempt, isTerminal, cancellationToken).ConfigureAwait(false);

            if (exception is null)
            {
                return response!;
            }

            if (cancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (isTerminal)
            {
                Rethrow(exception);
            }
        }
    }

    /// <inheritdoc/>
    public sealed override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var context = new RoutingContext(messages, options);
        int? maximumAttempts = MaximumAttemptsPerRequest;
        int attemptCount = 0;

        while (true)
        {
            IChatClient selectedClient =
                await SelectClientAsync(context, cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException($"{nameof(SelectClientAsync)} returned null.");

            attemptCount++;
            bool reachedAttemptLimit = maximumAttempts is int limit && attemptCount >= limit;
            IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
            TimeSpan? timeToFirstUpdate = null;
            bool hasCurrent;

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                enumerator = selectedClient
                    .GetStreamingResponseAsync(
                        context.Messages,
                        context.ChatOptions,
                        cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                hasCurrent = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Exception exception = (await DisposeEnumeratorAsync(enumerator, ex).ConfigureAwait(false))!;
                var attempt = new FailoverChatClientAttempt(
                    selectedClient,
                    exception,
                    stopwatch.Elapsed,
                    timeToFirstUpdate: null,
                    responseCompleted: false,
                    outputCommitted: false);
                bool cancellationRequested = cancellationToken.IsCancellationRequested;
                bool isTerminal = cancellationRequested || reachedAttemptLimit;

                await OnRoutingUpdateAsync(context, attempt, isTerminal, cancellationToken).ConfigureAwait(false);

                if (cancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (isTerminal)
                {
                    Rethrow(exception);
                }

                continue;
            }

            stopwatch.Stop();
            bool responseCompleted = false;
            bool outputCommitted = false;
            bool isTerminalAttempt = false;
            Exception? terminalException = null;

            try
            {
                while (hasCurrent)
                {
                    stopwatch.Start();
                    bool hasCurrentValue = TryGetCurrent(
                        enumerator,
                        out ChatResponseUpdate current,
                        out terminalException);
                    stopwatch.Stop();
                    if (!hasCurrentValue)
                    {
                        break;
                    }

                    timeToFirstUpdate ??= stopwatch.Elapsed;
                    outputCommitted = true;
                    yield return current;

                    stopwatch.Start();
                    try
                    {
                        hasCurrent = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        terminalException = ex;
                        break;
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }

                responseCompleted = terminalException is null;
            }
            finally
            {
                terminalException =
                    await DisposeEnumeratorAsync(enumerator, terminalException).ConfigureAwait(false);

                var attempt = new FailoverChatClientAttempt(
                    selectedClient,
                    terminalException,
                    stopwatch.Elapsed,
                    timeToFirstUpdate,
                    responseCompleted: responseCompleted && terminalException is null,
                    outputCommitted: outputCommitted);
                bool cancellationRequested =
                    terminalException is not null &&
                    cancellationToken.IsCancellationRequested;
                isTerminalAttempt =
                    attempt.ResponseCompleted ||
                    outputCommitted ||
                    cancellationRequested ||
                    reachedAttemptLimit;

                await OnRoutingUpdateAsync(
                    context,
                    attempt,
                    isTerminalAttempt,
                    cancellationToken).ConfigureAwait(false);

                if (terminalException is not null)
                {
                    if (cancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (isTerminalAttempt)
                    {
                        Rethrow(terminalException);
                    }
                }
            }

            if (!isTerminalAttempt)
            {
                continue;
            }

            yield break;
        }
    }

#pragma warning disable EA0014 // IAsyncDisposable.DisposeAsync doesn't support cancellation.
    private static async ValueTask<Exception?> DisposeEnumeratorAsync(
        IAsyncDisposable? disposable, Exception? exception)
#pragma warning restore EA0014
    {
        if (disposable is not null)
        {
            try
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        return exception;
    }

    private static bool TryGetCurrent(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        out ChatResponseUpdate current,
        out Exception? exception)
    {
        try
        {
            current = enumerator.Current;
            exception = null;
            return true;
        }
        catch (Exception ex)
        {
            current = null!;
            exception = ex;
            return false;
        }
    }

#if NET
    [DoesNotReturn]
#endif
    private static void Rethrow(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
