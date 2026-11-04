// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Documents;

/// <summary>Identifies the unit of a source page's coordinates.</summary>
public enum ExtractionDocumentCoordinateUnit
{
    /// <summary>Pixels in the source raster.</summary>
    Pixel,

    /// <summary>Points, each equal to 1/72 inch.</summary>
    Point,

    /// <summary>Inches.</summary>
    Inch,

    /// <summary>Coordinates relative to the page, in the range zero to one.</summary>
    Normalized
}

/// <summary>Identifies the origin and vertical axis of source coordinates.</summary>
public enum ExtractionDocumentCoordinateOrigin
{
    /// <summary>Top-left origin, with the vertical axis increasing downwards.</summary>
    TopLeft,

    /// <summary>Bottom-left origin, with the vertical axis increasing upwards.</summary>
    BottomLeft
}

/// <summary>Represents a vertex in its source page's coordinate frame.</summary>
public readonly struct ExtractionDocumentPoint : IEquatable<ExtractionDocumentPoint>
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentPoint"/> struct.</summary>
    public ExtractionDocumentPoint(double x, double y)
    {
        DocumentSourceValidation.Finite(x, nameof(x));
        DocumentSourceValidation.Finite(y, nameof(y));
        X = x;
        Y = y;
    }

    /// <summary>Gets the horizontal coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the vertical coordinate.</summary>
    public double Y { get; }

    /// <inheritdoc/>
    public bool Equals(ExtractionDocumentPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ExtractionDocumentPoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());

    /// <summary>Determines whether two points have equal coordinates.</summary>
    public static bool operator ==(ExtractionDocumentPoint left, ExtractionDocumentPoint right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two points have different coordinates.</summary>
    public static bool operator !=(ExtractionDocumentPoint left, ExtractionDocumentPoint right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Represents source page dimensions in the page's coordinate unit, or an empty (0, 0) value.</summary>
/// <remarks>Empty dimensions cannot be assigned to a source page. Use <see langword="null"/> for unknown page dimensions.</remarks>
public readonly struct ExtractionDocumentPageDimensions : IEquatable<ExtractionDocumentPageDimensions>
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentPageDimensions"/> struct.</summary>
    public ExtractionDocumentPageDimensions(double width, double height)
    {
        DocumentSourceValidation.Finite(width, nameof(width));
        DocumentSourceValidation.Finite(height, nameof(height));
        if (!width.Equals(0.0) || !height.Equals(0.0))
        {
            _ = Throw.IfLessThanOrEqual(width, 0);
            _ = Throw.IfLessThanOrEqual(height, 0);
        }

        Width = width;
        Height = height;
    }

    /// <summary>Gets the width.</summary>
    public double Width { get; }

    /// <summary>Gets the height.</summary>
    public double Height { get; }

    /// <inheritdoc/>
    public bool Equals(ExtractionDocumentPageDimensions other) => Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ExtractionDocumentPageDimensions other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => unchecked((Width.GetHashCode() * 397) ^ Height.GetHashCode());

    /// <summary>Determines whether two dimensions have equal width and height.</summary>
    public static bool operator ==(ExtractionDocumentPageDimensions left, ExtractionDocumentPageDimensions right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two dimensions have different width or height.</summary>
    public static bool operator !=(ExtractionDocumentPageDimensions left, ExtractionDocumentPageDimensions right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Describes a producer-supplied source page, independently of document assembly order.</summary>
public sealed class ExtractionDocumentSourcePage
{
    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentSourcePage"/> class.</summary>
    public ExtractionDocumentSourcePage(
        string identifier,
        int? pageNumber,
        ExtractionDocumentPageDimensions? dimensions = null,
        ExtractionDocumentCoordinateUnit? coordinateUnit = null,
        ExtractionDocumentCoordinateOrigin? coordinateOrigin = null)
    {
        Identifier = Throw.IfNullOrEmpty(identifier);
        PageNumber = pageNumber is { } number ? Throw.IfLessThanOrEqual(number, 0, nameof(pageNumber)) : null;
        if (dimensions is { } size && (size.Width <= 0 || size.Height <= 0))
        {
            Throw.ArgumentOutOfRangeException(nameof(dimensions));
        }

        if (coordinateUnit is { } unit && !Enum.IsDefined(typeof(ExtractionDocumentCoordinateUnit), unit))
        {
            Throw.ArgumentOutOfRangeException(nameof(coordinateUnit));
        }

        if (coordinateOrigin is { } origin && !Enum.IsDefined(typeof(ExtractionDocumentCoordinateOrigin), origin))
        {
            Throw.ArgumentOutOfRangeException(nameof(coordinateOrigin));
        }

        Dimensions = dimensions;
        CoordinateUnit = coordinateUnit;
        CoordinateOrigin = coordinateOrigin;
    }

    /// <summary>Gets the producer's frame identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets the one-based originating page number, or <see langword="null"/> for an unnumbered source frame.</summary>
    /// <remarks>The source identifier remains meaningful without a number. This value is never an assembly index.</remarks>
    public int? PageNumber { get; }

    /// <summary>Gets reported dimensions, or <see langword="null"/> when unknown.</summary>
    public ExtractionDocumentPageDimensions? Dimensions { get; }

    /// <summary>Gets the reported unit, or <see langword="null"/> when unknown.</summary>
    public ExtractionDocumentCoordinateUnit? CoordinateUnit { get; }

    /// <summary>Gets the reported origin, or <see langword="null"/> when unknown.</summary>
    public ExtractionDocumentCoordinateOrigin? CoordinateOrigin { get; }
}

/// <summary>Associates content with an originating page and an optional source polygon.</summary>
/// <remarks>
/// A reference without a polygon identifies a page, not a precise location. Native observations are
/// borrowed and excluded from semantic projection. A source association does not verify content.
/// </remarks>
public sealed class ExtractionDocumentSourceReference
{
    private readonly object? _rawRepresentation;

    /// <summary>Initializes a new instance of the <see cref="ExtractionDocumentSourceReference"/> class.</summary>
    /// <param name="page">The originating page and coordinate frame.</param>
    /// <param name="polygon">Ordered polygon vertices, or no vertices for a page-only reference.</param>
    /// <param name="rawRepresentation">An optional borrowed native observation.</param>
    public ExtractionDocumentSourceReference(
        ExtractionDocumentSourcePage page,
        IEnumerable<ExtractionDocumentPoint>? polygon = null,
        object? rawRepresentation = null)
    {
        Page = Throw.IfNull(page);
        Polygon = new ReadOnlyCollection<ExtractionDocumentPoint>((polygon ?? Array.Empty<ExtractionDocumentPoint>()).ToArray());
        if (Polygon.Count is 1 or 2)
        {
            Throw.ArgumentException(nameof(polygon), "A source polygon must contain at least three vertices.");
        }

        _rawRepresentation = rawRepresentation;
    }

    /// <summary>Gets the originating page and coordinate frame.</summary>
    public ExtractionDocumentSourcePage Page { get; }

    /// <summary>Gets every supplied polygon vertex in order, or an empty list for a page-only reference.</summary>
    public IReadOnlyList<ExtractionDocumentPoint> Polygon { get; }

    /// <summary>Gets the borrowed native observation for explicit inspection, when supplied.</summary>
    /// <returns>The borrowed native observation, or <see langword="null"/>.</returns>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Raw inspection is explicit and excluded from automatic property serialization.")]
    public object? GetRawRepresentation() => _rawRepresentation;
}

internal static class DocumentSourceValidation
{
    internal static void Finite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            Throw.ArgumentOutOfRangeException(name, "A coordinate must be finite.");
        }
    }
}
