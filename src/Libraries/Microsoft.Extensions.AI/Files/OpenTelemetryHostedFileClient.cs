// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;

#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter

namespace Microsoft.Extensions.AI;

/// <summary>Represents a delegating hosted file client that implements OpenTelemetry-compatible tracing and metrics for file operations.</summary>
/// <remarks>
/// <para>
/// Since there is currently no OpenTelemetry Semantic Convention for hosted file operations, this implementation
/// uses general client span conventions alongside standard <c>file.*</c> registry attributes where applicable.
/// </para>
/// <para>
/// The specification is subject to change as relevant OpenTelemetry conventions emerge; as such, the telemetry
/// output by this client is also subject to change.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIFiles, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class OpenTelemetryHostedFileClient : DelegatingHostedFileClient
{
    private const string UploadOperationName = "files.upload";
    private const string DownloadOperationName = "files.download";
    private const string GetInfoOperationName = "files.get_info";
    private const string ListOperationName = "files.list";
    private const string DeleteOperationName = "files.delete";

    private const string OperationDurationMetricName = "files.client.operation.duration";
    private const string OperationDurationMetricDescription = "Measures the duration of a file operation";

    private const string FilesOperationNameAttribute = "files.operation.name";
    private const string FilesProviderNameAttribute = "files.provider.name";
    private const string FilesIdAttribute = "files.id";
    private const string FilesPurposeAttribute = "files.purpose";
    private const string FilesScopeAttribute = "files.scope";
    private const string FilesListCountAttribute = "files.list.count";
    private const string FilesMediaTypeAttribute = "files.media_type";

    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Histogram<double> _operationDurationHistogram;

    private readonly string? _providerName;
    private readonly string? _serverAddress;
    private readonly int _serverPort;

    private readonly ILogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryHostedFileClient"/> class.</summary>
    /// <param name="innerClient">The underlying <see cref="IHostedFileClient"/>.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for emitting any logging data from the client.</param>
    /// <param name="sourceName">An optional source name that will be used on the telemetry data.</param>
    public OpenTelemetryHostedFileClient(IHostedFileClient innerClient, ILogger? logger = null, string? sourceName = null)
        : base(innerClient)
    {
        Debug.Assert(innerClient is not null, "Should have been validated by the base ctor");

        _logger = logger;

        if (innerClient!.GetService<HostedFileClientMetadata>() is HostedFileClientMetadata metadata)
        {
            _providerName = metadata.ProviderName;
            _serverAddress = metadata.ProviderUri?.Host;
            _serverPort = metadata.ProviderUri?.Port ?? 0;
        }

        string name = string.IsNullOrEmpty(sourceName) ? OpenTelemetryConsts.DefaultSourceName : sourceName!;
        _activitySource = new(name);
        _meter = new(name);

        _operationDurationHistogram = _meter.CreateHistogram<double>(
            OperationDurationMetricName,
            OpenTelemetryConsts.SecondsUnit,
            OperationDurationMetricDescription,
            advice: new() { HistogramBucketBoundaries = OpenTelemetryConsts.GenAI.Client.OperationDuration.ExplicitBucketBoundaries }
            );
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
    /// <para>
    /// By default, telemetry includes operation metadata such as provider name, duration,
    /// file IDs, file sizes, media types, purposes, and scopes.
    /// </para>
    /// <para>
    /// When enabled, telemetry will additionally include file names, which may contain sensitive information.
    /// </para>
    /// <para>
    /// The default value can be overridden by setting the <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
    /// environment variable to "true". Explicitly setting this property will override the environment variable.
    /// </para>
    /// </remarks>
    public bool EnableSensitiveData { get; set; } = TelemetryHelpers.EnableSensitiveDataDefault;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ActivitySource) ? _activitySource :
        base.GetService(serviceType, serviceKey);

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

    /// <inheritdoc/>
    public override async Task<HostedFileContent> UploadAsync(
        Stream content, string? mediaType = null, string? fileName = null, HostedFileClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        fileName ??= content is FileStream fs ? Path.GetFileName(fs.Name) : null;
        mediaType ??= fileName is not null ? MediaTypeMap.GetMediaType(fileName) : null;

        using Activity? activity = StartActivity(UploadOperationName);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        if (activity is { IsAllDataRequested: true })
        {
            if (mediaType is not null)
            {
                _ = activity.AddTag(FilesMediaTypeAttribute, mediaType);
            }

            if (options?.Purpose is string purpose)
            {
                _ = activity.AddTag(FilesPurposeAttribute, purpose);
            }

            if (options?.Scope is string scope)
            {
                _ = activity.AddTag(FilesScopeAttribute, scope);
            }

            if (EnableSensitiveData && fileName is not null)
            {
                _ = activity.AddTag(OpenTelemetryConsts.File.Name, fileName);
            }

            TagAdditionalProperties(activity, options);
        }

        HostedFileContent? result = null;
        Exception? error = null;
        try
        {
            result = await base.UploadAsync(content, mediaType, fileName, options, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            if (result is not null && activity is { IsAllDataRequested: true })
            {
                _ = activity.AddTag(FilesIdAttribute, result.FileId);

                if (result.SizeInBytes is long size)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.File.Size, size);
                }
            }

            RecordDuration(stopwatch, UploadOperationName, error);
            SetErrorStatus(activity, error);
        }
    }

    /// <inheritdoc/>
    public override async Task<HostedFileDownloadStream> DownloadAsync(
        string fileId, HostedFileClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        using Activity? activity = StartActivity(DownloadOperationName);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        if (activity is { IsAllDataRequested: true })
        {
            _ = activity.AddTag(FilesIdAttribute, fileId);

            if (options?.Scope is string scope)
            {
                _ = activity.AddTag(FilesScopeAttribute, scope);
            }

            TagAdditionalProperties(activity, options);
        }

        Exception? error = null;
        try
        {
            var result = await base.DownloadAsync(fileId, options, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            RecordDuration(stopwatch, DownloadOperationName, error);
            SetErrorStatus(activity, error);
        }
    }

    /// <inheritdoc/>
    public override async Task<HostedFileContent?> GetFileInfoAsync(
        string fileId, HostedFileClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        using Activity? activity = StartActivity(GetInfoOperationName);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        if (activity is { IsAllDataRequested: true })
        {
            _ = activity.AddTag(FilesIdAttribute, fileId);

            if (options?.Scope is string scope)
            {
                _ = activity.AddTag(FilesScopeAttribute, scope);
            }

            TagAdditionalProperties(activity, options);
        }

        HostedFileContent? result = null;
        Exception? error = null;
        try
        {
            result = await base.GetFileInfoAsync(fileId, options, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            if (result is not null && activity is { IsAllDataRequested: true })
            {
                if (EnableSensitiveData && result.Name is string name)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.File.Name, name);
                }

                if (result.SizeInBytes is long size)
                {
                    _ = activity.AddTag(OpenTelemetryConsts.File.Size, size);
                }
            }

            RecordDuration(stopwatch, GetInfoOperationName, error);
            SetErrorStatus(activity, error);
        }
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<HostedFileContent> ListFilesAsync(
        HostedFileClientOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using Activity? activity = StartActivity(ListOperationName);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        if (activity is { IsAllDataRequested: true })
        {
            if (options?.Scope is string scope)
            {
                _ = activity.AddTag(FilesScopeAttribute, scope);
            }

            if (options?.Purpose is string purpose)
            {
                _ = activity.AddTag(FilesPurposeAttribute, purpose);
            }

            TagAdditionalProperties(activity, options);
        }

        IAsyncEnumerator<HostedFileContent> e;
        Exception? error = null;
        try
        {
            e = base.ListFilesAsync(options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex;
            RecordDuration(stopwatch, ListOperationName, error);
            SetErrorStatus(activity, error);
            throw;
        }

        int count = 0;
        try
        {
            while (true)
            {
                try
                {
                    if (!await e.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    throw;
                }

                count++;
                yield return e.Current;
                if (activity is not null)
                {
                    Activity.Current = activity; // workaround for https://github.com/dotnet/runtime/issues/47802
                }
            }
        }
        finally
        {
            if (activity is { IsAllDataRequested: true })
            {
                _ = activity.AddTag(FilesListCountAttribute, count);
            }

            RecordDuration(stopwatch, ListOperationName, error);
            SetErrorStatus(activity, error);

            await e.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> DeleteAsync(
        string fileId, HostedFileClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        using Activity? activity = StartActivity(DeleteOperationName);
        Stopwatch? stopwatch = _operationDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        if (activity is { IsAllDataRequested: true })
        {
            _ = activity.AddTag(FilesIdAttribute, fileId);

            if (options?.Scope is string scope)
            {
                _ = activity.AddTag(FilesScopeAttribute, scope);
            }

            TagAdditionalProperties(activity, options);
        }

        Exception? error = null;
        try
        {
            return await base.DeleteAsync(fileId, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            RecordDuration(stopwatch, DeleteOperationName, error);
            SetErrorStatus(activity, error);
        }
    }

    private void SetErrorStatus(Activity? activity, Exception? error) =>
        OpenTelemetryLog.RecordOperationError(activity, _logger, error);

    private void TagAdditionalProperties(Activity activity, HostedFileClientOptions? options)
    {
        if (EnableSensitiveData && options?.AdditionalProperties is { } props)
        {
            foreach (var prop in props)
            {
                _ = activity.AddTag(prop.Key, prop.Value);
            }
        }
    }

    private Activity? StartActivity(string operationName)
    {
        Activity? activity = null;
        if (_activitySource.HasListeners())
        {
            activity = _activitySource.StartActivity(
                operationName,
                ActivityKind.Client);

            if (activity is { IsAllDataRequested: true })
            {
                _ = activity
                    .AddTag(FilesOperationNameAttribute, operationName)
                    .AddTag(FilesProviderNameAttribute, _providerName);

                if (_serverAddress is not null)
                {
                    _ = activity
                        .AddTag(OpenTelemetryConsts.Server.Address, _serverAddress)
                        .AddTag(OpenTelemetryConsts.Server.Port, _serverPort);
                }
            }
        }

        return activity;
    }

    private void RecordDuration(Stopwatch? stopwatch, string operationName, Exception? error)
    {
        if (_operationDurationHistogram.Enabled && stopwatch is not null)
        {
            TagList tags = default;
            tags.Add(FilesOperationNameAttribute, operationName);
            tags.Add(FilesProviderNameAttribute, _providerName);

            if (_serverAddress is string address)
            {
                tags.Add(OpenTelemetryConsts.Server.Address, address);
                tags.Add(OpenTelemetryConsts.Server.Port, _serverPort);
            }

            if (error is not null)
            {
                tags.Add(OpenTelemetryConsts.Error.Type, error.GetType().FullName);
            }

            _operationDurationHistogram.Record(stopwatch.Elapsed.TotalSeconds, tags);
        }
    }
}
