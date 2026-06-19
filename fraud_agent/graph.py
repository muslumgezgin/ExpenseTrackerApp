from __future__ import annotations
import os
import re

import requests as http
from langgraph.graph import StateGraph, START, END

from .models import (
    ExpenseRow, BudgetBreakdown, FraudFlag, FraudFinding,
    FraudAnalysisOutput, FraudState,
)
from .analyzers import (
    check_duplicates,
    check_policy_violations,
    check_round_numbers,
    check_velocity,
    check_budget_health,
)


# ── LLM ──────────────────────────────────────────────────────────────────────

def _build_triage_chain():
    from azure.identity import DefaultAzureCredential, get_bearer_token_provider
    from langchain_openai import AzureChatOpenAI

    project_endpoint = os.environ["AZURE_FOUNDRY_ENDPOINT"]
    azure_endpoint = re.sub(r"/api/.*$", "", project_endpoint).rstrip("/") + "/"
    token_provider = get_bearer_token_provider(
        DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"
    )
    llm = AzureChatOpenAI(
        azure_endpoint=azure_endpoint,
        azure_deployment=os.environ.get("AZURE_FOUNDRY_DEPLOYMENT", "gpt-4.1"),
        api_version=os.environ.get("AZURE_OPENAI_API_VERSION", "2024-12-01-preview"),
        azure_ad_token_provider=token_provider,
        temperature=0,
    )
    return llm.with_structured_output(FraudAnalysisOutput)


_FRAUD_SYSTEM = """\
You are a corporate expense-compliance officer reviewing flagged transactions.
You will receive candidate flags raised by automated heuristic rules.

For each flag:
1. Decide if it is a genuine risk (confirmed=true) or a benign edge case (confirmed=false).
2. Assign severity: HIGH / MEDIUM / LOW.
3. Write a concise title (≤ 8 words).
4. Explain the business risk in plain language.
5. Give one concrete remediation action (e.g. "require manager approval and original receipt").

Return exactly one FraudFinding per input flag — no more, no less.
"""


# ── Nodes ─────────────────────────────────────────────────────────────────────

def fetch_data_node(state: FraudState) -> dict:
    base = state["api_base_url"].rstrip("/")
    month, year = state["month"], state["year"]

    try:
        r = http.get(f"{base}/api/expenses", params={"month": month, "year": year}, timeout=10)
        expenses = r.json() if r.ok else []
    except Exception as exc:
        print(f"  [fetch] WARNING: expenses endpoint unreachable — {exc}")
        expenses = []

    try:
        r = http.get(f"{base}/api/budgets/breakdown", params={"month": month, "year": year}, timeout=10)
        breakdown = r.json() if r.ok and r.status_code != 204 else None
    except Exception:
        breakdown = None

    print(f"  [fetch] {len(expenses)} expense(s), breakdown={'found' if breakdown else 'none'}")
    return {"expenses": expenses, "breakdown": breakdown}


def duplicates_node(state: FraudState) -> dict:
    rows = [ExpenseRow(**e) for e in state["expenses"]]
    flags = check_duplicates(rows)
    print(f"  [duplicates] {len(flags)} flag(s)")
    return {"flags": flags}


def policy_node(state: FraudState) -> dict:
    rows = [ExpenseRow(**e) for e in state["expenses"]]
    flags = check_policy_violations(rows) + check_round_numbers(rows)
    print(f"  [policy] {len(flags)} flag(s)")
    return {"flags": flags}


def velocity_node(state: FraudState) -> dict:
    rows = [ExpenseRow(**e) for e in state["expenses"]]
    bd = BudgetBreakdown(**state["breakdown"]) if state["breakdown"] else None
    flags = check_velocity(rows) + (check_budget_health(bd) if bd else [])
    print(f"  [velocity+budget] {len(flags)} flag(s)")
    return {"flags": flags}


def llm_reason_node(state: FraudState) -> dict:
    flags: list[FraudFlag] = state["flags"]
    if not flags:
        print("  [llm] no flags — skipping")
        return {"llm_findings": []}

    flags_text = "\n\n".join(
        f"[{i+1}] Rule        : {f.rule}\n"
        f"     Severity    : {f.severity}\n"
        f"     Description : {f.description}\n"
        f"     Expense IDs : {f.expense_ids or 'n/a'}"
        for i, f in enumerate(flags)
    )
    print(f"  [llm] reasoning over {len(flags)} flag(s) …")
    chain = _build_triage_chain()
    result: FraudAnalysisOutput = chain.invoke([
        {"role": "system", "content": _FRAUD_SYSTEM},
        {"role": "user", "content": f"Review these {len(flags)} expense flag(s):\n\n{flags_text}"},
    ])
    confirmed = [f for f in result.findings if f.confirmed]
    print(f"  [llm] {len(confirmed)} finding(s) confirmed")
    return {"llm_findings": confirmed}


