namespace RagApi.Models;

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public string AnswerSource { get; set; } = string.Empty; // "rag" | "llm_fallback"
    public float? TopScore { get; set; }
    public int ChunksUsed { get; set; }
    public long ElapsedMs { get; set; }
}
