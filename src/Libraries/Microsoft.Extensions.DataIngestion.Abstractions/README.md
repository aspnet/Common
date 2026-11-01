# Microsoft.Extensions.DataIngestion.Abstractions

.NET developers need to efficiently process, chunk, and retrieve information from diverse document formats while preserving semantic meaning and structural context. The `Microsoft.Extensions.DataIngestion` libraries provide a unified approach for representing document ingestion components.

## The packages

The [Microsoft.Extensions.DataIngestion.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions) package provides `IngestionDocumentReader`, `IngestionDocumentProcessor`, `IngestionChunker`, `IngestionChunkProcessor`, `IngestionChunkWriter`, and the non-generic `IngestionChunk`. Readers and document processors exchange the experimental `ExtractionDocument` family from `Microsoft.Extensions.Documents.Abstractions`, in the `Microsoft.Extensions.Documents` namespace. Ingestion does not define a separate document model or depend on an extraction client.

The [Microsoft.Extensions.DataIngestion](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion) package has an implicit dependency on the `Microsoft.Extensions.DataIngestion.Abstractions` package. This package enables you to easily integrate components such as enrichment processors, vector storage writers, and telemetry into your applications using familiar dependency injection and pipeline patterns. For example, it provides processors for sentiment analysis, keyword extraction, and summarization that can be chained together in ingestion pipelines.

## Which package to reference

Libraries that provide implementations of the abstractions typically reference only `Microsoft.Extensions.DataIngestion.Abstractions`.

To also have access to higher-level utilities for working with document ingestion components, reference the `Microsoft.Extensions.DataIngestion` package instead (which itself references `Microsoft.Extensions.DataIngestion.Abstractions`). Most consuming applications and services should reference the `Microsoft.Extensions.DataIngestion` package along with one or more libraries that provide concrete implementations of the abstractions, such as `Microsoft.Extensions.DataIngestion.MarkItDown` or `Microsoft.Extensions.DataIngestion.Markdig`.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.DataIngestion.Abstractions --prerelease
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DataIngestion.Abstractions" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Documentation

Documents retain ordered nested sections, tables, addressable cells, and producer-supplied source references. Each document or element has an optional local `AIContent` payload; section and table payloads are not aggregate copies of their descendants. `TextContent` can contain plain text or producer-supplied formatted text without a mandatory Markdown convention.

Chunks retain their originating `ExtractionDocument` and expose `SourceElements` for contributing content and context. Follow these elements, their table cells, and their `Sources` to inspect the original source graph. A source page's nullable positive `PageNumber` describes a physical source page, not a section index. Missing associations do not imply page one. Producer-local identifiers are not durable citation keys.

Documents and detached content artifacts are data and require no disposal. Reader implementations must detach returned bytes before closing provider responses or temporary streams. Native objects exposed for explicit inspection remain borrowed; chunkers and writers must not automatically embed or persist raw provider output.

Refer to the [Microsoft.Extensions.DataIngestion libraries documentation](https://learn.microsoft.com/dotnet/dataingestion/microsoft-extensions-dataingestion) for more information and API usage examples.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
