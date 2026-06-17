# Expense Tracker App

An AI-powered expense tracking application built with .NET 10 and Blazor. It lets you manage budgets and expenses through a chat interface backed by an LLM with tool-calling support. The API also exposes an MCP server so any MCP-compatible client (e.g. Claude Desktop) can interact with your expense data directly.

## Architecture

```
ExpenseTrackerApp/
├── ExpenseTracker.Api      — REST API + MCP server (ASP.NET Core 10)
├── ExpenseTracker.Agent    — AI orchestration library (provider-agnostic)
└── ExpenseTracker.Web      — Blazor Server web UI
```

| Project | Port | Responsibility |
|---|---|---|
| `ExpenseTracker.Api` | `5132` | Expenses, budgets, categories, AI chat, MCP endpoint |
| `ExpenseTracker.Web` | `5080` | Dashboard UI + AI chat UI |

Data is stored in an **in-memory database** — it resets every time the API restarts.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An AI provider account (see [AI Provider Configuration](#ai-provider-configuration))

---

## Quick Start

Open two terminal windows.

**Terminal 1 — start the API:**
```bash
cd ExpenseTracker.Api
dotnet run --launch-profile http
```

**Terminal 2 — start the Web UI:**
```bash
cd ExpenseTracker.Web
dotnet run --launch-profile http
```

Then open **http://localhost:5080** in your browser.

---

## AI Orchestration

The Agent supports two orchestration **modes** and three AI **providers**.

### Orchestration Modes

Set `AiOrchestrator:Mode` in `ExpenseTracker.Api/appsettings.json`:

| Mode | Value | How it works |
|---|---|---|
| Foundry Agent | `FoundryAgent` (default) | Sends the user message to a pre-built Azure AI Foundry agent. The agent decides which tools to call (budget, expenses, categories) and executes them automatically via the MCP server. |
| Manual API | `ManualApi` | The configured LLM extracts intent from the user message as structured JSON, then the application manually calls the matching REST API endpoints. No Foundry agent required — works with any provider. |

If `FoundryAgent` mode fails (e.g. the agent is unreachable), the orchestrator **automatically falls back** to `ManualApi` mode.

---

## Azure AI Foundry Setup

[Azure AI Foundry](https://ai.azure.com) is Microsoft's platform for building and deploying AI agents. In this project it is the **default and recommended** mode — the Foundry agent is pre-configured with the expense tracking tools and handles all tool-calling decisions automatically.

### 1. Prerequisites

- An Azure subscription
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) installed
- An Azure AI Foundry hub and project created at [ai.azure.com](https://ai.azure.com)

### 2. Create the Agent in Azure AI Foundry

1. Open your project in the [Azure AI Foundry portal](https://ai.azure.com).
2. Go to **Agents** → **New Agent**.
3. Set the agent name (must match `AzureFoundry:AgentName` in config).
4. Choose a model.
5. In the agent's **Tools** section, add an **MCP server** connection:
   - URL: `http://localhost:5132/mcp` (your running API)
   - This gives the agent access to all five expense tools (list categories, add expense, check budget, etc.)
6. Save the agent.

### 3. Find Your Project Endpoint

In the Foundry portal, go to **Project settings** → **Overview**. Copy the **Project endpoint** — it looks like:

```
https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
```

### 4. Authenticate

The app uses `AzureCliCredential`. Log in before starting the API:

```bash
az login --tenant <your-tenant-id>
```

### 5. Configure appsettings.json

```json
"AiOrchestrator": {
  "Mode": "FoundryAgent",
  "Provider": "AzureFoundry"
},
"AzureFoundry": {
  "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
  "AgentName": "agent-<name>",
  "Deployment": "model",
  "TenantId": "<your-tenant-id>"
}
```

---

## AI Provider Configuration (Manual API mode)

When running in `ManualApi` mode, set `AiOrchestrator:Provider` to select the LLM used for intent extraction. The provider is also used as the `IChatClient` for Foundry-less setups.

### Azure OpenAI

```json
"AiOrchestrator": {
  "Mode": "ManualApi",
  "Provider": "AzureOpenAI"
},
"AzureOpenAI": {
  "Endpoint": "https://<your-resource>.openai.azure.com/",
  "ApiKey": "<your-api-key>",
  "Deployment": "model"
}
```

### OpenAI

```json
"AiOrchestrator": {
  "Mode": "ManualApi",
  "Provider": "OpenAI"
},
"OpenAI": {
  "ApiKey": "sk-...",
  "Model": "model"
}
```

> **Tip:** Store secrets with `dotnet user-secrets` rather than in `appsettings.json`:
> ```bash
> cd ExpenseTracker.Api
> dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
> ```

---

## Web UI

### Dashboard (`/`)

- **Budget Status** — current month's spending vs. limit with a progress bar
- **Category Spending** — breakdown of spending by category
- **AI Assistant** — quick link to the chat
- **Recent Expenses** — table of this month's expenses

### AI Chat (`/chat`)

Type natural language to manage your data. Examples:

| What you type | What happens |
|---|---|
| `Set a $3000 budget for this month` | Creates a budget for the current month |
| `I spent $45 on groceries` | Adds an expense, creates category if needed |
| `I spent $120 at the restaurant` | Adds expense under a food/dining category |
| `Check my budget` | Reports total spent, limit, and remaining |
| `List categories` | Shows all available categories |

The AI automatically resolves category names, checks the current budget status, and warns you when you go over budget.

---

## REST API

Base URL: `http://localhost:5132`

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/chat` | Send a chat message to the AI |
| `GET` | `/api/expenses?month=&year=` | List expenses for a month |
| `POST` | `/api/expenses` | Add an expense |
| `GET` | `/api/categories` | List all categories |
| `POST` | `/api/categories` | Create a category |
| `GET` | `/api/budgets/status?month=&year=` | Get budget status |
| `POST` | `/api/budgets` | Create or update a budget |
| `GET` | `/api/budgets/breakdown?month=&year=` | Get budget + per-category breakdown |

### Example: add an expense

```bash
curl -X POST http://localhost:5132/api/expenses \
  -H "Content-Type: application/json" \
  -d '{"amount": 45.50, "date": "2026-06-17T12:00:00", "description": "Lunch", "categoryId": 1}'
```

### Example: chat

```bash
curl -X POST http://localhost:5132/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "I spent $50 on groceries", "sessionId": "my-session-1"}'
```

The `sessionId` is optional — omit it to start a new session, or reuse the one returned in the response to maintain conversation history.

---

## MCP Server

The API exposes an [MCP](https://modelcontextprotocol.io) endpoint at:

```
http://localhost:5132/mcp
```

### Available MCP Tools

| Tool | Description |
|---|---|
| `ListCategoriesAsync` | List all expense categories |
| `CreateCategoryAsync` | Create a new category |
| `GetBudgetStatusAsync` | Get budget for a specific month/year |
| `GetBudgetBreakdownAsync` | Get budget + per-category spending breakdown |
| `AddExpenseAsync` | Add a new expense |

### Connect Claude Desktop

Add this to your Claude Desktop `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "expense-tracker": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:5132/mcp"]
    }
  }
}
```

Then start the API and reload Claude Desktop. You can now ask Claude to track expenses, check your budget, and manage categories directly.

---

## Web UI Security

The Web app authenticates to the API using a static API key defined in `ExpenseTracker.Web/appsettings.json`:

```json
"ApiAuth": {
  "ApiKey": "<your-api-key>"
}
```

The API key is sent as the `X-Api-Key` header on every request. Change both values if you deploy to a shared environment.

---

## Project Structure

```
ExpenseTracker.Api/
├── Api/
│   ├── Endpoints/          — Minimal API route definitions
│   └── Mcp/                — MCP tool definitions
├── Application/
│   ├── DTOs/               — Request/response models
│   ├── Interfaces/         — Service contracts
│   └── Services/           — Business logic
├── Domain/Entities/        — EF Core entities (Expense, Budget, Category)
├── Infrastructure/
│   ├── Data/               — DbContext + seeder
│   └── Middleware/         — Global exception handler
└── appsettings.json        — AI provider configuration

ExpenseTracker.Agent/
├── Interfaces/             — IAiOrchestrator contract
├── Models/                 — Shared API models
├── Services/               — AiOrchestrator implementation
└── Extensions/             — DI registration + provider selection

ExpenseTracker.Web/
├── Components/
│   ├── Pages/              — Home (dashboard) + Chat
│   └── Layout/             — MainLayout, ChatLayout
├── Services/               — ExpenseApiService, ChatSessionService, ApiAiOrchestrator
└── appsettings.json        — API base URL configuration
```
