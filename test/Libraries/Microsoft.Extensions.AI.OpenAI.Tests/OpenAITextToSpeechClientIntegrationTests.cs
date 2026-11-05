// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable MEAI001

namespace Microsoft.Extensions.AI;

public class OpenAITextToSpeechClientIntegrationTests : TextToSpeechClientIntegrationTests
{
    protected override ITextToSpeechClient? CreateClient()
        => IntegrationTestHelpers.GetOpenAIClient()?
            .GetAudioClient(TestRunnerConfiguration.Instance["OpenAI:TextToSpeechModel"] ?? "tts-1")
            .AsITextToSpeechClient();

    [Fact]
    public async Task GetStreamingAudioAsync_StreamingModel_ReturnsMultipleUpdatesWithUsage()
    {
        var openAIClient = IntegrationTestHelpers.GetOpenAIClient();
        Assert.SkipUnless(openAIClient is not null, "Client is not enabled.");

        using ITextToSpeechClient client = openAIClient
            .GetAudioClient(TestRunnerConfiguration.Instance["OpenAI:TextToSpeechStreamingModel"] ?? "gpt-4o-mini-tts")
            .AsITextToSpeechClient();

        int audioUpdatingCount = 0;
        bool gotSessionClose = false;
        bool gotUsage = false;

        await foreach (var update in client.GetStreamingAudioAsync("The quick brown fox jumps over the lazy dog."))
        {
            Assert.NotNull(update);
            Assert.NotNull(update.RawRepresentation);

            if (update.Kind == TextToSpeechResponseUpdateKind.AudioUpdating)
            {
                audioUpdatingCount++;
                var dataContent = update.Contents.OfType<DataContent>().Single();
                Assert.False(dataContent.Data.IsEmpty);
                Assert.StartsWith("audio/", dataContent.MediaType);
            }
            else if (update.Kind == TextToSpeechResponseUpdateKind.SessionClose)
            {
                gotSessionClose = true;
                var usageContent = update.Contents.OfType<UsageContent>().FirstOrDefault();
                if (usageContent is not null)
                {
                    gotUsage = true;
                    Assert.True(usageContent.Details.InputTokenCount > 0);
                    Assert.True(usageContent.Details.TotalTokenCount > 0);
                }
            }
        }

        Assert.True(audioUpdatingCount > 1, $"Expected multiple audio chunks, got {audioUpdatingCount}.");
        Assert.True(gotSessionClose, "Expected a SessionClose update.");
        Assert.True(gotUsage, "Expected usage information in SessionClose.");
    }
}
