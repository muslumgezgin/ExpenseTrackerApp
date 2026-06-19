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
        "--output", "-o",
        metavar="FILE",
        help="Write the markdown report to FILE instead of stdout",
    )
    args = parser.parse_args()

    if not os.environ.get("AZURE_FOUNDRY_ENDPOINT"):
        sys.exit(
            "ERROR: AZURE_FOUNDRY_ENDPOINT is not set.\n"
            "Export the Azure AI Foundry project endpoint before running:\n"
            "  $env:AZURE_FOUNDRY_ENDPOINT='https://<resource>.services.ai.azure.com/api/projects/<project>'\n"
            "Then authenticate with: az login"
        )

    source = str(Path(args.source_path).resolve())
    print(f"Security Review Agent")
    print(f"Scanning : {source}")
    print()

    initial_state: SecurityReviewState = {
        "source_path": source,
        "raw_findings": [],
        "triaged_findings": [],
        "report_markdown": "",
    }

    result = graph.invoke(initial_state)

    raw_count = len(result["raw_findings"])
    confirmed_count = len(result["triaged_findings"])
    report = result["report_markdown"]

    print()
    print(f"Heuristic candidates : {raw_count}")
    print(f"Confirmed by LLM     : {confirmed_count}")
    print()
    print("=" * 72)
    print(report)
    print("=" * 72)

    if args.output:
        Path(args.output).write_text(report, encoding="utf-8")
        print(f"\nReport written to: {args.output}")


if __name__ == "__main__":
    main()
