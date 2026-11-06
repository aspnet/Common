// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Http.Logging;

/// <summary>
/// Maps a status code or range of status codes to a specific log level.
/// </summary>
[Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
public class HttpStatusCodeLogLevelRule : IValidatableObject
{
    /// <summary>
    /// Gets or sets the minimum status code this rule applies to (inclusive).
    /// </summary>
    [Range(100, 599)]
    public int FromStatusCode { get; set; }

    /// <summary>
    /// Gets or sets the maximum status code this rule applies to (inclusive).
    /// When <see langword="null"/>, matches only <see cref="FromStatusCode"/> (exact match).
    /// </summary>
    [Range(100, 599)]
    public int? ToStatusCode { get; set; }

    /// <summary>
    /// Gets or sets the log level to use for responses matching this rule.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ToStatusCode.HasValue && ToStatusCode.Value < FromStatusCode)
        {
            yield return new ValidationResult(
                $"{nameof(ToStatusCode)} must be greater than or equal to {nameof(FromStatusCode)}.",
                [nameof(ToStatusCode)]);
        }
    }
}
