// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>Represents options for configuring a real-time session.</summary>
[Experimental(DiagnosticIds.Experiments.AIRealTime, UrlFormat = DiagnosticIds.UrlFormat)]
public class RealtimeSessionOptions
{
    /// <summary>
    /// Gets the session kind.
    /// </summary>
    /// <remarks>
    /// If set to <see cref="RealtimeSessionKind.Transcription"/>, most of the sessions properties will not apply to the session. Only InputAudioFormat and TranscriptionOptions will be used.
    /// </remarks>
    public RealtimeSessionKind SessionKind { get; init; } = RealtimeSessionKind.Conversation;

    /// <summary>
    /// Gets or initializes the model name to use for the session.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets or initializes the input audio format for the session.
    /// </summary>
    public RealtimeAudioFormat? InputAudioFormat { get; init; }

    /// <summary>
    /// Gets or initializes the transcription options for the session.
    /// </summary>
    public TranscriptionOptions? TranscriptionOptions { get; init; }

    /// <summary>
    /// Gets or initializes the output audio format for the session.
    /// </summary>
    public RealtimeAudioFormat? OutputAudioFormat { get; init; }

    /// <summary>
    /// Gets or initializes the output voice for the session.
    /// </summary>
    public string? Voice { get; init; }

    /// <summary>
    /// Gets or initializes the default system instructions for the session.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of response tokens for the session.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets or initializes the output modalities for the response, for example, "text" and "audio".
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, the default conversation modalities are used.
    /// </remarks>
    public IReadOnlyList<string>? OutputModalities { get; init; }

    /// <summary>
    /// Gets or initializes the tool choice mode for the session.
    /// </summary>
    public ChatToolMode? ToolMode { get; init; }

    /// <summary>
    /// Gets or initializes the AI tools available for generating the response.
    /// </summary>
    public IReadOnlyList<AITool>? Tools { get; init; }

    /// <summary>
    /// Gets or initializes the voice activity detection (VAD) options for the session.
    /// </summary>
    /// <remarks>
    /// When set, configures how the server detects user speech to manage turn-taking.
    /// When <see langword="null"/>, the provider's default VAD behavior is used.
    /// </remarks>
    public VoiceActivityDetectionOptions? VoiceActivityDetection { get; init; }

    /// <summary>
    /// Gets a callback responsible for creating the raw representation of the session options from an underlying implementation.
    /// </summary>
    /// <remarks>
    /// The underlying <see cref="IRealtimeClientSession" /> implementation might have its own representation of options.
    /// When a <see cref="SessionUpdateRealtimeClientMessage" /> is sent with a <see cref="RealtimeSessionOptions" />,
    /// that implementation might convert the provided options into its own representation in order to use it while
    /// performing the operation. For situations where a consumer knows which concrete <see cref="IRealtimeClientSession" />
    /// is being used and how it represents options, a new instance of that implementation-specific options type can be
    /// returned by this callback for the <see cref="IRealtimeClientSession" /> implementation to use, instead of creating a
    /// new instance. Such implementations might mutate the supplied options instance further based on other settings
    /// supplied on this <see cref="RealtimeSessionOptions" /> instance or from other inputs.
    /// Therefore, it is <b>strongly recommended</b> to not return shared instances and instead make the callback return
    /// a new instance on each call.
    /// This is typically used to set an implementation-specific setting that isn't otherwise exposed from the strongly typed
    /// properties on <see cref="RealtimeSessionOptions" />.
    /// <para>
    /// Unlike similar factories on other options types, this callback does not receive the session instance
    /// as a parameter because some providers need to evaluate it before the session is created
    /// (e.g., to produce connection configuration).
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public Func<object?>? RawRepresentationFactory { get; init; }
}
