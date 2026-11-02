// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Enriches <see cref="ExtractionDocumentImageElement"/> elements with alternative text using an AI service,
/// so the generated embeddings can include the image content information.
/// </summary>
public sealed class ImageAlternativeTextEnricher : IngestionDocumentProcessor
{
    private readonly EnricherOptions _options;
    private readonly ChatMessage _systemPrompt;
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageAlternativeTextEnricher"/> class.
    /// </summary>
    /// <param name="options">The options for generating alternative text.</param>
    public ImageAlternativeTextEnricher(EnricherOptions options)
    {
        _options = Throw.IfNull(options).Clone();
        _systemPrompt = new(ChatRole.System, "For each of the following images, write a detailed alternative text with fewer than 50 words.");
        _logger = _options.LoggerFactory?.CreateLogger<ImageAlternativeTextEnricher>();
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);

        List<ExtractionDocumentImageElement>? batch = null;

        foreach (var image in EnumerateImages(document))
        {
            if (ShouldProcess(image))
            {
                batch ??= new(_options.BatchSize);
                batch.Add(image);

                if (batch.Count == _options.BatchSize)
                {
                    await ProcessAsync(batch, cancellationToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }
        }

        if (batch?.Count > 0)
        {
            await ProcessAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    private static IEnumerable<ExtractionDocumentImageElement> EnumerateImages(IngestionDocument document)
    {
        HashSet<ExtractionDocumentImageElement> images = [];
        HashSet<ExtractionDocumentTableElement> path = [];
        foreach (var element in document.EnumerateContent())
        {
            foreach (var image in EnumerateImages(element, path))
            {
                if (images.Add(image))
                {
                    yield return image;
                }
            }
        }
    }

    private static IEnumerable<ExtractionDocumentImageElement> EnumerateImages(
        ExtractionDocumentElement element, HashSet<ExtractionDocumentTableElement> path)
    {
        if (element is ExtractionDocumentImageElement image)
        {
            yield return image;
        }
        else if (element is ExtractionDocumentTableElement table)
        {
            if (!path.Add(table))
            {
                throw new InvalidOperationException("Cyclic table relationships are not supported.");
            }

            try
            {
                foreach (var cell in table.Cells)
                {
                    if (cell?.Content is { } content)
                    {
                        foreach (var cellImage in EnumerateImages(content, path))
                        {
                            yield return cellImage;
                        }
                    }
                }
            }
            finally
            {
                _ = path.Remove(table);
            }
        }
    }

    private static bool ShouldProcess(ExtractionDocumentImageElement img) =>
        img.Content is DataContent && string.IsNullOrEmpty(img.AlternativeText);

    private async Task ProcessAsync(List<ExtractionDocumentImageElement> batch, CancellationToken cancellationToken)
    {
        List<AIContent> contents = new(batch.Count);
        foreach (var image in batch)
        {
            contents.Add(image.Content!);
        }

        try
        {
            ChatResponse<string[]> response = await _options.ChatClient.GetResponseAsync<string[]>(
                [_systemPrompt, new(ChatRole.User, contents)],
                _options.ChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Result.Length == contents.Count)
            {
                for (int i = 0; i < response.Result.Length; i++)
                {
                    batch[i].AlternativeText = response.Result[i];
                }
            }
            else
            {
                _logger?.UnexpectedResultsCount(response.Result.Length, contents.Count);
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
        {
            // Enricher failures should not fail the whole ingestion pipeline, as they are best-effort enhancements.
            _logger?.UnexpectedEnricherFailure(ex);
        }
    }
}
