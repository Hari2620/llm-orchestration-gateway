"""Offline eval CLI: replay everything the gateway logged to logs/evals.jsonl
and print a pass/fail summary. This is the "evaluation hooks" half of the
brief that isn't a live HTTP call — a promptfoo-style regression check you'd
wire into CI before shipping a prompt-version bump.

Usage (from the repo root, so the guardrails_eval package resolves):
    python -m guardrails_eval.evals.run_eval [path/to/evals.jsonl]
"""

import json
import sys
from pathlib import Path

from guardrails_eval.evals.heuristics import score_transcript


def main() -> int:
    log_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("logs/evals.jsonl")

    if not log_path.exists():
        print(f"No eval log found at {log_path}. Run the gateway and send a few requests first.")
        return 1

    total = 0
    failed = 0

    with log_path.open() as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            record = json.loads(line)
            total += 1

            result = score_transcript(
                input_text=record["input"],
                output_text=record["output"],
                latency_ms=record["latencyMs"],
            )

            if not result["passed"]:
                failed += 1
                print(f"FAIL  {record['correlationId']}  {record['promptName']}@{record['promptVersion']}  "
                      f"findings={result['findings']}")

    print(f"\n{total - failed}/{total} transcripts passed heuristic eval.")
    return 1 if failed > 0 else 0


if __name__ == "__main__":
    raise SystemExit(main())
