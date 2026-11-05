// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.AI;

/// <summary>Shared log methods for OpenTelemetry instrumentation classes.</summary>
internal static partial class OpenTelemetryLog
{
    [LoggerMessage(
        EventName = "gen_ai.client.operation.exception",
        Level = LogLevel.Warning,
        Message = "gen_ai.client.operation.exception")]
    internal static partial void OperationException(ILogger logger, Exception error);

    /// <summary>Stamps the operation error tag/status on <paramref name="activity"/> and logs the exception.</summary>
    /// <remarks>No-op when <paramref name="error"/> is <see langword="null"/>.</remarks>
    internal static void RecordOperationError(Activity? activity, ILogger? logger, Exception? error)
    {
        if (error is null)
        {
            return;
        }

        _ = activity?
            .AddTag(OpenTelemetryConsts.Error.Type, error.GetType().FullName)
            .SetStatus(ActivityStatusCode.Error, error.Message);

        if (logger is not null)
        {
            OperationException(logger, error);
        }
    }
}

