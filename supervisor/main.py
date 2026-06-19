from __future__ import annotations
import argparse
import os
import sys
from pathlib import Path

from .graph import supervisor_graph


def main() -> None:
    parser = argparse.ArgumentParser(
        prog="supervisor",
        description=(
            "Multi-agent supervisor — route natural-language requests to the "
            "security-review or fraud-detection subgraph."
        ),
    )
    parser.add_argument(
        "request",
        nargs="?",
        default=None,
        help=(
            'Natural-language request (optional — prompted interactively if omitted). Examples:\n'
            '  "review my API code for vulnerabilities"\n'
            '  "check this month\'s expenses for fraud"'
        ),
    )
    parser.add_argument(
        "--api-url",
        default="http://localhost:5132",
        metavar="URL",
        help="ExpenseTracker API base URL used by the fraud subgraph (default: http://localhost:5132)",
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
            "  $env:AZURE_FOUNDRY_ENDPOINT='https://<resource>.services.ai.azure.com/api/projects/<project>'\n"
            "Then authenticate:  az login"
        )

    request = args.request
    if not request:
        print("Supervisor Agent")
        print("Routes to: security-review  |  fraud-detection")
        print()
        request = input("What would you like to analyse? > ").strip()
        if not request:
            sys.exit("No request provided.")

    print("Supervisor Agent")
    print(f"Request  : {request}")
    print(f"API URL  : {args.api_url}")
    print()

    result = supervisor_graph.invoke({
        "user_request": request,
        "api_base_url": args.api_url,
        "route": "",
        "source_path": None,
        "no_llm": False,
        "month": None,
        "year": None,
        "report": "",
    })

    report = result["report"]
    print()
    print(f"Routed to : {result['route']}")
    print()
    print("=" * 72)
    print(report)
    print("=" * 72)

    if args.output:
        Path(args.output).write_text(report, encoding="utf-8")
        print(f"\nReport written to: {args.output}")


if __name__ == "__main__":
    main()
