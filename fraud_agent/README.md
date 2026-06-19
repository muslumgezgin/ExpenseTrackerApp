# Fraud & Policy Agent

A LangGraph subgraph that analyses **runtime expense transactions** for fraud signals, policy violations, and spending anomalies. It is designed to be invoked either standalone or as a compiled subgraph by the [Supervisor](../supervisor/README.md).

## Relationship to the other agents

```
┌─────────────────────────────────────────────────────┐
│                    Supervisor                        │
│  Routes "check expenses / fraud / policy" here  ──► │  ◄── you are here
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│                  Security Agent                      │
│  Reviews .NET source code (design-time risk)         │
└─────────────────────────────────────────────────────┘
```

**Design-time vs runtime boundary** — the security agent reasons over static source code before deployment; this agent reasons over live expense rows after deployment. That boundary is what makes them composable without overlap.

## What it checks

| Rule | Severity | Description |
|------|:--------:|-------------|
| `duplicate_receipt` | HIGH | Same amount + same category submitted within 24 hours — possible double-submission |
| `out_of_policy_amount` | MEDIUM | Single expense exceeds the $200 per-transaction policy limit |
| `velocity_anomaly` | MEDIUM | More than 5 expense submissions on a single day |
| `round_number` | LOW | Amount ≥ $50 that is a multiple of $50 — may indicate an estimate, not an actual receipt |
| `budget_overrun` | HIGH | Total spending has exceeded the monthly budget limit |
| `budget_near_limit` | LOW | 90%+ of the monthly budget has been consumed |
| `category_dominance` | LOW | One category accounts for 70%+ of all spending |

Policy thresholds are defined as constants at the top of `analyzers.py` and can be adjusted without touching the graph logic.

## Graph architecture

```
START
  │
  ▼
fetch_data_node          GET /api/expenses  +  GET /api/budgets/breakdown
  │
  ├──────────────────────────────────────────────┐
  │                                              │
  ▼                    ▼                         ▼
duplicates_node     policy_node           velocity_node
(duplicate_receipt) (out_of_policy,       (velocity_anomaly,
                     round_number)         budget checks)
  │                    │                         │
  └──────────────────────────────────────────────┘
                        │
                        ▼  flags accumulate via operator.add reducer
                  llm_reason_node        Confirms/dismisses each flag,
                        │               adds business-risk narrative
                        ▼
                   report_node           Markdown: budget snapshot +
                        │               findings table + detail sections
                        ▼
                       END
```

**Fan-out / fan-in** — after `fetch_data`, the three analyzer nodes run in parallel. Each appends to the shared `flags` list using a LangGraph `Annotated[list, operator.add]` reducer, so writes never conflict. `llm_reason` waits for all three before proceeding (LangGraph barrier semantics).

## Data source

The agent calls the live ExpenseTracker REST API:

| Call | Endpoint | Purpose |
|------|----------|---------|
| `GET /api/expenses?month=M&year=Y` | Expense rows for the period | All heuristic checks |
| `GET /api/budgets/breakdown?month=M&year=Y` | Budget summary + per-category spending | Budget and dominance checks |

If the API is unreachable the agent degrades gracefully — expenses default to an empty list, budget checks are skipped, and the report states no data was found.

## File structure

```
fraud_agent/
  __init__.py     package marker
  models.py       Pydantic types: ExpenseRow, BudgetSummary, FraudFlag, FraudFinding,
                  FraudAnalysisOutput, FraudState (TypedDict with reducer annotation)
  analyzers.py    Seven pure-function heuristic checks — no LLM, no I/O
  graph.py        LangGraph StateGraph: nodes, fan-out edges, LLM wiring, report formatter
  README.md       this file
```

## Prerequisites

- Python 3.11+
- Azure AI Foundry project with a `gpt-4.1` deployment
- Azure CLI — for `DefaultAzureCredential` authentication
- ExpenseTracker API running (default: `http://localhost:5132`)

## Installation

```powershell
pip install -r requirements.txt   # from repo root
```

## Configuration

```powershell
az login
$env:AZURE_FOUNDRY_ENDPOINT = "https://<resource>.services.ai.azure.com/api/projects/<project>"
```

Optional:

| Variable | Default | Description |
|----------|---------|-------------|
| `AZURE_FOUNDRY_DEPLOYMENT` | `gpt-4.1` | Deployment name |
| `AZURE_OPENAI_API_VERSION` | `2024-12-01-preview` | Azure OpenAI API version |

## Usage — standalone

```python
from fraud_agent.graph import fraud_graph

result = fraud_graph.invoke({
    "month": 6,
    "year": 2025,
    "api_base_url": "http://localhost:5132",
    "expenses": [],
    "breakdown": None,
    "flags": [],
    "llm_findings": [],
    "report_markdown": "",
})
print(result["report_markdown"])
```

Or via the [Supervisor](../supervisor/README.md):

```powershell
python -m supervisor.main "check this month's expenses for fraud"
python -m supervisor.main "any policy violations in June 2025?"
```

## Extending the analyzer

Add a new pure function to `analyzers.py`:

```python
def check_weekend_spending(expenses: list[ExpenseRow]) -> list[FraudFlag]:
    from datetime import datetime
    return [
        FraudFlag(
            rule="weekend_expense",
            severity="LOW",
            description=f"Expense on {e.date[:10]} falls on a weekend",
            expense_ids=[e.id],
        )
        for e in expenses
        if datetime.fromisoformat(e.date[:19]).weekday() >= 5
    ]
```

Then call it inside the appropriate node in `graph.py` — no graph rewiring needed unless you want it to run in its own parallel branch.
