namespace RagApi.Models;

public class IngestionResult
{
    public string DocumentName { get; set; } = string.Empty;
    public int PagesExtracted { get; set; }
    public int ChunksCreated { get; set; }
    public int ChunksIndexed { get; set; }
    public long ElapsedMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
