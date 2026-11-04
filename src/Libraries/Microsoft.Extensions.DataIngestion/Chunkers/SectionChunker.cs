// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

/// <summary>
/// Treats each <see cref="ExtractionDocumentSection" /> in a <see cref="IngestionDocument.Sections"/> as a separate entity.
/// </summary>
public sealed class SectionChunker : IngestionChunker
{
    private readonly ElementsChunker _elementsChunker;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionChunker"/> class.
    /// </summary>
    /// <param name="options">The options for the chunker.</param>
    public SectionChunker(IngestionChunkerOptions options)
    {
        _elementsChunker = new(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(IngestionDocument document, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);

        List<IngestionChunk> chunks = [];
        foreach (var chunk in _elementsChunker.Process(document, string.Empty, [],
            localContent: IngestionDocumentElementExtensions.GetSemanticContent(document.ExtractedDocument.Content)))
        {
            yield return chunk;
        }

        HashSet<ExtractionDocumentSection> path = [];
        foreach (ExtractionDocumentSection section in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Process(document, section, chunks, path);
            foreach (var chunk in chunks)
            {
                yield return chunk;
            }
            chunks.Clear();
        }
    }

    private void Process(IngestionDocument document, ExtractionDocumentSection section, List<IngestionChunk> chunks,
        HashSet<ExtractionDocumentSection> path, string? parentContext = null, IReadOnlyList<ExtractionDocumentElement>? parentSources = null)
    {
        if (section is null || !path.Add(section))
        {
            throw new InvalidOperationException("Null entries and cyclic relationships are not supported in the hierarchy.");
        }

        List<ExtractionDocumentElement> elements = new(section.Elements.Count);
        string context = parentContext ?? string.Empty;
        List<ExtractionDocumentElement> contextSources = parentSources is null ? [] : new(parentSources);
        contextSources.Add(section);
        if (!string.IsNullOrEmpty(section.GetSemanticContent()))
        {
            chunks.AddRange(_elementsChunker.Process(document, context, [section], contextSources));
        }

        try
        {
            for (int i = 0; i < section.Elements.Count; i++)
            {
                switch (section.Elements[i])
                {
                    // If the first element is a header, we use it as a context.
                    // This is common for various documents and readers.
                    case ExtractionDocumentTextElement documentHeader when i == 0 && string.Equals(documentHeader.Role, "heading", StringComparison.OrdinalIgnoreCase):
                        contextSources.Add(documentHeader);
                        context = string.IsNullOrEmpty(context)
                            ? documentHeader.GetSemanticContent() ?? string.Empty
                            : context + $" {documentHeader.GetSemanticContent()}";
                        break;
                    case ExtractionDocumentSection nestedSection:
                        Commit();
                        Process(document, nestedSection, chunks, path, context, contextSources);
                        break;
                    case null:
                        throw new InvalidOperationException("The hierarchy contains a null element.");
                    default:
                        elements.Add(section.Elements[i]);
                        break;
                }
            }

            Commit();
        }
        finally
        {
            _ = path.Remove(section);
        }

        void Commit()
        {
            if (elements.Count > 0)
            {
                foreach (var chunk in _elementsChunker.Process(document, context, elements, contextSources))
                {
                    chunks.Add(chunk);
                }
                elements.Clear();
            }
        }
    }
}
