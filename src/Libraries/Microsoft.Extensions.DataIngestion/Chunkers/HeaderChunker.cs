// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Splits documents into chunks based on headers and their corresponding levels, preserving the header context.
/// </summary>
public sealed class HeaderChunker : IngestionChunker
{
    private const int MaxHeaderLevel = 10;
    private readonly ElementsChunker _elementsChunker;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderChunker"/> class.
    /// </summary>
    /// <param name="options">The options for the chunker.</param>
    public HeaderChunker(IngestionChunkerOptions options)
    {
        _elementsChunker = new(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(ExtractionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);

        List<ExtractionDocumentElement> elements = [];
        string?[] headers = new string?[MaxHeaderLevel + 1];
        IReadOnlyList<ExtractionDocumentElement>?[] headerSources = new IReadOnlyList<ExtractionDocumentElement>?[MaxHeaderLevel + 1];
        Dictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>> elementSources = [];

        foreach (var (element, content, sources) in IngestionDocumentElementExtensions.EnumerateSemanticContent(document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is null)
            {
                foreach (var chunk in _elementsChunker.Process(document, string.Empty, [], localContent: content))
                {
                    yield return chunk;
                }

                continue;
            }

            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            elementSources[element] = sources;
            if (element is ExtractionDocumentTextElement header && string.Equals(header.Role, "heading", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var chunk in SplitIntoChunks(document, headers, headerSources, elements, elementSources))
                {
                    yield return chunk;
                }

                int headerLevel = header.HeadingLevel.GetValueOrDefault();
                headers[headerLevel] = content;
                headerSources[headerLevel] = sources;
                headers.AsSpan(headerLevel + 1).Clear(); // clear all lower level headers
                headerSources.AsSpan(headerLevel + 1).Clear();

                continue; // don't add headers to the elements list, they are part of the context
            }

            elements.Add(element);
        }

        // take care of any remaining paragraphs
        foreach (var chunk in SplitIntoChunks(document, headers, headerSources, elements, elementSources))
        {
            yield return chunk;
        }
    }

    private IEnumerable<IngestionChunk> SplitIntoChunks(ExtractionDocument document, string?[] headers,
        IReadOnlyList<ExtractionDocumentElement>?[] headerSources, List<ExtractionDocumentElement> elements,
        IReadOnlyDictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>> elementSources)
    {
        if (elements.Count > 0)
        {
            string chunkHeader = string.Join(" ", headers.Where(h => !string.IsNullOrEmpty(h)));

            var contextSources = headerSources.Where(sources => sources is not null).SelectMany(sources => sources!).Distinct().ToArray();
            foreach (var chunk in _elementsChunker.Process(document, chunkHeader, elements, contextSources, elementSources: elementSources))
            {
                yield return chunk;
            }

            elements.Clear();
        }
    }
}
