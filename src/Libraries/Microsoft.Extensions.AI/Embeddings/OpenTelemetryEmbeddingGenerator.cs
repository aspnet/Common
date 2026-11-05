// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter

namespace Microsoft.Extensions.AI;

/// <summary>Represents a delegating embedding generator that implements the OpenTelemetry Semantic Conventions for Generative AI systems.</summary>
/// <remarks>
/// This class provides an implementation of the Semantic Conventions for Generative AI systems v1.41, defined at <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />.
/// The specification is still experimental and subject to change; as such, the telemetry output by this client is also subject to change.
/// </remarks>
/// <typeparam name="TInput">The type of input used to produce embeddings.</typeparam>
/// <typeparam name="TEmbedding">The type of embedding generated.</typeparam>
public sealed class OpenTelemetryEmbeddingGenerator<TInput, TEmbedding> : DelegatingEmbeddingGenerator<TInput, TEmbedding>
    where TEmbedding : Embedding
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    private readonly Histogram<int> _tokenUsageHistogram;
    private readonly Histogram<double> _operationDurationHistogram;

    private readonly string? _providerName;
    private readonly string? _defaultModelId;
    private readonly int? _defaultModelDimensions;
    private readonly string? _endpointAddress;
    private readonly int _endpointPort;

    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenTelemetryEmbeddingGenerator{TInput, TEmbedding}"/> class.
    /// </summary>
    /// <param name="innerGenerator">The underlying <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>, which is the next stage of the pipeline.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for emitting any logging data from the generator.</param>
    /// <param name="sourceName">An optional source name that will be used on the telemetry data.</param>
    public OpenTelemetryEmbeddingGenerator(IEmbeddingGenerator<TInput, TEmbedding> innerGenerator, ILogger? logger = null, string? sourceName = null)
        : base(innerGenerator)
    {
        Debug.Assert(innerGenerator is not null, "Should have been validated by the base ctor.");

        _logger = logger;

        if (innerGenerator!.GetService<EmbeddingGeneratorMetadata>() is EmbeddingGeneratorMetadata metadata)
        {
            _defaultModelId = metadata.DefaultModelId;
            _defaultModelDimensions = metadata.DefaultModelDimensions;
            _providerName = metadata.ProviderName;
            _endpointAddress = metadata.ProviderUri?.Host;
            _endpointPort = metadata.ProviderUri?.Port ?? 0;
        }

        string name = string.IsNullOrEmpty(sourceName) ? OpenTelemetryConsts.DefaultSourceName : sourceName!;
        _activitySource = new(name);
        _meter = new(name);

        _tokenUsageHistogram = OtelMetricHelpers.CreateGenAITokenUsageHistogram(_meter);
        _operationDurationHistogram = OtelMetricHelpers.CreateGenAIOperationDurationHistogram(_meter);
    }

    /// <summary>
    /// Gets or sets a value indicating whether potentially sensitive information should be included in telemetry.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if potentially sensitive information should be included in telemetry;
    /// <see langword="false"/> if telemetry shouldn't include raw inputs and outputs.
    /// The default value is <see langword="false"/>, unless the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable is set to "true" (case-insensitive).
    /// </value>
    /// <remarks>
    /// By default, telemetry includes metadata, such as token counts, but not raw inputs
    /// and outputs or additional options data.
    /// The default value can be overridden by setting the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable to "true". Explicitly setting this property will override the environment variable.
    /// </remarks>
    public bool EnableSensitiveData { get; set; } = TelemetryHelpers.EnableSensitiveDataDefault;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ActivitySource) ? _activitySource :
        base.GetService(serviceType, serviceKey);

    /// <inheritdoc/>
    public override async Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(IEnumerable<TInput> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(values);

        using Activity? activity = CreateAndConfigureActivity(options);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;
        string? requestModelId = options?.ModelId ?? _defaultModelId;

        GeneratedEmbeddings<TEmbedding>? response = null;
        Exception? error = null;
        try
        {
            response = await base.GenerateAsync(values, options, cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            TraceResponse(activity, requestModelId, response, error, stopwatch);
        }

        return response;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activitySource.Dispose();
            _meter.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Creates an activity for an embedding generation request, or returns <see langword="null"/> if not enabled.</summary>
    private Activity? CreateAndConfigureActivity(EmbeddingGenerationOptions? options)
    {
        Activity? activity = null;
        if (_activitySource.HasListeners())
        {
            string? modelId = options?.ModelId ?? _defaultModelId;

            activity = _activitySource.StartActivity(
                string.IsNullOrWhiteSpace(modelId) ? OpenTelemetryConsts.GenAI.EmbeddingsName : $"{OpenTelemetryConsts.GenAI.EmbeddingsName} {modelId}",
                ActivityKind.Client,
                default(ActivityContext),
                [
                    new(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.EmbeddingsName),
                    new(OpenTelemetryConsts.GenAI.Request.Model, modelId),
                    new(OpenTelemetryConsts.GenAI.Provider.Name, _providerName),
                ]);

            if (activity is not null)
            {
                if (_endpointAddress is not null)
                {
                    _ = activity
                        .AddTag(OpenTelemetryConsts.Server.Address, _endpointAddress)
                        .AddTag(OpenTelemetryConsts.Server.Port, _endpointPort);
                }

                if ((options?.Dimensions ?? _defaultModelDimensions) is int dimensionsValue)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.GenAI.Embeddings.Dimension.Count, dimensionsValue);
                }

                // Log all additional request options as raw values on the span.
                // Since AdditionalProperties has undefined meaning, we treat it as potentially sensitive data.
                if (EnableSensitiveData && options?.AdditionalProperties is { } props)
                {
                    foreach (KeyValuePair<string, object?> prop in props)
                    {
                        _ = activity.AddTag(prop.Key, prop.Value);
                    }
                }
            }
        }

        return activity;
    }

    /// <summary>Adds embedding generation response information to the activity.</summary>
    private void TraceResponse(
        Activity? activity,
        string? requestModelId,
        GeneratedEmbeddings<TEmbedding>? embeddings,
        Exception? error,
        Stopwatch? stopwatch)
    {
        int? inputTokens = null;
        string? responseModelId = null;
        if (embeddings is not null)
        {
            responseModelId = embeddings.FirstOrDefault()?.ModelId;
            if (embeddings.Usage?.InputTokenCount is long i)
            {
                inputTokens = inputTokens.GetValueOrDefault() + (int)i;
            }
        }

        if (_operationDurationHistogram.Enabled && stopwatch is not null)
        {
            TagList tags = default;
            AddMetricTags(ref tags, requestModelId, responseModelId);
            if (error is not null)
            {
                tags.Add(OpenTelemetryConsts.Error.Type, error.GetType().FullName);
            }

            _operationDurationHistogram.Record(stopwatch.Elapsed.TotalSeconds, tags);
        }

        if (_tokenUsageHistogram.Enabled && inputTokens.HasValue)
        {
            TagList tags = default;
            tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeInput);
            AddMetricTags(ref tags, requestModelId, responseModelId);

            _tokenUsageHistogram.Record(inputTokens.Value, tags);
        }

        OpenTelemetryLog.RecordOperationError(activity, _logger, error);

        if (activity is not null)
        {
            if (inputTokens.HasValue)
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTokens, inputTokens);
            }

            if (responseModelId is not null)
            {
                _ = activity.AddTag(OpenTelemetryConsts.GenAI.Response.Model, responseModelId);
            }

            // Log all additional response properties as raw values on the span.
            // Since AdditionalProperties has undefined meaning, we treat it as potentially sensitive data.
            if (EnableSensitiveData && embeddings?.AdditionalProperties is { } props)
            {
                foreach (KeyValuePair<string, object?> prop in props)
                {
                    _ = activity.AddTag(prop.Key, prop.Value);
                }
            }
        }
    }

    private void AddMetricTags(ref TagList tags, string? requestModelId, string? responseModelId)
    {
        tags.Add(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.EmbeddingsName);

        if (requestModelId is not null)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Request.Model, requestModelId);
        }

        tags.Add(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

        if (_endpointAddress is string endpointAddress)
        {
            tags.Add(OpenTelemetryConsts.Server.Address, endpointAddress);
            tags.Add(OpenTelemetryConsts.Server.Port, _endpointPort);
        }

        // Assume all of the embeddings in the same batch used the same model
        if (responseModelId is not null)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Response.Model, responseModelId);
        }
    }
}
