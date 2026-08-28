"""Guardrails + eval sidecar. Two small responsibilities:

  POST /check  — synchronous, on the gateway's request path (input + output stages)
  POST /eval   — fire-and-forget, called from the gateway's background eval queue

Kept in Python/FastAPI rather than folded into the .NET gateway because the
guardrail and eval ecosystem (Presidio, NeMo Guardrails, RAGAS, promptfoo) is
overwhelmingly Python-first — this boundary is drawn so that swapping the
regex heuristics below for a real library later touches only this service.
"""

import logging

from fastapi import FastAPI

from guardrails_eval.checks.input_checks import check_input
from guardrails_eval.checks.output_checks import check_output
from guardrails_eval.evals.heuristics import score_transcript
from guardrails_eval.schemas import EvalTranscript, GuardrailCheckRequest, GuardrailCheckResult

logging.basicConfig(level=logging.INFO, format='{"level":"%(levelname)s","msg":"%(message)s"}')
logger = logging.getLogger("guardrails_eval")

app = FastAPI(title="guardrails-eval")


@app.get("/healthz")
def healthz():
    return {"status": "ok"}


@app.post("/check", response_model=GuardrailCheckResult)
def check(req: GuardrailCheckRequest) -> GuardrailCheckResult:
    if req.stage == "input":
        violations = check_input(req.text)
        return GuardrailCheckResult(allowed=len(violations) == 0, violations=violations, redacted_text=None)

    violations, redacted = check_output(req.text)
    # Output-stage: PII gets redacted-and-allowed; only banned_content hard-blocks.
    allowed = "banned_content" not in violations
    return GuardrailCheckResult(allowed=allowed, violations=violations, redacted_text=redacted)


@app.post("/eval")
def eval_transcript(transcript: EvalTranscript):
    result = score_transcript(transcript.input, transcript.output, transcript.latency_ms)
    if not result["passed"]:
        logger.warning(
            "eval_failed correlation_id=%s prompt=%s@%s findings=%s",
            transcript.correlation_id, transcript.prompt_name, transcript.prompt_version, result["findings"],
        )
    return {"correlationId": transcript.correlation_id, **result}
