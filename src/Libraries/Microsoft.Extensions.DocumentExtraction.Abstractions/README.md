# Microsoft.Extensions.DocumentExtraction.Abstractions

This experimental package defines a small extraction capability over the standalone
`Microsoft.Extensions.Documents.Abstractions` exchange model. It does not define a
second document tree or require an authoring, page-streaming or observation protocol.

The primary operation is:

```csharp
Task<ExtractionDocument> ExtractAsync(
    Stream document,
    string identifier,
    string mediaType,
    DocumentExtractionOptions? options = null,
    CancellationToken cancellationToken = default);
```

An adapter returns the canonical `Microsoft.Extensions.Documents.ExtractionDocument` directly.
The caller owns the input stream. The returned document is non-disposable data.
Adapters clean up
their parser, response, stream and temporary resources on success, failure and
cancellation, must attempt every acquired resource, and must preserve the primary
error if cleanup also fails. They must not dispose caller streams or borrowed native
SDK objects.

Documents can be constructed or consumed without Extraction. Extraction depends on
Documents, not MEDI or provider SDK packages. Documents uses standard AIContent
payloads from MEAI. MEDI or another consumer can accept
the same shared document from any producer without requiring Extraction. The
packages compose through the exchanged document rather than a mandatory pipeline.

## Mapping and source fidelity

Adapters map provider output to shared text, image, table and section elements.
Text roles such as `heading`, `pageHeader`, `pageFooter`, custom roles and optional
heading levels are preserved without inventing unspecified information.
Local standard `AIContent` payloads are inspected directly through the hierarchy.
The shared model has no structural renderer or required Markdown convention.
Text elements retain exactly the supplied `TextContent.Text` as their authoritative
text, with no duplicate plain-text view. Image OCR/transcription `Text` is independent
of image content and `AlternativeText`; captions and descriptions belong in
`AlternativeText`. Heading levels are nullable or positive, without an arbitrary cap.

Typed `Sources` retain source pages, coordinate frames and ordered polygons, with
explicit native-object access. A source without a polygon identifies a page, not a
precise location. Adapters add real blank-page frames even when no element refers to
them. Adding references does not populate the ordinary page list;
call `document.SourcePages.Add(frame)` explicitly. Page numbers are nullable and positive when present;
named frames do not acquire a number from assembly position. Table spans, roles and sources are independent
of whether a cell has content.

The Extraction contract has no staged element metadata, arbitrary semantic-artifact
bags, generalized detections or extension-element schema. Specialized SDK data is
available through borrowed native source objects; document-level metadata remains
available for consumers to populate explicitly. No source association establishes
verified grounding or calibrated confidence.

## Per-call options and break-glass output

`DocumentExtractionOptions.ExtractorName` selects an extractor/analyzer within an
already configured provider; it does not select the DI provider. Null uses that
provider's default, and nonblank opaque selectors are preserved verbatim. Genuine
reported provider model identity remains independent and can be inspected on the
native output when available. Options also provide an
adapter-specific `AdditionalProperties` dictionary. Providers must reject unsupported
explicit settings, create fresh requests per operation, and avoid mutating caller
arguments or shared defaults. Implementations must snapshot options before
asynchronous work. `DelegatingDocumentExtractionClient` copies scalar values and
dictionary entries before forwarding; opaque values remain borrowed and callers
must not mutate them while an operation is in flight.

`ExtractionDocument.RawRepresentation` optionally exposes the borrowed native
result, even for zero sections and no payload, and is ignored by default
System.Text.Json serialization. Standard `AIContent.RawRepresentation` and
source-reference `GetRawRepresentation()` retain local native inspection. None is
implicitly disposed or deep-cloned. Native objects may contain sensitive response
data: access is not redaction and must not automatically enter logs, embeddings or
ingestion records. Providers must not capture credentials or authorization headers.
The shared contract defines no raw-capture option, artifact/status graph, operation
registration, original-wire-byte guarantee or durable serialization guarantee.

All public Extraction APIs are experimental under `MEDE0001`; shared Documents uses
`EXTEXP0019`. The package does not include provider implementations, MEDI adapters,
URI fetching, progress/usage APIs, configuration middleware or OpenTelemetry.
