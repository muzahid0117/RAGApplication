namespace RagApi.Services.Interfaces;

public interface IEmbeddingService
{
    /// <summary>
    /// Returns a float vector for the given text using Azure OpenAI text-embedding-3-small.
    /// Result is cached in IMemoryCache by question hash to avoid duplicate API calls.
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}
