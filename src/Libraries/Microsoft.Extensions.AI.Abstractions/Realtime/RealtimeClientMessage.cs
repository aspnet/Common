// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a real-time message the client sends to the model.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIRealTime, UrlFormat = DiagnosticIds.UrlFormat)]
public class RealtimeClientMessage
{
    /// <summary>
    /// Gets or sets the optional message ID associated with the message.
    /// </summary>
    /// <remarks>
    /// This can be used for tracking and correlation purposes.
    /// </remarks>
    public string? MessageId { get; set; }

    /// <summary>
    /// Gets or sets the raw representation of the message.
    /// This can be used to send the raw data to the model.
    /// </summary>
    /// <remarks>
    /// The raw representation is typically used for custom or unsupported message types.
    /// For example, the model may accept a JSON serialized message.
    /// </remarks>
    public object? RawRepresentation { get; set; }
}
