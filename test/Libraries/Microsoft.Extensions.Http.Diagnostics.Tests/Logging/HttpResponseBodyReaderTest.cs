// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Http.Logging.Internal;
using Microsoft.Extensions.Http.Logging.Test.Internal;
using Microsoft.Shared.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpResponseBodyReaderTest
{
    private const string TextPlain = "text/plain";
    private readonly Fixture _fixture;

    public HttpResponseBodyReaderTest()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void Reader_NullOptions_Throws()
    {
        var act = () => new HttpResponseBodyReader(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Reader_SimpleContent_ReadsContent()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var expectedContentBody = _fixture.Create<string>();
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StringContent(expectedContentBody, Encoding.UTF8, TextPlain)
        };

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().BeEquivalentTo(expectedContentBody);
    }

    [Fact]
    public async Task Reader_NoContentType_ErrorMessage()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(new MemoryStream())
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().Be(Constants.NoContent);
    }

    [Fact]
    public async Task Reader_EmptyContent_ReturnsEmptyString()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, TextPlain)
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().BeEmpty();
    }

    [Theory]
    [CombinatorialData]
    public async Task Reader_UnreadableContent_ErrorMessage(
        [CombinatorialValues("application/octet-stream", "image/png", "audio/ogg", "application/x-www-form-urlencoded",
            "application/javascript")]
        string contentType)
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var expectedContentBody = _fixture.Create<string>();
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StringContent(expectedContentBody, Encoding.UTF8, contentType)
        };

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().Be(Constants.UnreadableContent);
    }

    [Fact]
    public async Task Reader_OperationCanceled_ThrowsTaskCanceledException()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var input = _fixture.Create<string>();
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StringContent(input, Encoding.UTF8, TextPlain)
        };

        var token = new CancellationToken(true);

        var act = async () => await httpResponseBodyReader.ReadAsync(httpResponse, token);

        await act.Should().ThrowAsync<TaskCanceledException>().Where(e => e.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [CombinatorialData]
    public async Task Reader_BigContent_TrimsAtTheEnd([CombinatorialValues(32, 256, 4095, 4096, 4097, 65536, 131072)] int limit)
    {
        var options = new LoggingOptions
        {
            BodySizeLimit = limit,
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var bigContent = RandomStringGenerator.Generate(limit * 2);
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(new NotSeekableStream(new(Encoding.UTF8.GetBytes(bigContent))))
        };
        httpResponse.Content.Headers.Add("Content-Type", TextPlain);

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().Be(bigContent.Substring(0, limit));

        // This should read from piped stream
        var response = await httpResponse.Content.ReadAsStringAsync();

        response.Should().Be(bigContent);
    }

    [Fact]
    public async Task Reader_ReaderCancelledAfterBuffering_ShouldCancelPipeReader()
    {
        const int BodySize = 10_000_000;
        var options = new LoggingOptions
        {
            BodySizeLimit = 1,
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };
        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var bigContent = RandomStringGenerator.Generate(BodySize);
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(new NotSeekableStream(new(Encoding.UTF8.GetBytes(bigContent))))
        };
        httpResponse.Content.Headers.Add("Content-Type", TextPlain);

        using var cts = new CancellationTokenSource();

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, cts.Token);

        responseBody.Should().HaveLength(1);

        // This should read from piped stream
        var responseStream = await httpResponse.Content.ReadAsStreamAsync();

        var buffer = new byte[BodySize];

        cts.Cancel(false);

        var act = async () => await responseStream.ReadAsync(buffer, 0, BodySize, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().Where(e => e.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Reader_ReadingTakesTooLong_TimesOut()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            BodyReadTimeout = TimeSpan.Zero
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var streamMock = new Mock<Stream>();
#if NET6_0_OR_GREATER
        streamMock.Setup(x => x.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();
#else
        streamMock.Setup(x => x.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();
#endif
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(streamMock.Object)
        };

        httpResponse.Content.Headers.Add("Content-type", TextPlain);

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().Be(Constants.ReadCancelledByTimeout);
    }

    [Fact]
    public async Task Reader_ReadingTakesTooLongAndOperationCancelled_Throws()
    {
        var options = new LoggingOptions
        {
            ResponseBodyContentTypes = new HashSet<string> { TextPlain },
            BodyReadTimeout = TimeSpan.Zero
        };
        var httpResponseBodyReader = new HttpResponseBodyReader(options);
        var streamMock = new Mock<Stream>();
        var token = new CancellationToken(true);
        var exception = new OperationCanceledException(token);
#if NET6_0_OR_GREATER
        streamMock.Setup(x => x.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>())).Throws(exception);
#else
        streamMock.Setup(x => x.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Throws(exception);
#endif
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(streamMock.Object)
        };
        httpResponse.Content.Headers.Add("Content-type", TextPlain);

        var act = async () => await httpResponseBodyReader.ReadAsync(httpResponse, token);

        await act.Should().ThrowAsync<OperationCanceledException>().Where(e => e.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void HttpResponseBodyReader_Has_Infinite_Timeout_For_Reading_A_Body_When_Debugger_Is_Attached()
    {
        var options = new LoggingOptions();
        var reader = new HttpResponseBodyReader(options, DebuggerState.Attached);

        Assert.Equal(reader.ResponseReadTimeout, Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void HttpResponseBodyReader_Has_Option_Defined_Timeout_For_Reading_A_Body_When_Debugger_Is_Detached()
    {
        var timeout = TimeSpan.FromSeconds(274);
        var options = new LoggingOptions { BodyReadTimeout = timeout };
        var reader = new HttpResponseBodyReader(options, DebuggerState.Detached);

        Assert.Equal(reader.ResponseReadTimeout, timeout);
    }

    [Theory]
    [CombinatorialData]
    public async Task Reader_DebuggerAttached_ReadsBodyAndLeavesContentReadable(bool seekable, bool bufferContent)
    {
        var options = new LoggingOptions
        {
            BodySizeLimit = 32,
            ResponseBodyContentTypes = new HashSet<string> { TextPlain }
        };

        var httpResponseBodyReader = new HttpResponseBodyReader(options, DebuggerState.Attached);
        var content = RandomStringGenerator.Generate(options.BodySizeLimit * 2);
        var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var httpResponse = new HttpResponseMessage
        {
            Content = new StreamContent(seekable ? memoryStream : new NotSeekableStream(memoryStream))
        };
        httpResponse.Content.Headers.Add("Content-Type", TextPlain);

        var responseBody = await httpResponseBodyReader.ReadAsync(httpResponse, CancellationToken.None);

        responseBody.Should().Be(content.Substring(0, options.BodySizeLimit));

        if (bufferContent)
        {
            // This is what HttpClient does once the handler pipeline is done when
            // HttpCompletionOption.ResponseContentRead is used, i.e. for GetAsync/PostAsJsonAsync.
            await httpResponse.Content.LoadIntoBufferAsync();
        }

        // The caller must still be able to read the whole body.
        var response = await httpResponse.Content.ReadAsStringAsync();

        response.Should().Be(content);
    }
}
