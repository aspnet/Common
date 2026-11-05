// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents options for enrichers that use an AI chat client.
/// </summary>
public class EnricherOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnricherOptions"/> class.
    /// </summary>
    /// <param name="chatClient">The AI chat client to be used.</param>
    public EnricherOptions(IChatClient chatClient)
    {
        ChatClient = Throw.IfNull(chatClient);
    }

    /// <summary>
    /// Gets the AI chat client to be used.
    /// </summary>
    public IChatClient ChatClient { get; }

    /// <summary>
    /// Gets or sets the options for the <see cref="ChatClient"/>.
    /// </summary>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>
    /// Gets or sets the logger factory to be used for logging.
    /// </summary>
    /// <remarks>
    /// Enricher failures should not fail the whole ingestion pipeline, as they are best-effort enhancements.
    /// This logger factory can be used to create loggers to log such failures.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Gets or sets the batch size for processing chunks.
    /// </summary>
    /// <value>
    /// The default is 20.
    /// </value>
    public int BatchSize { get; set => field = Throw.IfLessThanOrEqual(value, 0); } = 20;

    internal EnricherOptions Clone() => new(ChatClient)
    {
        ChatOptions = ChatOptions?.Clone(),
        LoggerFactory = LoggerFactory,
        BatchSize = BatchSize
    };
}
