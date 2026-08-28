"""Input-stage guardrails: regex/heuristic checks, not a model call.

Deliberately not an LLM-as-judge on the input path — that would double the
latency and cost of every request just to gate it. A real deployment layers
this with an actual classifier (Presidio for PII, a small prompt-injection
classifier) once the rule-based pass is proven not to be the bottleneck;
see README "what I'd do differently."
"""

import re

MAX_INPUT_CHARS = 8000

_INJECTION_PATTERNS = [
    re.compile(r"ignore (all|any|the) (previous|prior|above) instructions", re.I),
    re.compile(r"disregard (the )?system prompt", re.I),
    re.compile(r"reveal (your|the) (system prompt|instructions)", re.I),
    re.compile(r"you are now (in )?(dan|developer) mode", re.I),
    re.compile(r"pretend (you have no|there are no) (restrictions|rules|guidelines)", re.I),
]

PII_PATTERNS = {
    "email": re.compile(r"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"),
    "ssn_like": re.compile(r"\b\d{3}-\d{2}-\d{4}\b"),
    "credit_card_like": re.compile(r"\b(?:\d[ -]*?){13,16}\b"),
    "phone": re.compile(r"\b(\+?\d{1,2}[ .-]?)?\(?\d{3}\)?[ .-]?\d{3}[ .-]?\d{4}\b"),
}


def check_input(text: str) -> list[str]:
    """Returns a list of violation codes; empty list means clean."""
    violations: list[str] = []

    if len(text) > MAX_INPUT_CHARS:
        violations.append("input_too_long")

    for pattern in _INJECTION_PATTERNS:
        if pattern.search(text):
            violations.append("possible_prompt_injection")
            break

    for label, pattern in PII_PATTERNS.items():
        if pattern.search(text):
            violations.append(f"pii_detected:{label}")

    return violations
