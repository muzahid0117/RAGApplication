using RagApi.Models;

namespace RagApi.Services.Interfaces;

public interface IChunkingService
{
    /// <summary>
    /// Splits page text into overlapping chunks.
    /// Each chunk carries its source page number and a unique chunkId.
    /// </summary>
    List<DocumentChunk> ChunkDocument(
        Dictionary<int, string> pageTexts,
        string documentName);
}
