// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class HostedFileContentTests
{
    [Fact]
    public void Constructor_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>("fileId", () => new HostedFileContent(null!));
        Assert.Throws<ArgumentException>("fileId", () => new HostedFileContent(string.Empty));
        Assert.Throws<ArgumentException>("fileId", () => new HostedFileContent(" "));
    }

    [Fact]
    public void Constructor_String_PropsDefault()
    {
        string fileId = "id123";
        HostedFileContent c = new(fileId);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
        Assert.Null(c.SizeInBytes);
        Assert.Null(c.CreatedAt);
#pragma warning disable MEAI001
        Assert.Null(c.Purpose);
        Assert.Null(c.Scope);
#pragma warning restore MEAI001
        Assert.Equal(fileId, c.FileId);
    }

    [Fact]
    public void Constructor_PropsRoundtrip()
    {
        HostedFileContent c = new("id123");
        Assert.Equal("id123", c.FileId);

        c.FileId = "id456";
        Assert.Equal("id456", c.FileId);

        Assert.Throws<ArgumentNullException>("value", () => c.FileId = null!);
        Assert.Throws<ArgumentException>("value", () => c.FileId = string.Empty);
        Assert.Throws<ArgumentException>("value", () => c.FileId = " ");

        Assert.Null(c.RawRepresentation);
        object raw = new();
        c.RawRepresentation = raw;
        Assert.Same(raw, c.RawRepresentation);

        Assert.Null(c.AdditionalProperties);
        AdditionalPropertiesDictionary props = new() { { "key", "value" } };
        c.AdditionalProperties = props;
        Assert.Same(props, c.AdditionalProperties);
    }

    [Fact]
    public void Serialization_Roundtrips()
    {
        var content = new HostedFileContent("file123");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserializedContent = JsonSerializer.Deserialize<HostedFileContent>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserializedContent);
        Assert.Equal(content.FileId, deserializedContent.FileId);
    }

    [Fact]
    public void MediaType_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.MediaType);

        c.MediaType = "image/png";
        Assert.Equal("image/png", c.MediaType);

        c.MediaType = "application/pdf";
        Assert.Equal("application/pdf", c.MediaType);

        c.MediaType = null;
        Assert.Null(c.MediaType);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("type//subtype")]
    [InlineData("type/subtype/")]
    [InlineData("type/subtype;key=")]
    [InlineData("type/subtype;=value")]
    [InlineData("type/subtype;key=value;another=")]
    public void MediaType_InvalidValue_Throws(string invalidMediaType)
    {
        HostedFileContent c = new("id123");
        Assert.Throws<ArgumentException>("value", () => c.MediaType = invalidMediaType);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    [InlineData("text/plain;charset=UTF-8")]
    [InlineData("image/*")]
    public void MediaType_ValidValue_Roundtrips(string mediaType)
    {
        HostedFileContent c = new("id123") { MediaType = mediaType };
        Assert.Equal(mediaType, c.MediaType);
    }

    [Fact]
    public void Name_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.Name);

        c.Name = "document.pdf";
        Assert.Equal("document.pdf", c.Name);

        c.Name = "image.png";
        Assert.Equal("image.png", c.Name);

        c.Name = null;
        Assert.Null(c.Name);
    }

    [Fact]
    public void SizeInBytes_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.SizeInBytes);

        c.SizeInBytes = 12345L;
        Assert.Equal(12345L, c.SizeInBytes);

        c.SizeInBytes = null;
        Assert.Null(c.SizeInBytes);
    }

    [Fact]
    public void CreatedAt_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.CreatedAt);

        var now = DateTimeOffset.UtcNow;
        c.CreatedAt = now;
        Assert.Equal(now, c.CreatedAt);

        c.CreatedAt = null;
        Assert.Null(c.CreatedAt);
    }

#pragma warning disable MEAI001
    [Fact]
    public void Purpose_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.Purpose);

        c.Purpose = "assistants";
        Assert.Equal("assistants", c.Purpose);

        c.Purpose = null;
        Assert.Null(c.Purpose);
    }

    [Fact]
    public void Scope_Roundtrips()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.Scope);

        c.Scope = "container-1";
        Assert.Equal("container-1", c.Scope);

        c.Scope = null;
        Assert.Null(c.Scope);
    }
#pragma warning restore MEAI001

    [Fact]
    public void Serialization_IncludesExperimentalProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var content = new HostedFileContent("file123")
        {
            Name = "test.txt",
            MediaType = "text/plain",
            SizeInBytes = 1024,
            CreatedAt = now,
#pragma warning disable MEAI001
            Purpose = "fine-tune",
            Scope = "container-1",
#pragma warning restore MEAI001
        };

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);

        Assert.Contains("sizeInBytes", json);
        Assert.Contains("createdAt", json);
        Assert.Contains("purpose", json);
        Assert.Contains("scope", json);

        var deserialized = JsonSerializer.Deserialize<HostedFileContent>(json, AIJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(1024, deserialized.SizeInBytes);
        Assert.Equal(now, deserialized.CreatedAt);
#pragma warning disable MEAI001
        Assert.Equal("fine-tune", deserialized.Purpose);
        Assert.Equal("container-1", deserialized.Scope);
#pragma warning restore MEAI001
    }

    [Theory]
    [InlineData("image/gif", "image", true)]
    [InlineData("IMAGE/JPEG", "image", true)]
    [InlineData("image/vnd.microsoft.icon", "imAge", true)]
    [InlineData("audio/mpeg", "image", false)]
    [InlineData("audio/mpeg", "audio/mpeg", false)]
    [InlineData("audio/mpeg", "audio/", false)]
    public void HasTopLevelMediaType_ReturnsExpected(string mediaType, string topLevelType, bool expected)
    {
        HostedFileContent c = new("id123") { MediaType = mediaType };
        Assert.Equal(expected, c.HasTopLevelMediaType(topLevelType));
    }

    [Fact]
    public void HasTopLevelMediaType_NullMediaType_ReturnsFalse()
    {
        HostedFileContent c = new("id123");
        Assert.Null(c.MediaType);
        Assert.False(c.HasTopLevelMediaType("image"));
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "hostedFile",
              "fileId": "file-abc123",
              "mediaType": "application/pdf",
              "name": "document.pdf",
              "sizeInBytes": 1024,
              "createdAt": "2024-01-15T10:30:00+00:00",
              "purpose": "assistants",
              "scope": "user",
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var hostedFile = Assert.IsType<HostedFileContent>(result);
        Assert.Equal("file-abc123", hostedFile.FileId);
        Assert.Equal("application/pdf", hostedFile.MediaType);
        Assert.Equal("document.pdf", hostedFile.Name);
        Assert.Equal(1024, hostedFile.SizeInBytes);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero), hostedFile.CreatedAt);
#pragma warning disable MEAI001
        Assert.Equal("assistants", hostedFile.Purpose);
        Assert.Equal("user", hostedFile.Scope);
#pragma warning restore MEAI001
        Assert.NotNull(hostedFile.AdditionalProperties);
        Assert.Equal("val", hostedFile.AdditionalProperties["key"]?.ToString());
    }
}
