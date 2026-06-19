from __future__ import annotations
import os
import re
from datetime import datetime
from pathlib import Path
from typing import Literal, TypedDict

from langchain_core.messages import SystemMessage, HumanMessage
from pydantic import BaseModel, Field
from langgraph.graph import StateGraph, START, END

from security_agent.graph import graph as security_graph
from fraud_agent.graph import fraud_graph


# ── Supervisor state ──────────────────────────────────────────────────────────

class SupervisorState(TypedDict):
    user_request: str
    api_base_url: str
    # Populated by route_node
    route: str
    source_path: str | None
    no_llm: bool
    month: int | None
    year: int | None
    # Final output
    report: str


# ── Router LLM structured output ──────────────────────────────────────────────

class RouteDecision(BaseModel):
    route: Literal["security", "fraud"] = Field(
        description="'security' for code/API review; 'fraud' for expense/policy analysis"
    )
    reasoning: str = Field(description="One sentence explaining the routing decision")
    source_path: str | None = Field(
        None, description="Absolute or relative path to scan (security only)"
    )
    no_llm: bool = Field(
        False, description="True if user explicitly asks for scanner-only mode"
    )
    month: int | None = Field(None, description="Month to analyse 1-12 (fraud only)")
    year: int | None = Field(None, description="Year to analyse e.g. 2025 (fraud only)")


def _build_router():
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
    return llm.with_structured_output(RouteDecision)


_now = datetime.now()
_ROUTER_SYSTEM = f"""\
You are a routing agent for a multi-agent system with two subgraphs:

  • security — reviews .NET source code for vulnerabilities (OWASP API Top 10)
  • fraud    — analyses runtime expense transactions for fraud, duplicates, and policy violations

Route to "security" when the user mentions: code review, vulnerability scan, API security, OWASP, source code, appsettings, Program.cs.
Route to "fraud" when the user mentions: expenses, spending, receipts, budget, fraud, duplicates, policy violations, transactions.

Today is {_now.strftime("%Y-%m-%d")}.
If the user says "this month" or "current month", set month={_now.month} and year={_now.year}.
If no month/year is stated, default to the current month and year.
If no source_path is given for security, leave it null.
"""


# ── Nodes ─────────────────────────────────────────────────────────────────────

def route_node(state: SupervisorState) -> dict:
    print("  [supervisor] classifying request …")
    router = _build_router()
    decision: RouteDecision = router.invoke([
        SystemMessage(content=_ROUTER_SYSTEM),
        HumanMessage(content=state["user_request"]),
    ])
    print(f"  [supervisor] → {decision.route}  ({decision.reasoning})")
    return {
        "route": decision.route,
        "source_path": decision.source_path,
        "no_llm": decision.no_llm,
        "month": decision.month,
        "year": decision.year,
    }


def security_node(state: SupervisorState) -> dict:
    source = state["source_path"] or str(
        Path(__file__).resolve().parents[1] / "ExpenseTracker.Api"
    )
    result = security_graph.invoke({
        "source_path": source,
        "no_llm": state.get("no_llm", False),
        "raw_findings": [],
        "triaged_findings": [],
        "report_markdown": "",
    })
    return {"report": result["report_markdown"]}


def fraud_node(state: SupervisorState) -> dict:
    now = datetime.now()
    result = fraud_graph.invoke({
        "month": state.get("month") or now.month,
        "year": state.get("year") or now.year,
        "api_base_url": state.get("api_base_url") or "http://localhost:5132",
        "expenses": [],
        "breakdown": None,
        "flags": [],
        "llm_findings": [],
        "report_markdown": "",
    })
    return {"report": result["report_markdown"]}


def route_decision(state: SupervisorState) -> str:
    return state["route"]


# ── Graph ─────────────────────────────────────────────────────────────────────

def build_supervisor_graph():
    g = StateGraph(SupervisorState)

    g.add_node("route", route_node)
    g.add_node("security", security_node)
    g.add_node("fraud", fraud_node)

    g.add_edge(START, "route")
    g.add_conditional_edges(
        "route",
        route_decision,
        {"security": "security", "fraud": "fraud"},
    )
    g.add_edge("security", END)
    g.add_edge("fraud", END)

    return g.compile()


supervisor_graph = build_supervisor_graph()
