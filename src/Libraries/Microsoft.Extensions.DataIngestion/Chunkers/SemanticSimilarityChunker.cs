// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

/// <summary>
/// Splits a <see cref="IngestionDocument"/> into chunks based on semantic similarity between its elements based on cosine distance of their embeddings.
/// </summary>
public sealed class SemanticSimilarityChunker : IngestionChunker
{
    private readonly ElementsChunker _elementsChunker;
    private readonly IEmbeddingGenerator<TextContent, Embedding<float>> _embeddingGenerator;
    private readonly float _thresholdPercentile;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticSimilarityChunker"/> class.
    /// </summary>
    /// <param name="embeddingGenerator">Embedding generator.</param>
    /// <param name="options">The options for the chunker.</param>
    /// <param name="thresholdPercentile">Threshold percentile to consider the chunks to be sufficiently similar. 95th percentile will be used if not specified.</param>
    public SemanticSimilarityChunker(
        IEmbeddingGenerator<TextContent, Embedding<float>> embeddingGenerator,
        IngestionChunkerOptions options,
        float? thresholdPercentile = null)
    {
        _embeddingGenerator = embeddingGenerator;
        _elementsChunker = new(options);

        if (thresholdPercentile < 0f || thresholdPercentile > 100f)
        {
            Throw.ArgumentOutOfRangeException(nameof(thresholdPercentile), "Threshold percentile must be between 0 and 100.");
        }

        _thresholdPercentile = thresholdPercentile ?? 95.0f;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);

        foreach (var chunk in _elementsChunker.Process(document, string.Empty, [],
            localContent: IngestionDocumentElementExtensions.GetSemanticContent(document.ExtractedDocument.Content)))
        {
            yield return chunk;
        }

        Dictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>> sources = [];
        List<(ExtractionDocumentElement, float)> distances = await CalculateDistancesAsync(document, sources, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in MakeChunks(document, distances, sources))
        {
            yield return chunk;
        }
    }

    private async Task<List<(ExtractionDocumentElement element, float distance)>> CalculateDistancesAsync(
        IngestionDocument document, Dictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>> sources, CancellationToken cancellationToken)
    {
        List<(ExtractionDocumentElement element, float distance)> elementDistances = [];
        List<TextContent> semanticContents = [];

        foreach (var (element, semanticContent, provenance) in IngestionDocumentElementExtensions.EnumerateSemanticContent(document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is not null && !string.IsNullOrEmpty(semanticContent))
            {
                sources[element] = provenance;
                elementDistances.Add((element, default));
                semanticContents.Add(new TextContent(semanticContent!));
            }
        }

        if (elementDistances.Count > 0)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(semanticContents, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (embeddings.Count != elementDistances.Count)
            {
                Throw.InvalidOperationException("The number of embeddings returned does not match the number of document elements.");
            }

            for (int i = 0; i < elementDistances.Count - 1; i++)
            {
                float distance = 1 - TensorPrimitives.CosineSimilarity(embeddings[i].Vector.Span, embeddings[i + 1].Vector.Span);
                elementDistances[i] = (elementDistances[i].element, distance);
            }
        }

        return elementDistances;
    }

    private IEnumerable<IngestionChunk> MakeChunks(IngestionDocument document, List<(ExtractionDocumentElement element, float distance)> elementDistances,
        IReadOnlyDictionary<ExtractionDocumentElement, IReadOnlyList<ExtractionDocumentElement>> sources)
    {
        float distanceThreshold = Percentile(elementDistances);

        List<ExtractionDocumentElement> elementAccumulator = [];
        string context = string.Empty;
        for (int i = 0; i < elementDistances.Count; i++)
        {
            var (element, distance) = elementDistances[i];

            elementAccumulator.Add(element);
            if (distance > distanceThreshold || i == elementDistances.Count - 1)
            {
                foreach (var chunk in _elementsChunker.Process(document, context, elementAccumulator, elementSources: sources))
                {
                    yield return chunk;
                }
                elementAccumulator.Clear();
            }
        }
    }

    private float Percentile(List<(ExtractionDocumentElement element, float distance)> elementDistances)
    {
        if (elementDistances.Count == 0)
        {
            return 0f;
        }
        else if (elementDistances.Count == 1)
        {
            return elementDistances[0].distance;
        }

        float[] sorted = new float[elementDistances.Count];
        for (int elementIndex = 0; elementIndex < elementDistances.Count; elementIndex++)
        {
            sorted[elementIndex] = elementDistances[elementIndex].distance;
        }
        Array.Sort(sorted);

        float i = (_thresholdPercentile / 100f) * (sorted.Length - 1);
        int i0 = (int)i;
        int i1 = Math.Min(i0 + 1, sorted.Length - 1);
        return sorted[i0] + ((i - i0) * (sorted[i1] - sorted[i0]));
    }
}
