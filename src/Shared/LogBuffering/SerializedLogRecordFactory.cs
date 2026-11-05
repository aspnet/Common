// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Diagnostics.Buffering;

internal static class SerializedLogRecordFactory
{
    private const string OriginalFormat = "{OriginalFormat}";

    // The approximate size of a SerializedLogRecord instance (112 bytes) and of its slot in the buffer (16 bytes).
    private const int SerializedLogRecordSize = 128;

    private static readonly ObjectPool<SerializedLogRecord> _recordPool =
        PoolFactory.CreateResettingPool<SerializedLogRecord>();

    public static SerializedLogRecord Create(
        LogLevel logLevel,
        EventId eventId,
        DateTimeOffset timestamp,
        IReadOnlyList<KeyValuePair<string, object?>> attributes,
        Exception? exception,
        string formattedMessage,
        IExternalScopeProvider? scopeProvider)
    {
        SerializedLogRecord record = _recordPool.Get();
        int sizeInBytes = SerializedLogRecordSize;
        string? messageTemplate = null;
        List<KeyValuePair<string, object?>> serializedAttributes = record.Attributes;
        _ = serializedAttributes.EnsureCapacity(attributes.Count);
        for (int i = 0; i < attributes.Count; i++)
        {
            KeyValuePair<string, object?> attribute = attributes[i];
            string key = attribute.Key;
            string value = attribute.Value?.ToString() ?? string.Empty;

            // deliberately not counting the size of the key,
            // as it is constant strings in the vast majority of cases

            sizeInBytes += CalculateStringSize(value);

            if (key == OriginalFormat && attribute.Value is string template)
            {
                messageTemplate = template;
            }

            serializedAttributes.Add(new KeyValuePair<string, object?>(key, value));
        }

        if (scopeProvider is not null)
        {
            sizeInBytes += AddScopes(serializedAttributes, scopeProvider);
        }

        string? exceptionMessage = null;
        if (exception is not null)
        {
            exceptionMessage = exception.Message;
            sizeInBytes += CalculateStringSize(exceptionMessage);
        }

        sizeInBytes += CalculateStringSize(formattedMessage);

        // Capturing the state of the thread that creates the record, as it's no longer available when the record is flushed.
        // Trace and span IDs are not counted towards the size, because they wrap strings cached by the activity.
        ActivityTraceId activityTraceId = default;
        ActivitySpanId activitySpanId = default;
        Activity? activity = Activity.Current;
        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
        {
            activityTraceId = activity.TraceId;
            activitySpanId = activity.SpanId;
        }

        record.Set(
            logLevel,
            eventId,
            timestamp,
            exceptionMessage,
            formattedMessage,
            messageTemplate,
            activityTraceId,
            activitySpanId,
            Environment.CurrentManagedThreadId,
            sizeInBytes);

        return record;
    }

    /// <summary>
    /// Returns a record to the pool, so that the record can be reused.
    /// </summary>
    /// <remarks>
    /// Must only be called for records which have not been emitted, as loggers can still hold on to emitted records' attributes.
    /// </remarks>
    public static void Return(SerializedLogRecord bufferedRecord)
    {
        _recordPool.Return(bufferedRecord);
    }

    /// <summary>
    /// Adds the name/value pairs of the current scopes to the attributes of a log record.
    /// </summary>
    /// <returns>The approximate size in bytes of the added values.</returns>
    private static int AddScopes(List<KeyValuePair<string, object?>> serializedAttributes, IExternalScopeProvider scopeProvider)
    {
        int count = serializedAttributes.Count;
        scopeProvider.ForEachScope(static (scope, attributes) =>
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object?>> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    AddScopeItem(attributes, list[i]);
                }
            }
            else if (scope is IEnumerable<KeyValuePair<string, object?>> items)
            {
                foreach (KeyValuePair<string, object?> item in items)
                {
                    AddScopeItem(attributes, item);
                }
            }
        }, serializedAttributes);

        int sizeInBytes = 0;
        for (int i = count; i < serializedAttributes.Count; i++)
        {
            sizeInBytes += CalculateStringSize((string)serializedAttributes[i].Value!);
        }

        // "{OriginalFormat}" needs to stay the last attribute.
        if (count > 0 && serializedAttributes[count - 1].Key == OriginalFormat)
        {
            KeyValuePair<string, object?> originalFormat = serializedAttributes[count - 1];
            serializedAttributes.RemoveAt(count - 1);
            serializedAttributes.Add(originalFormat);
        }

        return sizeInBytes;
    }

    private static void AddScopeItem(List<KeyValuePair<string, object?>> attributes, KeyValuePair<string, object?> item)
    {
        // Skips the scope's {OriginalFormat} in favor of the log record's own {OriginalFormat}.
        if (item.Key != OriginalFormat)
        {
            attributes.Add(new KeyValuePair<string, object?>(item.Key, item.Value?.ToString() ?? string.Empty));
        }
    }

    private static int CalculateStringSize(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        // Base size: object overhead (16 bytes) + other stuff.
        const int BaseSize = 26;

        // Strings are aligned to 8-byte boundaries
        const int Alignment = 7;

        int charSize = str.Length * sizeof(char);
        return (BaseSize + charSize + Alignment) & ~Alignment;
    }
}
#endif
