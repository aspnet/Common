// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

/// <summary>Represents the options for a chat request.</summary>
/// <related type="Article" href="https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai#provide-options">Provide options.</related>
public class ChatOptions
{
    /// <summary>Initializes a new instance of the <see cref="ChatOptions"/> class.</summary>
    public ChatOptions()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChatOptions"/> class, performing a shallow copy of all properties from <paramref name="other"/>.</summary>
    protected ChatOptions(ChatOptions? other)
    {
        if (other is null)
        {
            return;
        }

        AdditionalProperties = other.AdditionalProperties?.Clone();
        AllowBackgroundResponses = other.AllowBackgroundResponses;
        AllowMultipleToolCalls = other.AllowMultipleToolCalls;
        ConversationId = other.ConversationId;
        ContinuationToken = other.ContinuationToken;
        FrequencyPenalty = other.FrequencyPenalty;
        Instructions = other.Instructions;
        MaxOutputTokens = other.MaxOutputTokens;
        ModelId = other.ModelId;
        PresencePenalty = other.PresencePenalty;
        RawRepresentationFactory = other.RawRepresentationFactory;
        Reasoning = other.Reasoning?.Clone();
        ResponseFormat = other.ResponseFormat;
        Seed = other.Seed;
        Temperature = other.Temperature;
        ToolMode = other.ToolMode;
        TopK = other.TopK;
        TopP = other.TopP;

        if (other.StopSequences is not null)
        {
            StopSequences = [.. other.StopSequences];
        }

        if (other.Tools is not null)
        {
            Tools = [.. other.Tools];
        }
    }

    /// <summary>Gets or sets an optional identifier used to associate a request with an existing conversation.</summary>
    /// <related type="Article" href="https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai#stateless-vs-stateful-clients">Stateless vs. stateful clients.</related>
    public string? ConversationId { get; set; }

    /// <summary>Gets or sets additional per-request instructions to be provided to the <see cref="IChatClient"/>.</summary>
    public string? Instructions { get; set; }

    /// <summary>Gets or sets the temperature for generating chat responses.</summary>
    /// <remarks>
    /// This value controls the randomness of predictions made by the model. Use a lower value to decrease randomness in the response.
    /// </remarks>
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the maximum number of tokens in the generated chat response.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Gets or sets the "nucleus sampling" factor (or "top p") for generating chat responses.</summary>
    /// <remarks>
    /// Nucleus sampling is an alternative to sampling with temperature where the model
    /// considers the results of the tokens with <see cref="TopP"/> probability mass.
    /// For example, 0.1 means only the tokens comprising the top 10% probability mass are considered.
    /// </remarks>
    public float? TopP { get; set; }

    /// <summary>
    /// Gets or sets the number of most probable tokens that the model considers when generating the next part of the text.
    /// </summary>
    /// <remarks>
    /// This property reduces the probability of generating nonsense. A higher value gives more diverse answers, while a lower value is more conservative.
    /// </remarks>
    public int? TopK { get; set; }

    /// <summary>
    /// Gets or sets the penalty for repeated tokens in chat responses proportional to how many times they've appeared.
    /// </summary>
    /// <remarks>
    /// You can modify this value to reduce the repetitiveness of generated tokens. The higher the value, the stronger a penalty
    /// is applied to previously present tokens, proportional to how many times they've already appeared in the prompt or prior generation.
    /// </remarks>
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Gets or sets a value that influences the probability of generated tokens appearing based on their existing presence in generated text.
    /// </summary>
    /// <remarks>
    /// You can modify this value to reduce repetitiveness of generated tokens. Similar to <see cref="FrequencyPenalty"/>,
    /// except that this penalty is applied equally to all tokens that have already appeared, regardless of their exact frequencies.
    /// </remarks>
    public float? PresencePenalty { get; set; }

    /// <summary>Gets or sets a seed value used by a service to control the reproducibility of results.</summary>
    public long? Seed { get; set; }

    /// <summary>
    /// Gets or sets the reasoning options for the chat request.
    /// </summary>
    public ReasoningOptions? Reasoning { get; set; }

    /// <summary>
    /// Gets or sets the response format for the chat request.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, no response format is specified and the client will use its default.
    /// This property can be set to <see cref="ChatResponseFormat.Text"/> to specify that the response should be unstructured text,
    /// to <see cref="ChatResponseFormat.Json"/> to specify that the response should be structured JSON data, or
    /// an instance of <see cref="ChatResponseFormatJson"/> constructed with a specific JSON schema to request that the
    /// response be structured JSON data according to that schema. It is up to the client implementation if or how
    /// to honor the request. If the client implementation doesn't recognize the specific kind of <see cref="ChatResponseFormat"/>,
    /// it can be ignored.
    /// </remarks>
    public ChatResponseFormat? ResponseFormat { get; set; }

