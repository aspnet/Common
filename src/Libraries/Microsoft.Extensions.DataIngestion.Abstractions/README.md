# Microsoft.Extensions.DataIngestion.Abstractions

.NET developers need to efficiently process, chunk, and retrieve information from diverse document formats while preserving semantic meaning and structural context. The `Microsoft.Extensions.DataIngestion` libraries provide a unified approach for representing document ingestion components.

## The packages

The [Microsoft.Extensions.DataIngestion.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions) package provides the core exchange types, including [`IngestionDocument`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.ingestiondocument), [`IngestionChunker<T>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.ingestionchunker-1), [`IngestionChunkProcessor<T>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.ingestionchunkprocessor-1), and [`IngestionChunkWriter<T>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.ingestionchunkwriter-1). Any .NET library that provides document processing capabilities can implement these abstractions to enable seamless integration with consuming code.

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

Refer to the [Microsoft.Extensions.DataIngestion libraries documentation](https://learn.microsoft.com/dotnet/dataingestion/microsoft-extensions-dataingestion) for more information and API usage examples.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
