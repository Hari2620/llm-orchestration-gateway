"""Wire contracts shared with the .NET gateway (mirrors gateway/Models/ChatModels.cs).

Both sides need to agree on JSON casing. ASP.NET Core's HttpClient JSON helpers
default to PascalCase, so the gateway explicitly serializes with
JsonSerializerDefaults.Web (camelCase) when calling this sidecar (see
gateway/Services/JsonDefaults.cs) — these models mirror that by exposing
camelCase aliases while keeping idiomatic snake_case attribute names in Python.
That casing handshake is exactly the kind of cross-service contract bug that's
invisible until an integration test actually POSTs one service's payload at
the other (tests/guardrails_eval_tests/test_wire_contract.py does that).
"""

from datetime import datetime

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel

_camel_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class GuardrailCheckRequest(BaseModel):
    model_config = _camel_config

    text: str
    stage: str  # "input" | "output"


class GuardrailCheckResult(BaseModel):
    model_config = _camel_config

    allowed: bool
    violations: list[str] = []
    redacted_text: str | None = None


class EvalTranscript(BaseModel):
    model_config = _camel_config

    correlation_id: str
    prompt_name: str
    prompt_version: str
    input: str
    output: str
    latency_ms: float
    cache_hit: bool
    timestamp: datetime
