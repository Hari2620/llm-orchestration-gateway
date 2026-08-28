using System.Text.Json;
using System.Threading.Channels;
using Gateway.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

/// <summary>
/// Every completed request gets appended to logs/evals.jsonl (durable, no
/// dependency) and fire-and-forget POSTed to the guardrails_eval sidecar's
/// /eval endpoint for live heuristic scoring. Runs off the request's own
/// hot path — evaluation must never add latency to a user-facing response,
/// which is the reason this is a BackgroundService with a channel instead
/// of an inline await in the /v1/chat handler.
/// </summary>
public class EvalQueue : BackgroundService
{
    private readonly Channel<EvalTranscript> _channel = Channel.CreateUnbounded<EvalTranscript>();
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EvalQueue> _logger;
    private readonly string _logPath;

    public EvalQueue(IHttpClientFactory httpFactory, ILogger<EvalQueue> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs", "evals.jsonl");
    }

    public void Enqueue(EvalTranscript transcript) => _channel.Writer.TryWrite(transcript);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                // Same camelCase options as the HTTP call below, so logs/evals.jsonl
                // and the sidecar's wire format never drift apart (see JsonDefaults.cs).
                await File.AppendAllTextAsync(_logPath, JsonSerializer.Serialize(item, JsonDefaults.CamelCase) + "\n", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to append eval transcript {CorrelationId} to disk", item.CorrelationId);
            }

            try
            {
                var client = _httpFactory.CreateClient("guardrails-eval");
                await client.PostAsJsonAsync("/eval", item, JsonDefaults.CamelCase, stoppingToken);
            }
            catch (Exception ex)
            {
                // Eval is observability, not a request-path dependency — log and move on.
                _logger.LogWarning(ex, "Eval sidecar POST failed for {CorrelationId}", item.CorrelationId);
            }
        }
    }
}
