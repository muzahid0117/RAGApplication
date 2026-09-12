using Azure.AI.OpenAI;
using OpenAI.Chat;
using RagApi.Models;
using RagApi.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace RagApi.Services;

public class RagService : IRagService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly ChatClient _ragChatClient;       // GPT-4o  — used for RAG answers
    private readonly ChatClient _fallbackChatClient;  // GPT-4o-mini — used for fallback
    private readonly ILogger<RagService> _logger;

    // System prompts
    private const string RagSystemPrompt = """
        You are a helpful assistant. Answer the user's question using ONLY the context chunks provided below.
        If the context does not contain enough information to answer, say "I don't have enough information in the document to answer that."
        Be concise and factual. Do not make up information.
        """;

    private const string FallbackSystemPrompt = """
        You are a helpful assistant. Answer the user's question to the best of your ability.
        Be concise and factual.
        """;

    public RagService(
        IEmbeddingService embeddingService,
        IVectorSearchService vectorSearchService,
        IConfiguration config,
        ILogger<RagService> logger)
    {
        _embeddingService    = embeddingService;
        _vectorSearchService = vectorSearchService;
        _logger              = logger;

        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required.");
        var apiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required.");

        var ragDeployment      = config["AzureOpenAI:RagDeployment"]      ?? "gpt-4o";
        var fallbackDeployment = config["AzureOpenAI:FallbackDeployment"] ?? "gpt-4o-mini";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
        _ragChatClient      = azureClient.GetChatClient(ragDeployment);
        _fallbackChatClient = azureClient.GetChatClient(fallbackDeployment);
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Embed the question ─────────────────────────────────────────
        var queryVector = await _embeddingService.GetEmbeddingAsync(request.Question, ct);

        // ── Step 2: Vector search ──────────────────────────────────────────────
        var chunks = await _vectorSearchService.SearchAsync(queryVector, request.TopK, ct);

        var topScore  = chunks.FirstOrDefault()?.Score ?? 0f;
        var useRag    = chunks.Count > 0 && topScore >= request.ScoreThreshold;

        _logger.LogInformation(
            "RAG decision: useRag={UseRag}, topScore={Score:F3}, threshold={Threshold}",
            useRag, topScore, request.ScoreThreshold);

        // ── Step 3: Generate answer ────────────────────────────────────────────
        string answer;
        string source;
        int chunksUsed;

        if (useRag)
        {
            (answer, chunksUsed) = await GenerateRagAnswerAsync(request.Question, chunks, ct);
            source = "rag";
        }
        else
        {
            answer     = await GenerateFallbackAnswerAsync(request.Question, ct);
            chunksUsed = 0;
            source     = "llm_fallback";
        }

        sw.Stop();

        return new ChatResponse
        {
            Answer       = answer,
            AnswerSource = source,
            TopScore     = topScore > 0 ? topScore : null,
            ChunksUsed   = chunksUsed,
            ElapsedMs    = sw.ElapsedMilliseconds
        };
    }

    // ── RAG answer: inject top chunks as context ───────────────────────────────
    private async Task<(string answer, int chunksUsed)> GenerateRagAnswerAsync(
        string question,
        List<SearchResult> chunks,
        CancellationToken ct)
    {
        // Build context block from top chunks
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("=== CONTEXT CHUNKS ===");
        for (int i = 0; i < chunks.Count; i++)
        {
            contextBuilder.AppendLine($"[Chunk {i + 1}]");
            contextBuilder.AppendLine(chunks[i].Content);
            contextBuilder.AppendLine();
        }

        var userMessage = $"{contextBuilder}\n=== QUESTION ===\n{question}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(RagSystemPrompt),
            new UserChatMessage(userMessage)
        };

        var options = new ChatCompletionOptions { MaxOutputTokenCount = 1024 };
        var response = await _ragChatClient.CompleteChatAsync(messages, options, ct);
        var answer = response.Value.Content[0].Text;

        return (answer, chunks.Count);
    }

    // ── Fallback answer: plain LLM, no context ────────────────────────────────
    private async Task<string> GenerateFallbackAnswerAsync(string question, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(FallbackSystemPrompt),
            new UserChatMessage(question)
        };

        var options = new ChatCompletionOptions { MaxOutputTokenCount = 1024 };
        var response = await _fallbackChatClient.CompleteChatAsync(messages, options, ct);
        return response.Value.Content[0].Text;
    }
}
