// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Drawing;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ImageGenerationOptionsTests
{
    [Fact]
    public void Constructor_Parameterless_PropsDefaulted()
    {
        ImageGenerationOptions options = new();
        Assert.Null(options.ResponseFormat);
        Assert.Null(options.Count);
        Assert.Null(options.ImageSize);
        Assert.Null(options.MediaType);
        Assert.Null(options.ModelId);
        Assert.Null(options.RawRepresentationFactory);
        Assert.Null(options.StreamingCount);

        ImageGenerationOptions clone = options.Clone();
        Assert.Null(clone.ResponseFormat);
        Assert.Null(clone.Count);
        Assert.Null(clone.ImageSize);
        Assert.Null(clone.MediaType);
        Assert.Null(clone.ModelId);
        Assert.Null(clone.RawRepresentationFactory);
        Assert.Null(clone.StreamingCount);
    }

    [Fact]
    public void Properties_Roundtrip()
    {
        ImageGenerationOptions options = new();

#pragma warning disable MEAI001 // IImageGenerator is experimental
        Func<IImageGenerator, object?> factory = generator => new { Representation = "raw data" };
#pragma warning restore MEAI001

        options.ResponseFormat = ImageGenerationResponseFormat.Data;
        options.Count = 5;
        options.ImageSize = new Size(1024, 768);
        options.MediaType = "image/png";
        options.ModelId = "modelId";
        options.RawRepresentationFactory = factory;
        options.StreamingCount = 2;

        Assert.Equal(ImageGenerationResponseFormat.Data, options.ResponseFormat);
        Assert.Equal(5, options.Count);
        Assert.Equal(new Size(1024, 768), options.ImageSize);
        Assert.Equal("image/png", options.MediaType);
        Assert.Equal("modelId", options.ModelId);
        Assert.Same(factory, options.RawRepresentationFactory);
        Assert.Equal(2, options.StreamingCount);

        ImageGenerationOptions clone = options.Clone();
        Assert.Equal(ImageGenerationResponseFormat.Data, clone.ResponseFormat);
        Assert.Equal(5, clone.Count);
        Assert.Equal(new Size(1024, 768), clone.ImageSize);
        Assert.Equal("image/png", clone.MediaType);
        Assert.Equal("modelId", clone.ModelId);
        Assert.Same(factory, clone.RawRepresentationFactory);
        Assert.Equal(2, clone.StreamingCount);
    }

    [Fact]
    public void JsonSerialization_Roundtrips()
    {
        ImageGenerationOptions options = new()
        {
            ResponseFormat = ImageGenerationResponseFormat.Data,
            Count = 3,
            ImageSize = new Size(256, 256),
            MediaType = "image/jpeg",
            ModelId = "test-model",
        };

        string json = JsonSerializer.Serialize(options, AIJsonUtilities.DefaultOptions);

        ImageGenerationOptions? deserialized = JsonSerializer.Deserialize<ImageGenerationOptions>(json, AIJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized);

        Assert.Equal(ImageGenerationResponseFormat.Data, deserialized.ResponseFormat);
        Assert.Equal(3, deserialized.Count);
        Assert.Equal(new Size(256, 256), deserialized.ImageSize);
        Assert.Equal("image/jpeg", deserialized.MediaType);
        Assert.Equal("test-model", deserialized.ModelId);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        ImageGenerationOptions original = new()
        {
            ResponseFormat = ImageGenerationResponseFormat.Data,
            Count = 2,
            ImageSize = new Size(512, 512),
            MediaType = "image/png",
            ModelId = "original-model"
        };

        ImageGenerationOptions clone = original.Clone();

        // Modify original
        original.ResponseFormat = ImageGenerationResponseFormat.Uri;
        original.Count = 1;
        original.ImageSize = new Size(1024, 1024);
        original.MediaType = "image/jpeg";
        original.ModelId = "modified-model";

        // Clone should remain unchanged
        Assert.Equal(ImageGenerationResponseFormat.Data, clone.ResponseFormat);
        Assert.Equal(2, clone.Count);
        Assert.Equal(new Size(512, 512), clone.ImageSize);
        Assert.Equal("image/png", clone.MediaType);
        Assert.Equal("original-model", clone.ModelId);
    }

    [Theory]
    [InlineData(ImageGenerationResponseFormat.Uri)]
    [InlineData(ImageGenerationResponseFormat.Data)]
#pragma warning disable MEAI001 // ImageGenerationResponseFormat.Hosted is experimental
    [InlineData(ImageGenerationResponseFormat.Hosted)]
#pragma warning restore MEAI001
    public void ImageGenerationResponseFormat_Values_AreValid(ImageGenerationResponseFormat responseFormat)
    {
        Assert.True(Enum.IsDefined(typeof(ImageGenerationResponseFormat), responseFormat));
    }

    [Fact]
    public void ImageGenerationResponseFormat_JsonSerialization_Roundtrips()
    {
        foreach (ImageGenerationResponseFormat responseFormat in Enum.GetValues(typeof(ImageGenerationResponseFormat)))
        {
            string json = JsonSerializer.Serialize(responseFormat, AIJsonUtilities.DefaultOptions);
            ImageGenerationResponseFormat deserialized = JsonSerializer.Deserialize<ImageGenerationResponseFormat>(json, AIJsonUtilities.DefaultOptions);
            Assert.Equal(responseFormat, deserialized);
        }
    }

    [Fact]
    public void CopyConstructors_EnableHierarchyCloning()
    {
        OptionsB b = new()
        {
            ModelId = "test",
            A = 42,
            B = 84,
        };

        ImageGenerationOptions clone = b.Clone();

        Assert.Equal("test", clone.ModelId);
        Assert.Equal(42, Assert.IsType<OptionsA>(clone, exactMatch: false).A);
        Assert.Equal(84, Assert.IsType<OptionsB>(clone, exactMatch: true).B);
    }

    private class OptionsA : ImageGenerationOptions
    {
        public OptionsA()
        {
        }

        protected OptionsA(OptionsA other)
            : base(other)
        {
            A = other.A;
        }

        public int A { get; set; }

        public override ImageGenerationOptions Clone() => new OptionsA(this);
    }

    private class OptionsB : OptionsA
    {
        public OptionsB()
        {
        }

        protected OptionsB(OptionsB other)
            : base(other)
        {
            B = other.B;
        }

        public int B { get; set; }

        public override ImageGenerationOptions Clone() => new OptionsB(this);
    }

    [Fact]
    public void CopyConstructor_Null_Valid()
    {
        PassedNullToBaseOptions options = new();
        Assert.NotNull(options);
    }

    private class PassedNullToBaseOptions : ImageGenerationOptions
    {
        public PassedNullToBaseOptions()
            : base(null)
        {
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "count": 2,
              "imageSize": {
                "width": 1024,
                "height": 768
              },
              "mediaType": "image/png",
              "modelId": "dall-e-3",
              "responseFormat": "Uri",
              "streamingCount": 3,
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        ImageGenerationOptions? result = JsonSerializer.Deserialize<ImageGenerationOptions>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(new Size(1024, 768), result.ImageSize);
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal("dall-e-3", result.ModelId);
        Assert.Equal(ImageGenerationResponseFormat.Uri, result.ResponseFormat);
        Assert.Equal(3, result.StreamingCount);
        Assert.NotNull(result.AdditionalProperties);
        Assert.Equal("val", result.AdditionalProperties["key"]?.ToString());
    }
}
