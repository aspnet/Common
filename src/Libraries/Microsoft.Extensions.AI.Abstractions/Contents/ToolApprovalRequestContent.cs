// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a request for approval before invoking a tool call.
/// </summary>
public sealed class ToolApprovalRequestContent : InputRequestContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolApprovalRequestContent"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier that correlates this request with its corresponding response.</param>
    /// <param name="toolCall">The tool call that requires approval before execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requestId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolCall"/> is <see langword="null"/>.</exception>
    [JsonConstructor]
    public ToolApprovalRequestContent(string requestId, ToolCallContent toolCall)
        : base(requestId)
    {
        ToolCall = Throw.IfNull(toolCall);
    }

    /// <summary>
    /// Gets the tool call that requires approval before execution.
    /// </summary>
    public ToolCallContent ToolCall { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the underlying tool call must be confirmed
    /// before it is invoked.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>. When <see langword="true"/>, the underlying tool
    /// requires a confirmation (such as a user prompt, a policy decision, or any other approver)
    /// before it can be invoked. When <see langword="false"/>, the underlying tool does not
    /// require a confirmation and the consumer may proceed without prompting; a corresponding
    /// <see cref="ToolApprovalResponseContent"/> still has to be supplied so the originating
    /// tool call can be invoked.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.AIApprovalsInvocationRequired, UrlFormat = DiagnosticIds.UrlFormat)]
    [JsonIgnore]
    public bool RequiresConfirmation
    {
        get => RequiresConfirmationCore;
        set => RequiresConfirmationCore = value;
    }

    // Including public, experimental properties leaks them into the source-generated
    // JSON metadata of any consumer, also forcing that consumer to suppress the
    // experimental diagnostic. The public property is annotated with [JsonIgnore] and
    // it routes its value through this internal property. The internal property is
    // annotated with [JsonInclude] and a [JsonPropertyName] to match the public
    // property's name, making it available in the default serialization options.
    [JsonInclude]
    [JsonPropertyName("requiresConfirmation")]
    internal bool RequiresConfirmationCore { get; set; } = true;

    /// <summary>
    /// Creates a <see cref="ToolApprovalResponseContent"/> indicating whether the tool call is approved or rejected.
    /// </summary>
    /// <param name="approved"><see langword="true"/> if the tool call is approved; otherwise, <see langword="false"/>.</param>
    /// <param name="reason">An optional reason for the approval or rejection.</param>
    /// <returns>The <see cref="ToolApprovalResponseContent"/> correlated with this request.</returns>
    public ToolApprovalResponseContent CreateResponse(bool approved, string? reason = null) =>
        new ToolApprovalResponseContent(RequestId, approved, ToolCall) { Reason = reason };
}
