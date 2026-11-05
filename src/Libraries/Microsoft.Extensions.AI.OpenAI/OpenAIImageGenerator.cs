// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using OpenAI;
using OpenAI.Images;

namespace Microsoft.Extensions.AI;

/// <summary>Represents an <see cref="IImageGenerator"/> for an OpenAI <see cref="OpenAIClient"/> or <see cref="ImageClient"/>.</summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIImageClient)]
internal sealed class OpenAIImageGenerator : IImageGenerator
{
    /// <summary>Metadata about the client.</summary>
    private readonly ImageGeneratorMetadata _metadata;

    /// <summary>The underlying <see cref="ImageClient" />.</summary>
    private readonly ImageClient _imageClient;

    /// <summary>Initializes a new instance of the <see cref="OpenAIImageGenerator"/> class for the specified <see cref="ImageClient"/>.</summary>
    /// <param name="imageClient">The underlying client.</param>
    /// <exception cref="ArgumentNullException"><paramref name="imageClient"/> is <see langword="null"/>.</exception>
    public OpenAIImageGenerator(ImageClient imageClient)
    {
        _imageClient = Throw.IfNull(imageClient);

        _metadata = new("openai", imageClient.Endpoint, _imageClient.Model);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(request);

        string? prompt = request.Prompt;
        _ = Throw.IfNull(prompt);

        // If the request has original images, treat this as an edit operation
        if (request.OriginalImages is not null && request.OriginalImages.Any())
        {
            ImageEditOptions editOptions = ToOpenAIImageEditOptions(options);
            string? fileName = null;
            Stream? imageStream = null;

            // Currently only a single image is supported for editing.
            var originalImage = request.OriginalImages.FirstOrDefault();

            if (originalImage is DataContent dataContent)
            {
                imageStream = MemoryMarshal.TryGetArray(dataContent.Data, out var array) ?
                    new MemoryStream(array.Array!, array.Offset, array.Count) :
                    new MemoryStream(dataContent.Data.ToArray());
                fileName =
                    dataContent.Name ??
                    $"{Guid.NewGuid():N}{MediaTypeMap.GetExtension(dataContent.MediaType) ?? ".png"}"; // Default to PNG if no content type is available.
            }

            GeneratedImageCollection editResult = await _imageClient.GenerateImageEditsAsync(
                imageStream, fileName, prompt, options?.Count ?? 1, editOptions, cancellationToken).ConfigureAwait(false);

            return ToImageGenerationResponse(editResult);
        }

        OpenAI.Images.ImageGenerationOptions openAIOptions = ToOpenAIImageGenerationOptions(options);

        GeneratedImageCollection result = await _imageClient.GenerateImagesAsync(prompt, options?.Count ?? 1, openAIOptions, cancellationToken).ConfigureAwait(false);

        return ToImageGenerationResponse(result);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType is null ? throw new ArgumentNullException(nameof(serviceType)) :
        serviceKey is not null ? null :
        serviceType == typeof(ImageGeneratorMetadata) ? _metadata :
        serviceType == typeof(ImageClient) ? _imageClient :
        serviceType.IsInstanceOfType(this) ? this :
        null;

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        // Nothing to dispose. Implementation required for the IImageGenerator interface.
    }

    /// <summary>
    /// Converts a <see cref="Size"/> to an OpenAI <see cref="GeneratedImageSize"/>.
    /// </summary>
    /// <param name="requestedSize">User's requested size.</param>
    /// <returns>Closest supported size.</returns>
    private static GeneratedImageSize? ToOpenAIImageSize(Size? requestedSize) =>
        requestedSize is null ? null : new GeneratedImageSize(requestedSize.Value.Width, requestedSize.Value.Height);

