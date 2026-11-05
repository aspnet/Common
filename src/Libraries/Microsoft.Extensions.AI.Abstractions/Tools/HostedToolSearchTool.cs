// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a hosted tool that can be specified to an AI service to enable it to search for and selectively load tool definitions on demand.
/// </summary>
/// <remarks>
/// <para>
/// This tool does not itself implement tool search. It is a marker that can be used to inform a service
/// that tool search should be enabled. When included, deferred tools are not placed into the model's context
/// upfront; instead, the model invokes tool search to surface relevant tools on demand, reducing the input
/// tokens consumed by tool definitions the model doesn't need.
/// </para>
/// <para>
/// By default, when a <see cref="HostedToolSearchTool"/> is present in the tools list, all other deferrable tools
/// are treated as having deferred loading enabled. Use <see cref="DeferredTools"/> to control which tools have deferred loading
/// on a per-tool basis.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIToolSearch, UrlFormat = DiagnosticIds.UrlFormat)]
public class HostedToolSearchTool : AITool
{
    /// <summary>Any additional properties associated with the tool.</summary>
    private IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>Initializes a new instance of the <see cref="HostedToolSearchTool"/> class.</summary>
    public HostedToolSearchTool()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HostedToolSearchTool"/> class.</summary>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    public HostedToolSearchTool(IReadOnlyDictionary<string, object?>? additionalProperties)
    {
        _additionalProperties = additionalProperties;
    }

    /// <inheritdoc />
    public override string Name => "tool_search";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties ?? base.AdditionalProperties;

    /// <summary>
    /// Gets or sets the list of tool names for which deferred loading should be enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default value is <see langword="null"/>, which enables deferred loading for all deferrable tools in the tools list.
    /// </para>
    /// <para>
    /// When non-null, only deferrable tools whose names appear in this list will have deferred loading enabled.
    /// </para>
    /// </remarks>
    public IList<string>? DeferredTools { get; set; }

    /// <summary>
    /// Gets or sets the namespace name under which deferred tools should be grouped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When non-null, all deferred tools are wrapped inside a <c>{"type":"namespace","name":"..."}</c>
    /// container. Non-deferred tools remain as top-level tools.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default), deferred tools are sent as top-level tools
    /// with <c>defer_loading</c> set individually.
    /// </para>
    /// <para>
    /// Use <see cref="NamespaceDescription"/> to supply a description for the namespace.
    /// </para>
    /// </remarks>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the description for the namespace produced when <see cref="Namespace"/> is specified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting this property alone does not create a namespace.
    /// </para>
    /// <para>
    /// When <see langword="null"/>, no description is emitted on the namespace. The underlying provider
    /// may require a description when a namespace is supplied.
    /// </para>
    /// </remarks>
    public string? NamespaceDescription { get; set; }
}
