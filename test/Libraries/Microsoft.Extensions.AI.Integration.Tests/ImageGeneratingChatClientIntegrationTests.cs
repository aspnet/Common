// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2214 // Do not call overridable methods in constructors

namespace Microsoft.Extensions.AI;

/// <summary>
/// Abstract base class for integration tests that verify ImageGeneratingChatClient with real IChatClient implementations.
/// Concrete test classes should inherit from this and provide a real IChatClient that supports function calling.
/// </summary>
public abstract class ImageGeneratingChatClientIntegrationTests : IDisposable
{
    private const string ImageKey = "meai_image";
    private readonly IChatClient? _baseChatClient;

    protected ImageGeneratingChatClientIntegrationTests()
    {
        _baseChatClient = CreateChatClient();
        ImageGenerator = new();

        if (_baseChatClient != null)
        {
            ChatClient = _baseChatClient
                .AsBuilder()
                .UseImageGeneration(ImageGenerator)
                .UseFunctionInvocation()
                .Build();
        }
    }

    /// <summary>Gets the ImageGeneratingChatClient configured with function invocation support.</summary>
    protected IChatClient? ChatClient { get; }

    /// <summary>Gets the IImageGenerator used for testing.</summary>
    protected CapturingImageGenerator ImageGenerator { get; }