    /// <summary>Gets or sets the model ID for the chat request.</summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the list of stop sequences.
    /// </summary>
    /// <remarks>
    /// After a stop sequence is detected, the model stops generating further tokens for chat responses.
    /// </remarks>
    public IList<string>? StopSequences { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether a single response is allowed to include multiple tool calls.
    /// </summary>
    /// <value>
    /// <see langword="true"/> for no limit. <see langword="false"/> if the <see cref="IChatClient"/> is asked to return a maximum of one tool call per request. If <see langword="null"/>, the provider can select its own default.
    /// </value>
    /// <remarks>
    /// <para>
    /// When used with function calling middleware, this does not affect the ability to perform multiple function calls in sequence.
    /// It only affects the number of function calls within a single iteration of the function calling loop.
    /// </para>
    /// <para>
    /// The underlying provider is not guaranteed to support or honor this flag. For example it might choose to ignore it and return multiple tool calls regardless.
    /// </para>
    /// </remarks>
    public bool? AllowMultipleToolCalls { get; set; }

    /// <summary>Gets or sets the tool mode for the chat request.</summary>
    /// <value>The default is <see langword="null"/>, which is treated the same as <see cref="ChatToolMode.Auto"/>.</value>
    public ChatToolMode? ToolMode { get; set; }

    /// <summary>Gets or sets the list of tools to include with a chat request.</summary>
    /// <related type="Article" href="https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai#tool-calling">Tool calling.</related>
    [JsonIgnore]
    public IList<AITool>? Tools { get; set; }

    /// <summary>Gets or sets a value indicating whether the background responses are allowed.</summary>
    /// <remarks>
    /// <para>
    /// Background responses allow running long-running operations or tasks asynchronously in the background that can be resumed by streaming APIs
    /// and polled for completion by non-streaming APIs.
    /// </para>
    /// <para>
    /// When this property is set to <see langword="true" />, non-streaming APIs have permission to start a background operation and return an initial
    /// response with a continuation token. Subsequent calls to the same API should be made in a polling manner with
    /// the continuation token to get the final result of the operation.
    /// </para>
    /// <para>
    /// When this property is set to <see langword="true" />, streaming APIs are also permitted to start a background operation and begin streaming
    /// response updates until the operation is completed. If the streaming connection is interrupted, the
    /// continuation token obtained from the last update that has one should be supplied to a subsequent call to the same streaming API
    /// to resume the stream from the point of interruption and continue receiving updates until the operation is completed.
    /// </para>
    /// <para>
    /// This property only takes effect if the implementation it's used with supports background responses.
    /// If the implementation does not support background responses, this property will be ignored.
    /// </para>
    /// </remarks>
    public bool? AllowBackgroundResponses { get; set; }

    /// <summary>Gets or sets the continuation token for resuming and getting the result of the chat response identified by this token.</summary>
    /// <remarks>
    /// This property is used for background responses that can be activated via the <see cref="AllowBackgroundResponses"/>
    /// property if the <see cref="IChatClient"/> implementation supports them.
    /// Streamed background responses, such as those returned by default by <see cref="IChatClient.GetStreamingResponseAsync"/>,
    /// can be resumed if interrupted. This means that a continuation token obtained from the <see cref="ChatResponseUpdate.ContinuationToken"/>
    /// of an update just before the interruption occurred can be passed to this property to resume the stream from the point of interruption.
    /// Non-streamed background responses, such as those returned by <see cref="IChatClient.GetResponseAsync"/>,
    /// can be polled for completion by obtaining the token from the <see cref="ChatResponse.ContinuationToken"/> property
    /// and passing it to this property on subsequent calls to <see cref="IChatClient.GetResponseAsync"/>.
    /// </remarks>
    public ResponseContinuationToken? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets a callback responsible for creating the raw representation of the chat options from an underlying implementation.
    /// </summary>
    /// <remarks>
    /// The underlying <see cref="IChatClient" /> implementation might have its own representation of options.
    /// When <see cref="IChatClient.GetResponseAsync" /> or <see cref="IChatClient.GetStreamingResponseAsync" />
    /// is invoked with a <see cref="ChatOptions" />, that implementation might convert the provided options into
    /// its own representation in order to use it while performing the operation. For situations where a consumer knows
    /// which concrete <see cref="IChatClient" /> is being used and how it represents options, a new instance of that
    /// implementation-specific options type can be returned by this callback for the <see cref="IChatClient" />
    /// implementation to use, instead of creating a new instance. Such implementations might mutate the supplied options
    /// instance further based on other settings supplied on this <see cref="ChatOptions" /> instance or from other inputs,
    /// like the enumerable of <see cref="ChatMessage"/>s. Therefore, it is <b>strongly recommended</b> to not return shared instances
    /// and instead make the callback return a new instance on each call.
    /// This is typically used to set an implementation-specific setting that isn't otherwise exposed from the strongly typed
    /// properties on <see cref="ChatOptions" />.
    /// </remarks>
    [JsonIgnore]
    public Func<IChatClient, object?>? RawRepresentationFactory { get; set; }

    /// <summary>Gets or sets any additional properties associated with the options.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>Produces a clone of the current <see cref="ChatOptions"/> instance.</summary>
    /// <returns>A clone of the current <see cref="ChatOptions"/> instance.</returns>
    /// <remarks>
    /// <para>
    /// The clone will have the same values for all properties as the original instance. Any collections, like <see cref="Tools"/>,
    /// <see cref="StopSequences"/>, and <see cref="AdditionalProperties"/>, are shallow-cloned, meaning a new collection instance is created,
    /// but any references contained by the collections are shared with the original.
    /// </para>
    /// <para>
    /// Derived types should override <see cref="Clone"/> to return an instance of the derived type.
    /// </para>
    /// </remarks>
    public virtual ChatOptions Clone() => new(this);
}
