"""Output-stage guardrails: redact rather than block where possible.

Blocking an input is cheap (nothing was generated yet). Blocking an *output*
throws away a completion the provider already charged you for — so for PII
leakage the default here is to redact and let the response through with a
violation flag, and only hard-block on the banned-content list. That's a
trade-off worth a paragraph in the README, not a silent default.
"""

import re

from .input_checks import PII_PATTERNS

_BANNED_PHRASES = [
    re.compile(r"\bhow to (make|build|synthesize) (a )?(bomb|explosive)\b", re.I),
]


def check_output(text: str) -> tuple[list[str], str | None]:
    """Returns (violation codes, redacted_text or None if nothing changed)."""
    violations: list[str] = []
    redacted = text

    for label, pattern in PII_PATTERNS.items():
        if pattern.search(redacted):
            violations.append(f"pii_leak:{label}")
            redacted = pattern.sub(f"[REDACTED_{label.upper()}]", redacted)

    for pattern in _BANNED_PHRASES:
        if pattern.search(text):
            violations.append("banned_content")

    redacted_text = redacted if redacted != text else None
    return violations, redacted_text
