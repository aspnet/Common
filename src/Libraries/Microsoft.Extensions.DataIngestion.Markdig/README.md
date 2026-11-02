# Microsoft.Extensions.DataIngestion.Markdig

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

using IngestionPipeline pipeline = new(reader, CreateChunker(), CreateWriter());
```

The reader returns an `IngestionDocument` envelope whose `ExtractedDocument` owns shared primitives in the `Microsoft.Extensions.Documents` namespace. `Sections` forwards that graph's exact list. Markdown text is retained as producer-supplied `TextContent`, with heading roles and levels, nested sections, addressable table cells, and image bytes or source references. Structural sections and tables do not retain duplicate aggregate Markdown payloads.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
