// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace Microsoft.Extensions.Telemetry.Bench;

internal sealed class SerializedExporterMetrics
{
    private long _batchesEmitted;
    private long _bytesEmitted;
    private long _recordsEmitted;

    public long BatchesEmitted => Interlocked.Read(ref _batchesEmitted);

    public long BytesEmitted => Interlocked.Read(ref _bytesEmitted);

    public long RecordsEmitted => Interlocked.Read(ref _recordsEmitted);

    public void Record(int bytes)
    {
        Interlocked.Increment(ref _recordsEmitted);
        Interlocked.Add(ref _bytesEmitted, bytes);
    }

    public void RecordBatch()
    {
        Interlocked.Increment(ref _batchesEmitted);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _batchesEmitted, 0);
        Interlocked.Exchange(ref _bytesEmitted, 0);
        Interlocked.Exchange(ref _recordsEmitted, 0);
    }
}
