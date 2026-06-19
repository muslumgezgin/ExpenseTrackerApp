# Supervisor Agent

The top-level multi-agent orchestrator. It classifies a natural-language request and routes it to the correct compiled subgraph — either the **Security Agent** (design-time code risk) or the **Fraud Agent** (runtime transaction risk).

## How the three agents relate

```
                        ┌─────────────────────────────────────────┐
                        │            Supervisor Agent              │
                        │                                          │
                        │  1. Accepts a natural-language request   │
                        │  2. LLM classifies intent + extracts     │
                        │     parameters (month/year or path)      │
                        │  3. Routes to the right subgraph         │
                        └────────────┬────────────────┬────────────┘
                                     │                │
               "review my API code"  │                │  "check this month's expenses"
                                     ▼                ▼
                        ┌────────────────┐  ┌──────────────────────┐
                        │ Security Agent │  │    Fraud Agent       │
                        │                │  │                      │
                        │ Design-time    │  │ Runtime risk         │
                        │ risk           │  │                      │
                        │                │  │ Reads live expense   │
                        │ Scans .NET     │  │ rows from the API,   │
                        │ source files   │  │ runs 7 heuristic     │
                        │ for OWASP      │  │ checks in parallel,  │
                        │ vulnerabilities│  │ maps to policy rules │
                        └────────────────┘  └──────────────────────┘
```

### The defensible boundary

| | Security Agent | Fraud Agent |
|---|---|---|
| **Input** | `.cs` and `appsettings*.json` files | Live `/api/expenses` + `/api/budgets` responses |
| **Risk type** | Design-time — vulnerabilities baked into the code | Runtime — suspicious behaviour in transaction data |
| **Output standard** | OWASP API Security Top 10 (2023) | Corporate expense-policy rules |
| **LLM role** | Confirm/dismiss heuristic findings, assign severity | Reason over flagged transactions, add business narrative |

This boundary means the two subgraphs never overlap: fixing a finding in one does not affect the other.

## Supervisor graph architecture

```
START
  │
  ▼
route_node    LLM extracts: route ("security"|"fraud"),
  │           source_path, no_llm, month, year
  │
  ├─ route == "security" ──► security_node ──► END
  │                          (wraps compiled security_graph)
  │
  └─ route == "fraud"    ──► fraud_node    ──► END
                             (wraps compiled fraud_graph)
```

The supervisor uses `add_conditional_edges` with a router function — the canonical LangGraph supervisor pattern. Each subgraph is a pre-compiled `StateGraph` invoked inside a wrapper node that translates between the supervisor state and the subgraph state.

## File structure

```
supervisor/
  __init__.py   package marker
  graph.py      SupervisorState, RouteDecision (Pydantic), route/security/fraud nodes,
                conditional edge, compiled supervisor_graph
  main.py       CLI entry point — interactive prompt or inline argument
  README.md     this file
```

## Prerequisites

- Python 3.11+
- Azure AI Foundry project with a `gpt-4.1` deployment (router + both subgraphs all share the same endpoint)
- Azure CLI — for `DefaultAzureCredential` authentication
- ExpenseTracker API running at `http://localhost:5132` (fraud routing only)

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

## Usage

```powershell
# Interactive prompt (omit the request argument)
python -m supervisor.main

# Inline — routes to fraud subgraph
python -m supervisor.main "check this month's expenses for fraud"
python -m supervisor.main "any duplicate receipts in June 2025?"
python -m supervisor.main "are we over budget?"

# Inline — routes to security subgraph
python -m supervisor.main "review my API code for vulnerabilities"
python -m supervisor.main "scan the source for OWASP issues"

# Save report
python -m supervisor.main "check for policy violations" --output fraud-report.md
python -m supervisor.main "security review" --output security-report.md

# Override API URL (if ExpenseTracker runs on a different port)
python -m supervisor.main "check expenses" --api-url http://localhost:5200
```

### Example session — interactive

```
$ python -m supervisor.main
Supervisor Agent
Routes to: security-review  |  fraud-detection

What would you like to analyse? > check this month's expenses for fraud and policy violations

Supervisor Agent
Request  : check this month's expenses for fraud and policy violations
API URL  : http://localhost:5132

  [supervisor] classifying request …
  [supervisor] → fraud  (user is asking about expense transactions, not source code)
  [fetch]      12 expense(s), breakdown=found
  [duplicates] 1 flag(s)
  [policy]     2 flag(s)
  [velocity+budget] 0 flag(s)
  [llm]        reasoning over 3 flag(s) …
  [llm]        3 finding(s) confirmed

Routed to : fraud
...
```

## Adding a new subgraph

1. Build and compile your `StateGraph` in a new package (e.g. `hr_agent/graph.py`).
2. Add an entry to the `RouteDecision.route` `Literal` type in `supervisor/graph.py`.
3. Add a wrapper node and a conditional edge branch.
4. Update `_ROUTER_SYSTEM` with a one-line description of when to use the new route.

No changes are needed to the existing subgraphs.

## Why this exists

Both subgraphs can be called directly — the supervisor is not required for them to work. It exists to solve a specific problem: **the caller should not need to know which agent to invoke.**

Without the supervisor, a chat interface or a user would have to decide upfront: "is this a security question or a fraud question?" That decision logic lives in the caller and has to be duplicated everywhere the agents are used.

The supervisor moves that decision into the system itself. A single natural-language entry point — `"check this month's expenses"` or `"review my API code"` — is enough. The LLM classifies the intent, extracts the parameters (month/year, source path), and dispatches to the right subgraph. The caller never touches routing logic.

**Use the supervisor when** the entry point is natural language (chat, voice, API with free-text input) or when the number of subgraphs is growing and you want one stable entry point.

**Call subgraphs directly when** you always know which agent you need — the supervisor adds a round-trip LLM call that is unnecessary if the route is already known.
