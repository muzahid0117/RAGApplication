using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RagApi.Services.Interfaces;

namespace RagApi.Functions;

public class BlobIngestionFunction
{
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<BlobIngestionFunction> _logger;

    public BlobIngestionFunction(
        IIngestionService ingestionService,
        ILogger<BlobIngestionFunction> logger)
    {
        _ingestionService = ingestionService;
        _logger           = logger;
    }

    /// <summary>
    /// Fires automatically when any file is uploaded to the
    /// 'documents' Blob container.
    /// Supported formats: .pdf  (Document Intelligence prebuilt-read)
    /// </summary>
    [Function(nameof(BlobIngestionFunction))]
    public async Task Run(
        [BlobTrigger("documents/{name}", Connection = "AzureBlobStorage:ConnectionString")]
        Stream blobStream,
        string name,
        FunctionContext context)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        _logger.LogInformation("BlobTrigger fired for '{Name}' ({Bytes} bytes)",
            name, blobStream.Length);

        // Only process supported file types
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext != ".pdf")
        {
            _logger.LogWarning("Skipping '{Name}' — only .pdf files are supported.", name);
            return;
        }

        var result = await _ingestionService.IngestDocumentAsync(blobStream, name, cts.Token);

        if (result.Success)
        {
            _logger.LogInformation(
                "Ingestion succeeded for '{Doc}': {Pages} pages, {Chunks} chunks indexed in {Ms}ms",
                result.DocumentName, result.PagesExtracted, result.ChunksIndexed, result.ElapsedMs);
        }
        else
        {
            _logger.LogError(
                "Ingestion failed for '{Doc}': {Error}",
                result.DocumentName, result.ErrorMessage);

            // Re-throw so Azure Functions retries (up to 5 times by default)
            throw new InvalidOperationException(
                $"Ingestion failed for '{name}': {result.ErrorMessage}");
        }
    }
}
