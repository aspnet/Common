// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable CA2214 // Do not call overridable methods in constructors

namespace Microsoft.Extensions.AI;

public abstract class TextToSpeechClientIntegrationTests : IDisposable
{
    private readonly ITextToSpeechClient? _client;

    protected TextToSpeechClientIntegrationTests()
    {
        _client = CreateClient();
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract ITextToSpeechClient? CreateClient();

    [Fact]
    public virtual async Task GetAudioAsync_SimpleText_ReturnsAudio()
    {
        SkipIfNotEnabled();

        var response = await _client.GetAudioAsync("Hello, world!");

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);

        var content = Assert.Single(response.Contents);
        var dataContent = Assert.IsType<DataContent>(content);
        Assert.False(dataContent.Data.IsEmpty);
        Assert.StartsWith("audio/", dataContent.MediaType, StringComparison.Ordinal);
    }

    [Fact]
    public virtual async Task GetAudioAsync_WithVoice_ReturnsAudio()
    {
        SkipIfNotEnabled();

        var response = await _client.GetAudioAsync("The quick brown fox jumps over the lazy dog.", new TextToSpeechOptions
        {
            VoiceId = "nova",
        });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);

        var content = Assert.Single(response.Contents);
        var dataContent = Assert.IsType<DataContent>(content);
        Assert.False(dataContent.Data.IsEmpty);
        Assert.StartsWith("audio/", dataContent.MediaType, StringComparison.Ordinal);
    }

    [Fact]
    public virtual async Task GetAudioAsync_WithAudioFormat_ReturnsCorrectMediaType()
    {
        SkipIfNotEnabled();

        var response = await _client.GetAudioAsync("Testing audio format selection.", new TextToSpeechOptions
        {
            AudioFormat = "opus",
        });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);

        var content = Assert.Single(response.Contents);
        var dataContent = Assert.IsType<DataContent>(content);
        Assert.False(dataContent.Data.IsEmpty);
        Assert.Equal("audio/opus", dataContent.MediaType);
    }

    [Fact]
    public virtual async Task GetAudioAsync_WithSpeed_ReturnsAudio()
    {
        SkipIfNotEnabled();

        var response = await _client.GetAudioAsync("This should be spoken quickly.", new TextToSpeechOptions
        {
            Speed = 1.5f,
        });

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);

        var content = Assert.Single(response.Contents);
        var dataContent = Assert.IsType<DataContent>(content);
        Assert.False(dataContent.Data.IsEmpty);
    }

    [Fact]
    public virtual async Task GetStreamingAudioAsync_SimpleText_ReturnsUpdates()
    {
        SkipIfNotEnabled();

        int updateCount = 0;
        await foreach (var update in _client.GetStreamingAudioAsync("Hello, world!"))
        {
            updateCount++;
            Assert.NotNull(update);

            var dataContents = update.Contents.OfType<DataContent>().ToList();
            Assert.NotEmpty(dataContents);

            foreach (var dataContent in dataContents)
            {
                Assert.False(dataContent.Data.IsEmpty);
                Assert.StartsWith("audio/", dataContent.MediaType, StringComparison.Ordinal);
            }
        }

        Assert.True(updateCount > 0, "Expected at least one streaming update.");
    }

    [MemberNotNull(nameof(_client))]
    protected void SkipIfNotEnabled()
    {
        string? skipIntegration = TestRunnerConfiguration.Instance["SkipIntegrationTests"];

        Assert.SkipUnless(skipIntegration is null && _client is not null, "Client is not enabled.");
    }
}
