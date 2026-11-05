// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>A delegating chat client that enables image generation capabilities by converting <see cref="HostedImageGenerationTool"/> instances to function tools.</summary>
/// <remarks>
/// <para>
/// The provided implementation of <see cref="IChatClient"/> is thread-safe for concurrent use so long as the
/// <see cref="IImageGenerator"/> employed is also thread-safe for concurrent use.
/// </para>
/// <para>
/// This client automatically detects <see cref="HostedImageGenerationTool"/> instances in the <see cref="ChatOptions.Tools"/> collection
/// and replaces them with equivalent function tools that the chat client can invoke to perform image generation and editing operations.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIImageGeneration, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class ImageGeneratingChatClient : DelegatingChatClient
{
    /// <summary>
    /// Specifies how image and other data content is handled when passing data to an inner client.
    /// </summary>
    /// <remarks>
    /// Use this enumeration to control whether images in the data content are passed as-is, replaced
    /// with unique identifiers, or only generated images are replaced. This setting affects how downstream clients
    /// receive and process image data.
    /// Reducing what's passed downstream can help manage the context window.
    /// </remarks>
    public enum DataContentHandling
    {
        /// <summary>Pass all DataContent to inner client.</summary>
        None,

        /// <summary>Replace all images with unique identifiers when passing to inner client.</summary>
        AllImages,

        /// <summary>Replace only images that were produced by past image generation requests with unique identifiers when passing to inner client.</summary>
        GeneratedImages
    }

    private const string ImageKey = "meai_image";

    private readonly IImageGenerator _imageGenerator;
    private readonly DataContentHandling _dataContentHandling;

    /// <summary>Initializes a new instance of the <see cref="ImageGeneratingChatClient"/> class.</summary>
    /// <param name="innerClient">The underlying <see cref="IChatClient"/>.</param>
    /// <param name="imageGenerator">An <see cref="IImageGenerator"/> instance that will be used for image generation operations.</param>
    /// <param name="dataContentHandling">Specifies how to handle <see cref="DataContent"/> instances when passing messages to the inner client.
    /// The default is <see cref="DataContentHandling.AllImages"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="imageGenerator"/> is <see langword="null"/>.</exception>
    public ImageGeneratingChatClient(IChatClient innerClient, IImageGenerator imageGenerator, DataContentHandling dataContentHandling = DataContentHandling.AllImages)
        : base(innerClient)
    {
        _imageGenerator = Throw.IfNull(imageGenerator);
        _dataContentHandling = dataContentHandling;
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var requestState = new RequestState(_imageGenerator, _dataContentHandling);

        // Process the chat options to replace HostedImageGenerationTool with functions
        var processedOptions = requestState.ProcessChatOptions(options);
        var processedMessages = requestState.ProcessChatMessages(messages);

        // Get response from base implementation
        var response = await base.GetResponseAsync(processedMessages, processedOptions, cancellationToken);

        // Replace FunctionResultContent instances with generated image content
        foreach (var message in response.Messages)
        {
            message.Contents = requestState.ReplaceImageGenerationFunctionResults(message.Contents);
        }

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        var requestState = new RequestState(_imageGenerator, _dataContentHandling);

        // Process the chat options to replace HostedImageGenerationTool with functions
        var processedOptions = requestState.ProcessChatOptions(options);
        var processedMessages = requestState.ProcessChatMessages(messages);

        await foreach (var update in base.GetStreamingResponseAsync(processedMessages, processedOptions, cancellationToken))
        {
            // Replace any FunctionResultContent instances with generated image content
            var newContents = requestState.ReplaceImageGenerationFunctionResults(update.Contents);

            if (!ReferenceEquals(newContents, update.Contents))
            {
                // Create a new update instance with modified contents
                var modifiedUpdate = update.Clone();
                modifiedUpdate.Contents = newContents;
                yield return modifiedUpdate;
            }
            else
            {
                yield return update;
            }
        }
    }

    /// <summary>Provides a mechanism for releasing unmanaged resources.</summary>
    /// <param name="disposing"><see langword="true"/> to dispose managed resources; otherwise, <see langword="false"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageGenerator.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Contains all the per-request state and methods for handling image generation requests.
    /// This class is created fresh for each request to ensure thread safety.
    /// This class is not exposed publicly and does not own any of it's resources.
    /// </summary>
    private sealed class RequestState
    {
        private readonly IImageGenerator _imageGenerator;
        private readonly DataContentHandling _dataContentHandling;
        private readonly HashSet<string> _toolNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AIContent>> _imageContentByCallId = [];
        private readonly Dictionary<string, AIContent> _imageContentById = new(StringComparer.OrdinalIgnoreCase);
        private ImageGenerationOptions? _imageGenerationOptions;

        public RequestState(IImageGenerator imageGenerator, DataContentHandling dataContentHandling)
        {
            _imageGenerator = imageGenerator;
            _dataContentHandling = dataContentHandling;
        }

        /// <summary>
        /// Processes the chat messages to replace images in data content with unique identifiers as needed.
        /// All images will be stored for later retrieval during image editing operations.
        /// See <see cref="DataContentHandling"/> for details on image replacement behavior.
        /// </summary>
        /// <param name="messages">Messages to process.</param>
        /// <returns>Processed messages, or the original messages if no changes were made.</returns>
        public IEnumerable<ChatMessage> ProcessChatMessages(IEnumerable<ChatMessage> messages)
        {
            List<ChatMessage>? newMessages = null;
            int messageIndex = 0;
            foreach (var message in messages)
            {
                List<AIContent>? newContents = null;
                for (int contentIndex = 0; contentIndex < message.Contents.Count; contentIndex++)
                {
                    var content = message.Contents[contentIndex];

                    void ReplaceImage(string imageId, DataContent dataContent)
                    {
                        // Replace image with a placeholder text content, to give an indication to the model of its placement in the context
                        newContents ??= CopyList(message.Contents, contentIndex);
                        newContents.Add(new TextContent($"[{ImageKey}:{imageId}] available for edit.")
                        {
                            Annotations = dataContent.Annotations,
                            AdditionalProperties = dataContent.AdditionalProperties
                        });
                    }

                    if (content is DataContent dataContent && dataContent.HasTopLevelMediaType("image"))
                    {
                        // Store the image to make available for edit
                        var imageId = StoreImage(dataContent);

                        if (_dataContentHandling == DataContentHandling.AllImages)
                        {
                            ReplaceImage(imageId, dataContent);
                            continue; // Skip adding the original content
                        }
                    }
                    else if (content is ImageGenerationToolResultContent toolResultContent)
                    {
                        foreach (var output in toolResultContent.Outputs ?? [])
                        {
                            if (output is DataContent generatedDataContent && generatedDataContent.HasTopLevelMediaType("image"))
                            {
                                // Store the image to make available for edit
                                var imageId = StoreImage(generatedDataContent, isGenerated: true);

                                if (_dataContentHandling == DataContentHandling.AllImages ||
                                    _dataContentHandling == DataContentHandling.GeneratedImages)
                                {
                                    ReplaceImage(imageId, generatedDataContent);
                                }
                            }
                        }

                        if (_dataContentHandling == DataContentHandling.AllImages ||
                            _dataContentHandling == DataContentHandling.GeneratedImages)
                        {
                            // skip adding the generated content
                            continue;
                        }
                    }

                    // Add the original content if no replacement was made
                    newContents?.Add(content);
                }

                if (newContents != null)
                {
                    newMessages ??= [.. messages.Take(messageIndex)];
                    var newMessage = message.Clone();
                    newMessage.Contents = newContents;
                    newMessages.Add(newMessage);
                }
                else
                {
                    newMessages?.Add(message);

                }

                messageIndex++;
            }

            return newMessages ?? messages;
        }

        public ChatOptions? ProcessChatOptions(ChatOptions? options)
        {
            if (options?.Tools is null || options.Tools.Count == 0)
            {
                return options;
            }

            List<AITool>? newTools = null;
            var tools = options.Tools;
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];

                // remove all instances of HostedImageGenerationTool and store the options from the last one
                if (tool is HostedImageGenerationTool imageGenerationTool)
                {
                    _imageGenerationOptions = imageGenerationTool.Options;

                    // for the first image generation tool, clone the options and insert our function tools
                    // remove any subsequent image generation tools
                    newTools ??= InitializeTools(tools, i);
                }
                else
                {
                    newTools?.Add(tool);
                }
            }

            if (newTools is not null)
            {
                var newOptions = options.Clone();
                newOptions.Tools = newTools;
                return newOptions;
            }

            return options;

            List<AITool> InitializeTools(IList<AITool> existingTools, int toOffsetExclusive)
            {
#if NET
                ReadOnlySpan<AITool> tools =
#else
                AITool[] tools =
#endif
                [
                    AIFunctionFactory.Create(GenerateImageAsync),
                    AIFunctionFactory.Create(EditImageAsync),
                    AIFunctionFactory.Create(GetImagesForEdit)
                ];

                foreach (var tool in tools)
                {
                    _toolNames.Add(tool.Name);
                }

                var result = CopyList(existingTools, toOffsetExclusive, tools.Length);
                result.AddRange(tools);
                return result;
            }
        }

        /// <summary>
        /// Replaces FunctionResultContent instances for image generation functions with actual generated image content.
        /// We will have two messages
        /// 1. Role: Assistant, FunctionCall
        /// 2. Role: Tool, FunctionResult
        /// We need to replace content from both but we shouldn't remove the messages.
        /// If we do not then ChatClient's may not accept our altered history.
        /// 
        /// When interating with a HostedImageGenerationTool we will have typically only see a single Message with
        /// Role: Assistant that contains the DataContent (or a provider specific content, that's exposed as DataContent).    
        /// </summary>
        /// <param name="contents">The list of AI content to process.</param>
        public IList<AIContent> ReplaceImageGenerationFunctionResults(IList<AIContent> contents)
        {
            List<AIContent>? newContents = null;

            // Replace image-generation FunctionCallContent with ImageGenerationToolCallContent, and FunctionResultContent with generated image content
            for (int i = 0; i < contents.Count; i++)
            {
                var content = contents[i];

                // We must lookup by name because in the streaming case we have not yet been called to record the CallId.
                if (content is FunctionCallContent functionCall &&
                    _toolNames.Contains(functionCall.Name))
                {
                    // create a new list copying all items before this one, and omit the FunctionCallContent
                    newContents ??= CopyList(contents, i);

                    if (functionCall.Name != nameof(GetImagesForEdit))
                    {
                        newContents.Add(new ImageGenerationToolCallContent(functionCall.CallId));
                    }
                }
                else if (content is FunctionResultContent functionResult &&
                    _imageContentByCallId.TryGetValue(functionResult.CallId, out var imageContents))
                {
                    // create a new list copying all items before this one
                    newContents ??= CopyList(contents, i);

                    if (imageContents.Any())
                    {
                        // Insert ImageGenerationToolResultContent in its place, do not preserve the FunctionResultContent
                        newContents.Add(new ImageGenerationToolResultContent(functionResult.CallId)
                        {
                            Outputs = imageContents
                        });
                    }

                    // Remove the mapping as it's no longer needed
                    _ = _imageContentByCallId.Remove(functionResult.CallId);
                }
                else
                {
                    // keep the existing content if we have a new list
                    newContents?.Add(content);
                }
            }

            return newContents ?? contents;
        }

        [Description("Generates images based on a text description.")]
        public async Task<string> GenerateImageAsync(
             [Description("A detailed description of the image to generate")] string prompt,
             CancellationToken cancellationToken = default)
        {
            // Get the call ID from the current function invocation context
            var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
            if (callId == null)
            {
                return "No call ID available for image generation.";
            }

            var request = new ImageGenerationRequest(prompt);
            var options = _imageGenerationOptions ?? new ImageGenerationOptions();
            options.Count ??= 1;

            var response = await _imageGenerator.GenerateAsync(request, options, cancellationToken);

            if (response.Contents.Count == 0)
            {
                return "No image was generated.";
            }

            List<string> imageIds = [];
            List<AIContent> imageContents = _imageContentByCallId[callId] = [];
            foreach (var content in response.Contents)
            {
                if (content is DataContent imageContent && imageContent.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    imageContents.Add(imageContent);
                    imageIds.Add(StoreImage(imageContent, true));
                }
            }

            return "Generated image successfully.";
        }

        [Description("Lists the identifiers of all images available for edit.")]
        public IEnumerable<string> GetImagesForEdit()
        {
            // Get the call ID from the current function invocation context
            var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
            if (callId == null)
            {
                return ["No call ID available for image editing."];
            }

            _imageContentByCallId[callId] = [];

            return _imageContentById.Keys.AsEnumerable();
        }

        [Description("Edits an existing image based on a text description.")]
        public async Task<string> EditImageAsync(
            [Description("A detailed description of the image to generate")] string prompt,
            [Description($"The image to edit from one of the available image identifiers returned by {nameof(GetImagesForEdit)}")] string imageId,
            CancellationToken cancellationToken = default)
        {
            // Get the call ID from the current function invocation context
            var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
            if (callId == null)
            {
                return "No call ID available for image editing.";
            }

            if (string.IsNullOrEmpty(imageId))
            {
                return "No imageId provided";
            }

            try
            {
                var originalImage = RetrieveImageContent(imageId);
                if (originalImage == null)
                {
                    return $"No image found with: {imageId}";
                }

                var request = new ImageGenerationRequest(prompt, [originalImage]);
                var response = await _imageGenerator.GenerateAsync(request, _imageGenerationOptions, cancellationToken);

                if (response.Contents.Count == 0)
                {
                    return "No edited image was generated.";
                }

                List<string> imageIds = [];
                List<AIContent> imageContents = _imageContentByCallId[callId] = [];
                foreach (var content in response.Contents)
                {
                    if (content is DataContent imageContent && imageContent.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        imageContents.Add(imageContent);
                        imageIds.Add(StoreImage(imageContent, true));
                    }
                }

                return "Edited image successfully.";
            }
            catch (FormatException)
            {
                return "Invalid image data format. Please provide a valid base64-encoded image.";
            }
        }

        private static List<T> CopyList<T>(IList<T> original, int toOffsetExclusive, int additionalCapacity = 0)
        {
            var newList = new List<T>(original.Count + additionalCapacity);

            // Copy all items up to and excluding the current index
            for (int j = 0; j < toOffsetExclusive; j++)
            {
                newList.Add(original[j]);
            }

            return newList;
        }

        private DataContent? RetrieveImageContent(string imageId)
        {
            if (_imageContentById.TryGetValue(imageId, out var imageContent))
            {
                return imageContent as DataContent;
            }

            return null;
        }

        private string StoreImage(DataContent imageContent, bool isGenerated = false)
        {
            // Generate a unique ID for the image if it doesn't have one
            string? imageId = null;
            if (imageContent.AdditionalProperties?.TryGetValue(ImageKey, out imageId) is false || imageId is null)
            {
                imageId = imageContent.Name ?? Guid.NewGuid().ToString();
            }

            if (isGenerated)
            {
                imageContent.AdditionalProperties ??= [];
                imageContent.AdditionalProperties[ImageKey] = imageId;
            }

            // Store the image content for later retrieval
            _imageContentById[imageId] = imageContent;

            return imageId;
        }
    }
}
