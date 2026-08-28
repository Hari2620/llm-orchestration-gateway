using Gateway.Models;

namespace Gateway.Services;

/// <summary>
/// Deterministic, no-network provider. This is what docker-compose runs by default
/// so the whole repo is clone-and-run with zero API keys — caching, rate limiting,
/// and guardrails are all real even though the "model" behind them is a stand-in.
/// </summary>
public class MockLlmProvider : ILlmProvider
{
    public string Name => "mock";

    public async Task<LlmCompletionResult> CompleteAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        // Simulate real latency variance so caching/latency metrics look meaningful in a demo.
        await Task.Delay(Random.Shared.Next(60, 220), ct);

        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var summary = words.Length > 12 ? string.Join(' ', words[..12]) + "..." : prompt;
        var text = $"[mock-completion] Acknowledged {words.Length} words. Summary: \"{summary}\"";

        return new LlmCompletionResult(
            Text: text,
            Model: "mock-echo-1",
            InputTokens: words.Length,
            OutputTokens: text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        );
    }
}
