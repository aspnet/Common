// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DataIngestion;

internal sealed class TestReader : IngestionDocumentReader
{
    public TestReader(Func<Stream, string, string, CancellationToken, Task<IngestionDocument>> readAsyncCallback)
    {
        ReadAsyncCallback = readAsyncCallback;
    }

    public Func<Stream, string, string, CancellationToken, Task<IngestionDocument>> ReadAsyncCallback { get; }

    public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
        => ReadAsyncCallback(source, identifier, mediaType, cancellationToken);
}
