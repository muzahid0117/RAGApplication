namespace RagApi.Models;

public class SearchResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
}
