"""Cheap, deterministic evals — the kind you run on every single request, not
just a nightly batch. No LLM-as-judge here on purpose: that's a separate,
sampled job (see evals/run_eval.py) because it costs a real model call per
transcript. These heuristics are what runs inline, for free, on 100% of traffic.
"""

from guardrails_eval.checks.input_checks import PII_PATTERNS


def score_transcript(input_text: str, output_text: str, latency_ms: float) -> dict:
    findings = []

    if latency_ms > 5000:
        findings.append("slow_response")

    if len(output_text.strip()) == 0:
        findings.append("empty_output")

    if len(output_text) > 4 * len(input_text) + 500:
        findings.append("output_disproportionately_long")

    leaked_pii = [label for label, pattern in PII_PATTERNS.items() if pattern.search(output_text)]
    if leaked_pii:
        findings.append(f"unredacted_pii_in_output:{','.join(leaked_pii)}")

    return {
        "findings": findings,
        "passed": len(findings) == 0,
    }
