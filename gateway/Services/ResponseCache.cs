using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Gateway.Models;

namespace Gateway.Services;

public interface ICacheStore
{
    bool TryGet(string key, out LlmCompletionResult? result);
    void Set(string key, LlmCompletionResult result, TimeSpan ttl);
}

/// <summary>
/// In-memory, TTL-based cache keyed on a hash of (prompt name, version, rendered
/// text, max tokens) — not the raw request body, so two requests that render to
/// the same final prompt hit the same cache entry even with different variable
/// dicts. ICacheStore is the seam: swapping this for a Redis-backed store is a
/// one-class change, which is the whole point of the interface existing at this
/// size of project (see README trade-offs: not built because it wasn't needed yet).
/// </summary>
public class InMemoryCacheStore : ICacheStore
{
    private record Entry(LlmCompletionResult Result, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public bool TryGet(string key, out LlmCompletionResult? result)
    {
        if (_store.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            result = entry.Result;
            return true;
        }

        _store.TryRemove(key, out _);
        result = null;
        return false;
    }

    public void Set(string key, LlmCompletionResult result, TimeSpan ttl)
        => _store[key] = new Entry(result, DateTime.UtcNow.Add(ttl));

    public static string BuildKey(string promptName, string version, string renderedPrompt, int maxTokens)
    {
        var raw = $"{promptName}|{version}|{maxTokens}|{renderedPrompt}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
