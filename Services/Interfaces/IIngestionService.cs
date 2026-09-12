using RagApi.Models;

namespace RagApi.Services.Interfaces;

public interface IIngestionService
{
    /// <summary>
    /// Full ingestion pipeline for a single document:
    ///   1. Delete existing chunks for this document (clean re-index)
    ///   2. Extract text per page  (Document Intelligence prebuilt-read)
    ///   3. Chunk text             (sliding window, 400 words / 50 overlap)
    ///   4. Embed each chunk       (text-embedding-3-small, batched)
    ///   5. Upsert to AI Search    (vector index)
    /// </summary>
    Task<IngestionResult> IngestDocumentAsync(
        Stream documentStream,
        string documentName,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all indexed chunks belonging to a specific document.
    /// Use before re-ingesting to avoid stale chunks polluting results.
    /// </summary>
    Task<int> DeleteDocumentChunksAsync(
        string documentName,
        CancellationToken ct = default);
}