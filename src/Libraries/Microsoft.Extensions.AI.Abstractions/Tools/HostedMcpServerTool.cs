// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a hosted MCP server tool that can be specified to an AI service.
/// </summary>
public class HostedMcpServerTool : AITool
{
    /// <summary>Any additional properties associated with the tool.</summary>
    private IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerTool"/> class.
    /// </summary>
    /// <param name="serverName">The name of the remote MCP server.</param>
    /// <param name="serverAddress">The address of the remote MCP server. This may be a URL, or in the case of a service providing built-in MCP servers with known names, it can be such a name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is empty or composed entirely of whitespace.</exception>
    public HostedMcpServerTool(string serverName, string serverAddress)
    {
        ServerName = Throw.IfNullOrWhitespace(serverName);
        ServerAddress = Throw.IfNullOrWhitespace(serverAddress);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerTool"/> class.
    /// </summary>
    /// <param name="serverName">The name of the remote MCP server.</param>
    /// <param name="serverAddress">The address of the remote MCP server. This may be a URL, or in the case of a service providing built-in MCP servers with known names, it can be such a name.</param>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is empty or composed entirely of whitespace.</exception>
    public HostedMcpServerTool(string serverName, string serverAddress, IReadOnlyDictionary<string, object?>? additionalProperties)
        : this(serverName, serverAddress)
    {
        _additionalProperties = additionalProperties;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerTool"/> class.
    /// </summary>
    /// <param name="serverName">The name of the remote MCP server.</param>
    /// <param name="serverAddress">The URL of the remote MCP server.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverAddress"/> is not an absolute URL.</exception>
    public HostedMcpServerTool(string serverName, Uri serverAddress)
        : this(serverName, ValidateUrl(serverAddress))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerTool"/> class.
    /// </summary>
    /// <param name="serverName">The name of the remote MCP server.</param>
    /// <param name="serverAddress">The URL of the remote MCP server.</param>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> or <paramref name="serverAddress"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverAddress"/> is not an absolute URL.</exception>
    public HostedMcpServerTool(string serverName, Uri serverAddress, IReadOnlyDictionary<string, object?>? additionalProperties)
        : this(serverName, ValidateUrl(serverAddress))
    {
        _additionalProperties = additionalProperties;
    }

    private static string ValidateUrl(Uri serverAddress)
    {
        _ = Throw.IfNull(serverAddress);

        if (!serverAddress.IsAbsoluteUri)
        {
            Throw.ArgumentException(nameof(serverAddress), "The provided URL is not absolute.");
        }

        return serverAddress.AbsoluteUri;
    }

    /// <inheritdoc />
    public override string Name => "mcp";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties ?? base.AdditionalProperties;

    /// <summary>
    /// Gets the name of the remote MCP server that is used to identify it.
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// Gets the address of the remote MCP server.
    /// </summary>
    /// <remarks>
    /// This may be a URL. For a service providing built-in MCP servers with known names, it can instead be such a name.
    /// </remarks>
    public string ServerAddress { get; }

    /// <summary>
    /// Gets or sets the description of the remote MCP server, used to provide more context to the AI service.
    /// </summary>
    public string? ServerDescription { get; set; }

    /// <summary>
    /// Gets or sets the list of tools allowed to be used by the AI service.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="null"/>, which allows any tool to be used.
    /// </remarks>
    public IList<string>? AllowedTools { get; set; }

    /// <summary>
    /// Gets or sets the approval mode that indicates when the AI service should require user approval for tool calls to the remote MCP server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can set this property to <see cref="HostedMcpServerToolApprovalMode.AlwaysRequire"/> to require approval for all tool calls,
    /// or to <see cref="HostedMcpServerToolApprovalMode.NeverRequire"/> to never require approval.
    /// </para>
    /// <para>
    /// The default value is <see langword="null"/>, which some providers might treat the same as <see cref="HostedMcpServerToolApprovalMode.AlwaysRequire"/>.
    /// </para>
    /// <para>
    /// The underlying provider is not guaranteed to support or honor the approval mode.
    /// </para>
    /// </remarks>
    public HostedMcpServerToolApprovalMode? ApprovalMode { get; set; }

    /// <summary>
    /// Gets or sets a mutable dictionary of HTTP headers to include when calling the remote MCP server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The underlying provider is not guaranteed to support or honor the headers.
    /// </para>
    /// <para>
    /// This property is useful for specifying the authentication header or other headers required by the MCP server.
    /// </para>
    /// <para>
    /// As HTTP header names are case-insensitive, callers should use <see cref="StringComparer.OrdinalIgnoreCase"/> comparison when constructing the dictionary.
    /// </para>
    /// </remarks>
    public IDictionary<string, string>? Headers { get; set; }
}
