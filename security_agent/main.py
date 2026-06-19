from __future__ import annotations
import argparse
import os
import sys
from pathlib import Path

from .graph import graph, SecurityReviewState


def main() -> None:
    parser = argparse.ArgumentParser(
        prog="security-agent",
        description="LangGraph security-review agent for .NET APIs — maps findings to OWASP API Top 10",
    )
    parser.add_argument(
        "source_path",
        nargs="?",
        default=str(Path(__file__).resolve().parents[1] / "ExpenseTracker.Api"),
        help="Root directory of the .NET project to scan (default: ../ExpenseTracker.Api)",
    )
    parser.add_argument(
        "--no-llm",
        action="store_true",
        help="Run heuristic scan only — skip LLM triage (no Azure credentials required)",
    )
    parser.add_argument(
        "--output", "-o",
        metavar="FILE",
        help="Write the markdown report to FILE instead of stdout",
    )
    args = parser.parse_args()

    if not args.no_llm and not os.environ.get("AZURE_FOUNDRY_ENDPOINT"):
        sys.exit(
            "ERROR: AZURE_FOUNDRY_ENDPOINT is not set.\n"
            "Export the Azure AI Foundry project endpoint before running:\n"
            "  $env:AZURE_FOUNDRY_ENDPOINT='https://<resource>.services.ai.azure.com/api/projects/<project>'\n"
            "Then authenticate with: az login\n"
            "\nTip: use --no-llm to run the heuristic scanner without Azure credentials."
        )

    source = str(Path(args.source_path).resolve())
    mode = "heuristic scan only" if args.no_llm else "heuristic scan + LLM triage"
    print("Security Review Agent")
    print(f"Scanning : {source}")
    print(f"Mode     : {mode}")
    print()

    initial_state: SecurityReviewState = {
        "source_path": source,
        "no_llm": args.no_llm,
        "raw_findings": [],
        "triaged_findings": [],
        "report_markdown": "",
    }

    result = graph.invoke(initial_state)

    raw_count = len(result["raw_findings"])
    report = result["report_markdown"]

    print()
    print(f"Heuristic candidates : {raw_count}")
    if not args.no_llm:
        print(f"Confirmed by LLM     : {len(result['triaged_findings'])}")
    print()
    print("=" * 72)
    print(report)
    print("=" * 72)

    if args.output:
        Path(args.output).write_text(report, encoding="utf-8")
        print(f"\nReport written to: {args.output}")


if __name__ == "__main__":
    main()
