// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;

namespace Microsoft.Extensions.Diagnostics.Buffering;

/// <summary>
/// Represents a log record that has been serialized for purposes of buffering or similar.
/// </summary>
/// <remarks>
/// Instances are pooled by <see cref="SerializedLogRecordFactory"/>.
/// </remarks>
[DebuggerDisplay("Message: {FormattedMessage}, LogLevel:{LogLevel}, Timestamp: {Timestamp.ToString(FormatSpecifier)}")]
internal sealed class SerializedLogRecord : BufferedLogRecord, IResettable
{
    private const string FormatSpecifier = "u";

    private DateTimeOffset _timestamp;
    private LogLevel _logLevel;
    private EventId _eventId;
    private string? _exception;
    private string? _formattedMessage;
    private string? _messageTemplate;
    private ActivityTraceId _activityTraceId;
    private ActivitySpanId _activitySpanId;
    private int _managedThreadId;

    /// <inheritdoc/>
    public override DateTimeOffset Timestamp => _timestamp;

    /// <inheritdoc/>
    public override LogLevel LogLevel => _logLevel;

    /// <inheritdoc/>
    public override EventId EventId => _eventId;

    /// <inheritdoc/>
    public override List<KeyValuePair<string, object?>> Attributes { get; } = [];

    /// <inheritdoc/>
    public override string? Exception => _exception;

    /// <inheritdoc/>
    public override string? FormattedMessage => _formattedMessage;

    /// <inheritdoc/>
    public override string? MessageTemplate => _messageTemplate;

    /// <inheritdoc/>
    public override ActivityTraceId? ActivityTraceId => _activityTraceId == default ? null : _activityTraceId;

    /// <inheritdoc/>
    public override ActivitySpanId? ActivitySpanId => _activitySpanId == default ? null : _activitySpanId;

    /// <inheritdoc/>
    public override int? ManagedThreadId => _managedThreadId;

    /// <summary>
    /// Gets the approximate size of the serialized log record in bytes.
    /// </summary>
    public int SizeInBytes { get; private set; }

    /// <summary>
    /// Sets the data of the record, except for its attributes, which are added to <see cref="Attributes"/>.
    /// </summary>
    /// <param name="logLevel">Logging severity level.</param>
    /// <param name="eventId">Event ID.</param>
    /// <param name="timestamp">The time when the log record was first created.</param>
    /// <param name="exceptionMessage">The message of the exception logged with the record, or <see langword="null"/> if there's none.</param>
    /// <param name="formattedMessage">The formatted log message.</param>
    /// <param name="messageTemplate">The original log message template.</param>
    /// <param name="activityTraceId">The activity trace ID of the thread that created the record, or <see langword="default"/> if not available.</param>
    /// <param name="activitySpanId">The activity span ID of the thread that created the record, or <see langword="default"/> if not available.</param>
    /// <param name="managedThreadId">The ID of the thread that created the record.</param>
    /// <param name="sizeInBytes">The approximate size in bytes of this instance.</param>
#pragma warning disable S107 // Methods should not have too many parameters
    public void Set(
        LogLevel logLevel,
        EventId eventId,
        DateTimeOffset timestamp,
        string? exceptionMessage,
        string formattedMessage,
        string? messageTemplate,
        ActivityTraceId activityTraceId,
        ActivitySpanId activitySpanId,
        int managedThreadId,
        int sizeInBytes)
#pragma warning restore S107 // Methods should not have too many parameters
    {
        _logLevel = logLevel;
        _eventId = eventId;
        _timestamp = timestamp;
        _exception = exceptionMessage;
        _formattedMessage = formattedMessage;
        _messageTemplate = messageTemplate;
        _activityTraceId = activityTraceId;
        _activitySpanId = activitySpanId;
        _managedThreadId = managedThreadId;
        SizeInBytes = sizeInBytes;
    }

    /// <summary>
    /// Releases the data of the record, so that the record can be reused.
    /// </summary>
    /// <returns><see langword="true"/>.</returns>
    public bool TryReset()
    {
        _eventId = default;
        Attributes.Clear();
        _exception = null;
        _formattedMessage = null;
        _messageTemplate = null;
        _activityTraceId = default;
        _activitySpanId = default;
        return true;
    }
}
#endif
