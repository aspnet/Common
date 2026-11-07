using aichatweb.Web.Services.Ingestion;
using Microsoft.Extensions.VectorData;

namespace aichatweb.Web.Services;

public class SemanticSearch(
    VectorStoreCollection<Guid, IngestedChunk> vectorCollection,
    [FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory,
    DataIngestor dataIngestor)
{
    private Task? _ingestionTask;

    public async Task LoadDocumentsAsync() => await ( _ingestionTask ??= dataIngestor.IngestDataAsync(ingestionDirectory, searchPattern: "*.*"));

    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        // Ensure documents have been loaded before searching
        await LoadDocumentsAsync();

        var nearest = vectorCollection.SearchAsync(text, maxResults, new VectorSearchOptions<IngestedChunk>
        {
            Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
        });

        return await nearest.Select(result => result.Record).ToListAsync();
    }
}
