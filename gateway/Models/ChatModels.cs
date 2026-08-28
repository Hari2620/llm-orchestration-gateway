namespace Gateway.Models;

// ---- Public API surface ----

public record ChatRequest(
    string PromptName,
    string? PromptVersion,
    Dictionary<string, string>? Variables,
    int? MaxTokens
);

public record ChatResponse(
    string Completion,
    string Model,
    string PromptName,
    string PromptVersion,
    bool CacheHit,
    string CorrelationId
);

// ---- Provider abstraction ----

public record LlmCompletionResult(
    string Text,
    string Model,
    int InputTokens,
    int OutputTokens
);

// ---- Guardrails sidecar contract (mirrors guardrails_eval/schemas.py) ----

public record GuardrailCheckRequest(string Text, string Stage);

public record GuardrailCheckResult(bool Allowed, List<string> Violations, string? RedactedText);

// ---- Eval sidecar contract ----

public record EvalTranscript(
    string CorrelationId,
    string PromptName,
    string PromptVersion,
    string Input,
    string Output,
    double LatencyMs,
    bool CacheHit,
    DateTime Timestamp
);
