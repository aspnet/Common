// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>
/// Represents an ordered, provider-neutral structured document.
/// </summary>
public sealed class ExtractionDocument
{
    private readonly List<ExtractionDocumentSourceOutput> _sourceOutputs = [];
    private readonly List<ExtractionDocumentSourcePage> _sourcePages = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionDocument"/> class.
    /// </summary>
    /// <param name="identifier">The local identifier of the document.</param>
    /// <param name="content">The local payload, or <see langword="null"/> for a structural document.</param>
    public ExtractionDocument(string identifier, AIContent? content = null)
    {
        Identifier = Throw.IfNullOrEmpty(identifier);
        Content = content;
        SourceOutputs = _sourceOutputs.AsReadOnly();
        SourcePages = _sourcePages.AsReadOnly();
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

    /// <summary>Gets registered source operations, with explicit break-glass access.</summary>
    public IReadOnlyList<ExtractionDocumentSourceOutput> SourceOutputs { get; }

    /// <summary>Gets registered source frames, independently of assembled section order.</summary>
    public IReadOnlyList<ExtractionDocumentSourcePage> SourcePages { get; }

    /// <summary>Registers a source operation without taking ownership of borrowed native objects.</summary>
    public void AddSourceOutput(ExtractionDocumentSourceOutput sourceOutput)
    {
        ValidateSourceOutput(Throw.IfNull(sourceOutput));
        if (!_sourceOutputs.Contains(sourceOutput))
        {
            _sourceOutputs.Add(sourceOutput);
        }
    }

    /// <summary>Registers a source frame and its source operation, when supplied.</summary>
    /// <remarks>Providers register frames explicitly, including frames with no extracted content.</remarks>
    public void AddSourcePage(ExtractionDocumentSourcePage page)
    {
        _ = Throw.IfNull(page);
        if (_sourcePages.Exists(item => item.Identifier == page.Identifier &&
            item.SourceOutput?.Identifier == page.SourceOutput?.Identifier && !ReferenceEquals(item, page)))
        {
            throw new InvalidOperationException("A source frame identifier must identify one frame instance within its operation.");
        }

        if (page.SourceOutput is { } output)
        {
            AddSourceOutput(output);
        }

        if (!_sourcePages.Contains(page))
        {
            _sourcePages.Add(page);
        }
    }

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

    private void ValidateSourceOutput(ExtractionDocumentSourceOutput source)
    {
        if (_sourceOutputs.Exists(item => item.Identifier == source.Identifier && !ReferenceEquals(item, source)))
        {
            throw new InvalidOperationException("A source operation identifier must identify one output instance.");
        }
    }
}
