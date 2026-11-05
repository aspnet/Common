// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>Configures one extraction without depending on provider SDKs or AI abstractions.</summary>
/// <remarks>
/// Providers apply absent overrides using their documented defaults and reject unsupported explicit settings.
/// Provider-specific values are borrowed; callers must not mutate them during an operation.
/// Each provider must construct a fresh SDK request rather than mutate shared client defaults.
/// </remarks>
public sealed class DocumentExtractionOptions
{
    private string? _extractorName;

    /// <summary>Gets or sets an opaque per-call extractor selector within the configured provider, when supported.</summary>
    /// <remarks>A null value uses the provider default. This does not select a dependency-injection registration.</remarks>
    public string? ExtractorName
    {
        get => _extractorName;
        set => _extractorName = value is null ? null : Throw.IfNullOrWhitespace(value);
    }

    /// <summary>Gets adapter-specific overrides, including typed provider settings.</summary>
    public Dictionary<string, object?> AdditionalProperties { get; } = new(StringComparer.Ordinal);

    /// <summary>Copies this request's scalars and property entries; opaque values remain borrowed.</summary>
    /// <returns>A separate request-options container.</returns>
    public DocumentExtractionOptions Clone()
    {
        DocumentExtractionOptions copy = new()
        {
            ExtractorName = ExtractorName
        };
        foreach (KeyValuePair<string, object?> property in AdditionalProperties)
        {
            copy.AdditionalProperties.Add(property.Key, property.Value);
        }

        return copy;
    }
}
