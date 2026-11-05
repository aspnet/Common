# Microsoft.Extensions.DocumentExtraction

These experimental packages provide a small extraction capability and optional
composition middleware over the standalone shared Documents exchange model.

`Microsoft.Extensions.DocumentExtraction.Abstractions` defines
`DocumentExtractionClient.ExtractAsync(Stream, identifier, mediaType, options,
cancellationToken)`, returning `Task<Microsoft.Extensions.Documents.ExtractionDocument>`
directly. There is no required public page/observation staging model, finalization
builder or result wrapper. Adapters build the canonical document and clean up their
provider resources on success, failure and cancellation. The returned document is
non-disposable data. Caller streams and native document, payload and source objects
remain borrowed; the result does not take implicit ownership.

`Microsoft.Extensions.DocumentExtraction` supplies the optional
`DocumentExtractionClientBuilder`, dependency-injection registration and lifecycle
logging. Delegation snapshots per-call options without mutating arguments or
shared defaults. Logging records only invocation, completion, cancellation and
failure; it does not serialize documents, source references, roles, options,
exceptions, credentials or provider payloads. When lifecycle logging succeeds,
the returned document and original failure/cancellation are forwarded unchanged.
Invocation logging precedes the provider call; if it throws, the provider is not
called. A completion logging failure prevents delivery of the returned document.
If failure or cancellation logging also throws, the resulting `AggregateException`
contains the original provider exception first and the logging exception second.
`DocumentExtractionOptions.ExtractorName`
selects within the configured provider, not the DI provider; null/default and opaque
selector semantics are preserved. Reported provider model identity remains
independently inspectable on native results when supplied.

## Migrating the preview client contract

The former `IDocumentExtractionClient` interface is replaced entirely by the
abstract `DocumentExtractionClient` class. This is a source- and binary-breaking
preview change: rebuild implementers and consumers together. There is no
compatibility interface or adapter. Change implementations from
`class MyClient : IDocumentExtractionClient` to
`class MyClient : DocumentExtractionClient` and make `ExtractAsync` an override.
The Stream input and `Task<ExtractionDocument>` result are unchanged.

Change caller declarations and factory return types to `DocumentExtractionClient`.
Resolve unkeyed registrations with
`GetRequiredService<DocumentExtractionClient>()` and keyed registrations with
`GetRequiredKeyedService<DocumentExtractionClient>(key)`. The builder and both
DI registration paths now use that same class; lifetime and pipeline ownership
rules are unchanged.

The abstract root provides virtual unkeyed self-service lookup and optional
protected `Dispose(bool)` cleanup. Move implementation cleanup into that override,
not a hidden public `Dispose` method. It adds no global disposed-state policy.
The class allows future operations to have useful virtual defaults without
requiring every provider to implement them immediately, at the cost of using
the provider's base-class slot. This change adds no operations, streaming,
PipeReader overloads or async disposal. Future operations still need defined
ownership, failure and middleware-interception semantics; discovering an inner
provider is not permission to bypass the configured pipeline.

Registering an existing client instance requires `ServiceLifetime.Singleton`;
unsupported lifetimes are rejected before changing the service collection.
The container disposes the composed singleton pipeline and inner client.
Factory and keyed-factory registrations support singleton, scoped and transient
lifetimes; factories must supply independent clients for independent pipelines.
If pipeline construction and cleanup both fail, the aggregate preserves the
construction error first and the cleanup error second.

Documents works without Extraction. Extraction depends on Documents, not MEDI.
MEDI and other consumers can accept the same shared document from any producer
without requiring an Extraction client. Composition is optional: these libraries
can be used independently and work together through the exchanged document.

Provider mappings preserve nullable/custom text roles and heading levels, images,
table spans and cell roles, typed source geometry and blank-page registration.
Local standard `AIContent` payloads are inspected directly through the hierarchy,
without a structural renderer or a required Markdown convention.
Text elements use `TextContent.Text` as their sole authoritative text, with no
format normalization. Image OCR/transcription `Text` and `AlternativeText` remain
separate; captions belong in `AlternativeText`. Source asset references are
independent of payload URIs. Frames are added explicitly to the ordinary
`SourcePages` list;
page numbers are nullable and positive when supplied, never inferred from assembly
order. Native source objects preserve specialized SDK information
without adding generic element metadata, arbitrary artifact bags or normalized
provider-specific schemas.

Optional `ExtractionDocument.RawRepresentation` supports native inspection even
with zero sections or no payload and is ignored by default System.Text.Json
serialization. Existing payload and source-reference native access remains.
Borrowed native objects are not implicitly disposed or deep-cloned and may contain
sensitive data. There is no shared capture option, artifact/status graph, operation
registration, original HTTP-byte guarantee or durable serialization guarantee.
Adapters still clean up all owned provider/parser/response/temp resources and
preserve primary errors if cleanup also fails.

The packages do not include provider clients, provider SDK dependencies,
MEDI integration, source fetching or package publication.
Production targets are net11.0, net10.0, net9.0, net8.0, net462 and netstandard2.0.
Public APIs use experimental diagnostic `MEDE0001`; Documents uses `EXTEXP0019`.

Local candidates are provisional, not API-approved or published packages. In-memory validation is not
live-provider, full-application or MEDI end-to-end acceptance.
