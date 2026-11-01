// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.VectorData;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Writes chunks to a <see cref="VectorStoreCollection{TKey, TRecord}"/>.
/// </summary>
/// <typeparam name="TRecord">The type of the record stored in the vector store.</typeparam>
public class VectorStoreWriter<TRecord> : IngestionChunkWriter
    where TRecord : IngestionChunkVectorRecord, new()
{
    private readonly VectorStoreWriterOptions _options;
    private bool _collectionEnsured;

    /// <summary>
    /// Initializes a new instance of the <see cref="VectorStoreWriter{TRecord}"/> class.
    /// </summary>
    /// <param name="collection">The <see cref="VectorStoreCollection{TKey, TRecord}"/> to use to store the <see cref="IngestionChunk"/> instances.</param>
    /// <param name="options">The options for the vector store writer.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="collection"/> is null.</exception>
    /// <remarks>
    /// You can use the <see cref="VectorStoreExtensions.GetIngestionRecordCollection{TRecord}(VectorStore, string, int, string?, string?)"/>
    /// helper to create a <see cref="VectorStoreCollection{TKey, TRecord}"/> with the appropriate schema for storing ingestion chunks.
    /// </remarks>
    public VectorStoreWriter(VectorStoreCollection<Guid, TRecord> collection, VectorStoreWriterOptions? options = default)
    {
        VectorStoreCollection = Throw.IfNull(collection);
        _options = options ?? new VectorStoreWriterOptions();
    }

    /// <summary>
    /// Gets the underlying <see cref="VectorStoreCollection{TKey,TRecord}"/> used to store the chunks.
    /// </summary>
    public VectorStoreCollection<Guid, TRecord> VectorStoreCollection { get; }

    /// <inheritdoc/>
    public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk> chunks, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(chunks);

        IReadOnlyList<Guid>? preExistingKeys = null;
        List<TRecord>? batch = null;
        long currentBatchTokenCount = 0;
        ExtractionDocument? document = null;

        await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
        {
            if (document is null)
            {
                document = chunk.Document;
            }
            else if (!ReferenceEquals(document, chunk.Document))
            {
                Throw.InvalidOperationException("All chunks passed to WriteAsync must belong to the same document.");
            }

            if (!_collectionEnsured)
            {
                await VectorStoreCollection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
                _collectionEnsured = true;
            }

            // We obtain the IDs of the pre-existing chunks for given document,
            // and delete them after we finish inserting the new chunks,
            // to avoid a situation where we delete the chunks and then fail to insert the new ones.
            preExistingKeys ??= await GetPreExistingChunksIdsAsync(document!, cancellationToken).ConfigureAwait(false);

            TRecord record = CreateRecord(chunk);

            if (chunk.HasMetadata)
            {
                foreach (var metadata in chunk.Metadata)
                {
                    SetMetadata(record, metadata.Key, metadata.Value);
                }
            }

            batch ??= [];

            // Check if adding this chunk would exceed the batch token limit
            // If the batch is empty or the chunk alone exceeds the limit, add it anyway.
            if (batch.Count > 0 && currentBatchTokenCount + chunk.TokenCount > _options.BatchTokenCount)
            {
                await VectorStoreCollection.UpsertAsync(batch, cancellationToken).ConfigureAwait(false);

                batch.Clear();
                currentBatchTokenCount = 0;
            }

            batch.Add(record);
            currentBatchTokenCount += chunk.TokenCount;
        }

        // Upsert any remaining chunks in the batch
        if (batch?.Count > 0)
        {
            await VectorStoreCollection.UpsertAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        if (preExistingKeys?.Count > 0)
        {
            await VectorStoreCollection.DeleteAsync(preExistingKeys, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a record from a chunk, including application-defined durable source references.
    /// </summary>
    /// <param name="chunk">The chunk to persist.</param>
    /// <returns>The record to insert.</returns>
    /// <remarks>
    /// The collection's auto-generated key schema assigns fresh keys when inserting default records.
    /// Incremental ingestion inserts new records before deleting old keys.
    /// Overrides must not reuse pre-existing keys, since those keys are deleted after all inserts succeed.
    /// </remarks>
    protected virtual TRecord CreateRecord(IngestionChunk chunk)
    {
        _ = Throw.IfNull(chunk);
        return new()
        {
            Content = chunk.Content,
            Context = chunk.Context,
            DocumentId = chunk.Document.Identifier,
        };
    }

    /// <summary>
    /// Sets a metadata value on the record.
    /// </summary>
    /// <param name="record">The record on which to set the metadata.</param>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <remarks>
    /// Override this method in derived classes to store metadata as typed properties with
    /// <see cref="VectorStoreDataAttribute"/> attributes.
    /// </remarks>
    protected virtual void SetMetadata(TRecord record, string key, object? value)
    {
        throw new NotSupportedException($"Metadata key '{key}' is not supported. Override {nameof(SetMetadata)} in a derived class to handle metadata.");
    }

    private async Task<IReadOnlyList<Guid>> GetPreExistingChunksIdsAsync(ExtractionDocument document, CancellationToken cancellationToken)
    {
        if (!_options.IncrementalIngestion)
        {
            return [];
        }

        // Each Vector Store has a different max top count limit, so we use low value and loop.
        // Use smaller batch size in debug to be able to test the looping logic without needing to insert a lot of records.
        const int MaxTopCount =
#if RELEASE
            1_000;
#else
            10;
#endif

        List<Guid> keys = [];
        int insertedCount;
        do
        {
            insertedCount = 0;

            await foreach (var record in VectorStoreCollection.GetAsync(
                filter: record => record.DocumentId == document.Identifier,
                top: MaxTopCount,
                options: new() { Skip = keys.Count },
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                keys.Add(record.Key);
                insertedCount++;
            }
        }
        while (insertedCount == MaxTopCount);

        return keys;
    }
}