def report_node(state: FraudState) -> dict:
    findings: list[FraudFinding] = state["llm_findings"]
    bd = BudgetBreakdown(**state["breakdown"]) if state["breakdown"] else None

    lines: list[str] = [
        "# Expense Fraud & Policy Report",
        f"**Period:** {state['month']:02d}/{state['year']}  ",
        f"**Expenses analysed:** {len(state['expenses'])}  ",
        f"**Flags raised:** {len(state['flags'])}  ",
        "",
    ]

    # Budget snapshot
    if bd:
        b = bd.budget
        bar_filled = int(min(b.total_spent / b.limit, 1.0) * 20) if b.limit else 0
        bar = "█" * bar_filled + "░" * (20 - bar_filled)
        lines += [
            "## Budget Snapshot",
            "",
            f"| | |",
            f"|---|---|",
            f"| Limit | ${b.limit:.2f} |",
            f"| Spent | ${b.total_spent:.2f} |",
            f"| Remaining | ${b.remaining:.2f} |",
            f"| Status | {'**OVER BUDGET**' if b.is_over_budget else 'Within budget'} |",
            "",
            f"`[{bar}]` {b.total_spent / b.limit * 100:.0f}%" if b.limit else "",
            "",
            "**By category:**",
            "",
            "| Category | Spent | % of total |",
            "|----------|------:|----------:|",
        ]
        total = b.total_spent or 1
        for cat in sorted(bd.categories, key=lambda c: c.total_spent, reverse=True):
            lines.append(
                f"| {cat.category_name} | ${cat.total_spent:.2f} | {cat.total_spent/total*100:.0f}% |"
            )
        lines.append("")

    if not findings:
        lines += ["## Result", "", "No confirmed fraud or policy violations found."]
        return {"report_markdown": "\n".join(lines)}

    severity_order = {"HIGH": 0, "MEDIUM": 1, "LOW": 2}
    sorted_findings = sorted(findings, key=lambda f: severity_order.get(f.severity, 3))

    lines += [
        f"## Findings — {len(sorted_findings)} confirmed",
        "",
        "| # | Severity | Title | Expense IDs |",
        "|---|:--------:|-------|-------------|",
    ]
    for i, f in enumerate(sorted_findings, 1):
        ids = ", ".join(f"#{x}" for x in f.expense_ids) or "—"
        lines.append(f"| {i} | **{f.severity}** | {f.title} | {ids} |")

    lines.append("")
    for i, f in enumerate(sorted_findings, 1):
        ids = ", ".join(f"#{x}" for x in f.expense_ids) or "—"
        lines += [
            "---",
            f"### [{i}] {f.title}",
            "",
            f"| Field | Value |",
            f"|-------|-------|",
            f"| **Severity** | {f.severity} |",
            f"| **Expense IDs** | {ids} |",
            "",
            f"**Risk**  \n{f.description}",
            "",
            f"**Action**  \n{f.recommendation}",
            "",
        ]

    return {"report_markdown": "\n".join(lines)}


# ── Graph ─────────────────────────────────────────────────────────────────────

def build_fraud_graph():
    g = StateGraph(FraudState)

    g.add_node("fetch_data", fetch_data_node)
    g.add_node("duplicates", duplicates_node)
    g.add_node("policy", policy_node)
    g.add_node("velocity", velocity_node)
    g.add_node("llm_reason", llm_reason_node)
    g.add_node("report", report_node)

    g.add_edge(START, "fetch_data")

    # Fan-out: three analyzers run in parallel after fetch
    g.add_edge("fetch_data", "duplicates")
    g.add_edge("fetch_data", "policy")
    g.add_edge("fetch_data", "velocity")

    # Fan-in: llm_reason waits for all three to complete
    g.add_edge("duplicates", "llm_reason")
    g.add_edge("policy", "llm_reason")
    g.add_edge("velocity", "llm_reason")

    g.add_edge("llm_reason", "report")
    g.add_edge("report", END)

    return g.compile()


fraud_graph = build_fraud_graph()
