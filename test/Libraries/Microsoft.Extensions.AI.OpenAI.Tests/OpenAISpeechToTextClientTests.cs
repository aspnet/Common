// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using Xunit;

#pragma warning disable MEAI001 // Experimental MEAI APIs
#pragma warning disable OPENAI001 // Experimental OpenAI APIs
#pragma warning disable S103 // Lines should not be too long

namespace Microsoft.Extensions.AI;

public class OpenAISpeechToTextClientTests
{
    [Fact]
    public void AsISpeechToTextClient_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("audioClient", () => ((AudioClient)null!).AsISpeechToTextClient());
    }

    [Fact]
    public void AsISpeechToTextClient_AudioClient_ProducesExpectedMetadata()
    {
        Uri endpoint = new("http://localhost/some/endpoint");
        string model = "amazingModel";

        var client = new OpenAIClient(new ApiKeyCredential("key"), new OpenAIClientOptions { Endpoint = endpoint });

        ISpeechToTextClient speechToTextClient = client.GetAudioClient(model).AsISpeechToTextClient();
        var metadata = speechToTextClient.GetService<SpeechToTextClientMetadata>();
        Assert.Equal("openai", metadata?.ProviderName);
        Assert.Equal(endpoint, metadata?.ProviderUri);
        Assert.Equal(model, metadata?.DefaultModelId);
    }

    [Fact]
    public void GetService_AudioClient_SuccessfullyReturnsUnderlyingClient()
    {
        AudioClient audioClient = new OpenAIClient(new ApiKeyCredential("key")).GetAudioClient("model");
        ISpeechToTextClient speechToTextClient = audioClient.AsISpeechToTextClient();
        Assert.Same(speechToTextClient, speechToTextClient.GetService<ISpeechToTextClient>());
        Assert.Same(audioClient, speechToTextClient.GetService<AudioClient>());
        using var factory = LoggerFactory.Create(b => b.AddFakeLogging());
        using ISpeechToTextClient pipeline = speechToTextClient
            .AsBuilder()
            .UseLogging(factory)
            .Build();

        Assert.NotNull(pipeline.GetService<LoggingSpeechToTextClient>());

        Assert.Same(audioClient, pipeline.GetService<AudioClient>());
        Assert.IsType<LoggingSpeechToTextClient>(pipeline.GetService<ISpeechToTextClient>());
    }

    [Theory]
    [InlineData("pt", null)]
    [InlineData("en", null)]
    [InlineData("en", "en")]
    [InlineData("pt", "pt")]
    public async Task GetTextAsync_BasicRequestResponse(string? speechLanguage, string? textLanguage)
    {
        string input = $$"""
                {
                    "model": "gpt-4o-transcribe",
                    "language": "{{speechLanguage}}"
                }
                """;

        const string Output = """
                {
                    "text":"I finally got back to the gym the other day."
                }
                """;

        using VerbatimMultiPartHttpHandler handler = new(input, Output) { ExpectedRequestUriContains = "audio/transcriptions" };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var audioSpeechStream = GetAudioStream();
        var response = await client.GetTextAsync(audioSpeechStream, new SpeechToTextOptions
        {
            SpeechLanguage = speechLanguage,
            TextLanguage = textLanguage
        });

        Assert.NotNull(response);

        Assert.Contains("I finally got back to the gym the other day", response.Text);

        Assert.NotNull(response.RawRepresentation);
        Assert.IsType<AudioTranscription>(response.RawRepresentation);
    }

    [Fact]
    public async Task GetTextAsync_Cancelled_Throws()
    {
        using HttpClient httpClient = new();
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var fileStream = GetAudioStream();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(()
            => client.GetTextAsync(fileStream, cancellationToken: cancellationTokenSource.Token));
    }

    [Fact]
    public async Task GetStreamingTextAsync_Cancelled_Throws()
    {
        using HttpClient httpClient = new();
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var fileStream = GetAudioStream();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(()
            => client
                .GetStreamingTextAsync(fileStream, cancellationToken: cancellationTokenSource.Token)
                .GetAsyncEnumerator()
                .MoveNextAsync()
                .AsTask());
    }

    [Theory]
    [InlineData("pt", null)]
    [InlineData("en", null)]
    [InlineData("en", "en")]
    [InlineData("pt", "pt")]
    public async Task GetStreamingTextAsync_BasicRequestResponse(string? speechLanguage, string? textLanguage)
    {
        // There's no support for streaming audio in the OpenAI API,
        // so we're just testing the client's ability to handle streaming responses.

        string input = $$"""
                {
                    "model": "gpt-4o-transcribe",
                    "language": "{{speechLanguage}}",
                    "stream":true
                }
                """;

        const string Output = """
                {
                    "text":"I finally got back to the gym the other day."
                }
                """;

        using VerbatimMultiPartHttpHandler handler = new(input, Output) { ExpectedRequestUriContains = "audio/transcriptions" };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var audioSpeechStream = GetAudioStream();
        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream, new SpeechToTextOptions
        {
            SpeechLanguage = speechLanguage,
            TextLanguage = textLanguage
        }))
        {
            Assert.Contains("I finally got back to the gym the other day", update.Text);
            Assert.NotNull(update.RawRepresentation);
            Assert.IsType<AudioTranscription>(update.RawRepresentation);
        }
    }

    [Fact]
    public async Task GetStreamingTextAsync_BasicTranslateRequestResponse()
    {
        string textLanguage = "en";

        // There's no support for non english translations, so no language is passed to the API.
        const string Input = $$"""
                {
                    "model": "gpt-4o-transcribe"
                }
                """;

        const string Output = """
                {
                    "text":"I finally got back to the gym the other day."
                }
                """;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output) { ExpectedRequestUriContains = "audio/translations" };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var audioSpeechStream = GetAudioStream();
        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream, new SpeechToTextOptions
        {
            SpeechLanguage = "pt",
            TextLanguage = textLanguage
        }))
        {
            Assert.Contains("I finally got back to the gym the other day", update.Text);
            Assert.NotNull(update.RawRepresentation);
            Assert.IsType<AudioTranslation>(update.RawRepresentation);
        }
    }

    [Fact]
    public async Task GetTextAsync_Transcription_StronglyTypedOptions_AllSent()
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe",
                    "language": "pt",
                    "prompt":"Hide any bad words with ",
                    "temperature": 0.5,
                    "response_format": "vtt",
                    "timestamp_granularities[]": ["word","segment"]
                }
                """;

        const string Output = """
                {
                    "text":"I finally got back to the gym the other day."
                }
                """;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var audioSpeechStream = GetAudioStream();
        Assert.NotNull(await client.GetTextAsync(audioSpeechStream, new()
        {
            SpeechLanguage = "en",
            RawRepresentationFactory = (s) =>
            new AudioTranscriptionOptions
            {
                Prompt = "Hide any bad words with ",
                Language = "pt",
                Temperature = 0.5f,
                TimestampGranularities = AudioTimestampGranularities.Segment | AudioTimestampGranularities.Word,
                ResponseFormat = AudioTranscriptionFormat.Vtt
            }
        }));
    }

    [Fact]
    public async Task GetTextAsync_Translation_StronglyTypedOptions_AllSent()
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe",
                    "prompt":"Hide any bad words with ",
                    "response_format": "vtt"
                }
                """;

        const string Output = """
                {
                    "text":"I finally got back to the gym the other day."
                }
                """;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output);
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        using var audioSpeechStream = GetAudioStream();
        Assert.NotNull(await client.GetTextAsync(audioSpeechStream, new()
        {
            TextLanguage = "pt",
            RawRepresentationFactory = (s) =>
            new AudioTranslationOptions
            {
                Prompt = "Hide any bad words with ",
                Temperature = 0.5f, // Temperature is ignored by OpenAI.
                ResponseFormat = AudioTranslationFormat.Vtt
            }
        }));
    }

    private static Stream GetAudioStream()
        => new MemoryStream([0x01, 0x02]);

    [Fact]
    public async Task GetStreamingTextAsync_SegmentUpdates_SurfaceTimingMetadata()
    {
        const string Input = """
                {
                    "model": "gpt-4o-mini-transcribe",
                    "stream":true
                }
                """;

        const string Output = """
                data: {"type":"transcript.text.delta","delta":"Hello world."}

                data: {"type":"transcript.text.segment","id":"seg_001","start":0.0,"end":2.5,"text":"Hello world.","speaker":"speaker_0"}

                data: {"type":"transcript.text.done","text":"Hello world.","usage":{"type":"tokens","input_tokens":43,"input_token_details":{"text_tokens":0,"audio_tokens":43},"output_tokens":13,"total_tokens":56}}

                data: [DONE]

                """;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output) { ExpectedRequestUriContains = "audio/transcriptions" };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-mini-transcribe");

        using var audioSpeechStream = GetAudioStream();
        var updates = new System.Collections.Generic.List<SpeechToTextResponseUpdate>();
        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream))
        {
            updates.Add(update);
        }

        // Expect 3 updates: delta, segment, done
        Assert.Equal(3, updates.Count);

        // First: delta with text
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, updates[0].Kind);
        Assert.Equal("Hello world.", updates[0].Text);
        Assert.IsType<StreamingAudioTranscriptionTextDeltaUpdate>(updates[0].RawRepresentation);

        // Second: segment with timing metadata, no text content (to avoid duplicating deltas)
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, updates[1].Kind);
        Assert.Equal(TimeSpan.Zero, updates[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(2.5), updates[1].EndTime);
        Assert.Empty(updates[1].Text);
        var segmentRaw = Assert.IsType<StreamingAudioTranscriptionTextSegmentUpdate>(updates[1].RawRepresentation);
        Assert.Equal("seg_001", segmentRaw.SegmentId);
        Assert.Equal("speaker_0", segmentRaw.SpeakerLabel);
        Assert.Equal("Hello world.", segmentRaw.Text);

        // Third: session close with usage
        Assert.Equal(SpeechToTextResponseUpdateKind.SessionClose, updates[2].Kind);
        Assert.IsType<StreamingAudioTranscriptionTextDoneUpdate>(updates[2].RawRepresentation);
        var usage = updates[2].Contents.OfType<UsageContent>().Single();
        Assert.Equal(43, usage.Details.InputTokenCount);
        Assert.Equal(13, usage.Details.OutputTokenCount);
        Assert.Equal(56, usage.Details.TotalTokenCount);
        Assert.Equal(43, usage.Details.InputAudioTokenCount);
        Assert.Equal(0, usage.Details.InputTextTokenCount);
    }

    private static ISpeechToTextClient CreateSpeechToTextClient(HttpClient httpClient, string modelId) =>
        new OpenAIClient(new ApiKeyCredential("apikey"), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) })
            .GetAudioClient(modelId)
            .AsISpeechToTextClient();

    public static TheoryData<byte[], string> AudioFormatDetectionData => new()
    {
        // WAV: RIFF____WAVE
        { "RIFF\x00\x00\x00\x00WAVE"u8.ToArray(), "audio.wav" },

        // MP3: ID3v2 tag
        { new byte[] { (byte)'I', (byte)'D', (byte)'3', 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "audio.mp3" },

        // MP3: MPEG sync word (0xFF 0xFB)
        { new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "audio.mp3" },

        // WebM/Matroska: EBML header
        { new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "audio.webm" },

        // M4A/MP4: ISO BMFF ftyp box
        { new byte[] { 0x00, 0x00, 0x00, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ' }, "audio.m4a" },

        // Unknown bytes: defaults to mp3
        { new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, "audio.mp3" },
    };

    [Theory]
    [MemberData(nameof(AudioFormatDetectionData))]
    public async Task GetTextAsync_DetectsAudioFormatFromMagicBytes(byte[] header, string expectedFilename)
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe"
                }
                """;

        const string Output = """
                {
                    "text":"Hello."
                }
                """;

        using var audioSpeechStream = new MemoryStream(header);

        using VerbatimMultiPartHttpHandler handler = new(Input, Output)
        {
            ExpectedAudioFilename = expectedFilename,
        };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        var response = await client.GetTextAsync(audioSpeechStream);
        Assert.NotNull(response);
    }

    [Theory]
    [MemberData(nameof(AudioFormatDetectionData))]
    public async Task GetStreamingTextAsync_DetectsAudioFormatFromMagicBytes(byte[] header, string expectedFilename)
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe",
                    "stream":true
                }
                """;

        const string Output = """
                {
                    "text":"Hello."
                }
                """;

        using var audioSpeechStream = new MemoryStream(header);

        using VerbatimMultiPartHttpHandler handler = new(Input, Output)
        {
            ExpectedRequestUriContains = "audio/transcriptions",
            ExpectedAudioFilename = expectedFilename,
        };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream))
        {
            Assert.NotNull(update);
        }
    }

    [Fact]
    public async Task GetTextAsync_StreamPositionNotAtZero_SkipsDetectionAndDefaultsToMp3()
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe"
                }
                """;

        const string Output = """
                {
                    "text":"Hello."
                }
                """;

        // WAV magic bytes, but position advanced past them — detection should be skipped.
        byte[] wavHeader = "RIFF\x00\x00\x00\x00WAVE"u8.ToArray();
        using var audioSpeechStream = new MemoryStream(wavHeader);
        audioSpeechStream.Position = 4;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output)
        {
            ExpectedAudioFilename = "audio.mp3",
        };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        var response = await client.GetTextAsync(audioSpeechStream);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetStreamingTextAsync_StreamPositionNotAtZero_SkipsDetectionAndDefaultsToMp3()
    {
        const string Input = """
                {
                    "model": "gpt-4o-transcribe",
                    "stream":true
                }
                """;

        const string Output = """
                {
                    "text":"Hello."
                }
                """;

        // WAV magic bytes, but position advanced past them — detection should be skipped.
        byte[] wavHeader = "RIFF\x00\x00\x00\x00WAVE"u8.ToArray();
        using var audioSpeechStream = new MemoryStream(wavHeader);
        audioSpeechStream.Position = 4;

        using VerbatimMultiPartHttpHandler handler = new(Input, Output)
        {
            ExpectedRequestUriContains = "audio/transcriptions",
            ExpectedAudioFilename = "audio.mp3",
        };
        using HttpClient httpClient = new(handler);
        using ISpeechToTextClient client = CreateSpeechToTextClient(httpClient, "gpt-4o-transcribe");

        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream))
        {
            Assert.NotNull(update);
        }
    }
}
