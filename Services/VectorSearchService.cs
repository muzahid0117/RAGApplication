using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using RagApi.Cache;
using RagApi.Models;
using RagApi.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RagApi.Services;

public class VectorSearchService : IVectorSearchService
{
    private readonly SearchClient _searchClient;
    private readonly CacheService _cache;
    private readonly ILogger<VectorSearchService> _logger;
    private readonly string _vectorFieldName;

    public VectorSearchService(
        IConfiguration config,
        CacheService cache,
        ILogger<VectorSearchService> logger)
    {
        _cache = cache;
        _logger = logger;

        var endpoint = config["AzureSearch:Endpoint"]
            ?? throw new InvalidOperationException("AzureSearch:Endpoint is required.");
        var apiKey = config["AzureSearch:ApiKey"]
            ?? throw new InvalidOperationException("AzureSearch:ApiKey is required.");
        var indexName = config["AzureSearch:IndexName"]
            ?? throw new InvalidOperationException("AzureSearch:IndexName is required.");

        _vectorFieldName = config["AzureSearch:VectorFieldName"] ?? "contentVector";

        _searchClient = new SearchClient(
            new Uri(endpoint),
            indexName,
            new AzureKeyCredential(apiKey));
    }

    public async Task<List<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken ct = default)
    {
        // ── Cache hit ──────────────────────────────────────────────────────────
        if (_cache.TryGetSearchResults(queryVector, out var cached) && cached is not null)
        {
            _logger.LogDebug("Search cache hit, returning {Count} chunks", cached.Count);
            return cached;
        }

        // ── Azure AI Search vector query ───────────────────────────────────────
        _logger.LogInformation("Running vector search, topK={TopK}", topK);

        var vectorQuery = new VectorizedQuery(queryVector)
        {
            KNearestNeighborsCount = topK,
            Fields = { _vectorFieldName }
        };

        var options = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions { Queries = { vectorQuery } },
            Size = topK,
            Select = { "chunkId", "content", "documentName", "pageNumber" }
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(searchText: null, options, ct);
        // try        {
        //      var response = await _searchClient.SearchAsync<SearchDocument>(searchText: null, options, ct);
        // }
        // catch (RequestFailedException ex)
        // {
        //     Console.WriteLine("Azure Search Error:");

        //     Console.WriteLine($"Status: {ex.Status}");

        //     Console.WriteLine($"Message: {ex.Message}");

        //     Console.WriteLine($"Stack: {ex.StackTrace}");
        // }


        var results = new List<SearchResult>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            results.Add(new SearchResult
            {
                ChunkId      = doc.TryGetValue("chunkId", out var id)   ? id?.ToString() ?? "" : "",
                Content      = doc.TryGetValue("content", out var c)    ? c?.ToString()  ?? "" : "",
                DocumentName = doc.TryGetValue("documentName", out var d) ? d?.ToString() ?? "" : "",
                PageNumber   = doc.TryGetValue("pageNumber", out var p) && p is int pg ? pg : 0,
                Score        = (float)result.Score.GetValueOrDefault()
            });
        }

        // Order by score descending
        results = results.OrderByDescending(r => r.Score).ToList();

        _cache.SetSearchResults(queryVector, results);

        _logger.LogInformation("Vector search returned {Count} chunks, top score={Score:F3}",
            results.Count, results.FirstOrDefault()?.Score);

        return results;
    }
}
