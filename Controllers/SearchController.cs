using Microsoft.AspNetCore.Mvc;
using RagApi.Models;
using RagApi.Services.Interfaces;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        IEmbeddingService embeddingService,
        IVectorSearchService vectorSearchService,
        ILogger<SearchController> logger)
    {
        _embeddingService    = embeddingService;
        _vectorSearchService = vectorSearchService;
        _logger              = logger;
    }

    /// <summary>
    /// Debug endpoint: returns raw vector search results for a query.
    /// Useful for tuning the score threshold before using /chat/ask.
    /// </summary>
    [HttpGet("debug")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Debug(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "query parameter is required." });

        _logger.LogInformation("Debug search for: '{Query}'", query);

        var vector  = await _embeddingService.GetEmbeddingAsync(query, ct);
        var results = await _vectorSearchService.SearchAsync(vector, topK, ct);

        return Ok(new
        {
            query,
            topK,
            resultCount = results.Count,
            topScore    = results.FirstOrDefault()?.Score,
            results     = results.Select(r => new
            {
                r.ChunkId,
                r.Score,
                r.DocumentName,
                r.PageNumber,
                contentPreview = r.Content.Length > 200
                    ? r.Content[..200] + "..."
                    : r.Content
            })
        });
    }
}
