// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable CA2214 // Do not call overridable methods in constructors

namespace Microsoft.Extensions.AI;

public abstract class ImageGeneratorIntegrationTests : IDisposable
{
    private readonly IImageGenerator? _generator;

    protected ImageGeneratorIntegrationTests()
    {
        _generator = CreateGenerator();
    }

    public void Dispose()
    {
        _generator?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract IImageGenerator? CreateGenerator();

    [Fact]
    public virtual async Task GenerateImagesAsync_SingleImageGeneration()
    {
        SkipIfNotEnabled();

        var options = new ImageGenerationOptions
        {
            Count = 1
        };

        var response = await _generator.GenerateImagesAsync("A simple drawing of a house", options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);

        var content = Assert.Single(response.Contents);
        switch (content)
        {
            case UriContent uc:
                Assert.StartsWith("http", uc.Uri.Scheme, StringComparison.Ordinal);
                break;

            case DataContent dc:
                Assert.False(dc.Data.IsEmpty);
                Assert.StartsWith("image/", dc.MediaType, StringComparison.Ordinal);
                break;

            default:
                Assert.Fail($"Unexpected content type: {content.GetType()}");
                break;
        }
    }

    [Fact]
    public virtual async Task GenerateImagesAsync_MultipleImages()
    {
        SkipIfNotEnabled();

        var options = new ImageGenerationOptions
        {
            Count = 2
        };

        var response = await _generator.GenerateImagesAsync("A cat sitting on a table", options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Equal(2, response.Contents.Count);

        foreach (var content in response.Contents)
        {
            Assert.IsType<DataContent>(content);
            var dataContent = (DataContent)content;
            Assert.False(dataContent.Data.IsEmpty);
            Assert.StartsWith("image/", dataContent.MediaType, StringComparison.Ordinal);
        }
    }

    [Fact]
    public virtual async Task EditImagesAsync_SingleImage()
    {
        SkipIfNotEnabled();

        var imageData = GetImageData("dotnet.png");
        AIContent[] originalImages = [new DataContent(imageData, "image/png") { Name = "dotnet.png" }];

        var options = new ImageGenerationOptions
        {
            Count = 1
        };

        var response = await _generator.EditImagesAsync(originalImages, "Add a red border and make the background tie-dye", options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);

        var content = response.Contents[0];
        Assert.IsType<DataContent>(content);
        var dataContent = (DataContent)content;
        Assert.False(dataContent.Data.IsEmpty);
        Assert.StartsWith("image/", dataContent.MediaType, StringComparison.Ordinal);
    }

    private static byte[] GetImageData(string fileName)
    {
        using Stream? s = typeof(ImageGeneratorIntegrationTests).Assembly.GetManifestResourceStream($"Microsoft.Extensions.AI.Resources.{fileName}");
        Assert.NotNull(s);
        using MemoryStream ms = new();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [MemberNotNull(nameof(_generator))]
    protected void SkipIfNotEnabled()
    {
        string? skipIntegration = TestRunnerConfiguration.Instance["SkipIntegrationTests"];

        Assert.SkipUnless(skipIntegration is null && _generator is not null, "Generator is not enabled.");
    }
}
