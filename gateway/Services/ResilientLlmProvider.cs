using Gateway.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

/// <summary>
/// Decorator: wraps any ILlmProvider with a timeout and a circuit breaker, so
/// MockLlmProvider and AnthropicLlmProvider stay free of resilience concerns.
/// This is the failure-mode boundary called out in the README — when it trips,
/// the caller gets a clean ProviderUnavailableException instead of a hung request.
/// </summary>
public class ResilientLlmProvider : ILlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly ILogger<ResilientLlmProvider> _logger;
    private readonly SimpleCircuitBreaker _breaker;
    private readonly TimeSpan _timeout;

    public string Name => _inner.Name;

    public ResilientLlmProvider(ILlmProvider inner, ILogger<ResilientLlmProvider> logger, IConfiguration config)
    {
        _inner = inner;
        _logger = logger;

        var timeoutMs = int.Parse(config["PROVIDER_TIMEOUT_MS"] ?? "15000");
        var threshold = int.Parse(config["CIRCUIT_BREAKER_FAILURE_THRESHOLD"] ?? "5");
        var breakSeconds = int.Parse(config["CIRCUIT_BREAKER_BREAK_SECONDS"] ?? "30");

        _timeout = TimeSpan.FromMilliseconds(timeoutMs);
        _breaker = new SimpleCircuitBreaker(threshold, TimeSpan.FromSeconds(breakSeconds));
    }

    public async Task<LlmCompletionResult> CompleteAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        if (_breaker.IsOpen)
            throw new ProviderUnavailableException($"Provider '{_inner.Name}' circuit is open — failing fast instead of piling up timeouts.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            var result = await _inner.CompleteAsync(prompt, maxTokens, cts.Token);
            _breaker.RecordSuccess();
            return result;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            _breaker.RecordFailure();
            _logger.LogError(ex, "Provider {Provider} call failed or timed out", _inner.Name);
            throw new ProviderUnavailableException($"Provider '{_inner.Name}' call failed: {ex.Message}", ex);
        }
    }
}
