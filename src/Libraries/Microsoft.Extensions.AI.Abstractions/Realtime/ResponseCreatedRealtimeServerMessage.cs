// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a real-time message for creating a response item.
/// </summary>
/// <remarks>
/// <para>
/// Used with the <see cref="RealtimeServerMessageType.ResponseDone"/> and <see cref="RealtimeServerMessageType.ResponseCreated"/> messages.
/// </para>
/// <para>
/// Provider implementations should emit this message with <see cref="RealtimeServerMessageType.ResponseCreated"/>
/// when the model begins generating a new response, and with <see cref="RealtimeServerMessageType.ResponseDone"/>
/// when the response is complete. The built-in <see langword="OpenTelemetryRealtimeClientSession"/> middleware depends
/// on these messages for tracing response lifecycle.
/// </para>
/// <para>
/// Providers that do not natively support response lifecycle events (e.g., those that only stream content parts
/// and signal turn completion) should synthesize these messages to ensure correct middleware behavior.
/// In such cases, <see cref="ResponseId"/> may be set to a synthetic value or left <see langword="null"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRealTime, UrlFormat = DiagnosticIds.UrlFormat)]
public class ResponseCreatedRealtimeServerMessage : RealtimeServerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseCreatedRealtimeServerMessage"/> class.
    /// </summary>
    /// <remarks>
    /// The <paramref name="type"/> should be <see cref="RealtimeServerMessageType.ResponseDone"/> or <see cref="RealtimeServerMessageType.ResponseCreated"/>.
    /// </remarks>
    public ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType type)
    {
        Type = type;
    }

    /// <summary>
    /// Gets or sets the output audio options for the response.
    /// </summary>
    /// <value>
    /// If <see langword="null"/>, the default conversation audio options are used.
    /// </value>
    public RealtimeAudioFormat? OutputAudioOptions { get; set; }

    /// <summary>
    /// Gets or sets the voice of the output audio.
    /// </summary>
    public string? OutputVoice { get; set; }

    /// <summary>
    /// Gets or sets the unique response ID.
    /// </summary>
    /// <remarks>
    /// Some providers (e.g., OpenAI) assign a unique ID to each response. Providers that do not
    /// natively track response lifecycles may set this to <see langword="null"/> or generate a synthetic ID.
    /// Consumers should not assume this value correlates to a provider-specific concept.
    /// </remarks>
    public string? ResponseId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of output tokens for the response, inclusive of all modalities and tool calls.
    /// </summary>
    /// <remarks>
    /// This limit applies to the total output tokens regardless of modality (text, audio, etc.).
    /// If <see langword="null"/>, the provider's default limit was used.
    /// </remarks>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets any additional properties associated with the response.
    /// </summary>
    /// <remarks>
    /// Contains arbitrary key-value metadata attached to the response.
    /// This is the metadata that was provided when the response was created
    /// (e.g., for tracking or disambiguating multiple simultaneous responses).
    /// </remarks>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>
    /// Gets or sets the list of the conversation items included in the response.
    /// </summary>
    public IList<RealtimeConversationItem>? Items { get; set; }

    /// <summary>
    /// Gets or sets the output modalities for the response.
    /// For example, <c>"text"</c> and <c>"audio"</c>.
    /// </summary>
    /// <value>
    /// If <see langword="null"/>, the default conversation modalities are used.
    /// </value>
    public IList<string>? OutputModalities { get; set; }

    /// <summary>
    /// Gets or sets the status of the response.
    /// </summary>
    /// <remarks>
    /// Typically set on <see cref="RealtimeServerMessageType.ResponseDone"/> messages to indicate
    /// how the response ended. See <see cref="RealtimeResponseStatus"/> for well-known values
    /// such as <see cref="RealtimeResponseStatus.Completed"/>, <see cref="RealtimeResponseStatus.Cancelled"/>
    /// (e.g., due to user barge-in), <see cref="RealtimeResponseStatus.Incomplete"/>,
    /// and <see cref="RealtimeResponseStatus.Failed"/>.
    /// </remarks>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the error content of the response, if any.
    /// </summary>
    public ErrorContent? Error { get; set; }

    /// <summary>
    /// Gets or sets the per-response token usage for billing purposes.
    /// </summary>
    /// <remarks>
    /// Populated when the response is complete (i.e., on <see cref="RealtimeServerMessageType.ResponseDone"/>).
    /// Input tokens include the entire conversation context, so they grow over successive turns
    /// as previous output becomes input for later responses.
    /// </remarks>
    public UsageDetails? Usage { get; set; }
}
