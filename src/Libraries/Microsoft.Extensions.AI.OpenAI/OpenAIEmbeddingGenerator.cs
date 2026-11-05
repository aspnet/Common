// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;
using OpenAI.Embeddings;

#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
#pragma warning disable MEAI001 // OpenAIRequestPolicies is experimental

namespace Microsoft.Extensions.AI;

/// <summary>An <see cref="IEmbeddingGenerator{String, Embedding}"/> for an OpenAI <see cref="EmbeddingClient"/>.</summary>
internal sealed class OpenAIEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    // This delegate instance is used to call the internal overload of GenerateEmbeddingsAsync that accepts
    // a RequestOptions. This should be replaced once a better way to pass RequestOptions is available.
    private static readonly Func<EmbeddingClient, IEnumerable<string>, OpenAI.Embeddings.EmbeddingGenerationOptions, RequestOptions, Task<ClientResult<OpenAIEmbeddingCollection>>>?
        _generateEmbeddingsAsync =
        (Func<EmbeddingClient, IEnumerable<string>, OpenAI.Embeddings.EmbeddingGenerationOptions, RequestOptions, Task<ClientResult<OpenAIEmbeddingCollection>>>?)
        typeof(EmbeddingClient)
        .GetMethod(
            nameof(EmbeddingClient.GenerateEmbeddingsAsync), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(IEnumerable<string>), typeof(OpenAI.Embeddings.EmbeddingGenerationOptions), typeof(RequestOptions)], null)
        ?.CreateDelegate(
            typeof(Func<EmbeddingClient, IEnumerable<string>, OpenAI.Embeddings.EmbeddingGenerationOptions, RequestOptions, Task<ClientResult<OpenAIEmbeddingCollection>>>));

    /// <summary>Metadata about the embedding generator.</summary>
    private readonly EmbeddingGeneratorMetadata _metadata;

    /// <summary>The underlying <see cref="OpenAI.Chat.ChatClient" />.</summary>
    private readonly EmbeddingClient _embeddingClient;

    /// <summary>The number of dimensions produced by the generator.</summary>
    private readonly int? _dimensions;

    /// <summary>Caller-registered policies applied to every <see cref="RequestOptions"/>.</summary>
    private readonly OpenAIRequestPolicies _requestPolicies = new();

    /// <summary>Initializes a new instance of the <see cref="OpenAIEmbeddingGenerator"/> class.</summary>
    /// <param name="embeddingClient">The underlying client.</param>
    /// <param name="defaultModelDimensions">The number of dimensions to generate in each embedding.</param>
    /// <exception cref="ArgumentNullException"><paramref name="embeddingClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultModelDimensions"/> is not positive.</exception>
    public OpenAIEmbeddingGenerator(EmbeddingClient embeddingClient, int? defaultModelDimensions = null)
    {
        _embeddingClient = Throw.IfNull(embeddingClient);
        _dimensions = defaultModelDimensions;

        if (defaultModelDimensions < 1)
        {
            Throw.ArgumentOutOfRangeException(nameof(defaultModelDimensions), "Value must be greater than 0.");
        }

#pragma warning disable OPENAI001 // Endpoint and Model are experimental
        _metadata = new("openai", embeddingClient.Endpoint, _embeddingClient.Model, defaultModelDimensions);
#pragma warning restore OPENAI001
    }

    /// <inheritdoc />
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        OpenAI.Embeddings.EmbeddingGenerationOptions? openAIOptions = ToOpenAIOptions(options);

        var t = _generateEmbeddingsAsync is not null ?
            _generateEmbeddingsAsync(_embeddingClient, values, openAIOptions, cancellationToken.ToRequestOptions(streaming: false, _requestPolicies)) :
            _embeddingClient.GenerateEmbeddingsAsync(values, openAIOptions, cancellationToken);
        var embeddings = (await t.ConfigureAwait(false)).Value;

        UsageDetails? usage = embeddings.Usage is not null ?
            new()
            {
                InputTokenCount = embeddings.Usage.InputTokenCount,
                TotalTokenCount = embeddings.Usage.TotalTokenCount
            } :
            null;

        return new(embeddings.Select(e =>
                new Embedding<float>(e.ToFloats())
                {
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModelId = embeddings.Model,
                }))
        {
            Usage = usage,
        };
    }

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        // Nothing to dispose. Implementation required for the IEmbeddingGenerator interface.
    }

    /// <inheritdoc />
    object? IEmbeddingGenerator.GetService(Type serviceType, object? serviceKey)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata :
            serviceType == typeof(EmbeddingClient) ? _embeddingClient :
            serviceType == typeof(OpenAIRequestPolicies) ? _requestPolicies :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <summary>Converts an extensions options instance to an OpenAI options instance.</summary>
    private OpenAI.Embeddings.EmbeddingGenerationOptions ToOpenAIOptions(EmbeddingGenerationOptions? options)
    {
        if (options?.RawRepresentationFactory?.Invoke(this) is not OpenAI.Embeddings.EmbeddingGenerationOptions result)
        {
            result = new();
        }

        result.Dimensions ??= options?.Dimensions ?? _dimensions;
#pragma warning disable SCME0001 // JsonPatch is experimental
        OpenAIClientExtensions.PatchModelIfNotSet(ref result.Patch, options?.ModelId);
#pragma warning restore SCME0001

        return result;
    }
}
