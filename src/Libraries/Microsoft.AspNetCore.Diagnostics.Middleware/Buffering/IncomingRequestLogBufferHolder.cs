// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using System;
using System.Collections.Concurrent;

namespace Microsoft.AspNetCore.Diagnostics.Buffering;

internal sealed class IncomingRequestLogBufferHolder
{
    private readonly ConcurrentDictionary<string, IncomingRequestLogBuffer> _buffers = new();

    public IncomingRequestLogBuffer GetOrAdd(string category, Func<string, IncomingRequestLogBuffer> valueFactory) =>
        _buffers.GetOrAdd(category, valueFactory);

    public void Flush()
    {
        foreach (IncomingRequestLogBuffer buffer in _buffers.Values)
        {
            buffer.Flush();
        }
    }
}
#endif
