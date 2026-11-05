// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics.Tensors;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Routes requests by semantic similarity to app-provided example utterances.</summary>
/// <remarks>
/// <para>
/// Profile embeddings are generated lazily and cached. Each request embeds the last user message and selects the
/// client with the highest score after aggregating the cosine similarities of the best-matching profile utterances.
/// The configured default client is selected when no user message is available or when the highest score is below the
/// configured threshold.
/// </para>
/// <para>
/// The configured client instances are used as stable routing identities and are distinguished by reference.
/// Per-call options do not participate in that identity. By default this instance owns the clients and embedding
/// generator and disposes them when it is disposed.
/// </para>
/// <para>
/// The example-utterance routing approach is inspired by
/// <see href="https://github.com/aurelio-labs/semantic-router">Aurelio Labs' semantic-router project</see>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIRoutingChat, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class SemanticRoutingChatClient : RoutingChatClient
{
    /// <summary>Specifies how profile similarity scores are aggregated for each client.</summary>
    public enum ScoreAggregation
    {
        /// <summary>Average the matching profile scores for each client.</summary>
        Mean,

        /// <summary>Sum the matching profile scores for each client.</summary>
        Sum,
    }

    private readonly IChatClient[] _clients;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly bool _leaveOpen;
    private readonly (int ClientIndex, string Text)[] _profiles;
    private readonly ScoreAggregation _scoreAggregation;
    private readonly float _scoreThreshold;
    private readonly int _topK;

    private bool _disposed;
    private EmbeddedProfile[]? _index;

    /// <summary>Initializes a new instance of the <see cref="SemanticRoutingChatClient"/> class.</summary>
    /// <param name="embeddingGenerator">The generator used to embed profile utterances and request text.</param>
    /// <param name="clientProfiles">The example utterances associated with each client.</param>
    /// <param name="defaultClient">
    /// The client selected when no profile satisfies <paramref name="scoreThreshold"/>.
    /// </param>
    /// <param name="scoreThreshold">The minimum aggregated score required to select a profiled client.</param>
    /// <param name="topK">
    /// The number of highest-scoring profile utterances, across all clients, whose scores are aggregated.
    /// The default is <c>1</c>.
    /// </param>
    /// <param name="scoreAggregation">The method used to aggregate matching profile scores for each client.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave the configured clients and embedding generator open when this instance is
    /// disposed; otherwise, <see langword="false"/>. The default is <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="embeddingGenerator"/>, <paramref name="clientProfiles"/>, or
    /// <paramref name="defaultClient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="clientProfiles"/> is empty or contains a null client, an empty utterance list, or a blank
    /// utterance.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="topK"/> is not positive, <paramref name="scoreAggregation"/> is invalid, or
    /// <paramref name="scoreThreshold"/> is outside the possible range for the configured aggregation.
    /// </exception>
    public SemanticRoutingChatClient(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IReadOnlyDictionary<IChatClient, IReadOnlyList<string>> clientProfiles,
        IChatClient defaultClient,
        float scoreThreshold = 0.3f,
        int topK = 1,
        ScoreAggregation scoreAggregation = ScoreAggregation.Mean,
        bool leaveOpen = false)
    {
        _ = Throw.IfNull(embeddingGenerator);
        _ = Throw.IfNull(clientProfiles);
        _ = Throw.IfNull(defaultClient);

        if (clientProfiles.Count == 0)
        {
            Throw.ArgumentException(nameof(clientProfiles), "At least one client profile must be provided.");
        }

        if (topK <= 0)
        {
            Throw.ArgumentOutOfRangeException(nameof(topK));
        }

        if (scoreAggregation is not ScoreAggregation.Mean and not ScoreAggregation.Sum)
        {
            Throw.ArgumentOutOfRangeException(nameof(scoreAggregation));
        }

        float scoreLimit = scoreAggregation == ScoreAggregation.Sum ? topK : 1;
        if (float.IsNaN(scoreThreshold) ||
            float.IsInfinity(scoreThreshold) ||
            scoreThreshold < -scoreLimit ||
            scoreThreshold > scoreLimit)
        {
            Throw.ArgumentOutOfRangeException(nameof(scoreThreshold));
        }

        _embeddingGenerator = embeddingGenerator;
        _leaveOpen = leaveOpen;
        _topK = topK;
        _scoreAggregation = scoreAggregation;
        _scoreThreshold = scoreThreshold;

        var profiles = new List<(int ClientIndex, string Text)>();
        var clients = new List<IChatClient> { defaultClient };
        foreach (KeyValuePair<IChatClient, IReadOnlyList<string>> profile in clientProfiles)
        {
            IChatClient client = profile.Key;
            IReadOnlyList<string> utterances = profile.Value;
            if (client is null)
            {
                Throw.ArgumentException(nameof(clientProfiles), "Profile clients must not be null.");
            }

            if (utterances is null || utterances.Count == 0)
            {
                Throw.ArgumentException(
                    nameof(clientProfiles),
                    "Every profile client must have at least one example utterance.");
            }

            int clientIndex = clients.FindIndex(candidate => ReferenceEquals(candidate, client));
            if (clientIndex < 0)
            {
                clientIndex = clients.Count;
                clients.Add(client);
            }

            foreach (string utterance in utterances)
            {
                if (string.IsNullOrWhiteSpace(utterance))
                {
                    Throw.ArgumentException(nameof(clientProfiles), "Profile utterances must not be blank.");
                }

                profiles.Add((clientIndex, utterance));
            }
        }

        _clients = [.. clients];
        _profiles = [.. profiles];
    }

    /// <inheritdoc/>
    protected override async ValueTask<IChatClient> SelectClientAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        _ = Throw.IfNull(context);

        string? query = context.Messages.LastOrDefault(
            static message => message.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            return _clients[0];
        }

        EmbeddedProfile[] index = await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        GeneratedEmbeddings<Embedding<float>> generated =
            await _embeddingGenerator.GenerateAsync(
                [query!],
                cancellationToken: cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException("The embedding generator returned null.");
        if (generated.Count != 1)
        {
            throw new InvalidOperationException("The embedding generator did not return one query embedding.");
        }

        ReadOnlySpan<float> queryVector = generated[0].Vector.Span;
        if (queryVector.Length != index[0].Vector.Length)
        {
            throw new InvalidOperationException(
                "The query embedding dimension does not match the profile embedding dimension.");
        }

        if (_topK == 1)
        {
            int bestClientIndex = -1;
            float bestScore = float.NegativeInfinity;
            foreach (EmbeddedProfile profile in index)
            {
                float score = TensorPrimitives.CosineSimilarity(queryVector, profile.Vector);
                if (score > bestScore)
                {
                    bestClientIndex = profile.ClientIndex;
                    bestScore = score;
                }
            }

            return bestClientIndex >= 0 && bestScore >= _scoreThreshold
                ? _clients[bestClientIndex]
                : _clients[0];
        }

        var matches = new ScoredProfile[index.Length];
        for (int i = 0; i < index.Length; i++)
        {
            matches[i] = new(
                i,
                TensorPrimitives.CosineSimilarity(queryVector, index[i].Vector));
        }

        Array.Sort(matches, static (left, right) =>
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.ProfileIndex.CompareTo(right.ProfileIndex);
        });

        int matchCount = Math.Min(_topK, matches.Length);
        var scoreSums = new float[_clients.Length];
        var scoreCounts = new int[_clients.Length];
        var clientOrder = new int[_clients.Length];
        int clientCount = 0;
        for (int i = 0; i < matchCount; i++)
        {
            EmbeddedProfile profile = index[matches[i].ProfileIndex];
            int clientIndex = profile.ClientIndex;
            if (scoreCounts[clientIndex] == 0)
            {
                clientOrder[clientCount++] = clientIndex;
            }

            scoreSums[clientIndex] += matches[i].Score;
            scoreCounts[clientIndex]++;
        }

        int bestAggregatedClientIndex = -1;
        float bestAggregatedScore = float.NegativeInfinity;
        for (int i = 0; i < clientCount; i++)
        {
            int clientIndex = clientOrder[i];
            float score = _scoreAggregation == ScoreAggregation.Mean
                ? scoreSums[clientIndex] / scoreCounts[clientIndex]
                : scoreSums[clientIndex];
            if (score > bestAggregatedScore)
            {
                bestAggregatedClientIndex = clientIndex;
                bestAggregatedScore = score;
            }
        }

        return bestAggregatedClientIndex >= 0 && bestAggregatedScore >= _scoreThreshold
            ? _clients[bestAggregatedClientIndex]
            : _clients[0];
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            _indexGate.Dispose();
            if (!_leaveOpen)
            {
                foreach (IChatClient client in _clients)
                {
                    client.Dispose();
                }

                _embeddingGenerator.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private async Task<EmbeddedProfile[]> EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_index is { } cached)
        {
            return cached;
        }

        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_index is { } existing)
            {
                return existing;
            }

            GeneratedEmbeddings<Embedding<float>> embeddings =
                await _embeddingGenerator.GenerateAsync(
                    _profiles.Select(profile => profile.Text),
                    cancellationToken: cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException("The embedding generator returned null.");
            if (embeddings.Count != _profiles.Length)
            {
                throw new InvalidOperationException(
                    "The embedding generator did not return one embedding per profile utterance.");
            }

            int dimensions = embeddings[0].Vector.Length;
            if (dimensions == 0)
            {
                throw new InvalidOperationException("Profile embeddings must not be empty.");
            }

            var index = new EmbeddedProfile[_profiles.Length];
            for (int i = 0; i < index.Length; i++)
            {
                if (embeddings[i].Vector.Length != dimensions)
                {
                    throw new InvalidOperationException(
                        "All profile embeddings must have the same dimension.");
                }

                index[i] = new(_profiles[i].ClientIndex, embeddings[i].Vector.ToArray());
            }

            return _index = index;
        }
        finally
        {
            _ = _indexGate.Release();
        }
    }

    private sealed record EmbeddedProfile(int ClientIndex, float[] Vector);

    private readonly record struct ScoredProfile(int ProfileIndex, float Score);
}
