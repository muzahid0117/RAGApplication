using RagApi.Models;

namespace RagApi.Services.Interfaces;

public interface IRagService
{
    /// <summary>
    /// Orchestrates the full RAG pipeline:
    ///   1. Embed question
    ///   2. Vector search
    ///   3. If top score >= threshold → RAG answer (GPT-4o + context)
    ///   4. Else → fallback LLM answer (GPT-4o-mini, no context)
    /// </summary>
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default);
}
