// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable S3996 // URI properties should not be strings

/// <summary>
/// An <see cref="HttpMessageHandler"/> that checks the multi-part request body as a root
/// JSON structure of properties and sends back an expected JSON response.
/// </summary>
/// <remarks>
/// The order of the properties does not affect the comparison.
/// <para>
/// An expected input of <c>{ "name": "something" }</c> will Assert for a multipart body that has
/// a <b>name</b> field with a value of <c>something</c>.
/// </para>
/// <para>
/// An expected input of <c>{ "multiple[]": ["one","two"] }</c> will Assert for a multipart body that has
/// two <b>multiple[]</b> fields each having "one" and "two" value respectively.
/// </para>
/// </remarks>
/// <param name="expectedInput">
/// A JSON string representing the expected structure and values of the multipart request body to be verified.
/// For example, <c>{ "name": "something" }</c> or <c>{ "multiple[]": ["one","two"] }</c>.
/// </param>
/// <param name="sentJsonOutput">
/// A JSON string that will be returned as the response body when the request matches the expected input.
/// </param>
public class VerbatimMultiPartHttpHandler(string expectedInput, string sentJsonOutput) : HttpClientHandler
{
    public string? ExpectedRequestUriContains { get; init; }

    public string? ExpectedAudioFilename { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Assert.NotNull(request.Content);
        Assert.NotNull(request.Content.Headers.ContentType);
        Assert.Equal("multipart/form-data", request.Content.Headers.ContentType.MediaType);

        Assert.NotNull(request.RequestUri);
        if (!string.IsNullOrEmpty(ExpectedRequestUriContains))
        {
            Assert.Contains(ExpectedRequestUriContains!, request.RequestUri!.ToString());
        }

        Dictionary<string, object> parameters = [];

        // Extract the boundary
        string? boundary = request.Content.Headers.ContentType.Parameters
            .FirstOrDefault(p => p.Name == "boundary")?.Value;

        if (string.IsNullOrEmpty(boundary))
        {
            throw new InvalidOperationException("Boundary not found.");
        }

        string fullBoundary = $"--{boundary!.Trim('"')}";

        // Read the entire body into memory (for simplicity; stream in production for large data)
#if NET
        byte[] bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
#else
        byte[] bodyBytes = await request.Content.ReadAsByteArrayAsync();
#endif
        using var stream = new MemoryStream(bodyBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8);
#if NET

        string bodyText = await reader.ReadToEndAsync(cancellationToken);
#else
        string bodyText = await reader.ReadToEndAsync();
#endif

        // Make it legible for debugging and splitting
        bodyText = RemoveSpecialCharacters(bodyText);

        string[] parts = bodyText.Split(new string[] { fullBoundary }, StringSplitOptions.None);

        foreach (string part in parts)
        {
            if (part.Trim() == "--")
            {
                continue; // End boundary
            }

            // Parse headers and body
            int headerEnd = part.IndexOf("\r\n\r\n");
            if (headerEnd < 0)
            {
                continue;
            }

            string headers = part.Substring(0, headerEnd).Trim();
            string rawValue = part.Substring(headerEnd + 4).TrimEnd('\r', '\n');

            // Get the parameter name and value
            if (headers.Contains("name="))
            {
                // Text field
                string name = ExtractNameFromHeaders(headers);

                // For file fields, optionally check the filename
                if (name.StartsWith("file"))
                {
                    if (ExpectedAudioFilename is not null)
                    {
                        string? actualFilename = ExtractFilenameFromHeaders(headers);
                        Assert.Equal(ExpectedAudioFilename, actualFilename);
                    }
                }
                else
                {
                    if (parameters.ContainsKey(name))
                    {
                        ((List<JsonElement>)parameters[name]).Add(ParseContentToJsonElement(rawValue));
                    }
                    else
                    {
                        parameters.Add(name, new List<JsonElement> { ParseContentToJsonElement(rawValue) });
                    }
                }
            }
        }

        // Transform one value lists into single values
        foreach (var key in parameters.Keys.ToList())
        {
            if (parameters[key] is List<JsonElement> list && list.Count == 1)
            {
                parameters[key] = list[0];
            }
        }

        var jsonParameters = JsonSerializer.Serialize(parameters);
        Assert.NotNull(jsonParameters);

        AssertJsonEquals(expectedInput, jsonParameters);

        return new() { Content = new StringContent(sentJsonOutput, Encoding.UTF8, "application/json") };
    }

    private static string RemoveSpecialCharacters(string input)
    {
        return Regex.Replace(input, @"[^a-zA-Z0-9_ .,!?\r\n""=;\//\[\]-]", "");
    }

    private static JsonElement ParseContentToJsonElement(string content)
    {
        // Try parsing as a number
        if (int.TryParse(content, out int intValue))
        {
            return JsonSerializer.SerializeToElement(intValue);
        }

        if (double.TryParse(content, out double doubleValue))
        {
            return JsonSerializer.SerializeToElement(doubleValue);
        }

        // Try parsing as a boolean
        if (bool.TryParse(content, out bool boolValue))
        {
            return JsonSerializer.SerializeToElement(boolValue);
        }

        // Default to string
        return JsonSerializer.SerializeToElement(content);
    }

    private static string ExtractNameFromHeaders(string headers)
    {
        const string NamePrefix = "name=";
        int start = headers.IndexOf(NamePrefix) + NamePrefix.Length;
        int end = headers.IndexOf(";", start);

        if (end == -1)
        {
            end = headers.Length;
        }

        return headers.Substring(start, end - start).Trim('"');
    }

    private static string? ExtractFilenameFromHeaders(string headers)
    {
        const string FilenamePrefix = "filename=";
        int start = headers.IndexOf(FilenamePrefix);
        if (start < 0)
        {
            return null;
        }

        start += FilenamePrefix.Length;
        int end = headers.IndexOf(";", start);
        if (end == -1)
        {
            end = headers.Length;
        }

        return headers.Substring(start, end - start).Trim('"');
    }

    public static string? RemoveWhiteSpace(string? text) =>
        text is null ? null :
        Regex.Replace(text, @"\s*", string.Empty);

    private static Dictionary<char, int>? GetCharacterFrequencies(string text)
        => RemoveWhiteSpace(text)?.GroupBy(c => c)
               .ToDictionary(g => g.Key, g => g.Count());

    private static void AssertJsonEquals(string expected, string actual)
    {
        var expectedFrequencies = GetCharacterFrequencies(expected);
        var actualFrequencies = GetCharacterFrequencies(actual);

        Assert.NotNull(expectedFrequencies);
        Assert.NotNull(actualFrequencies);

        foreach (var kvp in expectedFrequencies)
        {
            if (!actualFrequencies.ContainsKey(kvp.Key) || kvp.Value != actualFrequencies[kvp.Key])
            {
                Assert.Fail($"Expected: {expected}, Actual: {actual}");
            }

            // Ensure the frequencies are equal during the test
            Assert.Equal(kvp.Value, actualFrequencies[kvp.Key]);
        }
    }
}
