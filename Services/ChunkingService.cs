using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RagApi.Models;
using RagApi.Services.Interfaces;

namespace RagApi.Services;

public class ChunkingService : IChunkingService
{
    private readonly int _chunkSize;
    private readonly int _overlapSize;
    private readonly ILogger<ChunkingService> _logger;

    // TOC detection: if this % of lines end with page numbers, it's a TOC
    private const double TocLineRatioThreshold = 0.45;

    public ChunkingService(IConfiguration config, ILogger<ChunkingService> logger)
    {
        _logger      = logger;
        _chunkSize   = int.TryParse(config["Chunking:ChunkSize"],   out var cs) ? cs : 400;
        _overlapSize = int.TryParse(config["Chunking:OverlapSize"], out var ov) ? ov : 50;
    }

    public List<DocumentChunk> ChunkDocument(
        Dictionary<int, string> pageTexts,
        string documentName)
    {
        var chunks     = new List<DocumentChunk>();
        var chunkIndex = 0;

        foreach (var (pageNumber, pageText) in pageTexts.OrderBy(p => p.Key))
        {
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            // ── Skip Table of Contents pages ───────────────────────────────────
            // TOC pages contain every section keyword but zero actual content.
            // They cause weak false-positive matches on almost every query.
            if (IsTableOfContentsPage(pageText))
            {
                _logger.LogInformation(
                    "Skipping page {Page} of '{Doc}' — detected as Table of Contents", pageNumber, documentName);
                continue;
            }

            // ── Skip cover pages ───────────────────────────────────────────────
            // Cover pages are very short and contain no retrievable content.
            if (IsCoverPage(pageText))
            {
                _logger.LogInformation(
                    "Skipping page {Page} of '{Doc}' — detected as Cover page", pageNumber, documentName);
                continue;
            }

            // Normalize whitespace
            var words = pageText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (words.Length == 0)
                continue;

            // Sliding window over words
            for (int start = 0; start < words.Length; start += (_chunkSize - _overlapSize))
            {
                var end        = Math.Min(start + _chunkSize, words.Length);
                var chunkWords = words[start..end];
                var content    = string.Join(" ", chunkWords).Trim();

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                // Skip very short trailing chunks (< 20 words) — likely noise
                if (chunkWords.Length < 20 && chunkIndex > 0)
                    break;

                chunks.Add(new DocumentChunk
                {
                    ChunkId      = BuildChunkId(documentName, pageNumber, chunkIndex),
                    DocumentName = documentName,
                    PageNumber   = pageNumber,
                    Content      = content
                });

                chunkIndex++;

                if (end == words.Length)
                    break;
            }
        }

        _logger.LogInformation(
            "Chunking complete for '{Doc}': {ChunkCount} chunks from {PageCount} pages",
            documentName, chunks.Count, pageTexts.Count);

        return chunks;
    }

    // ── Detects Table of Contents pages ───────────────────────────────────────
    // A TOC page typically has many lines ending with a page number (digits),
    // and contains dotted leaders like "........"
    private static bool IsTableOfContentsPage(string pageText)
    {
        var lines = pageText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0) return false;

        // Signal 1: many lines end with a standalone number (page refs)
        int linesEndingWithNumber = lines.Count(l =>
            System.Text.RegularExpressions.Regex.IsMatch(l, @"\s+\d{1,3}$"));

        // Signal 2: dotted leader pattern common in TOC
        int linesWithDots = lines.Count(l => l.Contains("....") || l.Contains(". . ."));

        // Signal 3: phrase "table of contents" explicitly present
        bool hasTocHeading = pageText.Contains("Table of Contents", StringComparison.OrdinalIgnoreCase)
                          || pageText.Contains("Contents", StringComparison.OrdinalIgnoreCase);

        double ratio = (double)linesEndingWithNumber / lines.Count;

        return (ratio >= TocLineRatioThreshold && hasTocHeading)
            || (linesWithDots >= 3 && hasTocHeading)
            || (ratio >= 0.7); // very high ratio alone is enough
    }

    // ── Detects cover / title pages ───────────────────────────────────────────
    // Cover pages are short (< 80 words) and typically contain version/date metadata
    private static bool IsCoverPage(string pageText)
    {
        var wordCount = pageText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount > 120) return false; // too long to be a cover

        bool hasVersionOrDate =
            System.Text.RegularExpressions.Regex.IsMatch(pageText, @"Version\s+\d", RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(pageText, @"Effective Date", RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(pageText, @"Document\s+ID", RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(pageText, @"Issued\s+by", RegexOptions.IgnoreCase);

        return wordCount < 80 || (wordCount < 120 && hasVersionOrDate);
    }

    // ── chunkId builder ───────────────────────────────────────────────────────
    private static string BuildChunkId(string documentName, int page, int index)
    {
        var safeName = Path.GetFileNameWithoutExtension(documentName)
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        return $"{safeName}-p{page}-c{index}";
    }
}