// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Data.Validation;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.Http.Logging;

/// <summary>
/// Options to configure HTTP client requests logging.
/// </summary>
public class LoggingOptions
{
    private const int MaxIncomingBodySize = 1_572_864; // 1.5 MB
    private const int Millisecond = 1;
    private const int Minute = 60000;
    private const int DefaultReadSizeLimit = 32 * 1024;  // ≈ 32K
    private const OutgoingPathLoggingMode DefaultPathLoggingMode = OutgoingPathLoggingMode.Formatted;
    private const HttpRouteParameterRedactionMode DefaultPathParameterRedactionMode = HttpRouteParameterRedactionMode.Strict;

    /// <summary>
    /// Gets or sets a value indicating whether the request is logged additionally before any further processing.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// When enabled, two entries will be logged for each incoming request - one for request and one for response, if available.
    /// When disabled, only one entry will be logged for each incoming request, which includes both request and response data.
    /// </remarks>
    public bool LogRequestStart { get; set; }

    /// <summary>
    /// Gets or sets the set of HTTP request query parameters to log and their respective data classifications to use for redaction.
    /// </summary>
    /// <value>
    /// The default value is <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>.
    /// </value>
    /// <remarks>
    /// If empty, no HTTP request query parameters will be logged.
    /// If the data class is <see cref="DataClassification.None"/>, no redaction will be done.
    /// </remarks>
    [Required]
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public IDictionary<string, DataClassification> RequestQueryParametersDataClasses { get; set; } = new Dictionary<string, DataClassification>();

    /// <summary>
    /// Gets or sets a value indicating whether the HTTP request and response body are logged.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Avoid enabling this option in a production environment as it might lead to leaking privacy information.
    /// </remarks>
    public bool LogBody { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of bytes of the request or response body to read.
    /// </summary>
    /// <value>
    /// The default value is ≈ 32K.
    /// </value>
    /// <remarks>
    /// The number should ideally be below 85000 bytes to not be allocated on the <see href="https://learn.microsoft.com/dotnet/standard/garbage-collection/large-object-heap">large object heap</see>.
    /// </remarks>
    [Range(1, MaxIncomingBodySize)]
    public int BodySizeLimit { get; set; } = DefaultReadSizeLimit;

    /// <summary>
    /// Gets or sets the maximum amount of time to wait for the request or response body to be read.
    /// </summary>
    /// <value>
    /// The default value is 1 second.
    /// </value>
    /// <remarks>
    /// The value should be in the range of 1 millisecond to 1 minute.
    /// </remarks>
    [TimeSpan(Millisecond, Minute)]
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the list of HTTP request content types which are considered text and thus possible to serialize.
    /// </summary>
    [Required]
    public ISet<string> RequestBodyContentTypes { get; set; } = new HashSet<string>();

    /// <summary>
    /// Gets or sets the list of HTTP response content types which are considered text and thus possible to serialize.
    /// </summary>
    [Required]
    public ISet<string> ResponseBodyContentTypes { get; set; } = new HashSet<string>();

    /// <summary>
    /// Gets or sets the set of HTTP request headers to log and their respective data classifications to use for redaction.
    /// </summary>
    /// <value>
    /// The default value is <see cref="HashSet{T}"/>.
    /// </value>
    /// <remarks>
    /// If empty, no HTTP request headers will be logged.
    /// If the data class is <see cref="DataClassification.None"/>, no redaction will be done.
    /// </remarks>
    [Required]
    public IDictionary<string, DataClassification> RequestHeadersDataClasses { get; set; } = new Dictionary<string, DataClassification>();

    /// <summary>
    /// Gets or sets the set of HTTP response headers to log and their respective data classifications to use for redaction.
    /// </summary>
    /// <value>
    /// The default value is <see cref="HashSet{T}"/>.
    /// </value>
    /// <remarks>
    /// If the data class is <see cref="DataClassification.None"/>, no redaction will be done.
    /// If empty, no HTTP response headers will be logged.
    /// </remarks>
    [Required]
    public IDictionary<string, DataClassification> ResponseHeadersDataClasses { get; set; } = new Dictionary<string, DataClassification>();

    /// <summary>
    /// Gets or sets a value indicating how the outgoing HTTP request path should be logged.
    /// </summary>
    /// <value>
    /// The default value is <see cref="OutgoingPathLoggingMode.Formatted"/>.
    /// </value>
    /// <remarks>
    /// This option is applied only when the <see cref="RequestPathParameterRedactionMode"/> option is not set to
    /// <see cref="HttpRouteParameterRedactionMode.None"/>,
    /// otherwise this setting is ignored and the non-redacted HTTP request path is logged.
    /// </remarks>
    public OutgoingPathLoggingMode RequestPathLoggingMode { get; set; } = DefaultPathLoggingMode;

    /// <summary>
    /// Gets or sets a value indicating how outgoing HTTP request path parameters should be redacted.
    /// </summary>
    /// <value>
    /// The default value is <see cref="HttpRouteParameterRedactionMode.Strict"/>.
    /// </value>
    public HttpRouteParameterRedactionMode RequestPathParameterRedactionMode { get; set; } = DefaultPathParameterRedactionMode;

    /// <summary>
    /// Gets or sets the route parameters to redact with their corresponding data classifications to apply appropriate redaction.
    /// </summary>
    [Required]
    public IDictionary<string, DataClassification> RouteParameterDataClasses { get; set; } = new Dictionary<string, DataClassification>();

    /// <summary>
    /// Gets or sets a value indicating whether the HTTP request and response content headers are logged.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// This property controls whether the logging of HTTP request and response representation headers (e.g. <c>Content-Type</c>) is enabled.
    /// Keep this option disabled if <see cref="RequestHeadersDataClasses"/> or <see cref="ResponseHeadersDataClasses"/>
    /// don't contain any representation headers, otherwise it will create unnecessary minor performance impact on the headers logging.
    /// </remarks>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public bool LogContentHeaders { get; set; }

    /// <summary>
    /// Gets or sets a list of rules that map HTTP status codes or ranges to specific log levels.
    /// </summary>
    /// <value>
    /// The default value is an empty list. When empty, the built-in behavior applies: 400-599 logs at <see cref="Microsoft.Extensions.Logging.LogLevel.Error"/>,
    /// all other status codes log at <see cref="Microsoft.Extensions.Logging.LogLevel.Information"/>.
    /// </value>
    /// <remarks>
    /// Rules are evaluated in order; the first matching rule wins. If no rule matches, the built-in default applies.
    /// </remarks>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    [ValidateEnumeratedItems]
    public IList<HttpStatusCodeLogLevelRule> StatusCodeLogLevelRules { get; set; } = [];

    /// <summary>
    /// Gets or sets the log level used when an HTTP request throws an exception.
    /// </summary>
    /// <value>
    /// The default value is <see cref="Microsoft.Extensions.Logging.LogLevel.Error"/>.
    /// </value>
    [Experimental(diagnosticId: DiagnosticIds.Experiments.Telemetry, UrlFormat = DiagnosticIds.UrlFormat)]
    public LogLevel ExceptionLogLevel { get; set; } = LogLevel.Error;
}
