using Microsoft.AspNetCore.Mvc;
using RagApi.Models;
using RagApi.Services.Interfaces;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IRagService ragService, ILogger<ChatController> logger)
    {
        _ragService = ragService;
        _logger     = logger;
    }

    /// <summary>
    /// Ask a question. Uses RAG if relevant document chunks are found,
    /// otherwise falls back to a direct LLM answer.
    /// </summary>
    [HttpPost("ask")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Ask(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question cannot be empty." });

        if (request.Question.Length > 2000)
            return BadRequest(new { error = "Question exceeds maximum length of 2000 characters." });

        _logger.LogInformation("Chat request received: '{Question}'", request.Question[..Math.Min(80, request.Question.Length)]);

        var response = await _ragService.AskAsync(request, ct);
        return Ok(response);
    }
}
