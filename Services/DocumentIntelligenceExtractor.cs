using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RagApi.Services.Interfaces;

namespace RagApi.Services;

public class DocumentIntelligenceExtractor : IDocumentExtractor
{
    private readonly DocumentIntelligenceClient _client;
    private readonly ILogger<DocumentIntelligenceExtractor> _logger;

    // prebuilt-read: extracts text + reading order + layout from PDFs
    private const string ModelId = "prebuilt-read";

    public DocumentIntelligenceExtractor(
        IConfiguration config,
        ILogger<DocumentIntelligenceExtractor> logger)
    {
        _logger = logger;

        var endpoint = config["DocumentIntelligence:Endpoint"]
            ?? throw new InvalidOperationException("DocumentIntelligence:Endpoint is required.");
        var apiKey = config["DocumentIntelligence:ApiKey"]
            ?? throw new InvalidOperationException("DocumentIntelligence:ApiKey is required.");

        _client = new DocumentIntelligenceClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }

    public async Task<Dictionary<int, string>> ExtractTextByPageAsync(
        Stream documentStream,
        string fileName,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting Document Intelligence extraction for '{FileName}'", fileName);
            _logger.LogInformation("Stream length: {Length}, CanRead: {CanRead}, CanSeek: {CanSeek}",
            documentStream.Length, documentStream.CanRead, documentStream.CanSeek);

        // ── v1.0.0 correct API: read stream bytes → BinaryData → AnalyzeDocumentOptions ──
        using var memoryStream = new MemoryStream();
        await documentStream.CopyToAsync(memoryStream, ct);
        var docBytes    = memoryStream.ToArray();
        var binaryData  = BinaryData.FromBytes(docBytes);

        // v1.0.0: pass BinaryData directly into AnalyzeDocumentOptions
        var options = new AnalyzeDocumentOptions(ModelId, binaryData);

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            options,
            cancellationToken: ct);

        var result = operation.Value;

        _logger.LogInformation(
            "Document Intelligence completed: {PageCount} pages extracted from '{FileName}'",
            result.Pages.Count, fileName);

        // ── Group lines by page number (1-based) ──────────────────────────────
        var pageTexts = new Dictionary<int, string>();

        foreach (var page in result.Pages)
        {
            var pageNumber = page.PageNumber; // already 1-based

            // Collect all lines on this page in reading order
            var lines = page.Lines
                .Select(l => l.Content.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));

            pageTexts[pageNumber] = string.Join(" ", lines);

            _logger.LogDebug(
                "Page {Page}: extracted {CharCount} chars",
                pageNumber, pageTexts[pageNumber].Length);
        }

        // ── Append table content per page (prebuilt-read surfaces tables too) ─
        if (result.Tables is { Count: > 0 })
        {
            foreach (var table in result.Tables)
            {
                //var tablePage = table.BoundingRegions.FirstOrDefault()?.PageNumber ?? 1
                var firstRegion = table.BoundingRegions.FirstOrDefault();
                var tablePage = table.BoundingRegions.Count > 0 ? firstRegion.PageNumber : 1;;
                var tableText = BuildTableText(table);

                if (pageTexts.ContainsKey(tablePage))
                    pageTexts[tablePage] += "\n" + tableText;
                else
                    pageTexts[tablePage] = tableText;
            }
        }

        return pageTexts;
    }

    // ── Flatten a DocumentTable into pipe-delimited plain text ────────────────
    private static string BuildTableText(DocumentTable table)
    {
        var rows = table.Cells
            .GroupBy(c => c.RowIndex)
            .OrderBy(g => g.Key);

        var lines = rows.Select(row =>
            string.Join(" | ", row.OrderBy(c => c.ColumnIndex).Select(c => c.Content.Trim())));

        return string.Join("\n", lines);
    }
}