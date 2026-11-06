using System.Text.Json.Serialization;
using Microsoft.Extensions.VectorData;

namespace AIChatWeb_CSharp.Web.Services;

public class IngestedChunk
{
#if (IsOllama)
    public const int VectorDimensions = 384; // 384 is the default vector size for the all-minilm embedding model
#else
    public const int VectorDimensions = 1536; // 1536 is the default vector size for the OpenAI text-embedding-3-small model
#endif
#if (IsAzureAISearch || IsQdrant)
    public const string VectorDistanceFunction = DistanceFunction.CosineSimilarity;
#else
    public const string VectorDistanceFunction = DistanceFunction.CosineDistance;
#endif
    public const string CollectionName = "data-AIChatWeb-CSharp.Web-chunks";

    [VectorStoreKey(StorageName = "key")]
    [JsonPropertyName("key")]
#if (IsAzureAISearch)
    public required string Key { get; set; }
#else
    public required Guid Key { get; set; }
#endif

    [VectorStoreData(StorageName = "documentid")]
    [JsonPropertyName("documentid")]
    public required string DocumentId { get; set; }

    [VectorStoreData(StorageName = "content")]
    [JsonPropertyName("content")]
    public required string Text { get; set; }

    [VectorStoreData(StorageName = "context")]
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction, StorageName = "embedding")]
    [JsonPropertyName("embedding")]
    public string? Vector => Text;
}
