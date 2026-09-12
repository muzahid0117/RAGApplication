using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace RagApi.Cache;

public class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultExpiry = TimeSpan.FromMinutes(30);

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    // ── Embedding cache ────────────────────────────────────────────────────────

    public bool TryGetEmbedding(string text, out float[]? vector)
    {
        var key = EmbeddingKey(text);
        return _cache.TryGetValue(key, out vector);
    }

    public void SetEmbedding(string text, float[] vector)
    {
        var key = EmbeddingKey(text);
        _cache.Set(key, vector, new MemoryCacheEntryOptions
        {
            SlidingExpiration = _defaultExpiry,
            Size = vector.Length * sizeof(float)
        });
    }

    // ── Search result cache ────────────────────────────────────────────────────

    public bool TryGetSearchResults(float[] queryVector, out List<RagApi.Models.SearchResult>? results)
    {
        var key = VectorKey(queryVector);
        return _cache.TryGetValue(key, out results);
    }

    public void SetSearchResults(float[] queryVector, List<RagApi.Models.SearchResult> results)
    {
        var key = VectorKey(queryVector);
        _cache.Set(key, results, new MemoryCacheEntryOptions
        {
            SlidingExpiration = _defaultExpiry,
            Size = results.Count * 512 // rough byte estimate per chunk
        });
    }

    // ── Key helpers ────────────────────────────────────────────────────────────

    private static string EmbeddingKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"emb:{Convert.ToHexString(hash)[..16]}";
    }

    private static string VectorKey(float[] vector)
    {
        // Hash first 32 floats for a fast fingerprint (full vector is expensive to hash)
        var slice = vector.Take(32).SelectMany(BitConverter.GetBytes).ToArray();
        var hash = SHA256.HashData(slice);
        return $"vec:{Convert.ToHexString(hash)[..16]}";
    }
}
