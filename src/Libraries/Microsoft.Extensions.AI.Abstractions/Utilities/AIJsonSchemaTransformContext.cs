// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable CA1815 // Override equals and operator equals on value types

using System;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Defines the context for transforming a schema node withing a larger schema document.
/// </summary>
/// <remarks>
/// This struct is being passed to the user-provided <see cref="AIJsonSchemaTransformOptions.TransformSchemaNode"/>
/// callback by the <see cref="AIJsonUtilities.CreateJsonSchema"/> method and cannot be instantiated directly.
/// </remarks>
public readonly struct AIJsonSchemaTransformContext
{
    private readonly string[] _path;

    internal AIJsonSchemaTransformContext(string[] path)
    {
        _path = path;
    }

    /// <summary>
    /// Gets the path to the schema document currently being generated.
    /// </summary>
    public ReadOnlySpan<string> Path => _path;

    /// <summary>
    /// Gets the containing property name if the current schema is a property of an object.
    /// </summary>
    public string? PropertyName => Path is [.., "properties", string name] ? name : null;

    /// <summary>
    /// Gets a value indicating whether the current schema is a collection element.
    /// </summary>
    public bool IsCollectionElementSchema => Path is [.., "items"];

    /// <summary>
    /// Gets a value indicating whether the current schema is a dictionary value.
    /// </summary>
    public bool IsDictionaryValueSchema => Path is [.., "additionalProperties"];
}
