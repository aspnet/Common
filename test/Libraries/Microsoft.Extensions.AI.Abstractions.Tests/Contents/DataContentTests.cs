// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public sealed class DataContentTests
{
    [Theory]

    // Invalid URI
    [InlineData("", typeof(ArgumentException))]
    [InlineData("invalid", typeof(ArgumentException))]
    [InlineData("data", typeof(ArgumentException))]

    // Not a data URI
    [InlineData("http://localhost/blah.png", typeof(ArgumentException))]
    [InlineData("https://localhost/blah.png", typeof(ArgumentException))]
    [InlineData("ftp://localhost/blah.png", typeof(ArgumentException))]
    [InlineData("a://localhost/blah.png", typeof(ArgumentException))]

    // Format errors
    [InlineData("data:", typeof(UriFormatException))] // data missing comma
    [InlineData("data:something,", typeof(UriFormatException))] // mime type without subtype
    [InlineData("data:something;else,data", typeof(UriFormatException))] // mime type without subtype
    [InlineData("data:type/subtype;;parameter=value;else,", typeof(UriFormatException))] // parameter without value
    [InlineData("data:type/subtype;parameter=va=lue;else,", typeof(UriFormatException))] // parameter with multiple =
    [InlineData("data:type/subtype;=value;else,", typeof(UriFormatException))] // empty parameter name
    [InlineData("data:image/j/peg;base64,/9j/4AAQSkZJRgABAgAAZABkAAD", typeof(UriFormatException))] // multiple slashes in media type

    // Base64 Validation Errors
    [InlineData("data:text;base64,something!", typeof(UriFormatException))]  // Invalid base64 due to invalid character '!'
    [InlineData("data:text/plain;base64,U29tZQ==\t", typeof(UriFormatException))] // Invalid base64 due to tab character
    [InlineData("data:text/plain;base64,U29tZQ==\r", typeof(UriFormatException))] // Invalid base64 due to carriage return character
    [InlineData("data:text/plain;base64,U29tZQ==\n", typeof(UriFormatException))] // Invalid base64 due to line feed character
    [InlineData("data:text/plain;base64,U29t\r\nZQ==", typeof(UriFormatException))] // Invalid base64 due to carriage return and line feed characters
    [InlineData("data:text/plain;base64,U29", typeof(UriFormatException))] // Invalid base64 due to missing padding
    [InlineData("data:text/plain;base64,U29tZQ", typeof(UriFormatException))] // Invalid base64 due to missing padding
    [InlineData("data:text/plain;base64,U29tZQ=", typeof(UriFormatException))] // Invalid base64 due to missing padding
    public void Ctor_InvalidUri_Throws(string path, Type exception)
    {
        Assert.Throws(exception, () => new DataContent(path));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("type//subtype")]
    [InlineData("type/subtype/")]
    [InlineData("type/subtype;key=")]
    [InlineData("type/subtype;=value")]
    [InlineData("type/subtype;key=value;another=")]
    public void Ctor_InvalidMediaType_Throws(string type)
    {
        Assert.Throws<ArgumentException>("mediaType", () => new DataContent("data:image/png;base64,aGVsbG8=", type));
    }

    [Theory]
    [InlineData("type/subtype")]
    [InlineData("type/subtype;key=value")]
    [InlineData("type/subtype;key=value;another=value")]
    [InlineData("type/subtype;key=value;another=value;yet_another=value")]
    public void Ctor_ValidMediaType_Roundtrips(string mediaType)
    {
        var content = new DataContent("data:image/png;base64,aGVsbG8=", mediaType);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("aGVsbG8=", content.Base64Data.ToString());

        content = new DataContent("data:,", mediaType);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("", content.Base64Data.ToString());

        content = new DataContent("data:text/plain,", mediaType);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("", content.Base64Data.ToString());

        content = new DataContent(new Uri("data:text/plain,"), mediaType);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("", content.Base64Data.ToString());

        content = new DataContent(new byte[] { 0, 1, 2 }, mediaType);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("AAEC", content.Base64Data.ToString());

        content = new DataContent(content.Uri);
        Assert.Equal(mediaType, content.MediaType);
        Assert.Equal("AAEC", content.Base64Data.ToString());
    }

    [Fact]
    public void Ctor_NoMediaType_Roundtrips()
    {
        DataContent content;

        content = new DataContent("data:image/png;base64,aGVsbG8=");
        Assert.Equal("data:image/png;base64,aGVsbG8=", content.Uri);
        Assert.Equal("image/png", content.MediaType);
        Assert.Equal("aGVsbG8=", content.Base64Data.ToString());

        content = new DataContent(new Uri("data:image/png;base64,aGVsbG8="));
        Assert.Equal("data:image/png;base64,aGVsbG8=", content.Uri);
        Assert.Equal("image/png", content.MediaType);
        Assert.Equal("aGVsbG8=", content.Base64Data.ToString());
    }

    [Theory]
    [InlineData("data:,hello", "hello")]
    [InlineData("data:;base64,aGVsbG8=", "hello")]
    [InlineData("data:,hello%20world", "hello world")]
    [InlineData("data:,", "")]
    [InlineData("data:;base64,", "")]
    public void Ctor_OmittedMediaType_DefaultsToTextPlain(string uri, string expectedData)
    {
        // Per RFC 2397, if the media type is omitted, it defaults to "text/plain;charset=US-ASCII"
        static void Validate(DataContent content, string expectedData)
        {
            Assert.Equal("text/plain;charset=US-ASCII", content.MediaType);
            Assert.Equal(expectedData, Encoding.UTF8.GetString(content.Data.ToArray()));
        }

        Validate(new DataContent(uri), expectedData);
        Validate(new DataContent(new Uri(uri)), expectedData);
    }

    [Theory]
    [InlineData("data:,hello", "application/json")]
    [InlineData("data:;base64,aGVsbG8=", "application/octet-stream")]
    public void Ctor_OmittedMediaType_CanBeOverridden(string uri, string mediaType)
    {
        // When media type is omitted in the URI but provided as a parameter, the parameter takes precedence
        var content = new DataContent(uri, mediaType);
        Assert.Equal(mediaType, content.MediaType);
    }

    [Fact]
    public void Serialize_MatchesExpectedJson()
    {
        Assert.Equal(
            """{"uri":"data:application/octet-stream;base64,AQIDBA=="}""",
            JsonSerializer.Serialize(
                new DataContent(uri: "data:application/octet-stream;base64,AQIDBA=="),
                TestJsonSerializerContext.Default.Options));

        Assert.Equal(
            """{"uri":"data:application/octet-stream;base64,AQIDBA=="}""",
            JsonSerializer.Serialize(
                new DataContent(new ReadOnlyMemory<byte>([0x01, 0x02, 0x03, 0x04]), "application/octet-stream"),
                TestJsonSerializerContext.Default.Options));

        Assert.Equal(
            """{"uri":"data:application/octet-stream;base64,AQIDBA==","name":"test.bin"}""",
            JsonSerializer.Serialize(
                new DataContent(new ReadOnlyMemory<byte>([0x01, 0x02, 0x03, 0x04]), "application/octet-stream") { Name = "test.bin" },
                TestJsonSerializerContext.Default.Options));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "mediaType":"text/plain" }""")]
    public void Deserialize_MissingUriString_Throws(string json)
    {
        Assert.Throws<ArgumentNullException>("uri", () => JsonSerializer.Deserialize<DataContent>(json, TestJsonSerializerContext.Default.Options)!);
    }

    [Fact]
    public void Deserialize_MatchesExpectedData()
    {
        // Data + MimeType only
        var content = JsonSerializer.Deserialize<DataContent>("""{"uri":"data:application/octet-stream;base64,AQIDBA=="}""", TestJsonSerializerContext.Default.Options)!;

        Assert.Equal("data:application/octet-stream;base64,AQIDBA==", content.Uri);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], content.Data.ToArray());
        Assert.Equal("AQIDBA==", content.Base64Data.ToString());
        Assert.Equal("application/octet-stream", content.MediaType);

        // Uri referenced content-only
        content = JsonSerializer.Deserialize<DataContent>("""{"uri":"data:application/octet-stream;base64,AQIDBA=="}""", TestJsonSerializerContext.Default.Options)!;

        Assert.Equal("data:application/octet-stream;base64,AQIDBA==", content.Uri);
        Assert.Equal("application/octet-stream", content.MediaType);

        // Using extra metadata
        content = JsonSerializer.Deserialize<DataContent>("""
            {
                "uri": "data:audio/wav;base64,AQIDBA==",
                "modelId": "gpt-4",
                "additionalProperties":
                {
                    "key": "value"
                }
            }
        """, TestJsonSerializerContext.Default.Options)!;

        Assert.Equal("data:audio/wav;base64,AQIDBA==", content.Uri);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], content.Data.ToArray());
        Assert.Equal("AQIDBA==", content.Base64Data.ToString());
        Assert.Equal("audio/wav", content.MediaType);
        Assert.Equal("value", content.AdditionalProperties!["key"]!.ToString());
    }

    [Theory]
    [InlineData(
        """{"uri": "data:text/plain;base64,AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="}""",
        """{"uri":"data:text/plain;base64,AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="}""")]
    [InlineData( // Does not support non-readable content
        """{"uri": "data:text/plain;base64,AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", "unexpected": true}""",
        """{"uri":"data:text/plain;base64,AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="}""")]
    public void Serialize_Deserialize_Roundtrips(string serialized, string expectedToString)
    {
        var content = JsonSerializer.Deserialize<DataContent>(serialized, TestJsonSerializerContext.Default.Options)!;
        var reSerialization = JsonSerializer.Serialize(content, TestJsonSerializerContext.Default.Options);
        Assert.Equal(expectedToString, reSerialization);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf")]
    [InlineData("application/xml")]
    [InlineData("audio/mpeg")]
    [InlineData("audio/ogg")]
    [InlineData("audio/wav")]
    [InlineData("image/apng")]
    [InlineData("image/avif")]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/svg+xml")]
    [InlineData("image/tiff")]
    [InlineData("image/webp")]
    [InlineData("text/css")]
    [InlineData("text/csv")]
    [InlineData("text/html")]
    [InlineData("text/javascript")]
    [InlineData("text/plain")]
    [InlineData("text/plain;charset=UTF-8")]
    [InlineData("text/xml")]
    [InlineData("custom/mediatypethatdoesntexists")]
    public void MediaType_Roundtrips(string mediaType)
    {
        DataContent c = new("data:,", mediaType);
        Assert.Equal(mediaType, c.MediaType);
    }

    [Theory]
    [InlineData("image/gif", "image")]
    [InlineData("IMAGE/JPEG", "image")]
    [InlineData("image/vnd.microsoft.icon", "imAge")]
    [InlineData("image/svg+xml", "IMAGE")]
    [InlineData("image/nonexistentimagemimetype", "IMAGE")]
    [InlineData("audio/mpeg", "aUdIo")]
    public void HasMediaTypePrefix_ReturnsTrue(string mediaType, string prefix)
    {
        var content = new DataContent("data:application/octet-stream;base64,AQIDBA==", mediaType);
        Assert.True(content.HasTopLevelMediaType(prefix));
    }

    [Theory]
    [InlineData("audio/mpeg", "audio/")]
    [InlineData("audio/mpeg", "image")]
    [InlineData("audio/mpeg", "audio/mpeg")]
    [InlineData("text/css", "text/csv")]
    [InlineData("text/css", "/csv")]
    [InlineData("application/json", "application/json!")]
    public void HasMediaTypePrefix_ReturnsFalse(string mediaType, string prefix)
    {
        var content = new DataContent("data:application/octet-stream;base64,AQIDBA==", mediaType);
        Assert.False(content.HasTopLevelMediaType(prefix));
    }

    [Fact]
    public void Data_Roundtrips()
    {
        Random rand = new(42);
        for (int length = 0; length < 100; length++)
        {
            byte[] data = new byte[length];
            rand.NextBytes(data);

            var content = new DataContent(data, "application/octet-stream");
            Assert.Equal(data, content.Data.ToArray());
            Assert.Equal(Convert.ToBase64String(data), content.Base64Data.ToString());
            Assert.Equal($"data:application/octet-stream;base64,{Convert.ToBase64String(data)}", content.Uri);
        }
    }

    [Fact]
    public void NonBase64Data_Normalized()
    {
        var content = new DataContent("data:text/plain,hello world");
        Assert.Equal("data:text/plain;base64,aGVsbG8gd29ybGQ=", content.Uri);
        Assert.Equal("aGVsbG8gd29ybGQ=", content.Base64Data.ToString());
        Assert.Equal("hello world", Encoding.ASCII.GetString(content.Data.ToArray()));
    }

    [Theory]
    [InlineData("data:text/plain,1+1=2", new byte[] { 0x31, 0x2B, 0x31, 0x3D, 0x32 })]
    [InlineData("data:application/octet-stream,%FF%00%80", new byte[] { 0xFF, 0x00, 0x80 })]
    [InlineData("data:text/plain;charset=iso-8859-1,caf%E9", new byte[] { 0x63, 0x61, 0x66, 0xE9 })]
    public void NonBase64Data_PercentDecodedToOctets(string uri, byte[] expectedData)
    {
        // Per RFC 2397, each %xx escape is the octet it names, and '+' is a literal character
        Assert.Equal(expectedData, new DataContent(uri).Data.ToArray());
        Assert.Equal(expectedData, new DataContent(new Uri(uri)).Data.ToArray());
    }

    [Fact]
    public void FileName_Roundtrips()
    {
        DataContent content = new(new byte[] { 1, 2, 3 }, "application/octet-stream");
        Assert.Null(content.Name);
        content.Name = "test.bin";
        Assert.Equal("test.bin", content.Name);
    }

    [Fact]
    public async Task LoadFromAsync_Path_InfersMediaTypeAndName()
    {
        // Create a temporary file with known content
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        try
        {
            byte[] testData = Encoding.UTF8.GetBytes("{\"key\": \"value\"}");
            await File.WriteAllBytesAsync(tempPath, testData);

            // Load from path
            DataContent content = await DataContent.LoadFromAsync(tempPath);

            // Verify the content
            Assert.Equal("application/json", content.MediaType);
            Assert.Equal(Path.GetFileName(tempPath), content.Name);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Path_UsesProvidedMediaType()
    {
        // Create a temporary file with known content
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.bin");
        try
        {
            byte[] testData = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(tempPath, testData);

            // Load from path with specified media type
            DataContent content = await DataContent.LoadFromAsync(tempPath, "custom/type");

            // Verify the content uses the provided media type, not inferred
            Assert.Equal("custom/type", content.MediaType);
            Assert.Equal(Path.GetFileName(tempPath), content.Name);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Path_FallsBackToOctetStream()
    {
        // Create a temporary file with unknown extension
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.unknownextension");
        try
        {
            byte[] testData = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(tempPath, testData);

            // Load from path
            DataContent content = await DataContent.LoadFromAsync(tempPath);

            // Verify the content falls back to octet-stream
            Assert.Equal("application/octet-stream", content.MediaType);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Stream_InfersFromFileStream()
    {
        // Create a temporary file
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.png");
        try
        {
            byte[] testData = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }; // PNG signature
            await File.WriteAllBytesAsync(tempPath, testData);

            // Load from FileStream
            using FileStream fs = new(tempPath, FileMode.Open, FileAccess.Read);
            DataContent content = await DataContent.LoadFromAsync(fs);

            // Verify inference from FileStream path
            Assert.Equal("image/png", content.MediaType);
            Assert.Equal(Path.GetFileName(tempPath), content.Name);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Stream_UsesProvidedMediaType()
    {
        // Create a MemoryStream with test data
        byte[] testData = [1, 2, 3, 4];
        using MemoryStream ms = new(testData);

        // Load from stream with explicit media type
        DataContent content = await DataContent.LoadFromAsync(ms, "video/mp4");

        // Verify the explicit media type is used
        Assert.Equal("video/mp4", content.MediaType);
        Assert.Null(content.Name); // Name can be assigned after the call if needed
        Assert.Equal(testData, content.Data.ToArray());
    }

    [Fact]
    public async Task LoadFromAsync_Stream_FallsBackToOctetStream()
    {
        // Create a MemoryStream with test data (non-FileStream, no inference possible)
        byte[] testData = new byte[] { 1, 2, 3 };
        using MemoryStream ms = new(testData);

        // Load from stream without media type or name
        DataContent content = await DataContent.LoadFromAsync(ms);

        // Verify fallback to octet-stream
        Assert.Equal("application/octet-stream", content.MediaType);
        Assert.Null(content.Name);
        Assert.Equal(testData, content.Data.ToArray());
    }

    [Fact]
    public async Task SaveToAsync_WritesDataToFile()
    {
        // Create DataContent with known data
        byte[] testData = new byte[] { 1, 2, 3, 4, 5 };
        DataContent content = new(testData, "application/octet-stream");

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.bin");
        try
        {
            // Save to path
            string actualPath = await content.SaveToAsync(tempPath);

            // Verify data was written
            Assert.Equal(tempPath, actualPath);
            Assert.True(File.Exists(actualPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(actualPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_InfersExtension_WhenPathHasNoExtension()
    {
        // Create DataContent with JSON media type
        byte[] testData = Encoding.UTF8.GetBytes("{}");
        DataContent content = new(testData, "application/json");

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        try
        {
            // Save to path without extension
            string actualPath = await content.SaveToAsync(tempPath);

            // Verify extension was inferred
            Assert.Equal(tempPath, actualPath);
            Assert.True(File.Exists(actualPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(actualPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_UsesDirectoryAndInfersNameFromNameProperty()
    {
        // Create DataContent with JSON media type and a name
        byte[] testData = Encoding.UTF8.GetBytes("{}");
        DataContent content = new(testData, "application/json")
        {
            Name = "myfile.json"
        };

        string tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string expectedPath = Path.Combine(tempDir, "myfile.json");
        try
        {
            // Save to directory path
            string actualPath = await content.SaveToAsync(tempDir);

            // Verify the name was used
            Assert.Equal(expectedPath, actualPath);
            Assert.True(File.Exists(actualPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(actualPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_UsesRandomNameWhenDirectoryProvidedAndNoName()
    {
        // Create DataContent with JSON media type but no name
        byte[] testData = Encoding.UTF8.GetBytes("{}");
        DataContent content = new(testData, "application/json");

        string tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Save to directory path
            string actualPath = await content.SaveToAsync(tempDir);

            // Verify a file was created with .json extension
            Assert.StartsWith(tempDir, actualPath);
            Assert.EndsWith(".json", actualPath);
            Assert.True(File.Exists(actualPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(actualPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_DoesNotInferExtension_WhenPathAlreadyHasExtension()
    {
        // Create DataContent with JSON media type
        byte[] testData = Encoding.UTF8.GetBytes("{}");
        DataContent content = new(testData, "application/json");

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        try
        {
            // Save to path that already has an extension
            string actualPath = await content.SaveToAsync(tempPath);

            // Verify the original extension was preserved, not replaced
            Assert.Equal(tempPath, actualPath);
            Assert.True(File.Exists(actualPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_WithJustFilename_SavesInCurrentDirectory()
    {
        // Test that providing just a filename (no directory) saves to current directory
        byte[] testData = [1, 2, 3];
        DataContent content = new(testData, "application/json");

        string filename = $"test_{Guid.NewGuid()}.json";
        string cwdBefore = Directory.GetCurrentDirectory();
        string expectedAbsolute = Path.Combine(cwdBefore, filename);
        string? savedPath = null;

        try
        {
            savedPath = await content.SaveToAsync(filename);

            // The returned path should be in the current directory
            Assert.Equal(filename, Path.GetFileName(savedPath));
            Assert.True(File.Exists(expectedAbsolute));
            Assert.Equal(testData, await File.ReadAllBytesAsync(expectedAbsolute));
        }
        finally
        {
            if (File.Exists(expectedAbsolute))
            {
                File.Delete(expectedAbsolute);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_WithRelativeDirectory_UsesNameFromContent()
    {
        // Test that providing a relative directory path uses name from content
        byte[] testData = [4, 5, 6];
        DataContent content = new(testData, "image/png") { Name = "myimage.png" };

        string relativeDir = "subdir_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(relativeDir);

        try
        {
            string savedPath = await content.SaveToAsync(relativeDir);

            // The returned path should be in the relative directory using content's name
            Assert.Equal(content.Name, Path.GetFileName(savedPath));
            Assert.Contains(relativeDir, savedPath);
            Assert.True(File.Exists(savedPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            if (Directory.Exists(relativeDir))
            {
                Directory.Delete(relativeDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_WithEmptyPath_UsesCurrentDirectoryAndContentName()
    {
        // Test that providing an empty string path uses current directory with name from content
        byte[] testData = [7, 8, 9];
        DataContent content = new(testData, "text/plain") { Name = $"testfile_{Guid.NewGuid()}.txt" };

        string cwdBefore = Directory.GetCurrentDirectory();
        string expectedAbsolute = Path.Combine(cwdBefore, content.Name!);
        string? savedPath = null;

        try
        {
            savedPath = await content.SaveToAsync(string.Empty);

            // The returned path should be in the current directory using content's name
            Assert.Equal(content.Name, Path.GetFileName(savedPath));
            Assert.True(File.Exists(expectedAbsolute));
            Assert.Equal(testData, await File.ReadAllBytesAsync(expectedAbsolute));
        }
        finally
        {
            if (File.Exists(expectedAbsolute))
            {
                File.Delete(expectedAbsolute);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Path_ThrowsOnNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("path", async () => await DataContent.LoadFromAsync((string)null!));
    }

    [Fact]
    public async Task LoadFromAsync_Path_ThrowsOnEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>("path", async () => await DataContent.LoadFromAsync(string.Empty));
    }

    [Fact]
    public async Task LoadFromAsync_Stream_ThrowsOnNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("stream", async () => await DataContent.LoadFromAsync((Stream)null!));
    }

    [Fact]
    public async Task SaveToAsync_ThrowsOnNull()
    {
        DataContent content = new(new byte[] { 1 }, "application/octet-stream");
        await Assert.ThrowsAsync<ArgumentNullException>("path", async () => await content.SaveToAsync(null!));
    }

    [Fact]
    public async Task LoadFromAsync_ExtractsFilenameFromPath()
    {
        // Test that LoadFromAsync properly extracts the filename from a nested path
        string tempDir = Path.Combine(Path.GetTempPath(), $"test_subdir_{Guid.NewGuid()}");
        string subDir = Path.Combine(tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        try
        {
            string filePath = Path.Combine(subDir, "nested.html");
            byte[] testData = Encoding.UTF8.GetBytes("<html></html>");
            await File.WriteAllBytesAsync(filePath, testData);

            DataContent content = await DataContent.LoadFromAsync(filePath);

            Assert.Equal("text/html", content.MediaType);
            Assert.Equal("nested.html", content.Name);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_DirectoryPath_WithoutName_GeneratesRandomName()
    {
        byte[] testData = [5, 6, 7];
        DataContent content = new(testData, "text/css");

        // Note: Name is NOT set

        string tempDir = Path.Combine(Path.GetTempPath(), $"test_dir_noname_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string savedPath = await content.SaveToAsync(tempDir);

            // Should generate a random GUID-based name with .css extension
            Assert.StartsWith(tempDir, savedPath);
            Assert.EndsWith(".css", savedPath);
            Assert.True(File.Exists(savedPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_WithCancellationToken_PropagatesToken()
    {
        // This test verifies that the cancellation token is properly propagated.
        // With already-cancelled tokens, TaskCanceledException (a subclass of OperationCanceledException) may be thrown.
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_cancel_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllBytesAsync(tempPath, new byte[] { 1, 2, 3 });

            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DataContent.LoadFromAsync(tempPath, cancellationToken: cts.Token));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_WithCancellationToken_PropagatesToken()
    {
        // This test verifies that the cancellation token is properly propagated.
        // With already-cancelled tokens, TaskCanceledException (a subclass of OperationCanceledException) may be thrown.
        DataContent content = new(new byte[] { 1, 2, 3 }, "application/octet-stream");

        using CancellationTokenSource cts = new();
        cts.Cancel();

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_save_cancel_{Guid.NewGuid()}.bin");

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await content.SaveToAsync(tempPath, cts.Token));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_Stream_WithCancellationToken_PropagatesToken()
    {
        // This test verifies that the cancellation token is properly propagated.
        // With already-cancelled tokens, TaskCanceledException (a subclass of OperationCanceledException) may be thrown.
        byte[] testData = [1, 2, 3];
        using MemoryStream ms = new(testData);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DataContent.LoadFromAsync(ms, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SaveToAsync_ExistingDirectory_WithName_UsesNameAsFilename()
    {
        byte[] testData = [1, 2, 3];
        DataContent content = new(testData, "application/json")
        {
            Name = "specific-output.json"
        };

        string tempDir = Path.Combine(Path.GetTempPath(), $"test_dir_name_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string expectedPath = Path.Combine(tempDir, content.Name);

        try
        {
            string savedPath = await content.SaveToAsync(tempDir);

            Assert.Equal(expectedPath, savedPath);
            Assert.True(File.Exists(expectedPath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(expectedPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_NonExistentPath_TreatedAsFilePath()
    {
        // When the path doesn't exist, it's treated as a file path, not a directory
        byte[] testData = [1, 2, 3];
        DataContent content = new(testData, "application/json");

        string tempDir = Path.Combine(Path.GetTempPath(), $"test_nonexist_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "newfile");

        try
        {
            string savedPath = await content.SaveToAsync(filePath);

            // Since the path doesn't exist as a directory, it's used as the file path directly
            Assert.Equal(filePath, savedPath);
            Assert.True(File.Exists(filePath));
            Assert.Equal(testData, await File.ReadAllBytesAsync(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task LoadFromAsync_LargeFile_LoadsCorrectly()
    {
        // Test with a larger file to ensure streaming works properly
        byte[] testData = new byte[100_000];
        new Random(42).NextBytes(testData);

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_large_{Guid.NewGuid()}.bin");

        try
        {
            await File.WriteAllBytesAsync(tempPath, testData);

            DataContent content = await DataContent.LoadFromAsync(tempPath);

            Assert.Equal("application/octet-stream", content.MediaType);
            Assert.Equal(testData, content.Data.ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_LargeContent_SavesCorrectly()
    {
        // Test with larger content to ensure streaming works properly
        byte[] testData = new byte[100_000];
        new Random(42).NextBytes(testData);

        DataContent content = new(testData, "application/octet-stream");

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_save_large_{Guid.NewGuid()}.bin");

        try
        {
            string savedPath = await content.SaveToAsync(tempPath);

            Assert.Equal(tempPath, savedPath);
            Assert.Equal(testData, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveToAsync_ExistingFile_ThrowsAndPreservesOriginalContent()
    {
        // Create a file with original content
        byte[] originalContent = [10, 20, 30, 40, 50];
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_existing_{Guid.NewGuid()}.bin");

        try
        {
            await File.WriteAllBytesAsync(tempPath, originalContent);

            // Try to save different content to the same path
            byte[] newContent = [1, 2, 3];
            DataContent content = new(newContent, "application/octet-stream");

            // SaveToAsync should throw because the file already exists (using FileMode.CreateNew)
            await Assert.ThrowsAsync<IOException>(async () => await content.SaveToAsync(tempPath));

            // Verify the original file was not corrupted
            Assert.True(File.Exists(tempPath));
            byte[] actualContent = await File.ReadAllBytesAsync(tempPath);
            Assert.Equal(originalContent, actualContent);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "data",
              "uri": "data:audio/wav;base64,AQIDBA==",
              "name": "audio.wav",
              "additionalProperties": {
                "source": "microphone"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var dataContent = Assert.IsType<DataContent>(result);
        Assert.Equal("data:audio/wav;base64,AQIDBA==", dataContent.Uri);
        Assert.Equal("audio/wav", dataContent.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, dataContent.Data.ToArray());
        Assert.Equal("audio.wav", dataContent.Name);
        Assert.NotNull(dataContent.AdditionalProperties);
        Assert.Equal("microphone", dataContent.AdditionalProperties["source"]?.ToString());
    }
}
