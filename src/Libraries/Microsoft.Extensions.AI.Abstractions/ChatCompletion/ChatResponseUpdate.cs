// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a single streaming response chunk from an <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChatResponseUpdate"/> is so named because it represents updates
/// that layer on each other to form a single chat response. Conceptually, this combines the roles of
/// <see cref="ChatResponse"/> and <see cref="ChatMessage"/> in streaming output.
/// </para>
/// <para>
/// The relationship between <see cref="ChatResponse"/> and <see cref="ChatResponseUpdate"/> is
/// codified in the <see cref="ChatResponseExtensions.ToChatResponseAsync(IAsyncEnumerable{ChatResponseUpdate}, System.Threading.CancellationToken)"/> and
/// <see cref="ChatResponse.ToChatResponseUpdates"/>, which enable bidirectional conversions
/// between the two. Note, however, that the provided conversions might be lossy, for example, if multiple
/// updates all have different <see cref="RawRepresentation"/> objects whereas there's only one slot for
/// such an object available in <see cref="ChatResponse.RawRepresentation"/>. Similarly, if different
/// updates provide different values for properties like <see cref="ModelId"/>,
/// only one of the values will be used to populate <see cref="ChatResponse.ModelId"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("[{Role}] {ContentForDebuggerDisplay}{EllipsesForDebuggerDisplay,nq}")]
public class ChatResponseUpdate
{
    /// <summary>The response update content items.</summary>
    private IList<AIContent>? _contents;

    /// <summary>Initializes a new instance of the <see cref="ChatResponseUpdate"/> class.</summary>
    [JsonConstructor]
    public ChatResponseUpdate()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChatResponseUpdate"/> class.</summary>
    /// <param name="role">The role of the author of the update.</param>
    /// <param name="content">The text content of the update.</param>
    public ChatResponseUpdate(ChatRole? role, string? content)
        : this(role, content is null ? null : [new TextContent(content)])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChatResponseUpdate"/> class.</summary>
    /// <param name="role">The role of the author of the update.</param>
    /// <param name="contents">The contents of the update.</param>
    public ChatResponseUpdate(ChatRole? role, IList<AIContent>? contents)
    {
        Role = role;
        _contents = contents;
    }

    /// <summary>
    /// Creates a new ChatResponseUpdate instance that is a copy of the current object.
    /// </summary>
    /// <remarks>The cloned object is a shallow copy; reference-type properties will reference the same
    /// objects as the original. Use this method to duplicate the response update for further modification without
    /// affecting the original instance.</remarks>
    /// <returns>A new ChatResponseUpdate object with the same property values as the current instance.</returns>
    public ChatResponseUpdate Clone() =>
        new()
        {
            AdditionalProperties = AdditionalProperties,
            AuthorName = AuthorName,
            Contents = Contents,
            ContinuationToken = ContinuationToken,
            CreatedAt = CreatedAt,
            ConversationId = ConversationId,
            FinishReason = FinishReason,
            MessageId = MessageId,
            ModelId = ModelId,
            RawRepresentation = RawRepresentation,
            ResponseId = ResponseId,
            Role = Role,
        };

    /// <summary>Gets or sets the name of the author of the response update.</summary>
    public string? AuthorName
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Gets or sets the role of the author of the response update.</summary>
    public ChatRole? Role { get; set; }

    /// <summary>Gets the text of this update.</summary>
    /// <remarks>
    /// This property concatenates the text of all <see cref="TextContent"/> objects in <see cref="Contents"/>.
    /// </remarks>
    [JsonIgnore]
    public string Text => _contents is not null ? _contents.ConcatText() : string.Empty;

    /// <summary>Gets or sets the chat response update content items.</summary>
    [AllowNull]
    public IList<AIContent> Contents
    {
        get => _contents ??= [];
        set => _contents = value;
    }

    /// <summary>Gets or sets the raw representation of the response update from an underlying implementation.</summary>
    /// <remarks>
    /// If a <see cref="ChatResponseUpdate"/> is created to represent some underlying object from another object
    /// model, this property can be used to store that original object. This can be useful for debugging or
    /// for enabling a consumer to access the underlying object model if needed.
    /// </remarks>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets additional properties for the update.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>Gets or sets the ID of the response of which this update is a part.</summary>
    public string? ResponseId { get; set; }

    /// <summary>Gets or sets the ID of the message of which this update is a part.</summary>
    /// <remarks>
    /// A single streaming response might be composed of multiple messages, each of which might be represented
    /// by multiple updates. This property is used to group those updates together into messages.
    ///
    /// Some providers might consider streaming responses to be a single message, and in that case
    /// the value of this property might be the same as the response ID.
    ///
    /// This value is used when <see cref="ChatResponseExtensions.ToChatResponseAsync(IAsyncEnumerable{ChatResponseUpdate}, System.Threading.CancellationToken)"/>
    /// groups <see cref="ChatResponseUpdate"/> instances into <see cref="ChatMessage"/> instances.
    /// The value must be unique to each call to the underlying provider, and must be shared by
    /// all updates that are part of the same logical message within a streaming response.
    /// </remarks>
    public string? MessageId { get; set; }

    /// <summary>Gets or sets an identifier for the state of the conversation of which this update is a part.</summary>
    /// <remarks>
    /// Some <see cref="IChatClient"/> implementations are capable of storing the state for a conversation, such that
    /// the input messages supplied to <see cref="IChatClient.GetStreamingResponseAsync"/> need only be the additional messages beyond
    /// what's already stored. If this property is non-<see langword="null"/>, it represents an identifier for that state,
    /// and it should be used in a subsequent <see cref="ChatOptions.ConversationId"/> instead of supplying the same messages
    /// (and this streaming message) as part of the <c>messages</c> parameter. Note that the value might differ on every
    /// response, depending on whether the underlying provider uses a fixed ID for each conversation or updates it for each message.
    /// </remarks>
    public string? ConversationId { get; set; }

    /// <summary>Gets or sets a timestamp for the response update.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Gets or sets the finish reason for the operation.</summary>
    public ChatFinishReason? FinishReason { get; set; }

    /// <summary>Gets or sets the model ID associated with this response update.</summary>
    public string? ModelId { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <summary>Gets or sets the continuation token for resuming the streamed chat response of which this update is a part.</summary>
    /// <remarks>
    /// <see cref="IChatClient"/> implementations that support background responses return
    /// a continuation token on each update if background responses are allowed in <see cref="ChatOptions.AllowBackgroundResponses"/>.
    /// However, for the last update, the token will be <see langword="null"/>.
    /// <para>
    /// This property should be used for stream resumption, where the continuation token of the latest received update should be
    /// passed to <see cref="ChatOptions.ContinuationToken"/> on subsequent calls to <see cref="IChatClient.GetStreamingResponseAsync"/>
    /// to resume streaming from the point of interruption.
    /// </para>
    /// </remarks>
    public ResponseContinuationToken? ContinuationToken { get; set; }

    /// <summary>Gets a <see cref="AIContent"/> object to display in the debugger display.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private AIContent? ContentForDebuggerDisplay
    {
        get
        {
            string text = Text;
            return
                !string.IsNullOrWhiteSpace(text) ? new TextContent(text) :
                _contents is { Count: > 0 } ? _contents[0] :
                null;
        }
    }

    /// <summary>Gets an indication for the debugger display of whether there's more content.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string EllipsesForDebuggerDisplay => _contents is { Count: > 1 } ? ", ..." : string.Empty;
}
