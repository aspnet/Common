// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET9_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;
using Microsoft.Shared.Pools;

namespace Microsoft.AspNetCore.Diagnostics.Buffering;

internal sealed class IncomingRequestLogBuffer
{
    private const int MaxBatchSize = 256;
    private static readonly ObjectPool<List<SerializedLogRecord>> _recordsToEmitListPool =
        PoolFactory.CreateListPoolWithCapacity<SerializedLogRecord>(MaxBatchSize);

    private readonly IBufferedLogger _bufferedLogger;
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly IOptionsMonitor<PerRequestLogBufferingOptions> _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly LogBufferingFilterRule[] _filterRules;
    private readonly Lock _bufferSwapLock = new();
    private ConcurrentQueue<SerializedLogRecord> _activeBuffer = new();
    private ConcurrentQueue<SerializedLogRecord> _standbyBuffer = new();
    private int _activeBufferSize;
    private DateTimeOffset _lastFlushTimestamp;

    public IncomingRequestLogBuffer(
        IBufferedLogger bufferedLogger,
        string category,
        IOptionsMonitor<PerRequestLogBufferingOptions> options,
        IExternalScopeProvider scopeProvider)
    {
        _bufferedLogger = bufferedLogger;
        _scopeProvider = scopeProvider;
        _options = options;
        _filterRules = LogBufferingFilterRuleSelector.SelectByCategory(_options.CurrentValue.Rules.ToArray(), category);
    }

    public bool TryEnqueue<TState>(LogEntry<TState> logEntry)
    {
        if (_timeProvider.GetUtcNow() < _lastFlushTimestamp + _options.CurrentValue.AutoFlushDuration)
        {
            return false;
        }

        IReadOnlyList<KeyValuePair<string, object?>>? attributes = logEntry.State as IReadOnlyList<KeyValuePair<string, object?>>;
        if (attributes is null)
        {
            // we expect state to be either ModernTagJoiner or LegacyTagJoiner
            // which both implement IReadOnlyList<KeyValuePair<string, object?>>
            // and if not, we throw an exception
            Throw.InvalidOperationException(
                $"Unsupported type of log state detected: {typeof(TState)}, expected IReadOnlyList<KeyValuePair<string, object?>>");
        }

        if (LogBufferingFilterRuleSelector.Select(_filterRules, logEntry.LogLevel, logEntry.EventId, attributes) is null)
        {
            // buffering is not enabled for this log entry,
            // return false to indicate that the log entry should be logged normally.
            return false;
        }

        SerializedLogRecord serializedLogRecord = SerializedLogRecordFactory.Create(
            logEntry.LogLevel,
            logEntry.EventId,
            _timeProvider.GetUtcNow(),
            attributes,
            logEntry.Exception,
            logEntry.Formatter(logEntry.State, logEntry.Exception),
            _options.CurrentValue.IncludeScopes ? _scopeProvider : null);

        if (serializedLogRecord.SizeInBytes > _options.CurrentValue.MaxLogRecordSizeInBytes)
        {
            SerializedLogRecordFactory.Return(serializedLogRecord);
            return false;
        }

        lock (_bufferSwapLock)
        {
            _activeBuffer.Enqueue(serializedLogRecord);
            _ = Interlocked.Add(ref _activeBufferSize, serializedLogRecord.SizeInBytes);

        }

        TrimExcessRecords();

        return true;
    }

    public void Flush()
    {
        _lastFlushTimestamp = _timeProvider.GetUtcNow();

        ConcurrentQueue<SerializedLogRecord> tempBuffer;
        int numItemsToEmit;
        lock (_bufferSwapLock)
        {
            tempBuffer = _activeBuffer;
            _activeBuffer = _standbyBuffer;
            _standbyBuffer = tempBuffer;

            numItemsToEmit = tempBuffer.Count;

            _ = Interlocked.Exchange(ref _activeBufferSize, 0);
        }

        for (int offset = 0; offset < numItemsToEmit && !tempBuffer.IsEmpty; offset += MaxBatchSize)
        {
            int currentBatchSize = Math.Min(MaxBatchSize, numItemsToEmit - offset);
            List<SerializedLogRecord> recordsToEmit = _recordsToEmitListPool.Get();
            try
            {
                for (int i = 0; i < currentBatchSize && tempBuffer.TryDequeue(out SerializedLogRecord? bufferedRecord); i++)
                {
                    recordsToEmit.Add(bufferedRecord);
                }

                _bufferedLogger.LogRecords(recordsToEmit);
            }
            finally
            {
                _recordsToEmitListPool.Return(recordsToEmit);
            }
        }
    }

    private void TrimExcessRecords()
    {
        while (_activeBufferSize > _options.CurrentValue.MaxPerRequestBufferSizeInBytes &&
               _activeBuffer.TryDequeue(out SerializedLogRecord? item))
        {
            _ = Interlocked.Add(ref _activeBufferSize, -item.SizeInBytes);
            SerializedLogRecordFactory.Return(item);
        }
    }
}
#endif
