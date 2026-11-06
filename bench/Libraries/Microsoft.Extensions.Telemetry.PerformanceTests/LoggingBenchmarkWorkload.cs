// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Telemetry.Bench;

internal static class LoggingBenchmarkWorkload
{
    public const int CategoryCount = 4;
    public const int SamplingInvocationsPerIteration = 8;
    public const string CriticalCategoryPrefix = "Benchmark.Critical.";
    public const string HighVolumeCategoryPrefix = "Benchmark.HighVolume.";

    private const int EventCount = 16;

    private static readonly string[] _categories =
    [
        $"{HighVolumeCategoryPrefix}Orders",
        $"{HighVolumeCategoryPrefix}Payments",
        $"{CriticalCategoryPrefix}Audit",
        "Benchmark.Diagnostics"
    ];

    private static readonly Action<ILogger, int, Exception?>[] _messages = CreateMessages();

    public static ILogger[] CreateLoggers(ILoggerFactory factory)
    {
        var loggers = new ILogger[CategoryCount];
        for (int i = 0; i < loggers.Length; i++)
        {
            loggers[i] = factory.CreateLogger(_categories[i]);
        }

        return loggers;
    }

    public static ILogger[] CreateLoggers(ILoggerFactory factory, int categoryCount)
    {
        var loggers = new ILogger[categoryCount];
        for (int i = 0; i < loggers.Length; i++)
        {
            loggers[i] = factory.CreateLogger($"Benchmark.Category.{i:D3}");
        }

        return loggers;
    }

    public static void LogBatch(ILogger[] loggers, int recordCount)
    {
        for (int i = 0; i < recordCount; i++)
        {
            LogRecord(loggers, i);
        }
    }

    public static void LogBatchWithObserver(ILogger[] loggers, int recordCount, Action observer)
    {
        for (int i = 0; i < recordCount; i++)
        {
            LogRecord(loggers, i);
            if ((i & 255) == 255)
            {
                observer();
            }
        }

        observer();
    }

    public static void LogInterleaved(
        ILogger[] loggers,
        int firstRecord,
        int recordStride,
        int recordCount)
    {
        for (int i = firstRecord; i < recordCount; i += recordStride)
        {
            LogRecord(loggers, i);
        }
    }

    private static Action<ILogger, int, Exception?>[] CreateMessages()
    {
        var messages = new Action<ILogger, int, Exception?>[EventCount];
        for (int i = 0; i < messages.Length; i++)
        {
            messages[i] = LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(i + 1, $"BenchmarkEvent{i + 1}"),
                "Benchmark message {Value}");
        }

        return messages;
    }

    private static int SelectEventIndex(int recordIndex)
    {
        int percentile = recordIndex % 100;
        if (percentile < 70)
        {
            return 0;
        }

        if (percentile < 85)
        {
            return 1;
        }

        if (percentile < 93)
        {
            return 2;
        }

        return 3 + (recordIndex % (EventCount - 3));
    }

    private static void LogRecord(ILogger[] loggers, int recordIndex)
    {
        int eventIndex = SelectEventIndex(recordIndex);
        int categoryIndex = (recordIndex / 100) % loggers.Length;
        _messages[eventIndex](loggers[categoryIndex], recordIndex, null);
    }
}
