# Security Review Agent

A LangGraph-based security review agent that scans .NET Minimal API source code for vulnerabilities, confirms findings using an LLM hosted on Azure AI Foundry, and maps each one to the [OWASP API Security Top 10 (2023)](https://owasp.org/API-Security/editions/2023/en/0x00-header/).

## Architecture

```
START
  │
  ▼
┌─────────────┐
│  scan_node  │  Regex heuristics over .cs / appsettings*.json files
└──────┬──────┘
       │
       ├─── --no-llm flag OR no findings ────────────────────────┐
       │                                                          ▼
       └─── has findings ──► ┌───────────────┐  ┌─────────────────────┐
                             │ triage_node   │─►│     report_node     │──► END
                             │ (LLM confirm) │  │ (Markdown — raw or  │
                             └───────────────┘  │  triaged depending  │
                                                │  on mode)           │
                                                └─────────────────────┘
```

**Three nodes:**
| Node | Role |
|------|------|
| `scan_node` | Runs 7 regex rules + a project-level auth check; produces raw `RawFinding` objects |
| `triage_node` | Sends all candidates to `gpt-4.1` via Azure AI Foundry; gets back structured `TriagedFinding` objects (confirmed/dismissed, severity, OWASP category, recommendation) |
| `report_node` | Renders a raw-candidates report (`--no-llm`) or a confirmed-findings report with OWASP mapping (full mode) |

**Conditional routing:** the graph skips `triage_node` and jumps directly to `report_node` when `--no-llm` is set or the scanner finds zero candidates. The LLM is never imported or authenticated in those cases.

## Scanner rules

| Rule | What it detects |
|------|----------------|
| `wildcard_cors` | `SetIsOriginAllowed(_ => true)` or `AllowAnyOrigin()` in CORS policy |
| `hardcoded_secret` | Password / secret / API key literals in C# source |
| `sensitive_config_value` | `TenantId`, `ClientSecret`, `ApiKey`, etc. committed to `appsettings*.json` |
| `allowed_hosts_wildcard` | `"AllowedHosts": "*"` — disables host-header validation |
| `raw_sql_interpolation` | `FromSqlRaw` / `ExecuteSqlRaw` with `$"..."` interpolation |
| `weak_crypto` | MD5, SHA1, DES, RC2, TripleDES usage |
| `exception_detail_exposure` | `exception.Message` written directly to the HTTP response |
| `missing_authentication` | Project-level: endpoints registered but no auth middleware wired up |

## OWASP mapping

The LLM maps each confirmed finding to one of the OWASP API Security Top 10 2023 categories:

`API1` Object Level Authorization · `API2` Broken Authentication · `API3` Object Property Level Authorization · `API4` Unrestricted Resource Consumption · `API5` Function Level Authorization · `API6` Sensitive Business Flows · `API7` SSRF · `API8` Security Misconfiguration · `API9` Improper Inventory Management · `API10` Unsafe API Consumption

## Prerequisites

- Python 3.11+
- Azure AI Foundry project with a `gpt-4.1` (or compatible) deployment *(full mode only)*
- Azure CLI (`az`) — for local `DefaultAzureCredential` authentication *(full mode only)*

## Installation

```powershell
pip install -r security_agent/requirements.txt
```

`security_agent/requirements.txt`:
```
langgraph>=0.2
langchain-openai>=0.3
azure-identity>=1.19
pydantic>=2.0
```

## Configuration

`--no-llm` mode requires no credentials and no environment variables — just run it.

For full mode, authentication uses `DefaultAzureCredential` — no API keys needed. Log in once with the Azure CLI:

```powershell
az login
```

Set the required environment variable (copy `ProjectEndpoint` from your Foundry project):

```powershell
$env:AZURE_FOUNDRY_ENDPOINT = "https://<resource>.services.ai.azure.com/api/projects/<project>"
```

Optional overrides:

| Variable | Default | Description |
|----------|---------|-------------|
| `AZURE_FOUNDRY_DEPLOYMENT` | `gpt-4.1` | Deployment name in your Foundry project |
| `AZURE_OPENAI_API_VERSION` | `2024-12-01-preview` | Azure OpenAI API version |

The agent strips the `/api/projects/...` path internally and derives the Azure OpenAI endpoint (`https://<resource>.services.ai.azure.com/`) that `AzureChatOpenAI` expects.

## Usage

```powershell
# Step 1 — heuristic scan only, no credentials required
python -m security_agent.main --no-llm

# Step 2 — full run: LLM confirms findings, filters false positives, maps to OWASP
python -m security_agent.main

# Scan a specific project directory
python -m security_agent.main path\to\YourApi --no-llm
python -m security_agent.main path\to\YourApi

# Save the report to a file
python -m security_agent.main --output report.md
python -m security_agent.main --no-llm --output candidates.md
```

### Example output — `--no-llm`

```
Security Review Agent
Scanning : C:\...\ExpenseTracker.Api
Mode     : heuristic scan only

  [scan] 5 candidate finding(s) detected

Heuristic candidates : 5

========================================================================
# Security Review Report
**Mode:** heuristic scan only — LLM triage skipped.
> Re-run without `--no-llm` to confirm findings, filter false positives, and get OWASP mapping.

## Candidates — 5 raw finding(s)

| # | Scanner                  | Location                          | Description                              |
|---|--------------------------|-----------------------------------|------------------------------------------|
| 1 | `wildcard_cors`          | `Program.cs:22`                   | CORS policy allows any origin …          |
| 2 | `sensitive_config_value` | `appsettings.json:12`             | Sensitive identifier committed …         |
| 3 | `allowed_hosts_wildcard` | `appsettings.json:19`             | AllowedHosts:'*' disables …              |
| 4 | `exception_detail_exposure` | `GlobalExceptionHandler.cs:19` | exception.Message written to response … |
| 5 | `missing_authentication` | `Program.cs:1`                    | No auth middleware configured …          |
========================================================================
```

### Example output — full mode

```
Security Review Agent
Scanning : C:\...\ExpenseTracker.Api
Mode     : heuristic scan + LLM triage

  [scan]   5 candidate finding(s) detected
  [triage] sending 5 finding(s) to LLM for confirmation …
  [triage] 5 finding(s) confirmed by LLM

Heuristic candidates : 5
Confirmed by LLM     : 5

========================================================================
# Security Review Report

## Summary — 5 confirmed finding(s)

| # | Severity   | OWASP | Title                        | Location                      |
|---|:----------:|-------|------------------------------|-------------------------------|
| 1 | **HIGH**   | API2  | No Authentication Middleware | Program.cs:1                  |
| 2 | **HIGH**   | API8  | Sensitive Config in VCS      | appsettings.json:12           |
| 3 | **MEDIUM** | API8  | Wildcard CORS Policy         | Program.cs:22                 |
| 4 | **MEDIUM** | API8  | AllowedHosts Wildcard        | appsettings.json:19           |
| 5 | **MEDIUM** | API8  | Exception Detail Exposure    | GlobalExceptionHandler.cs:19  |
========================================================================
```

## File structure

```
security_agent/
  __init__.py       package marker
  models.py         Pydantic types: RawFinding, TriagedFinding, TriageOutput, Severity, OWASPCategory
  scanners.py       Regex scanner rules + project-level auth check
  graph.py          LangGraph StateGraph: nodes, conditional routing, LLM wiring
  main.py           CLI entry point
  requirements.txt  Python dependencies
  README.md         this file
```

## Extending the scanner

Add a new rule to `RULES` in `scanners.py`:

```python
ScannerRule(
    name="my_rule",
    description="Human-readable description passed to the LLM as context",
    pattern=r"your_regex_here",
    glob="*.cs",          # passed to Path.rglob()
),
```

No other changes are needed — the rule is automatically picked up by `scan_directory` and its findings flow through triage and into the report.
