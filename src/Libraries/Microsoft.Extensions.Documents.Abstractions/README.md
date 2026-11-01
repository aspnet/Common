# Microsoft.Extensions.Documents.Abstractions

`Microsoft.Extensions.Documents.Abstractions` is an **experimental**, provider-neutral in-memory model for structured documents. It provides a shared handoff so independently implemented producers and consumers can compose without depending on each other's SDKs or an ingestion framework. It is not an extraction engine or a durable interchange format.

Extraction adapters, application-created content, source viewers and ingestion integrations are examples of components that can use this boundary. Payloads use the standard `AIContent` types from `Microsoft.Extensions.AI.Abstractions`. The model does not depend on DataIngestion, Markdig, provider SDKs, credentials or network calls.

The model contains an ordered section hierarchy. Sections contain ordered `ExtractionDocumentElement` instances, including nested sections, text, images and tables. Applications can derive their own element types when needed. `ExtractionDocument.EnumerateContent()` preserves its depth-first traversal of non-section elements: it returns tables as elements, not flattened cells, and does not return document/section-local payloads. Inspect the hierarchy and grid directly for those payloads. This package has no structural renderer or public rendering methods.

## Intentional API boundaries

- `ExtractionDocumentElement.Content` and `ExtractionDocument.Content` hold optional **local** `AIContent` payloads. Use `TextContent` for text, `DataContent` for bytes with MIME, and `UriContent` for references with MIME. Structural containers can have null content. Do not populate ancestors with aggregate copies of their descendants.
- `TextContent` does not itself specify a textual format. Plain text and producer-supplied formatted text are preserved without conversion or a required Markdown convention. The independent `Text` property can retain meaningful producer-supplied plain/OCR text. It is not automatically generated or synchronized.
- `ExtractionDocumentImageElement.Content` is inherited from `ExtractionDocumentElement`, not a second byte-valued slot. MIME is supplied by `DataContent` or `UriContent`; there is no separate mutable `MediaType` to disagree with it. `Source` remains an independent original image reference, which can coexist with bytes. `AlternativeText` remains separate semantic information. No URI is fetched.
- `ExtractionDocumentTextElement.Role` is optional: unspecified text needs no classification. Common roles are `heading`, `pageHeader` and `pageFooter`; custom values are retained verbatim. Consumers should compare recognized roles case-insensitively and treat unrecognized roles as ordinary text. Repeated page headers are not document headings. Optional `HeadingLevel` is between 1 and 10; an absent level does not imply a hierarchy level.
- `Metadata` is an application-property bag on the document, not on every element. For example, an application can retain the original file's hash for citation identity. Provider-specific confidence, detections and transforms are not standardized by this model; native observations remain available through source references.
- `Sources` on an element or table cell contains `ExtractionDocumentSourceReference` instances: a typed source page, an optional polygon and explicit borrowed native-observation access. A page-only reference has no polygon. References preserve order and duplicates; they do not verify content or establish durable citation identity.
- `ExtractionDocumentSourcePage` identifies an originating page and coordinate frame separately from assembly order. `PageNumber` is nullable and positive when supplied; named or unnumbered frames keep their identity without fabricating a number. Dimensions, coordinate unit and origin are optional: unknown values stay unknown. Geometry uses explicit readonly value structs with coordinate-based equality. A source reference copies every supplied polygon vertex in order; it does not reduce rotated regions to rectangles.
- Table cells are constructed through the row/column indexer and can contain any non-section element, including images, or retain an empty anchor with sources. Positive row/column spans describe merged cells; covered positions do not contain a second anchor. Optional `Role` describes a cell classification such as `columnHeader` or `rowHeader`, not the content's CLR type. The grid rejects overlapping/out-of-bounds spans and reuse of a cell instance at multiple addresses. This is a constructible exchange model, not a document editor.
- Mutable collections use ordinary `List<T>` and `Dictionary<TKey, TValue>` properties. Composition does not attach elements to a parent, register sources, or track mutation versions. Elements can be shared. Structural traversal rejects null entries and cyclic section paths while returning every occurrence of shared acyclic content. It does not recurse into table cells; the typed grid validates its own cell coordinates and spans. Consumers performing additional recursive traversal must validate their active paths.
- `ExtractionDocumentArtifact` copies original response bytes into managed memory and retains their media type and optional source URI. The document and artifact are non-disposable data. Elements do not expose an additional artifact collection. The package does not fetch or interpret artifacts.
- Applications supply their own durable identifiers and revisions; the model has no editing-framework ownership, address, mutation-version or disposal bookkeeping.

