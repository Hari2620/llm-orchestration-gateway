using Gateway.Models;

namespace Gateway.Services;

/// <summary>
/// Everything the gateway knows about "an LLM": one method. Swapping providers,
/// or fronting two at once behind a router, means implementing this interface —
/// nothing else in the gateway changes. This is the seam the Azure gateway docs
/// call "routing" and the Anthropic tool-design writeup calls a narrow, high-signal
/// contract: small surface, one job.
/// </summary>
public interface ILlmProvider
{
    string Name { get; }

    Task<LlmCompletionResult> CompleteAsync(string prompt, int maxTokens, CancellationToken ct);
}

public class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
