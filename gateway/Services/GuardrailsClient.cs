using Gateway.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

/// <summary>
/// Talks to the Python guardrails_eval sidecar over HTTP. Deliberately fails
/// OPEN (allows the request through, flagged with a violation string) when the
/// sidecar is slow, down, or circuit-broken — availability over strict
/// enforcement. That's a real trade-off, not an oversight: it's written up
/// as one in the README, and a compliance-sensitive deployment would flip
/// this default rather than assume it.
/// </summary>
public class GuardrailsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GuardrailsClient> _logger;
    private readonly SimpleCircuitBreaker _breaker;

    public GuardrailsClient(HttpClient http, ILogger<GuardrailsClient> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;

        var threshold = int.Parse(config["CIRCUIT_BREAKER_FAILURE_THRESHOLD"] ?? "5");
        var breakSeconds = int.Parse(config["CIRCUIT_BREAKER_BREAK_SECONDS"] ?? "30");
        _breaker = new SimpleCircuitBreaker(threshold, TimeSpan.FromSeconds(breakSeconds));
    }

    public async Task<GuardrailCheckResult> CheckAsync(string text, string stage, CancellationToken ct)
    {
        if (_breaker.IsOpen)
        {
            _logger.LogWarning("Guardrails circuit open; failing open for stage={Stage}", stage);
            return new GuardrailCheckResult(true, new List<string> { "guardrails-sidecar-unavailable" }, null);
        }

        try
        {
            var response = await _http.PostAsJsonAsync("/check", new GuardrailCheckRequest(text, stage), JsonDefaults.CamelCase, ct);
            response.EnsureSuccessStatusCode();
            _breaker.RecordSuccess();

            var result = await response.Content.ReadFromJsonAsync<GuardrailCheckResult>(JsonDefaults.CamelCase, ct);
            return result ?? new GuardrailCheckResult(true, new List<string>(), null);
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure();
            _logger.LogWarning(ex, "Guardrails check failed for stage={Stage}; failing open", stage);
            return new GuardrailCheckResult(true, new List<string> { "guardrails-check-error" }, null);
        }
    }
}
