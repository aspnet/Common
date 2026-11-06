// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Telemetry.Metrics;

/// <summary>
/// Provides a way to add common tags to the all the metrics in this class.
/// </summary>
/// <example>
/// <code language="csharp">
/// [CommonTag("Category", "Http")]
/// [CommonTag("MetricVersion", "v1")]
/// static partial class Metric
/// {
///     [Counter("RequestName", "RequestStatusCode")]
///     static partial RequestCounter CreateRequestCounter(Meter meter);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
[Conditional("CODE_GENERATION_ATTRIBUTES")]
[Experimental("EXPERIMENTAL_COMMON_TAG_ATTRIBUTE")]
public sealed class CommonTagAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommonTagAttribute"/> class.
    /// </summary>
    /// <param name="tagName">Tag name.</param>
    /// <param name="tagValue">Tag value.</param>
    public CommonTagAttribute(string tagName, string tagValue)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            Throw.ArgumentException(nameof(tagName), "tagName name cannot be null or empty.");
        }

        TagName = tagName;
        TagValue = tagValue;
    }

    /// <summary>
    /// Gets the metric's tag name.
    /// </summary>
    public string TagName { get; }

    /// <summary>
    /// Gets the metric's tag value.
    /// </summary>
    public string TagValue { get; }
}
