# Microsoft.Extensions.DocumentExtraction.Abstractions

Extract structured content through a common .NET client contract, without tying
your application to a particular provider SDK. `DocumentExtractionClient` accepts
a document `Stream` and returns a completed `ExtractionDocument` containing text,
images, tables and sections.

This package defines the client, per-call options and a base for delegating
middleware. **It does not include an OCR engine or a provider implementation.**
Choose a provider that supports your document formats and environment.

Use the companion `Microsoft.Extensions.DocumentExtraction` package if you also
want pipeline builders, logging and dependency-injection registration. If you only
need to construct or consume the document graph, use
[Microsoft.Extensions.Documents.Abstractions](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Documents.Abstractions/README.md).

## Install

```shell
dotnet add package Microsoft.Extensions.DocumentExtraction.Abstractions --prerelease
```

Install your chosen provider separately and follow its configuration requirements.

The direct dependency is `Microsoft.Extensions.Documents.Abstractions`, which
uses `Microsoft.Extensions.AI.Abstractions` for content payloads. Prefer .NET 8 or
later. Compatibility assets for .NET Framework and .NET Standard do not guarantee
support for every older runtime; check package compatibility and your provider's
requirements.

## Extract a document

This application helper takes an already configured provider client. It opens
and disposes the input file; the caller retains responsibility for the client's
lifetime. The media type must be supported by that provider.

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

public static class ExtractDocument
{
    public static async Task<ExtractionDocument> RunAsync(
        DocumentExtractionClient client, string path, string mediaType,
        CancellationToken cancellationToken = default)
    {
        using FileStream input = File.OpenRead(path);
        ExtractionDocument result = await client.ExtractAsync(
            input, Path.GetFileName(path), mediaType,
            cancellationToken: cancellationToken);

        foreach (ExtractionDocumentElement element in result.EnumerateContent())
        {
            if (element is ExtractionDocumentTextElement text &&
                text.Content is TextContent content)
            {
                Console.WriteLine(content.Text);
            }
        }

        return result;
    }
}
```

The identifier names the input; it does not fetch a file or URI. The task returns
one completed document, not a stream of partial results. `EnumerateContent()`
walks non-section elements in document order. Inspect the hierarchy directly for
table cells and document/section-local payloads.

## Select an extractor

`ExtractorName` selects an extractor or analyzer within the configured provider;
null uses its default. Use a selector that your provider documents as supported.
`AdditionalProperties` accepts provider-specific settings whose names and types
are defined by that provider, not by this package.

This alternative helper accepts an existing stream and leaves it open:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

public static class SelectExtractor
{
    public static Task<ExtractionDocument> RunAsync(
        DocumentExtractionClient client, Stream input, string mediaType,
        string supportedSelector, CancellationToken cancellationToken = default)
    {
        var options = new DocumentExtractionOptions
        {
            ExtractorName = supportedSelector
        };
        return client.ExtractAsync(
            input, "document", mediaType, options, cancellationToken);
    }
}
```

Unsupported explicit settings should be rejected rather than silently ignored.
Do not mutate options or their referenced values while a call is in progress.
`Clone()` creates a separate options container, but does not deep-clone values in
`AdditionalProperties`.

## Implement a local client

Implement `DocumentExtractionClient` when adapting another extraction library.
This optional, self-contained example only decodes plain text into one section:
it does not perform OCR or parse PDFs. It preserves the input stream and rejects
settings it does not support.

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

public sealed class PlainTextExtractionClient : DocumentExtractionClient
{
    public override async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        if (!string.Equals(mediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This example accepts only text/plain.");
        }

        DocumentExtractionOptions? settings = options?.Clone();
        if (settings?.ExtractorName is not null ||
            settings?.AdditionalProperties.Count > 0)
        {
            throw new NotSupportedException("This example has no configurable extractor settings.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StreamReader(
            document, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        string text = await reader.ReadToEndAsync(cancellationToken);

        var result = new ExtractionDocument(identifier);
        var section = new ExtractionDocumentSection();
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent(text)));
        result.Sections.Add(section);
        return result;
    }
}
```

## Ownership, cancellation and errors

Callers own input streams. Returned documents are non-disposable data. Optional
native objects exposed through `RawRepresentation` or source references are
borrowed, may be unavailable, and are not automatically disposed or safe to log.
Native access does not guarantee original response bytes.

Pass a cancellation token to the operation; cancellation support and format errors
depend on the selected provider. Providers are responsible for releasing their own
resources on success, failure and cancellation without disposing caller-owned
streams or borrowed native objects.

Extraction APIs are experimental under `MEDE0001`; the Documents model uses
`EXTEXP0019`. Acknowledge these diagnostics only if you accept experimental APIs.

## Feedback

Report contract issues through
[dotnet/extensions issues](https://github.com/dotnet/extensions/issues).
For supported formats, credentials and provider-specific errors, consult the
provider's documentation and issue tracker.
