// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>
/// Provides logging middleware configuration for extraction pipelines.
/// </summary>
public static class LoggingDocumentExtractionClientBuilderExtensions
{
    /// <summary>
    /// Adds lifecycle logging to the extraction pipeline.
    /// </summary>
    /// <returns>The updated builder.</returns>
    public static DocumentExtractionClientBuilder UseLogging(
        this DocumentExtractionClientBuilder builder,
        ILoggerFactory? loggerFactory = null)
    {
        _ = Throw.IfNull(builder);
        return builder.Use((inner, services) =>
        {
            ILoggerFactory resolvedFactory = loggerFactory ?? services.GetService(typeof(ILoggerFactory)) as ILoggerFactory
                ?? NullLoggerFactory.Instance;

            if (resolvedFactory == NullLoggerFactory.Instance)
            {
                return inner;
            }

            LoggingDocumentExtractionClient client = new(
                inner,
                resolvedFactory.CreateLogger(typeof(LoggingDocumentExtractionClient)));
            return client;
        });
    }
}
