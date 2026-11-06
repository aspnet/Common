# Microsoft.Extensions.DataIngestion.Markdig

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

Provides an implementation of the `IngestionDocumentReader` class for the Markdown files using [MarkDig](https://github.com/xoofx/markdig) library.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.DataIngestion.Markdig --prerelease
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DataIngestion.Markdig" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Examples

### Creating a MarkdownReader for Data Ingestion

```csharp
using Microsoft.Extensions.DataIngestion;

IngestionDocumentReader reader = new MarkdownReader();

using IngestionPipeline<string> pipeline = new(reader, CreateChunker(), CreateWriter());
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
