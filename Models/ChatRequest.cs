namespace RagApi.Models;

public class ChatRequest
{
    public string Question { get; set; } = string.Empty;
    public float ScoreThreshold { get; set; } = 0.60f;
    public int TopK { get; set; } = 5;
}