    public void Dispose()
    {
        ChatClient?.Dispose();
        _baseChatClient?.Dispose();
        ImageGenerator.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates the base IChatClient implementation to test with.
    /// Should return a real chat client that supports function calling.
    /// </summary>
    /// <returns>An IChatClient instance, or null to skip tests.</returns>
    protected abstract IChatClient? CreateChatClient();

    /// <summary>
    /// Helper method to get a chat response using either streaming or non-streaming based on the parameter.
    /// </summary>
    /// <param name="useStreaming">Whether to use streaming or non-streaming response.</param>
    /// <param name="messages">The chat messages to send.</param>
    /// <param name="options">The chat options to use.</param>
    /// <returns>A ChatResponse from either streaming or non-streaming call.</returns>
    protected async Task<ChatResponse> GetResponseAsync(bool useStreaming, IEnumerable<ChatMessage> messages, ChatOptions? options = null, IChatClient? chatClient = null)
    {
        chatClient ??= ChatClient ?? throw new InvalidOperationException("ChatClient is not initialized.");

        if (useStreaming)
        {
            return ValidateChatResponse(await chatClient.GetStreamingResponseAsync(messages, options).ToChatResponseAsync());
        }
        else
        {
            return ValidateChatResponse(await chatClient.GetResponseAsync(messages, options));
        }

        static ChatResponse ValidateChatResponse(ChatResponse response)
        {
            var contents = response.Messages.SelectMany(m => m.Contents).ToArray();

            List<string> imageIds = [];
            foreach (var toolResult in contents.OfType<ImageGenerationToolResultContent>())
            {
                Assert.NotNull(toolResult.Outputs);

                foreach (var dataContent in toolResult.Outputs.OfType<DataContent>())
                {
                    var imageId = dataContent.AdditionalProperties?[ImageKey] as string;
                    Assert.NotNull(imageId);
                    imageIds.Add(imageId);
                }
            }

            foreach (var textContent in contents.OfType<TextContent>())
            {
                Assert.DoesNotContain(ImageKey, textContent.Text, StringComparison.OrdinalIgnoreCase);
                foreach (var imageId in imageIds)
                {
                    // Ensure no image IDs appear in text content
                    Assert.DoesNotContain(imageId, textContent.Text, StringComparison.OrdinalIgnoreCase);
                }
            }

            return response;
        }
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task GenerateImage_CallsGenerateFunction_ReturnsDataContent(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool()]
        };

        // Act
        var response = await GetResponseAsync(useStreaming,
            [new ChatMessage(ChatRole.User, "Please generate an image of a cat")],
            chatOptions);

        // Assert
        Assert.Single(imageGenerator.GenerateCalls);
        var (request, _) = imageGenerator.GenerateCalls[0];
        Assert.Contains("cat", request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Null(request.OriginalImages); // Generation, not editing

        // Verify that we get ImageGenerationToolResultContent back in the response
        var imageResults = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<ImageGenerationToolResultContent>();

        var imageResult = Assert.Single(imageResults);
        Assert.NotNull(imageResult.Outputs);
        var imageContent = Assert.Single(imageResult.Outputs.OfType<DataContent>());
        Assert.Equal("image/png", imageContent.MediaType);
        Assert.False(imageContent.Data.IsEmpty);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task EditImage_WithImageInSameRequest_PassesExactDataContent(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var testImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var originalImageData = new DataContent(testImageData, "image/png") { Name = "original.png" };
        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool()]
        };

        // Act
        var response = await GetResponseAsync(useStreaming,
            [new ChatMessage(ChatRole.User, [new TextContent("Please edit this image to add a red border"), originalImageData])],
            chatOptions);

        // Assert
        var (request, _) = Assert.Single(imageGenerator.GenerateCalls);
        Assert.NotNull(request.OriginalImages);

        var originalImage = Assert.Single(request.OriginalImages);
        var originalImageContent = Assert.IsType<DataContent>(originalImage);
        Assert.Equal(testImageData, originalImageContent.Data.ToArray());
        Assert.Equal("image/png", originalImageContent.MediaType);
        Assert.Equal("original.png", originalImageContent.Name);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task GenerateThenEdit_FromChatHistory_EditsGeneratedImage(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool()]
        };

        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "Please generate an image of a dog")
        };

        // First request: Generate image
        var firstResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);
        chatHistory.AddRange(firstResponse.Messages);

        // Second request: Edit the generated image
        chatHistory.Add(new ChatMessage(ChatRole.User, "Please edit the image to make it more colorful"));
        var secondResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);

        // Assert
        Assert.Equal(2, imageGenerator.GenerateCalls.Count);

        // First call should be generation (no original images)
        var (firstRequest, _) = imageGenerator.GenerateCalls[0];
        Assert.Null(firstRequest.OriginalImages);

        // Extract the DataContent from the ImageGenerationToolResultContent
        var firstToolResultContent = Assert.Single(firstResponse.Messages.SelectMany(m => m.Contents).OfType<ImageGenerationToolResultContent>());
        Assert.NotNull(firstToolResultContent.Outputs);
        var firstContent = Assert.Single(firstToolResultContent.Outputs.OfType<DataContent>());

        // Second call should be editing (with original images)
        var (secondRequest, _) = imageGenerator.GenerateCalls[1];
        Assert.Single(secondResponse.Messages.SelectMany(m => m.Contents).OfType<ImageGenerationToolResultContent>().SelectMany(t => t.Outputs!.OfType<DataContent>()));
        Assert.NotNull(secondRequest.OriginalImages);
        var editContent = Assert.Single(secondRequest.OriginalImages);
        Assert.Equal(firstContent, editContent); // Should be the same image as generated in first call

        var editedImage = Assert.IsType<DataContent>(secondRequest.OriginalImages.First());
        Assert.Equal("image/png", editedImage.MediaType);
        Assert.Contains("generated_image_1", editedImage.Name);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task MultipleEdits_EditsLatestImage(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool()]
        };

        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "Please generate an image of a tree")
        };

        // First: Generate image
        var firstResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);
        chatHistory.AddRange(firstResponse.Messages);

        // Second: First edit
        chatHistory.Add(new ChatMessage(ChatRole.User, "Please edit the image to add flowers"));
        var secondResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);
        chatHistory.AddRange(secondResponse.Messages);

        // Third: Second edit (should edit the latest version by default)
        chatHistory.Add(new ChatMessage(ChatRole.User, "Please edit that last image to add birds"));
        var thirdResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);

        // Assert
        Assert.Equal(3, imageGenerator.GenerateCalls.Count);

        // Third call should edit the second generated image (from first edit), not the original
        var (thirdRequest, _) = imageGenerator.GenerateCalls[2];
        Assert.NotNull(thirdRequest.OriginalImages);

        // Extract the DataContent from the second response's ImageGenerationToolResultContent
        var secondToolResultContent = Assert.Single(secondResponse.Messages.SelectMany(m => m.Contents).OfType<ImageGenerationToolResultContent>());
        var secondImage = Assert.Single(secondToolResultContent.Outputs!.OfType<DataContent>());
        var lastImageToEdit = Assert.Single(thirdRequest.OriginalImages.OfType<DataContent>());
        Assert.Equal(secondImage, lastImageToEdit);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task MultipleEdits_EditsFirstImage(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool()]
        };

        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "Please generate an image of a tree")
        };

        // First: Generate image
        var firstResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);
        chatHistory.AddRange(firstResponse.Messages);

        // Second: First edit
        chatHistory.Add(new ChatMessage(ChatRole.User, "Please edit the image to add fruit"));
        var secondResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);
        chatHistory.AddRange(secondResponse.Messages);

        // Third: Second edit (should edit the latest version by default)
        chatHistory.Add(new ChatMessage(ChatRole.User, "That didn't work out.  Please edit the original image to add birds"));
        var thirdResponse = await GetResponseAsync(useStreaming, chatHistory, chatOptions);

        // Assert
        Assert.Equal(3, imageGenerator.GenerateCalls.Count);

        // Third call should edit the original generated image (not from edit)
        var (thirdRequest, _) = imageGenerator.GenerateCalls[2];
        Assert.NotNull(thirdRequest.OriginalImages);

        // Extract the DataContent from the first response's ImageGenerationToolResultContent
        var firstToolResultContent = Assert.Single(firstResponse.Messages.SelectMany(m => m.Contents).OfType<ImageGenerationToolResultContent>());
        var firstGeneratedImage = Assert.Single(firstToolResultContent.Outputs!.OfType<DataContent>());
        var lastImageToEdit = Assert.IsType<DataContent>(thirdRequest.OriginalImages.First());
        Assert.Equal(firstGeneratedImage, lastImageToEdit);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task ImageGeneration_WithOptions_PassesOptionsToGenerator(bool useStreaming)
    {
        SkipIfNotEnabled();

        var imageGenerator = ImageGenerator;
        var imageGenerationOptions = new ImageGenerationOptions
        {
            Count = 2,
            ImageSize = new System.Drawing.Size(512, 512)
        };

        var chatOptions = new ChatOptions
        {
            Tools = [new HostedImageGenerationTool { Options = imageGenerationOptions }]
        };

        // Act
        var response = await GetResponseAsync(useStreaming,
            [new ChatMessage(ChatRole.User, "Generate an image of a castle")],
            chatOptions);

        // Assert
        Assert.Single(imageGenerator.GenerateCalls);
        var (_, options) = imageGenerator.GenerateCalls[0];
        Assert.NotNull(options);
        Assert.Equal(2, options.Count);
        Assert.Equal(new System.Drawing.Size(512, 512), options.ImageSize);
    }

    [Theory]
    [InlineData(false)] // Non-streaming
    [InlineData(true)]  // Streaming
    public virtual async Task ImageContentHandling_AllImages_ReplacesImagesWithPlaceholders(bool useStreaming)
    {
        SkipIfNotEnabled();

        var testImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var capturedMessages = new List<IEnumerable<ChatMessage>>();

        // Create a new ImageGeneratingChatClient with AllImages data content handling
        using var imageGeneratingClient = _baseChatClient!
            .AsBuilder()
            .UseImageGeneration(ImageGenerator)
            .Use((messages, options, next, cancellationToken) =>
            {
                capturedMessages.Add(messages);
                return next(messages, options, cancellationToken);
            })
            .UseFunctionInvocation()
            .Build();

        var originalImage = new DataContent(testImageData, "image/png") { Name = "test.png" };

        // Act
        await GetResponseAsync(useStreaming,
            [
                new ChatMessage(ChatRole.User,
                [
                    new TextContent("Here's an image to process"),
                    originalImage
                ])
            ],
            new ChatOptions { Tools = [new HostedImageGenerationTool()] },
            imageGeneratingClient);

        // Assert
        Assert.NotEmpty(capturedMessages);
        var processedMessages = capturedMessages.First().ToList();
        var userMessage = processedMessages.First(m => m.Role == ChatRole.User);

        // Should have text content with placeholder instead of original image
        var textContents = userMessage.Contents.OfType<TextContent>().ToList();
        Assert.Contains(textContents, tc => tc.Text.Contains(ImageKey) && tc.Text.Contains("] available for edit"));

        // Should not contain the original DataContent
        Assert.DoesNotContain(userMessage.Contents, c => c == originalImage);
    }

    /// <summary>
    /// Test image generator that captures calls and returns fake image data.
    /// </summary>
    protected sealed class CapturingImageGenerator : IImageGenerator
    {
        private const string TestImageMediaType = "image/png";
        private static readonly byte[] _testImageData = [0x89, 0x50, 0x4E, 0x47]; // PNG header

        public List<(ImageGenerationRequest request, ImageGenerationOptions? options)> GenerateCalls { get; } = [];
        public int ImageCounter { get; private set; }

        public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            GenerateCalls.Add((request, options));

            // Create fake image data with unique content
            var imageData = new byte[_testImageData.Length + 4];
            _testImageData.CopyTo(imageData, 0);
            BitConverter.GetBytes(++ImageCounter).CopyTo(imageData, _testImageData.Length);

            var imageContent = new DataContent(imageData, TestImageMediaType)
            {
                Name = $"generated_image_{ImageCounter}.png"
            };

            return Task.FromResult(new ImageGenerationResponse([imageContent]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // No resources to dispose
        }
    }

    [MemberNotNull(nameof(ChatClient))]
    protected void SkipIfNotEnabled()
    {
        string? skipIntegration = TestRunnerConfiguration.Instance["SkipIntegrationTests"];

        Assert.SkipUnless(skipIntegration is null && ChatClient is not null, "Client is not enabled.");
    }
}
