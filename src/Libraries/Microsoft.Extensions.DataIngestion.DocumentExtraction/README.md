# Microsoft.Extensions.DataIngestion.DocumentExtraction

An optional reader integration between document extraction and data ingestion. The core ingestion packages reference only the shared Documents model, not extraction clients or provider SDKs.

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;

IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient, extractionOptions);
ExtractionDocument document = await reader.ReadAsync(stream, "document-id", "application/pdf", cancellationToken);
```

`DocumentExtractionReader` returns the exact shared document supplied by `IDocumentExtractionClient`. It does not stringify, render, or reparse the result. Nested sections, table cells, local payloads, source operations, and source references retain their identity.

The reader borrows the client and input stream. It snapshots options at construction and again for each call, and passes cancellation to extraction. Returned documents and detached byte artifacts are non-disposable. Extraction clients remain responsible for detaching returned bytes before releasing provider resources.
