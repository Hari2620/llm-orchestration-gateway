using System.Collections.Concurrent;

namespace Gateway.Services;

/// <summary>
/// Process-local counters, exposed at GET /metrics as JSON. Not Prometheus —
/// deliberately: wiring OpenTelemetry/Prometheus exporters is a real afternoon
/// of work that would roughly double this repo's line count for a demo that
/// nobody scrapes. The README calls this out as the first thing to swap for
/// a real deployment.
/// </summary>
public class MetricsRegistry
{
    private long _total, _cacheHits, _guardrailBlocks, _providerErrors, _rateLimited;
    private readonly ConcurrentQueue<double> _latenciesMs = new();
    private const int MaxSamples = 500;

    public void RecordRequest() => Interlocked.Increment(ref _total);
    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public void RecordGuardrailBlock() => Interlocked.Increment(ref _guardrailBlocks);
    public void RecordProviderError() => Interlocked.Increment(ref _providerErrors);
    public void RecordRateLimited() => Interlocked.Increment(ref _rateLimited);

    public void RecordLatency(double ms)
    {
        _latenciesMs.Enqueue(ms);
        while (_latenciesMs.Count > MaxSamples) _latenciesMs.TryDequeue(out _);
    }

    public object Snapshot()
    {
        var samples = _latenciesMs.ToArray();
        Array.Sort(samples);

        double Percentile(double p)
        {
            if (samples.Length == 0) return 0;
            var idx = (int)Math.Clamp(Math.Round(p * (samples.Length - 1)), 0, samples.Length - 1);
            return samples[idx];
        }

        var total = Interlocked.Read(ref _total);

        return new
        {
            totalRequests = total,
            cacheHits = _cacheHits,
            cacheHitRate = total == 0 ? 0 : Math.Round((double)_cacheHits / total, 3),
            guardrailBlocks = _guardrailBlocks,
            providerErrors = _providerErrors,
            rateLimited = _rateLimited,
            latencyMsP50 = Math.Round(Percentile(0.50), 1),
            latencyMsP95 = Math.Round(Percentile(0.95), 1)
        };
    }
}
