# Microsoft.Extensions.DataIngestion

.NET developers need to efficiently process, chunk, and retrieve information from diverse document formats while preserving semantic meaning and structural context. The `Microsoft.Extensions.DataIngestion` libraries provide a unified approach for representing document ingestion components.

## The packages

The [Microsoft.Extensions.DataIngestion.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions) package provides readers, document processors, chunkers, chunk processors, writers, and non-generic `AIContent` chunks. Document stages retain `IngestionDocument` as the ingestion envelope. Its `ExtractedDocument` holds the single shared content graph using the experimental `ExtractionDocument` family from `Microsoft.Extensions.Documents.Abstractions`, in the `Microsoft.Extensions.Documents` namespace. Any .NET library can produce these graphs without referencing `Microsoft.Extensions.DocumentExtraction`.

The [Microsoft.Extensions.DataIngestion](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion) package has an implicit dependency on the `Microsoft.Extensions.DataIngestion.Abstractions` package. This package enables you to easily integrate components such as enrichment processors, vector storage writers, and telemetry into your applications using familiar dependency injection and pipeline patterns. For example, it provides the [`SentimentEnricher`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.sentimentenricher), [`KeywordEnricher`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.keywordenricher), and [`SummaryEnricher`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion.summaryenricher) processors that can be chained together in ingestion pipelines.

## Which package to reference

Libraries that provide implementations of the abstractions typically reference only `Microsoft.Extensions.DataIngestion.Abstractions`.

To also have access to higher-level utilities for working with document ingestion components, reference the `Microsoft.Extensions.DataIngestion` package instead (which itself references `Microsoft.Extensions.DataIngestion.Abstractions`). Most consuming applications and services should reference the `Microsoft.Extensions.DataIngestion` package along with one or more libraries that provide concrete implementations of the abstractions, such as `Microsoft.Extensions.DataIngestion.MarkItDown` or `Microsoft.Extensions.DataIngestion.Markdig`.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.DataIngestion --prerelease
```
Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DataIngestion" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Writing chunks to a vector store

### Ingesting a shared document directly

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Documents;

ExtractionDocument extracted = new("producer-document", new TextContent("Root-local text."));
IngestionDocument document = new("ingestion-key", extracted);
document.Sections.Add(new ExtractionDocumentSection
{
    Elements = { new ExtractionDocumentTextElement(new TextContent("Plain text to ingest.")) }
});

using IngestionPipeline pipeline = new(
    new DocumentTokenChunker(new(tokenizer)),
    new VectorStoreWriter<IngestionChunkVectorRecord>(collection));

IngestionDocument processed = await pipeline.ProcessAsync(document);
```

The readerless constructor supports manually assembled shared graphs through an ingestion envelope; the existing constructor accepting an `IngestionDocumentReader` also supports file ingestion. `new IngestionDocument(identifier)` creates an empty shared graph. The two-argument constructor retains the supplied graph without copying. `Sections` forwards its exact `List<ExtractionDocumentSection>`, and `EnumerateContent()` forwards shared structural traversal. Root payload, metadata, source pages, and native state live only on `ExtractedDocument`. The ingestion `Identifier` is the writer's persistence key, independent of the producer's shared document identifier. Neither is rewritten.

Document processors can return the original envelope or a replacement. Pipeline `DocumentProcessors` and `ChunkProcessors` are concrete mutable `List` collections. Documents require no disposal, and reader implementations detach returned payload bytes before closing provider resources; direct ingestion propagates failures and cancellation. Existing reader-bound sequencing and failure results are unchanged. Retaining the ingestion root does not restore the former element hierarchy: adopting the shared element primitives is an intentional preview API break.

Chunkers consume each document, section, and element's local text payload once. `TextContent.Text` is authoritative for text nodes; producer-supplied text is preserved without forcing Markdown. Heading levels can be any positive value, without allocating storage proportional to the level. Structural tables use tab-separated semantic row text, with repeated column headers only when the producer explicitly identifies them. This is embedding/chunk content, not a display renderer. Images prefer `AlternativeText`, falling back to image-only OCR `Text`; an available image `Source` remains the original asset reference, independent of the current `DataContent` or `UriContent` payload. Token limits include context and separators.

