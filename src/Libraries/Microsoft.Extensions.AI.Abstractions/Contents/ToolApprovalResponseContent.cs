// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a response to a <see cref="ToolApprovalRequestContent"/>, indicating whether the tool call was approved.
/// </summary>
public sealed class ToolApprovalResponseContent : InputResponseContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolApprovalResponseContent"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier of the <see cref="ToolApprovalRequestContent"/> associated with this response.</param>
    /// <param name="approved"><see langword="true"/> if the tool call is approved; otherwise, <see langword="false"/>.</param>
    /// <param name="toolCall">The tool call that was subject to approval.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requestId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolCall"/> is <see langword="null"/>.</exception>
    [JsonConstructor]
    public ToolApprovalResponseContent(string requestId, bool approved, ToolCallContent toolCall)
        : base(requestId)
    {
        Approved = approved;
        ToolCall = Throw.IfNull(toolCall);
    }

    /// <summary>
    /// Gets a value indicating whether the tool call was approved for execution.
    /// </summary>
    public bool Approved { get; }

    /// <summary>
    /// Gets the tool call that was subject to approval.
    /// </summary>
    public ToolCallContent ToolCall { get; }

    /// <summary>
    /// Gets or sets the optional reason for the approval or rejection.
    /// </summary>
    /// <remarks>
    /// When a function call is rejected by function invocation middleware, this reason is appended to the rejection message.
    /// Callers can use the reason to indicate whether the rejection is final or a new call may be requested for approval.
    /// </remarks>
    public string? Reason { get; set; }
}
