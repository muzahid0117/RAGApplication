namespace RagApi.Services.Interfaces;

public interface IDocumentExtractor
{
    /// <summary>
    /// Extracts text from a document stream using Azure Document Intelligence
    /// prebuilt-read model. Returns a dictionary keyed by 1-based page number.
    /// </summary>
    Task<Dictionary<int, string>> ExtractTextByPageAsync(
        Stream documentStream,
        string fileName,
        CancellationToken ct = default);
}
