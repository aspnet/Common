# Microsoft.Extensions.DataIngestion.DocumentExtraction

An optional reader integration between document extraction and data ingestion. The core ingestion packages reference only the shared Documents model, not extraction clients or provider SDKs.

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;

IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient, extractionOptions);
IngestionDocument document = await reader.ReadAsync(stream, "document-id", "application/pdf", cancellationToken);
ExtractionDocument extracted = document.ExtractedDocument;
```

`DocumentExtractionReader` creates an `IngestionDocument` envelope using the requested ingestion identifier. `ExtractedDocument` is the exact shared document supplied by `DocumentExtractionClient`, not a copy. Its producer identifier can differ from the ingestion persistence key, and neither is rewritten. The reader does not stringify, render, or reparse the result. Nested sections, table cells, local payloads, source pages/polygons, source references, and optional `ExtractedDocument.RawRepresentation` retain their identity.

The preview extraction contract is now an abstract `DocumentExtractionClient`, replacing `IDocumentExtractionClient` entirely. Rebuild consumers and derive custom clients from the new class, overriding `ExtractAsync`; move owned-resource cleanup to `protected override void Dispose(bool disposing)` and call `base.Dispose(disposing)`. Update extraction DI registrations and lookups to the new class, including keyed lookups. There is no interface shim or compatibility adapter. The reader's constructor accepts the new class with the same optional options argument; `ReadAsync` still accepts a borrowed `Stream` and returns `Task<IngestionDocument>`. The core ingestion packages remain independent of extraction.

The reader borrows the client and input stream. It snapshots options at construction and again for each call, and passes cancellation to extraction. Returned documents are non-disposable. Extraction clients remain responsible for detaching returned payload bytes before releasing provider resources. Native objects remain borrowed for explicit inspection; the bridge does not dispose them or capture raw response artifacts. Shared options have no raw-content capture switch.
