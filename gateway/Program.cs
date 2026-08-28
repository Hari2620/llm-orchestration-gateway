using System.Diagnostics;
using Gateway.Models;
using Gateway.Services;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ---- DI wiring ----

    var promptsDir = Path.Combine(AppContext.BaseDirectory, "Prompts");
    builder.Services.AddSingleton(new PromptRegistry(promptsDir));

    builder.Services.AddSingleton<ICacheStore, InMemoryCacheStore>();

    builder.Services.AddSingleton(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        return new TokenBucketRateLimiter(
            rpm: int.Parse(config["RATE_LIMIT_RPM"] ?? "60"),
            tpm: int.Parse(config["RATE_LIMIT_TPM"] ?? "40000"));
    });

    builder.Services.AddSingleton<MetricsRegistry>();

    builder.Services.AddHttpClient("anthropic");

    builder.Services.AddSingleton<ILlmProvider>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        ILlmProvider inner = (config["LLM_PROVIDER"] ?? "mock").ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicLlmProvider(httpFactory.CreateClient("anthropic"), config),
            _ => new MockLlmProvider()
        };

        return new ResilientLlmProvider(inner, sp.GetRequiredService<ILogger<ResilientLlmProvider>>(), config);
    });

    builder.Services.AddHttpClient<GuardrailsClient>((sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(config["GUARDRAILS_EVAL_URL"] ?? "http://localhost:8081");
        client.Timeout = TimeSpan.FromMilliseconds(int.Parse(config["GUARDRAILS_TIMEOUT_MS"] ?? "2000"));
    });

    // Named client the EvalQueue's background loop uses to POST to the same sidecar.
    builder.Services.AddHttpClient("guardrails-eval", (sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(config["GUARDRAILS_EVAL_URL"] ?? "http://localhost:8081");
        client.Timeout = TimeSpan.FromSeconds(5);
    });

    builder.Services.AddSingleton<EvalQueue>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EvalQueue>());

    var app = builder.Build();

    // ---- Correlation ID + structured logging scope ----

    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next();
        }
    });

    // ---- Endpoints ----

    app.MapGet("/healthz", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

    app.MapGet("/metrics", (MetricsRegistry metrics) => Results.Ok(metrics.Snapshot()));

    app.MapPost("/v1/chat", async (
        HttpContext http,
        ChatRequest req,
        IConfiguration config,
        PromptRegistry prompts,
        ICacheStore cache,
        TokenBucketRateLimiter limiter,
        GuardrailsClient guardrails,
        ILlmProvider provider,
        EvalQueue evalQueue,
        MetricsRegistry metrics,
        ILogger<Program> logger) =>
    {
        var correlationId = (string)http.Items["CorrelationId"]!;
        var apiKey = http.Request.Headers.TryGetValue("X-Api-Key", out var k) ? k.ToString() : "anonymous";
        metrics.RecordRequest();
        var sw = Stopwatch.StartNew();

        PromptTemplate template;
        try
        {
            template = prompts.Resolve(req.PromptName, req.PromptVersion);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message, correlationId });
        }

        var rendered = PromptRegistry.Render(template.Template, req.Variables);
        var maxTokens = req.MaxTokens ?? 512;

        // Crude but standard ~4-chars-per-token estimate, used only for rate-limit accounting.
        var estimatedTokens = rendered.Length / 4 + maxTokens;
        var rateLimit = limiter.TryConsume(apiKey, estimatedTokens);
        if (!rateLimit.Allowed)
        {
            metrics.RecordRateLimited();
            http.Response.Headers["Retry-After"] = rateLimit.RetryAfterSeconds.ToString();
            logger.LogWarning("Rate limited apiKey={ApiKey} correlation={CorrelationId}", apiKey, correlationId);
            return Results.Json(
                new { error = "rate_limited", retryAfterSeconds = rateLimit.RetryAfterSeconds, correlationId },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var inputCheck = await guardrails.CheckAsync(rendered, "input", http.RequestAborted);
        if (!inputCheck.Allowed)
        {
            metrics.RecordGuardrailBlock();
            logger.LogWarning("Input rejected by guardrails violations={Violations} correlation={CorrelationId}",
                inputCheck.Violations, correlationId);
            return Results.Json(
                new { error = "input_rejected", violations = inputCheck.Violations, correlationId },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var cacheKey = InMemoryCacheStore.BuildKey(template.Name, template.Version, rendered, maxTokens);
        var cacheHit = cache.TryGet(cacheKey, out var cached);

        LlmCompletionResult result;
        if (cacheHit && cached is not null)
        {
            result = cached;
            metrics.RecordCacheHit();
        }
        else
        {
            try
            {
                result = await provider.CompleteAsync(rendered, maxTokens, http.RequestAborted);
            }
            catch (ProviderUnavailableException ex)
            {
                metrics.RecordProviderError();
                logger.LogError(ex, "Provider unavailable correlation={CorrelationId}", correlationId);
                return Results.Json(
                    new { error = "provider_unavailable", correlationId },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var ttl = TimeSpan.FromSeconds(int.Parse(config["CACHE_TTL_SECONDS"] ?? "300"));
            cache.Set(cacheKey, result, ttl);
        }

        var outputCheck = await guardrails.CheckAsync(result.Text, "output", http.RequestAborted);
        if (!outputCheck.Allowed) metrics.RecordGuardrailBlock();
        var finalText = outputCheck.RedactedText ?? result.Text;

        sw.Stop();
        metrics.RecordLatency(sw.Elapsed.TotalMilliseconds);

        evalQueue.Enqueue(new EvalTranscript(
            CorrelationId: correlationId,
            PromptName: template.Name,
            PromptVersion: template.Version,
            Input: rendered,
            Output: finalText,
            LatencyMs: sw.Elapsed.TotalMilliseconds,
            CacheHit: cacheHit,
            Timestamp: DateTime.UtcNow));

        return Results.Ok(new ChatResponse(finalText, result.Model, template.Name, template.Version, cacheHit, correlationId));
    });

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
