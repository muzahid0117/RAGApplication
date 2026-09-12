using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RagApi.Models;
using RagApi.Services.Interfaces;
using System.Diagnostics;

namespace RagApi.Services;

public class IngestionService : IIngestionService
{
    private readonly IDocumentExtractor  _extractor;
    private readonly IChunkingService    _chunker;
    private readonly IEmbeddingService   _embeddingService;
    private readonly SearchClient        _searchClient;
    private readonly ILogger<IngestionService> _logger;

    private const int EmbeddingBatchSize = 16;
    private const int IndexBatchSize     = 50;

    public IngestionService(
        IDocumentExtractor extractor,
        IChunkingService chunker,
        IEmbeddingService embeddingService,
        IConfiguration config,
        ILogger<IngestionService> logger)
    {
        _extractor        = extractor;
        _chunker          = chunker;
        _embeddingService = embeddingService;
        _logger           = logger;

        var endpoint  = config["AzureSearch:Endpoint"]
            ?? throw new InvalidOperationException("AzureSearch:Endpoint is required.");
        var apiKey    = config["AzureSearch:ApiKey"]
            ?? throw new InvalidOperationException("AzureSearch:ApiKey is required.");
        var indexName = config["AzureSearch:IndexName"] ?? "documents";

        _searchClient = new SearchClient(
            new Uri(endpoint),
            indexName,
            new Azure.AzureKeyCredential(apiKey));
    }

    public async Task<IngestionResult> IngestDocumentAsync(
        Stream documentStream,
        string documentName,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Ingestion started for '{Doc}'", documentName);

        try
        {
            // ── Step 1: Delete existing stale chunks for this document ─────────
            var deleted = await DeleteDocumentChunksAsync(documentName, ct);
            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} stale chunks for '{Doc}'", deleted, documentName);

            // ── Step 2: Extract text per page ─────────────────────────────────
            var pageTexts = await _extractor.ExtractTextByPageAsync(documentStream, documentName, ct);

            if (pageTexts.Count == 0)
                return Fail(documentName, "Document Intelligence returned no pages.", sw);

            // ── Step 3: Chunk (TOC + cover pages are automatically skipped) ───
            var chunks = _chunker.ChunkDocument(pageTexts, documentName);

            if (chunks.Count == 0)
                return Fail(documentName, "No chunks produced — document may be empty or all pages were filtered.", sw);

            // ── Step 4: Embed in batches ───────────────────────────────────────
            await EmbedChunksAsync(chunks, ct);

            // ── Step 5: Upsert to Azure AI Search ─────────────────────────────
            var indexed = await UpsertChunksAsync(chunks, ct);

            sw.Stop();
            _logger.LogInformation(
                "Ingestion complete for '{Doc}': {Pages} pages, {Chunks} chunks, {Indexed} indexed in {Ms}ms",
                documentName, pageTexts.Count, chunks.Count, indexed, sw.ElapsedMilliseconds);

            return new IngestionResult
            {
                DocumentName   = documentName,
                PagesExtracted = pageTexts.Count,
                ChunksCreated  = chunks.Count,
                ChunksIndexed  = indexed,
                ElapsedMs      = sw.ElapsedMilliseconds,
                Success        = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for '{Doc}'", documentName);
            return Fail(documentName, ex.Message, sw);
        }
    }

    public async Task<int> DeleteDocumentChunksAsync(
        string documentName,
        CancellationToken ct = default)
    {
        // Search for all chunks belonging to this document by documentName filter
        var options = new SearchOptions
        {
            Filter = $"documentName eq '{documentName.Replace("'", "''")}'",
            Select = { "chunkId" },
            Size   = 1000
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: "*", options, ct);

        var chunkIds = new List<string>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (result.Document.TryGetValue("chunkId", out var id) && id is string chunkId)
                chunkIds.Add(chunkId);
        }

        if (chunkIds.Count == 0) return 0;

        // Delete in batches
        int deleted = 0;
        for (int i = 0; i < chunkIds.Count; i += IndexBatchSize)
        {
            var batch = chunkIds.Skip(i).Take(IndexBatchSize)
                .Select(id => new SearchDocument { ["chunkId"] = id })
                .ToList();

            await _searchClient.DeleteDocumentsAsync("chunkId", chunkIds.Skip(i).Take(IndexBatchSize), cancellationToken: ct);
            deleted += batch.Count;
        }

        return deleted;
    }

    // ── Embed all chunks in batches ────────────────────────────────────────────
    private async Task EmbedChunksAsync(List<DocumentChunk> chunks, CancellationToken ct)
    {
        _logger.LogInformation("Embedding {Count} chunks in batches of {Batch}",
            chunks.Count, EmbeddingBatchSize);

        for (int i = 0; i < chunks.Count; i += EmbeddingBatchSize)
        {
            var batch = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();

            var tasks = batch.Select(async chunk =>
            {
                chunk.ContentVector = await _embeddingService.GetEmbeddingAsync(chunk.Content, ct);
            });

            await Task.WhenAll(tasks);

            _logger.LogDebug("Embedded batch {From}-{To} of {Total}",
                i + 1, Math.Min(i + EmbeddingBatchSize, chunks.Count), chunks.Count);

            if (i + EmbeddingBatchSize < chunks.Count)
                await Task.Delay(200, ct);
        }
    }

    // ── Upsert all chunks to Azure AI Search ──────────────────────────────────
    private async Task<int> UpsertChunksAsync(List<DocumentChunk> chunks, CancellationToken ct)
    {
        int totalIndexed = 0;

        for (int i = 0; i < chunks.Count; i += IndexBatchSize)
        {
            var batch = chunks.Skip(i).Take(IndexBatchSize).ToList();

            var documents = batch.Select(chunk => new
            {
                chunkId       = chunk.ChunkId,
                documentName  = chunk.DocumentName,
                pageNumber    = chunk.PageNumber,
                content       = chunk.Content,
                contentVector = chunk.ContentVector
            });

            var response = await _searchClient.MergeOrUploadDocumentsAsync(
                documents, new IndexDocumentsOptions { ThrowOnAnyError = false }, ct);

            var succeeded = response.Value.Results.Count(r => r.Succeeded);
            totalIndexed += succeeded;

            _logger.LogDebug("Indexed batch {From}-{To}: {Ok}/{Total} succeeded",
                i + 1, Math.Min(i + IndexBatchSize, chunks.Count), succeeded, batch.Count);
        }

        return totalIndexed;
    }

    private static IngestionResult Fail(string docName, string error, Stopwatch sw)
    {
        sw.Stop();
        return new IngestionResult
        {
            DocumentName = docName,
            Success      = false,
            ErrorMessage = error,
            ElapsedMs    = sw.ElapsedMilliseconds
        };
    }
}