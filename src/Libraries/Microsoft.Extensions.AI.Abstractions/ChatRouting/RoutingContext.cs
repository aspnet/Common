// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Provides request-specific inputs to a <see cref="RoutingChatClient"/>.</summary>
/// <remarks>
/// <para>
/// One context is created for each call to <see cref="IChatClient.GetResponseAsync"/> and for each enumeration
/// started from the sequence returned by <see cref="IChatClient.GetStreamingResponseAsync"/>.
/// </para>
/// <para>
/// <see cref="ChatOptions"/> is cloned (via <see cref="ChatOptions.Clone"/>) from the caller-supplied instance when
/// the context is created, so that instance is never handed to a selected client and subsequent changes to it are not
/// observed. The clone is shallow, so referenced objects may still be shared.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public class RoutingContext
{
    /// <summary>Initializes a new instance of the <see cref="RoutingContext"/> class.</summary>
    /// <param name="messages">The messages to route.</param>
    /// <param name="chatOptions">The options associated with this context.</param>
    public RoutingContext(
        IEnumerable<ChatMessage> messages,
        ChatOptions? chatOptions)
    {
        _ = Throw.IfNull(messages);

        Messages = messages;
        ChatOptions = chatOptions?.Clone();
    }

    /// <summary>Gets the messages supplied to client selection and the selected client.</summary>
    /// <remarks>
    /// Selection and failover may enumerate this sequence multiple times. Callers should supply a repeatable sequence.
    /// </remarks>
    public IEnumerable<ChatMessage> Messages { get; }

    /// <summary>Gets the options for the request.</summary>
    /// <remarks>
    /// This is a clone of the caller's options and is what the selected client receives. Modifying it shapes the
    /// request, including any later invocation of a different client for the same request. Options that belong to a
    /// particular route should be configured on the client instead.
    /// </remarks>
    public ChatOptions? ChatOptions { get; }
}
