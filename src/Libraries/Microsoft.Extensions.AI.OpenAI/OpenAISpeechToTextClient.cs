// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using OpenAI;
using OpenAI.Audio;

#pragma warning disable MEAI001 // Type is for evaluation purposes only
#pragma warning disable OPENAI001 // Streaming transcription segment updates are experimental
#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace Microsoft.Extensions.AI;

/// <summary>Represents an <see cref="ISpeechToTextClient"/> for an OpenAI <see cref="OpenAIClient"/> or <see cref="OpenAI.Audio.AudioClient"/>.</summary>
[Experimental(DiagnosticIds.Experiments.AISpeechToText, UrlFormat = DiagnosticIds.UrlFormat)]
internal sealed class OpenAISpeechToTextClient : ISpeechToTextClient
{
    /// <summary>Metadata about the client.</summary>
    private readonly SpeechToTextClientMetadata _metadata;

    /// <summary>The underlying <see cref="AudioClient" />.</summary>
    private readonly AudioClient _audioClient;

    /// <summary>Initializes a new instance of the <see cref="OpenAISpeechToTextClient"/> class for the specified <see cref="AudioClient"/>.</summary>
    /// <param name="audioClient">The underlying client.</param>
    public OpenAISpeechToTextClient(AudioClient audioClient)
    {
        _audioClient = Throw.IfNull(audioClient);

        _metadata = new("openai", audioClient.Endpoint, _audioClient.Model);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(SpeechToTextClientMetadata) ? _metadata :
            serviceType == typeof(AudioClient) ? _audioClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <inheritdoc />
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(audioSpeechStream);

        SpeechToTextResponse response = new();

        string filename = ResolveFilename(audioSpeechStream);

        if (IsTranslationRequest(options))
        {
            var translation = (await _audioClient.TranslateAudioAsync(audioSpeechStream, filename, ToOpenAITranslationOptions(options), cancellationToken).ConfigureAwait(false)).Value;

            response.Contents = [new TextContent(translation.Text)];
            response.RawRepresentation = translation;

            int segmentCount = translation.Segments.Count;
            if (segmentCount > 0)
            {
                response.StartTime = translation.Segments[0].StartTime;
                response.EndTime = translation.Segments[segmentCount - 1].EndTime;
            }
        }
        else
        {
            var transcription = (await _audioClient.TranscribeAudioAsync(audioSpeechStream, filename, ToOpenAITranscriptionOptions(options), cancellationToken).ConfigureAwait(false)).Value;

            response.Contents = [new TextContent(transcription.Text)];
            response.RawRepresentation = transcription;

            int segmentCount = transcription.Segments.Count;
            if (segmentCount > 0)
            {
                response.StartTime = transcription.Segments[0].StartTime;
                response.EndTime = transcription.Segments[segmentCount - 1].EndTime;
            }
            else
            {
                int wordCount = transcription.Words.Count;
                if (wordCount > 0)
                {
                    response.StartTime = transcription.Words[0].StartTime;
                    response.EndTime = transcription.Words[wordCount - 1].EndTime;
                }
            }

            if (transcription.Usage is AudioTranscriptionTokenUsage tokenUsage)
            {
                response.Usage = ToUsageDetails(tokenUsage);
            }
        }

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream, SpeechToTextOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(audioSpeechStream);

        string filename = ResolveFilename(audioSpeechStream);

        if (IsTranslationRequest(options))
        {
            foreach (var update in (await GetTextAsync(audioSpeechStream, options, cancellationToken).ConfigureAwait(false)).ToSpeechToTextResponseUpdates())
            {
                yield return update;
            }
        }
        else
        {
            await foreach (var update in _audioClient.TranscribeAudioStreamingAsync(
                audioSpeechStream,
                filename,
                ToOpenAITranscriptionOptions(options),
                cancellationToken).ConfigureAwait(false))
            {
                SpeechToTextResponseUpdate result = new()
                {
                    ModelId = options?.ModelId,
                    RawRepresentation = update,
                };

                switch (update)
                {
                    case StreamingAudioTranscriptionTextDeltaUpdate deltaUpdate:
                        result.Kind = SpeechToTextResponseUpdateKind.TextUpdated;
                        result.Contents = [new TextContent(deltaUpdate.Delta)];
                        break;

                    case StreamingAudioTranscriptionTextSegmentUpdate segmentUpdate:
                        result.Kind = SpeechToTextResponseUpdateKind.TextUpdated;
                        result.StartTime = segmentUpdate.StartTime;
                        result.EndTime = segmentUpdate.EndTime;
                        break;

                    case StreamingAudioTranscriptionTextDoneUpdate doneUpdate:
                        result.Kind = SpeechToTextResponseUpdateKind.SessionClose;
                        if (doneUpdate.Usage is { } usage)
                        {
                            result.Contents = [new UsageContent(ToUsageDetails(usage))];
                        }

                        break;
                }

                yield return result;
            }
        }
    }

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        // Nothing to dispose. Implementation required for the IAudioTranscriptionClient interface.
    }

    // <summary>A translation is triggered when the target text language is specified and the source language is not provided or different.</summary>
    private static bool IsTranslationRequest(SpeechToTextOptions? options) =>
        options is not null &&
        options.TextLanguage is not null &&
        (options.SpeechLanguage is null || options.SpeechLanguage != options.TextLanguage);

    /// <summary>
    /// Resolves the filename to use for the audio stream in the multipart request.
    /// Priority: <see cref="FileStream"/> name, then magic-byte detection (seekable streams only), then default.
    /// </summary>
    private static string ResolveFilename(Stream audioSpeechStream)
    {
        const int FormatDetectionByteCount = 12;

        if (audioSpeechStream is FileStream fileStream)
        {
            return Path.GetFileName(fileStream.Name);
        }

        // For seekable streams positioned at the start, peek at the header to detect audio format, then rewind.
        if (audioSpeechStream.CanSeek && audioSpeechStream.Position == 0)
        {
            byte[] header = new byte[FormatDetectionByteCount];
            int bytesRead = 0;
            while (bytesRead < header.Length)
            {
                int n = audioSpeechStream.Read(header, bytesRead, header.Length - bytesRead);
                if (n <= 0)
                {
                    break;
                }

                bytesRead += n;
            }

            audioSpeechStream.Position -= bytesRead;
            return $"audio.{DetectAudioExtension(header.AsSpan(0, bytesRead))}";
        }

        return "audio.mp3";
    }

    /// <summary>Detects the audio format extension from the leading bytes of the audio data.</summary>
    private static string DetectAudioExtension(ReadOnlySpan<byte> header)
    {
        // WAV: "RIFF" at offset 0 and "WAVE" at offset 8.
        if (header.Length >= 12 &&
            header.Slice(0, 4).SequenceEqual("RIFF"u8) &&
            header.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return "wav";
        }

        // WebM/Matroska: EBML header ID at offset 0.
        if (header.Length >= 4 &&
            header.Slice(0, 4).SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            return "webm";
        }

        // M4A/MP4: ISO BMFF "ftyp" box type at offset 4.
        if (header.Length >= 8 &&
            header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return "m4a";
        }

        // MP3: ID3v2 tag at offset 0.
        if (header.Length >= 3 &&
            header.Slice(0, 3).SequenceEqual("ID3"u8))
        {
            return "mp3";
        }

        // MP3: MPEG frame sync word (11 set bits).
        if (header.Length >= 2 &&
            header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return "mp3";
        }

        return "mp3";
    }

    /// <summary>Converts an extensions options instance to an OpenAI transcription options instance.</summary>
    private AudioTranscriptionOptions ToOpenAITranscriptionOptions(SpeechToTextOptions? options)
    {
        AudioTranscriptionOptions result = options?.RawRepresentationFactory?.Invoke(this) as AudioTranscriptionOptions ?? new();

        result.Language ??= options?.SpeechLanguage;

        return result;
    }

    /// <summary>Converts an extensions options instance to an OpenAI translation options instance.</summary>
    private AudioTranslationOptions ToOpenAITranslationOptions(SpeechToTextOptions? options)
    {
        AudioTranslationOptions result = options?.RawRepresentationFactory?.Invoke(this) as AudioTranslationOptions ?? new();

        return result;
    }

    /// <summary>Maps <see cref="AudioTranscriptionTokenUsage"/> to <see cref="UsageDetails"/>.</summary>
    private static UsageDetails ToUsageDetails(AudioTranscriptionTokenUsage tokenUsage)
    {
        var details = new UsageDetails
        {
            InputTokenCount = tokenUsage.InputTokenCount,
            OutputTokenCount = tokenUsage.OutputTokenCount,
            TotalTokenCount = tokenUsage.TotalTokenCount,
        };

        if (tokenUsage.InputTokenDetails is { } inputDetails)
        {
            details.InputAudioTokenCount = inputDetails.AudioTokenCount;
            details.InputTextTokenCount = inputDetails.TextTokenCount;
        }

        return details;
    }
}
