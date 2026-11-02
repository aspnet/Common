// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents an element in a structured document.</summary>
public abstract class ExtractionDocumentElement
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentElement"/> class.</summary>
    /// <param name="content">The local payload, or <see langword="null"/> for a structural element.</param>
    protected ExtractionDocumentElement(AIContent? content) => Content = content;

    /// <summary>Gets or sets the local payload of this element.</summary>
    /// <remarks>
    /// TextContent can contain plain text or producer-supplied formatted text. Structural elements can have no payload.
    /// Content is local: do not store an aggregate copy of descendant content here.
    /// Payloads and their metadata are borrowed and mutable.
    /// Text elements require nonnull <see cref="TextContent"/>, including assignments through a base reference.
    /// </remarks>
    public AIContent? Content
    {
        get;
        set
        {
            if (this is ExtractionDocumentTextElement)
            {
                _ = Throw.IfNull(value);
                if (value is not TextContent)
                {
                    Throw.ArgumentException(nameof(value), "A text element requires TextContent.");
                }
            }

            field = value;
        }
    }

    /// <summary>Gets optional page and region associations.</summary>
    /// <remarks>Adding a reference does not add its page to a document. Producers explicitly populate the source-page list.</remarks>
    public List<ExtractionDocumentSourceReference> Sources { get; } = [];
}

/// <summary>Represents a section in a document. Sections can be nested in the ordered element collection.</summary>
public sealed class ExtractionDocumentSection : ExtractionDocumentElement
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentSection"/> class.</summary>
    /// <param name="content">The local payload, or <see langword="null"/> for a structural section.</param>
    public ExtractionDocumentSection(AIContent? content = null)
        : base(content)
    {
    }

    /// <summary>Gets the ordered elements in this section, including nested sections.</summary>
    public List<ExtractionDocumentElement> Elements { get; } = [];
}

/// <summary>Represents text in a document, with an optional semantic role.</summary>
public sealed class ExtractionDocumentTextElement : ExtractionDocumentElement
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentTextElement"/> class.</summary>
    /// <param name="content">The text payload, without a required formatting convention.</param>
    /// <param name="role">The optional semantic role, or <see langword="null"/> when unspecified.</param>
    public ExtractionDocumentTextElement(TextContent content, string? role = null)
        : base(Throw.IfNull(content))
    {
        Role = role is null ? null : Throw.IfNullOrWhitespace(role);
    }

    /// <summary>Gets the optional semantic role of this text element.</summary>
    /// <remarks>
    /// Common roles are <c>heading</c> for a document heading, <c>pageHeader</c> for repeated page headers,
    /// and <c>pageFooter</c> for repeated page footers. Custom roles are preserved without normalization.
    /// Consumers should treat unspecified or unrecognized roles as ordinary text.
    /// </remarks>
    public string? Role { get; }

    /// <summary>Gets or sets the positive heading level, or <see langword="null"/> when unspecified.</summary>
    /// <remarks>This property is meaningful for the <c>heading</c> role. An unspecified level does not imply a hierarchy level.</remarks>
    public int? HeadingLevel
    {
        get;
        set
        {
            if (value is not null)
            {
                _ = Throw.IfLessThanOrEqual(value.Value, 0, nameof(value));
            }

            field = value;
        }
    }
}

/// <summary>Represents an image in a document.</summary>
public sealed class ExtractionDocumentImageElement : ExtractionDocumentElement
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentImageElement"/> class.</summary>
    /// <param name="content">The local image payload, such as image bytes, a reference, or producer-supplied text.</param>
    /// <param name="text">The optional OCR or transcription text, independently of any caption or description.</param>
    public ExtractionDocumentImageElement(AIContent? content = null, string? text = null)
        : base(content)
    {
        Text = text;
    }

    /// <summary>Gets or sets the original asset reference, independently of any payload URI.</summary>
    /// <remarks>No URI is fetched or fabricated by the model.</remarks>
    public Uri? Source { get; set; }

    /// <summary>Gets or sets OCR or transcription text associated with the image.</summary>
    /// <remarks>Captions and descriptions belong in <see cref="AlternativeText"/> instead.</remarks>
    public string? Text { get; set; }

    /// <summary>Gets or sets the alternative text for the image.</summary>
    public string? AlternativeText { get; set; }
}
