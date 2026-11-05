// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Nodes;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides options for configuring the behavior of <see cref="AIJsonUtilities"/> JSON schema transformation functionality.
/// </summary>
public sealed record class AIJsonSchemaTransformOptions
{
    /// <summary>
    /// Gets or initializes a callback that is invoked for every schema that is generated within the type graph.
    /// </summary>
    public Func<AIJsonSchemaTransformContext, JsonNode, JsonNode>? TransformSchemaNode { get; init; }

    /// <summary>
    /// Gets a value indicating whether to convert boolean schemas to equivalent object-based representations.
    /// </summary>
    public bool ConvertBooleanSchemas { get; init; }

    /// <summary>
    /// Gets a value indicating whether to generate schemas with the additionalProperties set to false for .NET objects.
    /// </summary>
    public bool DisallowAdditionalProperties { get; init; }

    /// <summary>
    /// Gets a value indicating whether to mark all properties as required in the schema.
    /// </summary>
    public bool RequireAllProperties { get; init; }

    /// <summary>
    /// Gets a value indicating whether to substitute nullable "type" keywords with OpenAPI 3.0 style "nullable" keywords in the schema.
    /// </summary>
    public bool UseNullableKeyword { get; init; }

    /// <summary>
    /// Gets a value indicating whether to move the default keyword to the description field in the schema.
    /// </summary>
    public bool MoveDefaultKeywordToDescription { get; init; }

    /// <summary>
    /// Gets the default options instance.
    /// </summary>
    internal static AIJsonSchemaTransformOptions Default { get; } = new();
}
