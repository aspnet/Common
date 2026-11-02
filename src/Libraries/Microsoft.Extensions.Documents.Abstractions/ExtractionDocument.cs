// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>
/// Represents an ordered, provider-neutral structured document.
/// </summary>
public sealed class ExtractionDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionDocument"/> class.
    /// </summary>
    /// <param name="identifier">The local identifier of the document.</param>
    /// <param name="content">The local payload, or <see langword="null"/> for a structural document.</param>
    public ExtractionDocument(string identifier, AIContent? content = null)
    {
        Identifier = Throw.IfNullOrEmpty(identifier);
        Content = content;
    }

    /// <summary>Gets the document identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets or sets the local payload of the document, excluding its sections.</summary>
    /// <remarks>Payloads are borrowed and mutable; their interpretation is determined by the producer.</remarks>
    public AIContent? Content { get; set; }

    /// <summary>Gets the ordered root sections of the document.</summary>
    public List<ExtractionDocumentSection> Sections { get; } = [];

    /// <summary>Gets the metadata associated with the document.</summary>
    public Dictionary<string, object?> Metadata { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets source frames, independently of assembled section order.</summary>
    /// <remarks>Producers explicitly add frames, including blank frames with no content elements.</remarks>
    public List<ExtractionDocumentSourcePage> SourcePages { get; } = [];

    /// <summary>Gets or sets the optional borrowed native document result.</summary>
    /// <remarks>
    /// Native data is not implicitly disposed, deep-cloned, logged or serialized.
    /// This does not guarantee original response bytes or durable serialization.
    /// </remarks>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Enumerates non-section content in depth-first document order.</summary>
    /// <remarks>
    /// This structural traversal does not flatten table cells or return document/section-local payloads.
    /// Use the hierarchy to inspect those local payloads and table cells directly.
    /// Cyclic section relationships and null entries are rejected when traversed.
    /// </remarks>
    /// <returns>The content elements in document order.</returns>
    public IEnumerable<ExtractionDocumentElement> EnumerateContent()
    {
        HashSet<ExtractionDocumentSection> path = [];
        foreach (ExtractionDocumentSection section in Sections)
        {
            foreach (ExtractionDocumentElement element in EnumerateSection(section, path))
            {
                yield return element;
            }
        }
    }

    private static IEnumerable<ExtractionDocumentElement> EnumerateSection(
        ExtractionDocumentSection section, HashSet<ExtractionDocumentSection> path)
    {
        if (section is null || !path.Add(section))
        {
            throw new InvalidOperationException("A document cannot traverse a null section or a cyclic section relationship.");
        }

        try
        {
            foreach (ExtractionDocumentElement element in section.Elements)
            {
                if (element is null)
                {
                    throw new InvalidOperationException("A document cannot traverse a null element.");
                }

                if (element is ExtractionDocumentSection nestedSection)
                {
                    foreach (ExtractionDocumentElement nestedElement in EnumerateSection(nestedSection, path))
                    {
                        yield return nestedElement;
                    }
                }
                else
                {
                    yield return element;
                }
            }
        }
        finally
        {
            _ = path.Remove(section);
        }
    }
}
