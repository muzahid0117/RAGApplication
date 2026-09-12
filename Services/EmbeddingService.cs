using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using RagApi.Cache;
using RagApi.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RagApi.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly CacheService _cache;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _modelName;

    public EmbeddingService(
        IConfiguration config,
        CacheService cache,
        ILogger<EmbeddingService> logger)
    {
        _cache = cache;
        _logger = logger;

        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required.");
        var apiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required.");

        _modelName = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-large";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
        _client = azureClient.GetEmbeddingClient(_modelName);
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // ── Cache hit ──────────────────────────────────────────────────────────
        if (_cache.TryGetEmbedding(text, out var cached) && cached is not null)
        {
            _logger.LogDebug("Embedding cache hit for text (length={Len})", text.Length);
            return cached;
        }

        // ── Azure OpenAI call ──────────────────────────────────────────────────
        _logger.LogInformation("Calling Azure OpenAI embeddings for text (length={Len})", text.Length);

        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        var vector = response.Value.ToFloats().ToArray();

        _cache.SetEmbedding(text, vector);

        return vector;
    }
}