    /// <summary>Converts a <see cref="GeneratedImageCollection"/> to a <see cref="ImageGenerationResponse"/>.</summary>
    private static ImageGenerationResponse ToImageGenerationResponse(GeneratedImageCollection generatedImages)
    {
#pragma warning disable OPENAI001
        string contentType = generatedImages.OutputFileFormat?.ToString() is { } outputFormat ?
            $"image/{outputFormat}" :
            "image/png"; // Default content type for images
#pragma warning restore OPENAI001

        List<AIContent> contents = [];

        foreach (GeneratedImage image in generatedImages)
        {
            if (image.ImageBytes is not null)
            {
                contents.Add(new DataContent(image.ImageBytes.ToMemory(), contentType));
            }
            else if (image.ImageUri is not null)
            {
                contents.Add(new UriContent(image.ImageUri, contentType));
            }
            else
            {
                throw new InvalidOperationException("Generated image does not contain a valid URI or byte array.");
            }
        }

        UsageDetails? ud = null;
        if (generatedImages.Usage is { } usage)
        {
            ud = new()
            {
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                TotalTokenCount = usage.TotalTokenCount,
            };

            if (usage.InputTokenDetails is { } inputDetails)
            {
                ud.AdditionalCounts ??= [];
                ud.AdditionalCounts.Add($"{nameof(usage.InputTokenDetails)}.{nameof(inputDetails.ImageTokenCount)}", inputDetails.ImageTokenCount);
                ud.AdditionalCounts.Add($"{nameof(usage.InputTokenDetails)}.{nameof(inputDetails.TextTokenCount)}", inputDetails.TextTokenCount);
            }
        }

        return new ImageGenerationResponse(contents)
        {
            RawRepresentation = generatedImages,
            Usage = ud,
        };
    }

    /// <summary>Converts a <see cref="ImageGenerationOptions"/> to a <see cref="OpenAI.Images.ImageGenerationOptions"/>.</summary>
    private OpenAI.Images.ImageGenerationOptions ToOpenAIImageGenerationOptions(ImageGenerationOptions? options)
    {
        OpenAI.Images.ImageGenerationOptions result = options?.RawRepresentationFactory?.Invoke(this) as OpenAI.Images.ImageGenerationOptions ?? new();

        if (result.OutputFileFormat is null)
        {
            if (options?.MediaType?.Equals("image/png", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Png;
            }
            else if (options?.MediaType?.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Jpeg;
            }
            else if (options?.MediaType?.Equals("image/webp", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Webp;
            }
        }

        result.ResponseFormat ??= options?.ResponseFormat switch
        {
            ImageGenerationResponseFormat.Uri => GeneratedImageFormat.Uri,
            ImageGenerationResponseFormat.Data => GeneratedImageFormat.Bytes,
            _ => (GeneratedImageFormat?)null
        };

        result.Size ??= ToOpenAIImageSize(options?.ImageSize);

        return result;
    }

    /// <summary>Converts a <see cref="ImageGenerationOptions"/> to a <see cref="ImageEditOptions"/>.</summary>
    private ImageEditOptions ToOpenAIImageEditOptions(ImageGenerationOptions? options)
    {
        ImageEditOptions result = options?.RawRepresentationFactory?.Invoke(this) as ImageEditOptions ?? new();

        if (result.OutputFileFormat is null)
        {
            if (options?.MediaType?.Equals("image/png", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Png;
            }
            else if (options?.MediaType?.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Jpeg;
            }
            else if (options?.MediaType?.Equals("image/webp", StringComparison.OrdinalIgnoreCase) is true)
            {
                result.OutputFileFormat = GeneratedImageFileFormat.Webp;
            }
        }

        result.ResponseFormat ??= options?.ResponseFormat switch
        {
            ImageGenerationResponseFormat.Uri => GeneratedImageFormat.Uri,
            ImageGenerationResponseFormat.Data => GeneratedImageFormat.Bytes,
            _ => (GeneratedImageFormat?)null
        };

        result.Size ??= ToOpenAIImageSize(options?.ImageSize);

        return result;
    }
}
