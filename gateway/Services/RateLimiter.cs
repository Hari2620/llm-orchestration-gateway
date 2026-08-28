using System.Collections.Concurrent;

namespace Gateway.Services;

public class RateLimitResult
{
    public bool Allowed { get; init; }
    public int RetryAfterSeconds { get; init; }
}

/// <summary>
/// Per-API-key token bucket, checking requests-per-minute AND tokens-per-minute
/// at once — the same TPM/RPM split Azure's <c>llm-token-limit-policy</c> enforces
/// centrally at the gateway (Tier 2 material). Buckets refill continuously based
/// on elapsed time rather than resetting on a fixed clock tick, so there's no
/// "everyone's quota resets at :00" thundering herd.
/// </summary>
public class TokenBucketRateLimiter
{
    private class Bucket
    {
        public double Requests;
        public double Tokens;
        public DateTime LastRefill = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly int _rpm;
    private readonly int _tpm;

    public TokenBucketRateLimiter(int rpm, int tpm)
    {
        _rpm = rpm;
        _tpm = tpm;
    }

    public RateLimitResult TryConsume(string apiKey, int estimatedTokens)
    {
        var bucket = _buckets.GetOrAdd(apiKey, _ => new Bucket { Requests = _rpm, Tokens = _tpm });

        lock (bucket)
        {
            var now = DateTime.UtcNow;
            var elapsedMinutes = (now - bucket.LastRefill).TotalMinutes;

            if (elapsedMinutes > 0)
            {
                bucket.Requests = Math.Min(_rpm, bucket.Requests + elapsedMinutes * _rpm);
                bucket.Tokens = Math.Min(_tpm, bucket.Tokens + elapsedMinutes * _tpm);
                bucket.LastRefill = now;
            }

            if (bucket.Requests >= 1 && bucket.Tokens >= estimatedTokens)
            {
                bucket.Requests -= 1;
                bucket.Tokens -= estimatedTokens;
                return new RateLimitResult { Allowed = true };
            }

            var secondsForRequest = bucket.Requests < 1 ? (1 - bucket.Requests) / _rpm * 60 : 0;
            var secondsForTokens = bucket.Tokens < estimatedTokens ? (estimatedTokens - bucket.Tokens) / _tpm * 60 : 0;
            var retryAfter = (int)Math.Ceiling(Math.Max(secondsForRequest, secondsForTokens));

            return new RateLimitResult { Allowed = false, RetryAfterSeconds = Math.Max(1, retryAfter) };
        }
    }
}
