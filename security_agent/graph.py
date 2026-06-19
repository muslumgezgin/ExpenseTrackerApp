from __future__ import annotations
import os
import re
from typing import TypedDict

from langgraph.graph import StateGraph, START, END

from .models import RawFinding, TriagedFinding, TriageOutput
from .scanners import scan_directory


# ── State ─────────────────────────────────────────────────────────────────────

class SecurityReviewState(TypedDict):
    source_path: str
    no_llm: bool
    raw_findings: list[RawFinding]
    triaged_findings: list[TriagedFinding]
    report_markdown: str


# ── LLM — built lazily so --no-llm never needs Azure credentials ──────────────

def _build_triage_chain():
    from azure.identity import DefaultAzureCredential, get_bearer_token_provider
    from langchain_openai import AzureChatOpenAI

    project_endpoint = os.environ["AZURE_FOUNDRY_ENDPOINT"]
    azure_endpoint = re.sub(r"/api/.*$", "", project_endpoint).rstrip("/") + "/"
    token_provider = get_bearer_token_provider(
        DefaultAzureCredential(),
        "https://cognitiveservices.azure.com/.default",
    )
    llm = AzureChatOpenAI(
        azure_endpoint=azure_endpoint,
        azure_deployment=os.environ.get("AZURE_FOUNDRY_DEPLOYMENT", "gpt-4.1"),
        api_version=os.environ.get("AZURE_OPENAI_API_VERSION", "2024-12-01-preview"),
        azure_ad_token_provider=token_provider,
        temperature=0,
    )
    return llm.with_structured_output(TriageOutput)


_TRIAGE_SYSTEM = """\
You are a senior application-security engineer specialising in .NET minimal-API security.
You will receive candidate findings produced by a heuristic source-code scanner.

For each finding you must:
1. Decide whether it is a genuine risk (confirmed=true) or a false positive (confirmed=false).
   - A finding guarded exclusively by `#if DEBUG` is lower risk but still worth noting; mark it confirmed=true with severity LOW or MEDIUM.
   - A finding in a test project or example code is a false positive.
2. Assign a severity: HIGH / MEDIUM / LOW / INFO.
3. Map it to the single best-fitting OWASP API Security Top 10 2023 category.
4. Write a concrete, actionable recommendation (one to three sentences).

Return exactly one TriagedFinding per input finding — no more, no less.
"""


# ── Nodes ─────────────────────────────────────────────────────────────────────

def scan_node(state: SecurityReviewState) -> dict:
    findings = scan_directory(state["source_path"])
    print(f"  [scan] {len(findings)} candidate finding(s) detected")
    return {"raw_findings": findings}


def triage_node(state: SecurityReviewState) -> dict:
    findings = state["raw_findings"]
    findings_text = "\n\n".join(
        f"[{i + 1}] Scanner : {f.scanner}\n"
        f"    File    : {f.file}:{f.line}\n"
        f"    Snippet : {f.snippet}\n"
        f"    Hint    : {f.description}"
        for i, f in enumerate(findings)
    )
    print(f"  [triage] sending {len(findings)} finding(s) to LLM for confirmation …")
    triage_chain = _build_triage_chain()
    result: TriageOutput = triage_chain.invoke(
        [
            {"role": "system", "content": _TRIAGE_SYSTEM},
            {
                "role": "user",
                "content": (
                    f"Review these {len(findings)} candidate finding(s) "
                    f"from the ExpenseTracker .NET API project:\n\n{findings_text}"
                ),
            },
        ]
    )
    confirmed = [f for f in result.findings if f.confirmed]
    print(f"  [triage] {len(confirmed)} finding(s) confirmed by LLM")
    return {"triaged_findings": confirmed}


def report_node(state: SecurityReviewState) -> dict:
    lines: list[str] = [
        "# Security Review Report",
        f"**Source:** `{state['source_path']}`",
        "",
    ]

    if state.get("no_llm"):
        return {"report_markdown": "\n".join(lines + _render_raw(state["raw_findings"]))}

    return {"report_markdown": "\n".join(lines + _render_triaged(state.get("triaged_findings", [])))}


def _render_raw(findings: list[RawFinding]) -> list[str]:
    lines = [
        "**Mode:** heuristic scan only — LLM triage skipped.",
        "> Re-run without `--no-llm` to confirm findings, filter false positives, and get OWASP mapping.",
        "",
    ]
    if not findings:
        return lines + ["## Result", "", "No candidates detected."]

    lines += [
        f"## Candidates — {len(findings)} raw finding(s)",
        "",
        "| # | Scanner | Location | Description |",
        "|---|---------|----------|-------------|",
    ]
    for i, f in enumerate(findings, 1):
        lines.append(f"| {i} | `{f.scanner}` | `{f.file}:{f.line}` | {f.description} |")

    lines.append("")
    for i, f in enumerate(findings, 1):
        lines += [
            "---",
            f"### [{i}] `{f.scanner}`",
            "",
            f"**Location:** `{f.file}:{f.line}`",
            "",
            "**Evidence**",
            "```",
            f.snippet,
            "```",
            "",
            f"**Scanner hint:** {f.description}",
            "",
        ]
    return lines


def _render_triaged(confirmed: list[TriagedFinding]) -> list[str]:
    if not confirmed:
        return ["## Result", "", "No confirmed security findings — the scanned code appears clean."]

    severity_order = {"HIGH": 0, "MEDIUM": 1, "LOW": 2, "INFO": 3}
    findings = sorted(confirmed, key=lambda f: severity_order.get(f.severity.value, 4))

    lines = [
        f"## Summary — {len(findings)} confirmed finding(s)",
        "",
        "| # | Severity | OWASP | Title | Location |",
        "|---|:--------:|-------|-------|----------|",
    ]
    for i, f in enumerate(findings, 1):
        owasp_id = f.owasp_category.value.split(" ", 1)[0]
        lines.append(f"| {i} | **{f.severity.value}** | {owasp_id} | {f.title} | `{f.file}:{f.line}` |")

    lines.append("")
    for i, f in enumerate(findings, 1):
        lines += [
            "---",
            f"### [{i}] {f.title}",
            "",
            "| Field | Value |",
            "|-------|-------|",
            f"| **Severity** | {f.severity.value} |",
            f"| **OWASP** | {f.owasp_category.value} |",
            f"| **Location** | `{f.file}:{f.line}` |",
            "",
            "**Evidence**",
            "```",
            f.snippet,
            "```",
            "",
            f"**Risk**  \n{f.description}",
            "",
            f"**Fix**  \n{f.recommendation}",
            "",
        ]
    return lines


# ── Routing ───────────────────────────────────────────────────────────────────

def route_after_scan(state: SecurityReviewState) -> str:
    if state.get("no_llm") or not state["raw_findings"]:
        return "report"
    return "triage"


# ── Graph ─────────────────────────────────────────────────────────────────────

def build_graph():
    g = StateGraph(SecurityReviewState)

    g.add_node("scan", scan_node)
    g.add_node("triage", triage_node)
    g.add_node("report", report_node)

    g.add_edge(START, "scan")
    g.add_conditional_edges(
        "scan",
        route_after_scan,
        {"triage": "triage", "report": "report"},
    )
    g.add_edge("triage", "report")
    g.add_edge("report", END)

    return g.compile()


graph = build_graph()
