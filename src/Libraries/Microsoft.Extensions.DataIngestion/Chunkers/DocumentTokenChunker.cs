// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

/// <summary>
/// Processes a document by tokenizing its semantic content and dividing it into overlapping chunks.
/// </summary>
/// <remarks>Tables may be split mid-row. Token limits include separators between local payloads.</remarks>
public sealed class DocumentTokenChunker : IngestionChunker
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokensPerChunk;
    private readonly int _chunkOverlap;

    /// <summary>Initializes a new instance of the <see cref="DocumentTokenChunker"/> class.</summary>
    /// <param name="options">The tokenizer, chunk size, and overlap options.</param>
    public DocumentTokenChunker(IngestionChunkerOptions options)
    {
        _ = Throw.IfNull(options);
        _tokenizer = options.Tokenizer;
        _maxTokensPerChunk = options.MaxTokensPerChunk;
        _chunkOverlap = options.OverlapTokens;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(
        IngestionDocument document, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);
        StringBuilder text = new();
        List<(int Start, int End, IReadOnlyList<ExtractionDocumentElement> Sources)> ranges = [];
        foreach (var (_, content, sources) in IngestionDocumentElementExtensions.EnumerateSemanticContent(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            if (text.Length > 0)
            {
                _ = text.AppendLine();
            }

            int start = text.Length;
            _ = text.Append(content);
            ranges.Add((start, text.Length, sources));
        }

        string remaining = text.ToString();
        int offset = 0;
        while (remaining.Length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = _tokenizer.GetIndexByTokenCount(remaining, _maxTokensPerChunk, out _, out _, considerNormalization: false);
            if (index <= 0)
            {
                throw new InvalidOperationException("The tokenizer could not fit any content within the chunk token limit.");
            }

            string chunkText = remaining.Substring(0, index);
            int tokenCount = _tokenizer.CountTokens(chunkText, considerNormalization: false);
            List<ExtractionDocumentElement> sources = [];
            foreach (var range in ranges)
            {
                if (range.Start < offset + index && range.End > offset)
                {
                    foreach (var source in range.Sources)
                    {
                        if (!sources.Contains(source))
                        {
                            sources.Add(source);
                        }
                    }
                }
            }

            yield return new(new TextContent(chunkText), document, tokenCount, string.Empty) { SourceElements = sources.ToArray() };
            if (index == remaining.Length)
            {
                yield break;
            }

            int advance = index;
            if (_chunkOverlap > 0)
            {
                advance = _tokenizer.GetIndexByTokenCountFromEnd(chunkText, _chunkOverlap, out _, out _, considerNormalization: false);
            }

            if (advance <= 0)
            {
                throw new InvalidOperationException("The tokenizer could not advance beyond the chunk overlap.");
            }

            offset += advance;
            remaining = remaining.Substring(advance);
        }
    }
}
