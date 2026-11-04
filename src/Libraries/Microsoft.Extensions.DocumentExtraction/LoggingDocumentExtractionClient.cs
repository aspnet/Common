// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>
/// Logs extraction lifecycle events without logging source content, roles, artifacts, or provider state.
/// </summary>
public partial class LoggingDocumentExtractionClient : DelegatingDocumentExtractionClient
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingDocumentExtractionClient"/> class.
    /// </summary>
    public LoggingDocumentExtractionClient(DocumentExtractionClient innerClient, ILogger logger)
        : base(innerClient)
    {
        _logger = Throw.IfNull(logger);
    }

    /// <inheritdoc />
    public override async Task<ExtractionDocument> ExtractAsync(
        System.IO.Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LogInvoked();

        ExtractionDocument result;
        try
        {
            result = await base.ExtractAsync(document, identifier, mediaType, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException failure)
        {
            LogFailure(failure, canceled: true);
            throw;
        }
        catch (Exception failure)
        {
            LogFailure(failure, canceled: false);
            throw;
        }

        LogCompleted();
        return result;
    }

    private void LogFailure(Exception failure, bool canceled)
    {
        try
        {
            if (canceled)
            {
                LogCanceled();
            }
            else
            {
                LogFailed();
            }
        }
        catch (Exception loggingFailure)
        {
            throw new AggregateException(failure, loggingFailure);
        }
    }

    [LoggerMessage(LogLevel.Debug, "Document extraction invoked.")]
    private partial void LogInvoked();

    [LoggerMessage(LogLevel.Debug, "Document extraction completed.")]
    private partial void LogCompleted();

    [LoggerMessage(LogLevel.Debug, "Document extraction canceled.")]
    private partial void LogCanceled();

    [LoggerMessage(LogLevel.Error, "Document extraction failed.")]
    private partial void LogFailed();
}
