// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

// dummy implementation for collecting test output
internal class LogCollector : ILoggerProvider
{
    private readonly List<(string categoryName, LogLevel logLevel, EventId eventId, Exception? exception, string message)> _items = [];

    public (string categoryName, LogLevel logLevel, EventId eventId, Exception? exception, string message)[] ToArray()
    {
        lock (_items)
        {
            return _items.ToArray();
        }
    }

    public void WriteTo(ITestOutputHelper log)
    {
        lock (_items)
        {
            foreach (var logItem in _items)
            {
                var errSuffix = logItem.exception is null ? "" : $" - {logItem.exception.Message}";
                log.WriteLine($"{logItem.categoryName} {logItem.eventId}: {logItem.message}{errSuffix}");
            }
        }
    }

    public async Task WaitForLogsAsync(int[] expectedErrorIds, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_items)
            {
                // Check for exact match to avoid race conditions with AssertErrors
                if (_items.Count == expectedErrorIds.Length)
                {
                    bool match = true;
                    for (int i = 0; i < expectedErrorIds.Length; i++)
                    {
                        if (_items[i].eventId.Id != expectedErrorIds[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        return; // Success
                    }
                }

                // If we have more logs than expected, something unexpected happened
                // Stop waiting to let AssertErrors report the mismatch
                if (_items.Count > expectedErrorIds.Length)
                {
                    return;
                }
            }

            await Task.Delay(10);
        }

        // Timeout reached, will fail in AssertErrors
    }

    public void AssertErrors(int[] errorIds)
    {
        lock (_items)
        {
            bool same;
            if (errorIds.Length == _items.Count)
            {
                int index = 0;
                same = true;
                foreach (var item in _items)
                {
                    if (item.eventId.Id != errorIds[index++])
                    {
                        same = false;
                        break;
                    }
                }
            }
            else
            {
                same = false;
            }

            if (!same)
            {
                // we expect this to fail, then
                Assert.Equal(string.Join(",", errorIds), string.Join(",", _items.Select(static x => x.eventId.Id)));
            }
        }
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) => new TypedLogCollector(this, categoryName);

    void IDisposable.Dispose()
    {
        // nothing to do
    }

    private sealed class TypedLogCollector(LogCollector parent, string categoryName) : ILogger
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        bool ILogger.IsEnabled(LogLevel logLevel) => true;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (parent._items)
            {
                parent._items.Add((categoryName, logLevel, eventId, exception, formatter(state, exception)));
            }
        }
    }
}
