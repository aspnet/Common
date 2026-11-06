// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>
/// Builds a pipeline of document extraction middleware around a provider client.
/// </summary>
public sealed class DocumentExtractionClientBuilder
{
    private readonly Func<IServiceProvider, DocumentExtractionClient> _innerClientFactory;
    private readonly List<Func<DocumentExtractionClient, IServiceProvider, DocumentExtractionClient>> _factories = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentExtractionClientBuilder"/> class around an existing client.
    /// </summary>
    public DocumentExtractionClientBuilder(DocumentExtractionClient innerClient)
    {
        _ = Throw.IfNull(innerClient);
        _innerClientFactory = _ => innerClient;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentExtractionClientBuilder"/> class around a service-provider-aware factory.
    /// </summary>
    public DocumentExtractionClientBuilder(Func<IServiceProvider, DocumentExtractionClient> innerClientFactory)
    {
        _innerClientFactory = Throw.IfNull(innerClientFactory);
    }

    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    /// <returns>The updated builder.</returns>
    public DocumentExtractionClientBuilder Use(
        Func<DocumentExtractionClient, IServiceProvider, DocumentExtractionClient> factory)
    {
        _factories.Add(Throw.IfNull(factory));
        return this;
    }

    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    /// <returns>The updated builder.</returns>
    public DocumentExtractionClientBuilder Use(Func<DocumentExtractionClient, DocumentExtractionClient> factory)
    {
        _ = Throw.IfNull(factory);
        return Use((inner, _) => factory(inner));
    }

    /// <summary>
    /// Builds the configured pipeline.
    /// </summary>
    /// <param name="services">The service provider used by middleware factories.</param>
    /// <returns>The composed extraction client.</returns>
    public DocumentExtractionClient Build(IServiceProvider? services = null)
    {
        services ??= EmptyServiceProvider.Instance;
        DocumentExtractionClient client = _innerClientFactory(services)
            ?? throw new InvalidOperationException("The inner client factory returned null.");

        try
        {
            for (int index = _factories.Count - 1; index >= 0; index--)
            {
                client = _factories[index](client, services)
                    ?? throw new InvalidOperationException($"Middleware factory at index {index} returned null.");
            }

            return client;
        }
        catch (Exception failure)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }

            throw;
        }
    }
}
