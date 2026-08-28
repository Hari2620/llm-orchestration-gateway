using System.Text.Json;
using Gateway.Models;
using Microsoft.Extensions.Configuration;

namespace Gateway.Services;

/// <summary>
/// Real provider: Anthropic's Messages API. Only constructed when LLM_PROVIDER=anthropic,
/// so a missing key never breaks the default (mock) path.
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public string Name => "anthropic";

    public AnthropicLlmProvider(HttpClient http, IConfiguration config)
    {
        var apiKey = config["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY is required when LLM_PROVIDER=anthropic.");

        _http = http;
        _http.BaseAddress = new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _model = config["ANTHROPIC_MODEL"] ?? "claude-3-5-haiku-latest";
    }

    public async Task<LlmCompletionResult> CompleteAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var response = await _http.PostAsJsonAsync("v1/messages", payload, ct);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var text = doc.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
        var usage = doc.GetProperty("usage");

        return new LlmCompletionResult(
            Text: text,
            Model: _model,
            InputTokens: usage.GetProperty("input_tokens").GetInt32(),
            OutputTokens: usage.GetProperty("output_tokens").GetInt32()
        );
    }
}
