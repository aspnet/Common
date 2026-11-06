// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.Telemetry.Bench;

internal sealed class SerializedExporterLoggerProvider : ILoggerProvider
{
    private readonly SerializedExporterMetrics? _metrics;

    public SerializedExporterLoggerProvider(SerializedExporterMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public ILogger CreateLogger(string categoryName) => new SerializedExporterLogger(categoryName, _metrics);

    public void Dispose()
    {
    }

    private sealed class SerializedExporterLogger : ILogger, IBufferedLogger
    {
        private sealed class Scope : IDisposable
        {
            public static Scope Instance { get; } = new();

            public void Dispose()
            {
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNull(name);
                    break;
                case bool boolean:
                    writer.WriteBoolean(name, boolean);
                    break;
                case int integer:
                    writer.WriteNumber(name, integer);
                    break;
                case long longInteger:
                    writer.WriteNumber(name, longInteger);
                    break;
                case double doubleValue:
                    writer.WriteNumber(name, doubleValue);
                    break;
                case string text:
                    writer.WriteString(name, text);
                    break;
                default:
                    writer.WriteString(name, value.ToString());
                    break;
            }
        }

        private readonly ArrayBufferWriter<byte> _output = new();
        private readonly string _category;
        private readonly SerializedExporterMetrics? _metrics;

        public SerializedExporterLogger(string category, SerializedExporterMetrics? metrics)
        {
            _category = category;
            _metrics = metrics;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => Scope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Serialize(
                logLevel,
                eventId,
                state as IReadOnlyList<KeyValuePair<string, object?>>,
                formatter(state, exception),
                exception?.ToString());
        }

        public void LogRecords(IEnumerable<BufferedLogRecord> records)
        {
            _metrics?.RecordBatch();

            foreach (BufferedLogRecord record in records)
            {
                Serialize(
                    record.LogLevel,
                    record.EventId,
                    record.Attributes,
                    record.FormattedMessage ?? string.Empty,
                    record.Exception);
            }
        }

        private void Serialize(
            LogLevel logLevel,
            EventId eventId,
            IReadOnlyList<KeyValuePair<string, object?>>? attributes,
            string formattedMessage,
            string? exception)
        {
            _output.Clear();

            using var writer = new Utf8JsonWriter(_output);
            writer.WriteStartObject();
            writer.WriteString("category", _category);
            writer.WriteString("level", logLevel.ToString());
            writer.WriteNumber("eventId", eventId.Id);
            writer.WriteString("eventName", eventId.Name);
            writer.WriteString("message", formattedMessage);

            if (exception is not null)
            {
                writer.WriteString("exception", exception);
            }

            writer.WriteStartObject("attributes");
            if (attributes is not null)
            {
                for (int i = 0; i < attributes.Count; i++)
                {
                    KeyValuePair<string, object?> attribute = attributes[i];
                    WriteValue(writer, attribute.Key, attribute.Value);
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();

            _metrics?.Record(_output.WrittenCount);
        }
    }
}
