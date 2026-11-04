// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Diagnostics.Metrics;

/// <summary>
/// Provides information to guide the production of a strongly typed histogram metric factory method and associated type.
/// </summary>
/// <remarks>
/// This attribute is applied to a method that has the following constraints:
/// <list type="bullet">
///   <item>Must be a partial method.</item>
///   <item>Must return <c>metricName</c> as the type. A class with that name will be generated.</item>
///   <item>Must not be generic.</item>
///   <item>Must have <c>System.Diagnostics.Metrics.Meter</c> as first parameter.</item>
///   <item>Must have all the keys provided in <c>staticTags</c> as string type parameters.</item>
/// </list>
/// </remarks>
/// <example>
/// <code language="csharp">
/// static partial class Metric
/// {
///     [Histogram("RequestName", "RequestStatusCode")]
///     static partial RequestLatency CreateRequestLatency(Meter meter);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("CODE_GENERATION_ATTRIBUTES")]
public sealed class HistogramAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HistogramAttribute"/> class.
    /// </summary>
    /// <param name="tagNames">variable array of tag names.</param>
    public HistogramAttribute(params string[] tagNames)
    {
        TagNames = tagNames;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HistogramAttribute"/> class.
    /// </summary>
    /// <param name="type">A type providing the metric tag names. The tag values are taken from the type's public fields and properties.</param>
    public HistogramAttribute(Type type)
    {
        Type = type;
    }

    /// <summary>
    /// Gets or sets the name of the metric.
    /// </summary>
    /// <example>
    /// In this example, the metric name is <c>SampleMetric</c>. When <c>Name</c> is not provided, the return type of the method is used as the metric name. In this example,
    /// the metric name would be <c>RequestLatency</c> if <c>Name</c> wasn't provided.
    /// <code language="csharp">
    /// static partial class Metric
    /// {
    ///     [Histogram("RequestName", "RequestStatusCode", Name="SampleMetric")]
    ///     static partial RequestLatency CreateRequestLatency(Meter meter);
    /// }
    /// </code>
    /// </example>
    public string? Name { get; set; }

    /// <summary>
    /// Gets the metric's tags.
    /// </summary>
    public string[]? TagNames { get; }

    /// <summary>
    /// Gets the type that supplies metric tag values.
    /// </summary>
    public Type? Type { get; }

    /// <summary>
    /// Gets or sets the unit of measurement for the metric.
    /// </summary>
    [Experimental(DiagnosticIds.Experiments.Telemetry)]
    public string? Unit { get; set; }

    /// <summary>
    /// Gets or sets the description of the metric.
    /// </summary>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public string? Description { get; set; }
}
