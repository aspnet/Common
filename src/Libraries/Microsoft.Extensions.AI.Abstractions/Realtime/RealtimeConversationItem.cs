// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a real-time conversation item.
/// </summary>
/// <remarks>
/// This class is used to encapsulate the details of a real-time item that can be inserted into a conversation,
/// or sent as part of a real-time response creation process.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRealTime, UrlFormat = DiagnosticIds.UrlFormat)]
public class RealtimeConversationItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RealtimeConversationItem"/> class.
    /// </summary>
    /// <param name="contents">The contents of the conversation item.</param>
    /// <param name="id">The ID of the conversation item.</param>
    /// <param name="role">The role of the conversation item.</param>
    public RealtimeConversationItem(IList<AIContent> contents, string? id = null, ChatRole? role = null)
    {
        Id = id;
        Role = role;
        Contents = contents;
    }

    /// <summary>
    /// Gets or sets the ID of the conversation item.
    /// </summary>
    /// <remarks>
    /// This ID can be null in case passing Function or MCP content where the ID is not required.
    /// The Id only needed of having contents representing a user, system, or assistant message with contents like text, audio, image or similar.
    /// </remarks>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the role of the conversation item.
    /// </summary>
    /// <remarks>
    /// The role not used in case of Function or MCP content.
    /// The role only needed of having contents representing a user, system, or assistant message with contents like text, audio, image or similar.
    /// </remarks>
    public ChatRole? Role { get; set; }

    /// <summary>
    /// Gets or sets the content of the conversation item.
    /// </summary>
    public IList<AIContent> Contents { get; set; }

    /// <summary>
    /// Gets or sets the raw representation of the conversation item.
    /// </summary>
    /// <remarks>
    /// This can be used to hold the original data structure received from or sent to the provider.
    /// </remarks>
    public object? RawRepresentation { get; set; }
}
