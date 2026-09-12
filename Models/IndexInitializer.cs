using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RagApi.Services;

public class IndexInitializer
{
    private readonly SearchIndexClient _indexClient;
    private readonly string _indexName;
    private readonly ILogger<IndexInitializer> _logger;

    // text-embedding-3-large produces 3072-dimensional vectors
    private const int VectorDimensions = 3072;
    private const string VectorFieldName = "contentVector";
    private const string HnswProfile = "rag-hnsw-profile";
    private const string HnswAlgorithm = "rag-hnsw";

    public IndexInitializer(IConfiguration config, ILogger<IndexInitializer> logger)
    {
        _logger    = logger;
        _indexName = config["AzureSearch:IndexName"] ?? "documents";

        var endpoint = config["AzureSearch:Endpoint"]
            ?? throw new InvalidOperationException("AzureSearch:Endpoint is required.");
        var apiKey = config["AzureSearch:ApiKey"]
            ?? throw new InvalidOperationException("AzureSearch:ApiKey is required.");

        _indexClient = new SearchIndexClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }

    public async Task EnsureIndexExistsAsync(CancellationToken ct = default)
    {
        try
        {
            // Check if index already exists
            await _indexClient.GetIndexAsync(_indexName, ct);
            _logger.LogInformation("AI Search index '{Index}' already exists — skipping creation.", _indexName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("AI Search index '{Index}' not found — creating now.", _indexName);
            await CreateIndexAsync(ct);
        }
    }

    private async Task CreateIndexAsync(CancellationToken ct)
    {
        var index = new SearchIndex(_indexName)
        {
            Fields =
            {
                // ── Key field ──────────────────────────────────────────────────
                new SimpleField("chunkId", SearchFieldDataType.String)
                {
                    IsKey        = true,
                    IsFilterable = true
                },

                // ── Metadata fields ────────────────────────────────────────────
                new SearchableField("documentName")
                {
                    IsFilterable  = true,
                    IsSortable    = true,
                    IsFacetable   = true
                },
                new SimpleField("pageNumber", SearchFieldDataType.Int32)
                {
                    IsFilterable = true,
                    IsSortable   = true
                },

                // ── Content field (keyword + BM25 searchable) ─────────────────
                new SearchableField("content")
                {
                    IsFilterable = false
                },

                // ── Vector field (1536-dim, cosine, HNSW) ─────────────────────
                new VectorSearchField(VectorFieldName, VectorDimensions, HnswProfile)
            },

            // ── HNSW vector search config ──────────────────────────────────────
            VectorSearch = new VectorSearch
            {
                Algorithms =
                {
                    new HnswAlgorithmConfiguration(HnswAlgorithm)
                    {
                        Parameters = new HnswParameters
                        {
                            M                = 4,   // graph connections — lower = less memory (good for free tier)
                            EfConstruction   = 400,
                            EfSearch         = 500,
                            Metric           = VectorSearchAlgorithmMetric.Cosine
                        }
                    }
                },
                Profiles =
                {
                    new VectorSearchProfile(HnswProfile, HnswAlgorithm)
                }
            }
        };

        await _indexClient.CreateIndexAsync(index, ct);
        _logger.LogInformation(
            "AI Search index '{Index}' created with {Dims}-dim HNSW vector field.", 
            _indexName, VectorDimensions);
    }
}