# Microsoft.Extensions.DocumentExtraction

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

Add middleware, lifecycle logging and dependency-injection registration around a
configured `DocumentExtractionClient`. Your provider still performs extraction;
**this package is not an OCR engine or a provider SDK**.

Use `Microsoft.Extensions.DocumentExtraction.Abstractions` alone if you only need
to call or implement the client. The returned document uses the shared
[Microsoft.Extensions.Documents.Abstractions](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Documents.Abstractions/README.md)
model, which can also be used independently of extraction.

## Install

```shell
dotnet add package Microsoft.Extensions.DocumentExtraction --prerelease
```

Install and configure a provider separately.

Direct dependencies are `Microsoft.Extensions.DocumentExtraction.Abstractions`,
`Microsoft.Extensions.DependencyInjection.Abstractions` and
`Microsoft.Extensions.Logging.Abstractions`. The shared Documents and AI content
types are transitive dependencies. Prefer .NET 8 or later; compatibility assets
for .NET Framework and .NET Standard do not guarantee support for every older
runtime. Check package compatibility and the provider's runtime requirements.

## Build a logging pipeline

This helper accepts a factory that creates a configured provider client and an
application-owned logger factory. The returned document is ready to consume when
the task completes. The helper owns and disposes its pipeline, but leaves the
supplied stream and logger factory open.

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;

public static class LoggingPipeline
{
    public static async Task<ExtractionDocument> RunAsync(
        Func<DocumentExtractionClient> createClient, ILoggerFactory loggerFactory,
        Stream input, string identifier, string mediaType,
        CancellationToken cancellationToken = default)
    {
        using DocumentExtractionClient client =
            new DocumentExtractionClientBuilder(_ => createClient())
                .UseLogging(loggerFactory)
                .Build();

        return await client.ExtractAsync(
            input, identifier, mediaType, cancellationToken: cancellationToken);
    }
}
```

Use a factory that creates a new client for each independently owned pipeline;
do not share an inner client across pipelines that will dispose it.
Add custom middleware with `Use(inner => new YourMiddleware(inner))`, where
`YourMiddleware` derives from `DelegatingDocumentExtractionClient`. The first
middleware registered is outermost.

## Register with dependency injection

This independent example additionally uses `Microsoft.Extensions.DependencyInjection`
and `Microsoft.Extensions.Logging`. Pass your provider factory and the
application's logging configuration; a console logging configuration would also
require `Microsoft.Extensions.Logging.Console`.

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;

public static class ScopedExtraction
{
    public static async Task<ExtractionDocument> RunAsync(
        Func<IServiceProvider, DocumentExtractionClient> createClient,
        Action<ILoggingBuilder> configureLogging,
        Stream input, string identifier, string mediaType,
        CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddLogging(configureLogging);
        services.AddDocumentExtractionClient(createClient, ServiceLifetime.Scoped)
            .UseLogging();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();
        DocumentExtractionClient client =
            scope.ServiceProvider.GetRequiredService<DocumentExtractionClient>();

        return await client.ExtractAsync(
            input, identifier, mediaType, cancellationToken: cancellationToken);
    }
}
```

`UseLogging()` without an explicit factory resolves `ILoggerFactory` from the
services used to build the pipeline. Without a factory, it adds no logging wrapper.
Invocation, completion and cancellation events use Debug; failure events use Error.

Factory registrations support singleton, scoped and transient lifetimes. An
existing-client registration supports only singleton. The container owns and
disposes registered pipelines and their inner clients; do not separately dispose
a resolved client. In an existing host, register with the host's service
collection rather than building another container as this standalone helper does.

## Select a keyed client

Use keyed registration when an application needs separately configured clients.
This independent helper needs `Microsoft.Extensions.DependencyInjection`.
The key selects the DI registration; it is not the per-call
`DocumentExtractionOptions.ExtractorName`, which selects within a provider.

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

public static class KeyedExtraction
{
    public static async Task<ExtractionDocument> RunAsync(
        Func<IServiceProvider, DocumentExtractionClient> createClient,
        Stream input, string identifier, string mediaType,
        CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddKeyedDocumentExtractionClient(
            "documents", createClient, ServiceLifetime.Scoped);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        DocumentExtractionClient client =
            scope.ServiceProvider.GetRequiredKeyedService<DocumentExtractionClient>("documents");

        return await client.ExtractAsync(
            input, identifier, mediaType, cancellationToken: cancellationToken);
    }
}
```

## Ownership and errors

Callers own input streams. Results are completed, non-disposable document data,
not page streams. Borrowed native objects are not implicitly disposed and may
contain sensitive data. Do not mutate options or their referenced values while a
call is in progress. Pass cancellation tokens through to your provider.

Lifecycle logging does not log document content, options, exception details or
provider payloads. When logging succeeds, results and provider failures are
forwarded unchanged. Logger failures are observable: invocation logging can
prevent the provider call, and completion logging can prevent result delivery.
If both a provider failure/cancellation and terminal logging fail,
`AggregateException` contains the original provider exception first and the logger
exception second; the operation faults rather than completing as canceled.
If pipeline construction and cleanup both fail, the construction exception is
likewise first in the aggregate.

## Troubleshooting and feedback

If logging is silent, supply a real logger factory or configure logging in DI,
and enable Debug for lifecycle events. Match keyed/unkeyed registration with the
corresponding resolution method, and resolve scoped clients inside a scope.
For unsupported formats or settings, consult the selected provider.

Extraction APIs are experimental under `MEDE0001`; Documents uses `EXTEXP0019`.
Acknowledge these diagnostics only if you accept experimental APIs.
Report composition issues through
[dotnet/extensions issues](https://github.com/dotnet/extensions/issues);
report provider-specific behavior to the provider's issue tracker.
