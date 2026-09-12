using Microsoft.AspNetCore.Mvc;
using RagApi.Infrastructure;
using RagApi.Models;
using RagApi.Services.Interfaces;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<IngestionController> _logger;

    private static readonly string[] AllowedExtensions = { ".pdf" };

    public IngestionController(
        IIngestionService ingestionService,
        ILogger<IngestionController> logger)
    {
        _ingestionService = ingestionService;
        _logger           = logger;
    }

    /// <summary>
    /// Manually upload and ingest a PDF document via Swagger / Postman.
    /// Use this to test ingestion without needing an Azure Blob upload.
    /// Extracts text (Document Intelligence), chunks, embeds, and indexes into AI Search.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [RequestSizeLimit(52_428_800)] // 50MB max upload
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"Unsupported file type '{ext}'. Only .pdf is supported." });

        if (file.Length > 52_428_800)
            return BadRequest(new { error = "File exceeds 50MB limit." });

        _logger.LogInformation(
            "Manual ingestion upload: '{Name}' ({Size} bytes)", file.FileName, file.Length);

        var fileBytes = new byte[file.Length];
        using var readStream = file.OpenReadStream();
        var totalRead = 0;
        while (totalRead < fileBytes.Length)
        {
            var read = await readStream.ReadAsync(
                fileBytes, totalRead, fileBytes.Length - totalRead, ct);
            if (read == 0) break;
            totalRead += read;
        }

        // Wrap in a fresh MemoryStream from the complete byte array
        using var safeStream = new TimeoutIgnoringStream(new MemoryStream(fileBytes));
        var result = await _ingestionService.IngestDocumentAsync(safeStream, file.FileName, ct);

        if (!result.Success)
        {
            _logger.LogError("Manual ingestion failed for '{Name}': {Error}",
                file.FileName, result.ErrorMessage);
            return StatusCode(500, new { error = result.ErrorMessage });
        }

        return Ok(result);
    }


    /// <summary>
    /// Deletes all indexed chunks for a specific document from Azure AI Search.
    /// Use this before re-uploading the same document to avoid stale chunk pollution.
    /// Note: IngestDocument already calls this automatically — use only for manual cleanup.
    /// </summary>
    [HttpDelete("delete")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(
        [FromQuery] string documentName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentName))
            return BadRequest(new { error = "documentName query parameter is required." });

        _logger.LogInformation("Manual delete request for '{Name}'", documentName);

        var deleted = await _ingestionService.DeleteDocumentChunksAsync(documentName, ct);

        return Ok(new
        {
            documentName,
            chunksDeleted = deleted,
            message = deleted > 0
                ? $"Successfully deleted {deleted} chunks for '{documentName}'."
                : $"No chunks found for '{documentName}' — nothing to delete."
        });
    }
}