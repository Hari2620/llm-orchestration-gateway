"""These exist because the gateway and sidecar are two separate languages with
two separate default JSON casings (see gateway/Services/JsonDefaults.cs and
guardrails_eval/schemas.py). A regex test passing doesn't prove the HTTP
boundary works — only actually round-tripping camelCase JSON through the
pydantic models does.
"""

import json

from guardrails_eval.schemas import EvalTranscript, GuardrailCheckRequest, GuardrailCheckResult


def test_guardrail_check_request_accepts_dotnet_style_camelcase():
    payload = {"text": "hello", "stage": "input"}
    req = GuardrailCheckRequest.model_validate(payload)
    assert req.text == "hello"
    assert req.stage == "input"


def test_guardrail_check_result_serializes_to_camelcase():
    result = GuardrailCheckResult(allowed=False, violations=["pii_leak:email"], redacted_text="a [REDACTED_EMAIL] b")
    dumped = json.loads(result.model_dump_json(by_alias=True))
    assert dumped == {
        "allowed": False,
        "violations": ["pii_leak:email"],
        "redactedText": "a [REDACTED_EMAIL] b",
    }


def test_eval_transcript_accepts_dotnet_style_camelcase_payload():
    payload = {
        "correlationId": "abc123",
        "promptName": "chat-support",
        "promptVersion": "v1",
        "input": "hi",
        "output": "hello",
        "latencyMs": 42.5,
        "cacheHit": False,
        "timestamp": "2026-08-28T12:00:00Z",
    }
    transcript = EvalTranscript.model_validate(payload)
    assert transcript.correlation_id == "abc123"
    assert transcript.prompt_name == "chat-support"
    assert transcript.latency_ms == 42.5
