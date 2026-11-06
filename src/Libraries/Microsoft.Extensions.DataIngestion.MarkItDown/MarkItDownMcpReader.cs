// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Reads documents by converting them to Markdown using the <see href="https://github.com/microsoft/markitdown">MarkItDown</see> MCP server.
/// </summary>
public class MarkItDownMcpReader : IngestionDocumentReader
{
    private readonly Uri _mcpServerUri;
    private readonly McpClientOptions? _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkItDownMcpReader"/> class.
    /// </summary>
    /// <param name="mcpServerUri">The URI of the MarkItDown MCP server (e.g., http://localhost:3001/mcp).</param>
    /// <param name="options">Optional MCP client options for configuring the connection.</param>
    public MarkItDownMcpReader(Uri mcpServerUri, McpClientOptions? options = null)
    {
        _mcpServerUri = Throw.IfNull(mcpServerUri);
        _options = options;
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        if (!source.Exists)
        {
            throw new FileNotFoundException("The specified file does not exist.", source.FullName);
        }

        // Read file content and create DataContent (media type is inferred from extension if not provided)
        DataContent dataContent = await DataContent.LoadFromAsync(source.FullName, mediaType, cancellationToken).ConfigureAwait(false);

        string markdown = await ConvertToMarkdownAsync(dataContent, cancellationToken).ConfigureAwait(false);

        return MarkdownParser.Parse(markdown, identifier);
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        // Read stream content and create DataContent (media type is inferred from FileStream path if applicable)
        DataContent dataContent = await DataContent.LoadFromAsync(source, mediaType, cancellationToken).ConfigureAwait(false);

        string markdown = await ConvertToMarkdownAsync(dataContent, cancellationToken).ConfigureAwait(false);

        return MarkdownParser.Parse(markdown, identifier);
    }

    private async Task<string> ConvertToMarkdownAsync(DataContent dataContent, CancellationToken cancellationToken)
    {
        // Create HTTP client transport for MCP
        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = _mcpServerUri
        });

        await using (transport.ConfigureAwait(false))
        {
            // Create MCP client
            McpClient client = await McpClient.CreateAsync(transport, _options, cancellationToken: cancellationToken).ConfigureAwait(false);

            await using (client.ConfigureAwait(false))
            {
                // Build parameters for convert_to_markdown tool
                Dictionary<string, object?> parameters = new()
                {
                    ["uri"] = dataContent.Uri
                };

                // Call the convert_to_markdown tool
                var result = await client.CallToolAsync("convert_to_markdown", parameters, cancellationToken: cancellationToken).ConfigureAwait(false);

                // FIX: Check if the tool execution returned an error (handles bool?)
                if (result.IsError == true)
                {
                    // Extract the error message from the content block if available, or fall back to a default error
                    string errorText = "An unknown error occurred during document conversion.";

                    if (result.Content != null && result.Content.Count > 0)
                    {
                        foreach (var content in result.Content)
                        {
                            if (content.Type == "text" && content is TextContentBlock textBlock)
                            {
                                errorText = textBlock.Text;
                                break;
                            }
                        }
                    }

                    throw new InvalidOperationException($"Failed to convert document to markdown: {errorText}");
                }

                // Extract markdown content from result
                // The result is expected to be in the format: { "content": [{ "type": "text", "text": "markdown content" }] }
                if (result.Content != null && result.Content.Count > 0)
                {
                    foreach (var content in result.Content)
                    {
                        if (content.Type == "text" && content is TextContentBlock textBlock)
                        {
                            return textBlock.Text;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("Failed to convert document to markdown: unexpected response format from MCP server.");
    }
}