## Source output and lifetime

`ExtractionDocumentSourceOutput` represents one source operation. Call `AddSourceOutput` or `AddSourcePage` explicitly; registering a page also registers its supplied output, but adding an element or source reference does not register either. Multiple pages may reference the same output instance. Operation identifiers identify one output instance within the document, and frame identifiers identify one frame instance within their operation. `SourcePages` and `SourceOutputs` inventory registered frames and operations, including blank pages with no content elements.

`GetRawRepresentation()` exposes a borrowed native result when supplied, or null, independently of response-content capture. Disabling content capture never disables native inspection and is not redaction of the native object. `GetRawContent()` exposes an artifact containing a detached managed copy of the exact original response bytes when available, **not** a reserialization of mapped SDK objects. `ContentStatus` distinguishes retained, unavailable and explicitly disabled content capture; retained empty bytes are distinct from unavailable output. `ExtractionDocumentSourceOutput` accepts native output even when `contentCaptureDisabled` is true, but rejects a response-content artifact in that case. Artifacts can be shared across results, including results with zero sections. Providers clean up their own parser, response, stream and temporary resources before returning data, including on failure or cancellation. Borrowed native objects, caller streams and metadata values are not implicitly disposed.

Source references, metadata and raw outputs do not enter `Text` or local payloads automatically. There is no automatic logging, embedding, persistence or document wire serialization contract. Consumers must deliberately select which supporting data to inspect or persist and protect sensitive response content. Null content is distinct from an explicitly empty `TextContent`; neither fabricates a text payload for a structural container.

## Payload ownership and memory

Wrapping an existing string in `TextContent` reuses that string, but allocates a payload object. `DataContent` retains supplied byte memory rather than detaching it. Producers must copy provider-owned/recycled buffers before publishing; stable application-owned buffers can be reused. Payloads, their metadata and their source references are not deep-cloned by this model.

Payloads remain mutable. Replacing `Content`, changing `TextContent.Text` or editing collections does not notify the document. A consumer that retains an independently serialized snapshot must refresh it explicitly after mutation.

The `ExtractionDocumentTextElement` constructor requires `TextContent`, but its inherited mutable `Content` property remains `AIContent?`; the constructor does not establish a permanent text-only invariant. Text-only consumers must validate the current payload kind rather than cast based on the element's constructor. The document does not freeze or dispose its borrowed payload objects.

The memory improvement comes from not storing repeated descendant aggregates. Explicitly serializing `DataContent` can create and retain a base64 data URI alongside its original bytes. Text-only consumers should deliberately select semantic text or reject media; they must not obtain embeddings by converting binary payloads to strings.

Generalized annotations/confidence, opaque evidence/page-reference wrappers, per-element metadata/artifact bags, a generic extension envelope, first-class list/item/nesting semantics and normalized barcode/language/link/email/address schemas are outside this first-pass surface. Provider-specific information remains available through native inspection; the package does not perform recognition, infer grounding or define provider-specific feature flags.

The API is subject to change during preview and API review. It is not a replacement for the existing `Microsoft.Extensions.DataIngestion` APIs in this slice.

The package's public API is marked experimental with diagnostic `EXTEXP0019`. Consuming applications can suppress that diagnostic explicitly while evaluating the package; it does not provide compatibility or serialization guarantees.

## Example

```csharp
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.AI;

ExtractionDocument document = new("invoice-42");
ExtractionDocumentSection section = new();
section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("# Invoice"), "Invoice", role: "heading") { HeadingLevel = 1 });
section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("Total: $10"), "Total: $10"));
document.Sections.Add(section);

foreach (ExtractionDocumentElement element in document.EnumerateContent())
{
    if (element is ExtractionDocumentTextElement text)
    {
        Console.WriteLine(text.Text);
    }
}
```
