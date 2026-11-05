// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter

namespace Microsoft.Extensions.AI;

/// <summary>Represents a delegating image generator that implements the OpenTelemetry Semantic Conventions for Generative AI systems.</summary>
/// <remarks>
/// This class provides an implementation of the Semantic Conventions for Generative AI systems v1.41, defined at <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />.
/// The specification is still experimental and subject to change; as such, the telemetry output by this client is also subject to change.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIImageGeneration, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class OpenTelemetryImageGenerator : DelegatingImageGenerator
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    private readonly Histogram<int> _tokenUsageHistogram;
    private readonly Histogram<double> _operationDurationHistogram;

    private readonly string? _defaultModelId;
    private readonly string? _providerName;
    private readonly string? _serverAddress;
    private readonly int _serverPort;

    private readonly ILogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryImageGenerator"/> class.</summary>
    /// <param name="innerGenerator">The underlying <see cref="IImageGenerator"/>.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for emitting any logging data from the client.</param>
    /// <param name="sourceName">An optional source name that will be used on the telemetry data.</param>
    public OpenTelemetryImageGenerator(IImageGenerator innerGenerator, ILogger? logger = null, string? sourceName = null)
        : base(innerGenerator)
    {
        Debug.Assert(innerGenerator is not null, "Should have been validated by the base ctor");

        _logger = logger;

        if (innerGenerator!.GetService<ImageGeneratorMetadata>() is ImageGeneratorMetadata metadata)
        {
            _defaultModelId = metadata.DefaultModelId;
            _providerName = metadata.ProviderName;
            _serverAddress = metadata.ProviderUri?.Host;
            _serverPort = metadata.ProviderUri?.Port ?? 0;
        }

        string name = string.IsNullOrEmpty(sourceName) ? OpenTelemetryConsts.DefaultSourceName : sourceName!;
        _activitySource = new(name);
        _meter = new(name);

        _tokenUsageHistogram = OtelMetricHelpers.CreateGenAITokenUsageHistogram(_meter);
        _operationDurationHistogram = OtelMetricHelpers.CreateGenAIOperationDurationHistogram(_meter);
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
    /// and outputs, such as message content, function call arguments, and function call results.
    /// The default value can be overridden by setting the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable to "true". Explicitly setting this property will override the environment variable.
    /// </remarks>
    public bool EnableSensitiveData { get; set; } = TelemetryHelpers.EnableSensitiveDataDefault;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ActivitySource) ? _activitySource :
        base.GetService(serviceType, serviceKey);

    /// <inheritdoc/>
    public async override Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(request);

        using Activity? activity = CreateAndConfigureActivity(request, options);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;
        string? requestModelId = options?.ModelId ?? _defaultModelId;

        ImageGenerationResponse? response = null;
        Exception? error = null;
        try
        {
            response = await base.GenerateAsync(request, options, cancellationToken).ConfigureAwait(false);
            return response;
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
    }

    /// <summary>Creates an activity for an image generation request, or returns <see langword="null"/> if not enabled.</summary>
    private Activity? CreateAndConfigureActivity(ImageGenerationRequest request, ImageGenerationOptions? options)
    {
        Activity? activity = null;
        if (_activitySource.HasListeners())
        {
            string? modelId = options?.ModelId ?? _defaultModelId;

            activity = _activitySource.StartActivity(
                string.IsNullOrWhiteSpace(modelId) ? OpenTelemetryConsts.GenAI.GenerateContentName : $"{OpenTelemetryConsts.GenAI.GenerateContentName} {modelId}",
                ActivityKind.Client);

            if (activity is { IsAllDataRequested: true })
            {
                _ = activity
                    .AddTag(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.GenerateContentName)
                    .AddTag(OpenTelemetryConsts.GenAI.Output.Type, OpenTelemetryConsts.TypeImage)
                    .AddTag(OpenTelemetryConsts.GenAI.Request.Model, modelId)
                    .AddTag(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

                if (_serverAddress is not null)
                {
                    _ = activity
                        .AddTag(OpenTelemetryConsts.Server.Address, _serverAddress)
                        .AddTag(OpenTelemetryConsts.Server.Port, _serverPort);
                }

                if (options is not null)
                {
                    if (options.Count is int count)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Request.ChoiceCount, count);
                    }

                    // Otel hasn't yet standardized tags for image generation parameters; these are based on other systems.
                    if (options.ImageSize is Size size)
                    {
                        _ = activity
                            .AddTag("gen_ai.request.image.width", size.Width)
                            .AddTag("gen_ai.request.image.height", size.Height);
                    }
                }

                if (EnableSensitiveData)
                {
                    List<AIContent> content = [];

                    if (request.Prompt is not null)
                    {
                        content.Add(new TextContent(request.Prompt));
                    }

                    if (request.OriginalImages is not null)
                    {
                        content.AddRange(request.OriginalImages);
                    }

                    _ = activity.AddTag(
                        OpenTelemetryConsts.GenAI.Input.Messages,
                        OtelMessageSerializer.SerializeChatMessages([new(ChatRole.User, content)]));

                    if (options?.AdditionalProperties is { } props)
                    {
                        foreach (KeyValuePair<string, object?> prop in props)
                        {
                            _ = activity.AddTag(prop.Key, prop.Value);
                        }
                    }
                }
            }
        }

        return activity;
    }

    /// <summary>Adds image generation response information to the activity.</summary>
    private void TraceResponse(
        Activity? activity,
        string? requestModelId,
        ImageGenerationResponse? response,
        Exception? error,
        Stopwatch? stopwatch)
    {
        if (_operationDurationHistogram.Enabled && stopwatch is not null)
        {
            TagList tags = default;

            AddMetricTags(ref tags, requestModelId);
            if (error is not null)
            {
                tags.Add(OpenTelemetryConsts.Error.Type, error.GetType().FullName);
            }

            _operationDurationHistogram.Record(stopwatch.Elapsed.TotalSeconds, tags);
        }

        OpenTelemetryLog.RecordOperationError(activity, _logger, error);

        if (response is not null)
        {
            if (EnableSensitiveData &&
                response.Contents is { Count: > 0 } contents &&
                activity is { IsAllDataRequested: true })
            {
                _ = activity.AddTag(
                    OpenTelemetryConsts.GenAI.Output.Messages,
                    OtelMessageSerializer.SerializeChatMessages([new(ChatRole.Assistant, contents)]));
            }

            if (response.Usage is { } usage)
            {
                if (_tokenUsageHistogram.Enabled)
                {
                    if (usage.InputTokenCount is long inputTokens)
                    {
                        TagList tags = default;
                        tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeInput);
                        AddMetricTags(ref tags, requestModelId);
                        _tokenUsageHistogram.Record((int)inputTokens, tags);
                    }

                    if (usage.OutputTokenCount is long outputTokens)
                    {
                        TagList tags = default;
                        tags.Add(OpenTelemetryConsts.GenAI.Token.Type, OpenTelemetryConsts.TokenTypeOutput);
                        AddMetricTags(ref tags, requestModelId);
                        _tokenUsageHistogram.Record((int)outputTokens, tags);
                    }
                }

                if (activity is { IsAllDataRequested: true })
                {
                    if (usage.InputTokenCount is long inputTokens)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.InputTokens, (int)inputTokens);
                    }

                    if (usage.OutputTokenCount is long outputTokens)
                    {
                        _ = activity.AddTag(OpenTelemetryConsts.GenAI.Usage.OutputTokens, (int)outputTokens);
                    }
                }
            }
        }

        void AddMetricTags(ref TagList tags, string? requestModelId)
        {
            tags.Add(OpenTelemetryConsts.GenAI.Operation.Name, OpenTelemetryConsts.GenAI.GenerateContentName);

            if (requestModelId is not null)
            {
                tags.Add(OpenTelemetryConsts.GenAI.Request.Model, requestModelId);
            }

            tags.Add(OpenTelemetryConsts.GenAI.Provider.Name, _providerName);

            if (_serverAddress is string endpointAddress)
            {
                tags.Add(OpenTelemetryConsts.Server.Address, endpointAddress);
                tags.Add(OpenTelemetryConsts.Server.Port, _serverPort);
            }
        }
    }
}
