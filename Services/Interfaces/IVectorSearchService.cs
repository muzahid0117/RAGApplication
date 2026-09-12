using RagApi.Models;

namespace RagApi.Services.Interfaces;

public interface IVectorSearchService
{
    /// <summary>
    /// Runs a vector search against Azure AI Search and returns top-K chunks
    /// ordered by cosine similarity score descending.
    /// Results are cached in IMemoryCache by query vector hash.
    /// </summary>
    Task<List<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken ct = default);
}