`DocumentTokenChunker`, `SectionChunker`, `HeaderChunker`, and `SemanticSimilarityChunker` consume the shared graph, including root-local and section-local text, without aggregating that text into the envelope. Image enrichment updates shared image nodes, including nested table-cell images.

`IngestionChunk.Document` retains the envelope. Its read-only `SourceElements` snapshot retains contributing shared elements and their source graph, including heading context and nested sections. Source frame identifiers and physical page numbers are not assembly positions or durable citations. `ExtractedDocument.SourcePages` retains blank/unnumbered frames; page dimensions, coordinate units/origin and source polygons remain available for region interpretation. Borrowed native objects in `ExtractedDocument.RawRepresentation` or source-reference `GetRawRepresentation()` are excluded from semantic chunk text and default storage.

### Configuring the vector store

The vector store must be configured with an embedding generator that accepts `AIContent` (or a type that derives from `AIContent`) inputs. This allows the ingestion pipeline to pass the original chunked content directly for embedding generation:

```csharp
IEmbeddingGenerator<TextContent, Embedding<float>> textContentEmbeddingGenerator =
    stringEmbeddingGenerator.AsTextContentEmbeddingGenerator();

using VectorStore vectorStore = new InMemoryVectorStore(new()
{
    EmbeddingGenerator = textContentEmbeddingGenerator
});
```

### Basic usage

The simplest way to store ingestion chunks in a vector store is to use the `GetIngestionRecordCollection` extension method to create a collection, and then pass it to a `VectorStoreWriter`:

```csharp
VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
    vectorStore.GetIngestionRecordCollection("chunks", dimensionCount: 1536);

using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);

await writer.WriteAsync(chunks);
```

### Custom metadata

To store custom metadata alongside each chunk, create a type derived from `IngestionChunkVectorRecord` with additional properties, and a `VectorStoreWriter` subclass that overrides `SetMetadata`:

```csharp
public class ChunkWithMetadata : IngestionChunkVectorRecord
{
    [VectorStoreVector(1536)]
    public override AIContent? Embedding => Content;

    [VectorStoreData(StorageName = "classification")]
    public string? Classification { get; set; }
}

public class MetadataWriter : VectorStoreWriter<ChunkWithMetadata>
{
    public MetadataWriter(VectorStoreCollection<Guid, ChunkWithMetadata> collection)
        : base(collection) { }

    protected override void SetMetadata(ChunkWithMetadata record, string key, object? value)
    {
        switch (key)
        {
            case nameof(ChunkWithMetadata.Classification):
                record.Classification = value as string;
                break;
            default:
                throw new UnreachableException($"Unknown metadata key: {key}");
        }
    }
}
```

### Persisting durable source references

Override `VectorStoreWriter<TRecord>.CreateRecord(IngestionChunk)` to map application-defined source identifiers into a typed record. Inspect `chunk.SourceElements`, their `Sources`, and table cells without serializing the complete document or raw provider output. Call `base.CreateRecord(chunk)` to preserve `AIContent`, context, and document identity. The collection's auto-generated key schema assigns a fresh `Guid` when inserting each record.

`TRecord` is the stored record type, not the key type or chunk content type. `WriteAsync` accepts chunks from one document instance. Incremental ingestion inserts all new records before deleting previous keys, so overrides must not reuse those previous keys.

### Custom collection schema

To map to a pre-existing collection that uses different storage names, create a `VectorStoreCollectionDefinition` manually:

```csharp
VectorStoreCollectionDefinition definition = new()
{
    Properties =
    {
        new VectorStoreKeyProperty(nameof(IngestionChunkVectorRecord.Key), typeof(Guid))
            { StorageName = "my_key" },
        new VectorStoreVectorProperty<AIContent>(nameof(IngestionChunkVectorRecord.Embedding), 1536)
            { StorageName = "my_embedding" },
        new VectorStoreDataProperty(nameof(IngestionChunkVectorRecord.SerializedContent), typeof(string))
            { StorageName = "my_content" },
        new VectorStoreDataProperty(nameof(IngestionChunkVectorRecord.Context), typeof(string))
            { StorageName = "my_context" },
        new VectorStoreDataProperty(nameof(IngestionChunkVectorRecord.DocumentId), typeof(string))
            { StorageName = "my_documentid", IsIndexed = true },
    },
};

VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
    vectorStore.GetCollection<Guid, IngestionChunkVectorRecord>("chunks", definition);
using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
