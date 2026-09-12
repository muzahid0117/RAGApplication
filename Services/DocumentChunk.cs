namespace RagApi.Models;

public class DocumentChunk
{
    /// <summary>Unique ID: {documentName}-page{pageNumber}-chunk{index}</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Original blob filename e.g. "policy.pdf"</summary>
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>1-based page number this chunk came from</summary>
    public int PageNumber { get; set; }

    /// <summary>Raw text content of this chunk</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>1536-dim embedding vector (text-embedding-3-small)</summary>
    public float[] ContentVector { get; set; } = Array.Empty<float>();
}
