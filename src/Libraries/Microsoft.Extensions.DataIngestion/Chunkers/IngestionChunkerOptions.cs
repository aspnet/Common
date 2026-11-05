// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Options for configuring the ingestion chunker.
/// </summary>
public class IngestionChunkerOptions
{
    // Default values come from https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-chunk-documents#text-split-skill-example
    private const int DefaultOverlapTokens = 500;
    private const int DefaultTokensPerChunk = 2_000;
    private int? _overlapTokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunkerOptions"/> class.
    /// </summary>
    /// <param name="tokenizer">The tokenizer to use for tokenizing input.</param>
    public IngestionChunkerOptions(Tokenizer tokenizer)
    {
        Tokenizer = Throw.IfNull(tokenizer);
    }

    /// <summary>
    /// Gets the <see cref="Tokenizer"/> instance used to process and tokenize input data.
    /// </summary>
    public Tokenizer Tokenizer { get; }

    /// <summary>
    /// Gets or sets the maximum number of tokens allowed in each chunk.
    /// </summary>
    /// <value>
    /// The default is 2000.
    /// </value>
    public int MaxTokensPerChunk
    {
        get => field == default ? DefaultTokensPerChunk : field;
        set
        {
            _ = Throw.IfLessThanOrEqual(value, 0);

            if (_overlapTokens.HasValue && value <= _overlapTokens.Value)
            {
                Throw.ArgumentOutOfRangeException(nameof(value), "Chunk size must be greater than chunk overlap.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of overlapping tokens between consecutive chunks.
    /// </summary>
    /// <value>
    /// The default is 500.
    /// </value>
    public int OverlapTokens
    {
        get
        {
            if (_overlapTokens.HasValue)
            {
                return _overlapTokens.Value;
            }
            else if (MaxTokensPerChunk > DefaultOverlapTokens)
            {
                return DefaultOverlapTokens;
            }
            else
            {
                return 0;
            }
        }
        set
        {
            if (Throw.IfLessThan(value, 0) >= MaxTokensPerChunk)
            {
                Throw.ArgumentOutOfRangeException(nameof(value), "Chunk overlap must be less than chunk size.");
            }

            _overlapTokens = value;
        }
    }
}
