// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction.Tests;

public sealed class DocumentExtractionCleanupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Adapter_FailureOrCancellationCleansProviderStreamsAndKeepsPrimaryFirst(bool cancel, bool throwingCleanup)
    {
        using MemoryStream input = new([42, 43]);
        using Native native = new();
        using CancellationTokenSource cancellation = new();
        IOException firstFailure = new("first body cleanup");
        IOException lastFailure = new("last body cleanup");
        using CleanupStream first = new(throwingCleanup ? firstFailure : null);
        using CleanupStream middle = new();
        using CleanupStream last = new(throwingCleanup ? lastFailure : null);
        InvalidOperationException primary = new("provider mapping failed");
        using SemaphoreSlim acquired = new(0, 1);
        using SemaphoreSlim release = new(0, 1);
        using PartialAdapter adapter = new([first, middle, last], native, primary, acquired, release);

        Exception actual = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            Task<ExtractionDocument> pending = adapter.ExtractAsync(input, "partial", "application/pdf",
                new DocumentExtractionOptions { }, cancellation.Token);
            await acquired.WaitAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal(3, adapter.AcquireCount);
            Assert.True(first.CanRead);
            Assert.True(middle.CanRead);
            Assert.True(last.CanRead);
            if (cancel)
            {
                cancellation.Cancel();
            }

            release.Release();
            await pending;
        });
        Exception actualPrimary = actual;
        if (throwingCleanup)
        {
            AggregateException aggregate = Assert.IsType<AggregateException>(actual).Flatten();
            Assert.Equal(3, aggregate.InnerExceptions.Count);
            actualPrimary = aggregate.InnerExceptions[0];
            Assert.Same(firstFailure, aggregate.InnerExceptions[1]);
            Assert.Same(lastFailure, aggregate.InnerExceptions[2]);
        }

        if (cancel)
        {
            Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(actualPrimary).CancellationToken);
        }
        else
        {
            Assert.Same(primary, actualPrimary);
        }

        Assert.Equal(cancellation.Token, adapter.Token);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, middle.DisposeCount);
        Assert.Equal(1, last.DisposeCount);
        Assert.False(first.CanRead);
        Assert.False(middle.CanRead);
        Assert.False(last.CanRead);
        adapter.Dispose();
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task Adapter_PreCanceledCallAcquiresNothing()
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using CleanupStream stream = new();
        using SemaphoreSlim acquired = new(0, 1);
        using SemaphoreSlim release = new(0, 1);
        using PartialAdapter adapter = new([stream], native, new InvalidOperationException("unused"), acquired, release);

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ExtractAsync(input, "document", "application/pdf", cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, adapter.AcquireCount);
        Assert.Equal(0, acquired.CurrentCount);
        Assert.Equal(0, stream.DisposeCount);
        Assert.True(stream.CanRead);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(42, input.ReadByte());
    }

    // This fake models an adapter's responsibility, not middleware or a core body collector.
    // Resources belong to the adapter's operation, never to the data result.
    private sealed class PartialAdapter(CleanupStream[] bodies, Native native, Exception primary, SemaphoreSlim acquired, SemaphoreSlim release) : DocumentExtractionClient
    {
        internal int AcquireCount { get; private set; }
        internal CancellationToken Token { get; private set; }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Attempts cleanup of every resource before reporting primary and cleanup failures together.")]
        public override async Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            ExtractionDocument partial = new(identifier) { RawRepresentation = native };
            try
            {
                foreach (CleanupStream body in bodies)
                {
                    AcquireCount++;
                }

                acquired.Release();
                Assert.Same(native, partial.RawRepresentation);
                Assert.Empty(partial.Sections);

                // Cancellation is observed explicitly after the deterministic acquisition gate.
                await release.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw primary;
            }
            catch (Exception failure)
            {
                List<Exception> failures = [failure];
                for (int i = 0; i < AcquireCount; i++)
                {
                    try
                    {
                        bodies[i].Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        failures.Add(cleanup);
                    }
                }

                if (failures.Count > 1)
                {
                    throw new AggregateException(failures);
                }

                throw;
            }
        }
    }

    private sealed class Native : IDisposable
    {
        internal int DisposeCount { get; private set; }
        internal int ToStringCount { get; private set; }
        public void Dispose() => DisposeCount++;
        public override string ToString()
        {
            ToStringCount++;
            return "NATIVE_MUST_NOT_BE_INSPECTED";
        }
    }

    private sealed class CleanupStream(Exception? failure = null) : MemoryStream(new byte[] { 17, 23 })
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (DisposeCount != 0)
            {
                return;
            }

            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
            if (disposing && failure is not null)
            {
                throw failure;
            }
        }
    }
}
