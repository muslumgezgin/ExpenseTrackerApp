using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using ExpenseTracker.Agent.Interfaces;
using ExpenseTracker.Agent.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpenseTracker.Agent.Services;

public class AiOrchestrator(IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IChatClient chatClient,
    ILogger<AiOrchestrator> logger) : IAiOrchestrator
{
    private readonly Lazy<ProjectResponsesClient> _responseClient = new(() =>
    {
        var endpoint = configuration["AzureFoundry:ProjectEndpoint"];
        var agentName = configuration["AzureFoundry:AgentName"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(agentName))
            throw new InvalidOperationException("Azure Foundry settings are missing. AzureFoundry:ProjectEndpoint and AzureFoundry:AgentName must be configured.");

        var projectClient = new AIProjectClient(new Uri(endpoint), new AzureCliCredential(
            new AzureCliCredentialOptions { TenantId = configuration["AzureFoundry:TenantId"] }));

        return projectClient.OpenAI.GetProjectResponsesClientForAgent(agentName);
    });

    private static readonly ConcurrentDictionary<string, SessionState> Sessions = new();

    private const string SystemPrompt =
         "You are a meticulous, finance-focused AI assistant that helps users track their expenses.\n" +
         "You have access to tools for budget management, category querying, and adding new expenses/budgets.\n" +
         "When a user wants to add an expense, follow these 3 steps in order:\n" +
         "1. Use your tools to find the correct Category ID.\n" +
         "2. Check the current budget status.\n" +
         "3. Add the expense.\n" +
         "Then politely inform the user that the expense has been added; if the new expense exceeds the budget limit, provide a brief warning.\n" +
         "ALWAYS format your responses beautifully with markdown.\n" +
         "Give precise and confident answers. Only respond in English. Use USD as the currency. ";


    private const string ActionExtractionPrompt =
        "You are a finance assistant. Analyze the user's message and respond ONLY with a valid JSON object (no markdown or explanation).\n" +
        "Possible actions: add_expense, list_categories, check_budget, unknown.\n" +
        "JSON format:\n" +
        "{\"action\":\"<action>\",\"amount\":<number or null>,\"description\":\"<string or null>\",\"category_hint\":\"<string or null>\"}";


    public async Task<string> ProcessUserRequestAsync(string userMessage, string sessionId = null, CancellationToken cancellationToken = default)
    {
        // Send message to LLM
        // ? LLM should make a Tool-Call
        // Return the response to the UI

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return "Please enter a message.";
        }

        var activeSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;


        var mode = configuration["AiOrchestrator:Mode"] ?? "FoundryAgent";

        if (mode.Equals("ManualApi", StringComparison.OrdinalIgnoreCase))
        {
            return await ProcessWithManualApiCallsAsync(activeSessionId, userMessage, cancellationToken);
        }

        try
        {
            return await ProcessWithFoundryAgentAsync(activeSessionId, userMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Foundry agent flow failed.");

            return await ProcessWithManualApiCallsAsync(activeSessionId, userMessage, cancellationToken);
        }
    }

    private async Task<string> ProcessWithManualApiCallsAsync(string sessionId, string userMessage, CancellationToken cancellationToken)
    {
        try
        {
            var session = Sessions.GetOrAdd(sessionId, _ => new SessionState());

            // 1. Send user message to LLM and get back a structured action decision
            var action = await ExtractActionFromLlmAsync(session, userMessage, cancellationToken);

            // 2. Execute the action by calling our own API endpoints
            var result = await ExecuteActionAsync(action, cancellationToken);

            // 3. Update session with this turn
            lock (session.SyncRoot)
            {
                session.Turns.Add(new ConversationTurn("User", userMessage));
                session.Turns.Add(new ConversationTurn("Assistant", result));
                if (session.Turns.Count > 20)
                {
                    session.Turns.RemoveRange(0, session.Turns.Count - 20);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual API flow failed.");
            return "An error occurred while processing your request.";
        }
    }

    private async Task<string> ExecuteActionAsync(LlmAction action, CancellationToken ct)
    {
        var apiBaseUrl = configuration["AiOrchestrator:ApiBaseUrl"] ?? "https://localhost:5101";
        var client = httpClientFactory.CreateClient("InternalApi");
        client.BaseAddress = new Uri(apiBaseUrl);

        switch (action.Action)
        {
            case "add_expense":
                {
                    if (action.Amount is null or <= 0)
                    {
                        return "I could not determine the expense amount. Please be more specific (e.g. *'I spent $200 at the grocery store'*).*";
                    }

                    var categories = await client.GetFromJsonAsync<List<CategoryModel>>("/api/categories", ct) ?? [];

                    var category = categories.FirstOrDefault(c =>
                        action.CategoryHint != null &&
                        c.Name.Contains(action.CategoryHint, StringComparison.OrdinalIgnoreCase));

                    if (category is null)
                    {
                        var categoryName = string.IsNullOrWhiteSpace(action.CategoryHint)
                            ? (string.IsNullOrWhiteSpace(action.Description) ? "Other" : action.Description)
                            : action.CategoryHint;

                        var createCategoryResponse = await client.PostAsJsonAsync(
                            "/api/categories",
                            new CreateCategoryRequest(categoryName),
                            ct);

                        if (!createCategoryResponse.IsSuccessStatusCode)
                        {
                            return "No suitable category found or could not be created. Please try again.";
                        }

                        category = await createCategoryResponse.Content.ReadFromJsonAsync<CategoryModel>(cancellationToken: ct);
                        if (category is null)
                        {
                            return "Category could not be created. Please try again.";
                        }
                    }

                    var description = string.IsNullOrWhiteSpace(action.Description) ? "Expense" : action.Description;
                    var now = DateTime.UtcNow;

                    var expenseResponse = await client.PostAsJsonAsync(
                        "/api/expenses",
                        new CreateExpenseRequest(action.Amount.Value, now, description, category.Id),
                        ct);

                    if (!expenseResponse.IsSuccessStatusCode)
                    {
                        return "Expense could not be saved at this time. Please try again.";
                    }

                    var result = await expenseResponse.Content.ReadFromJsonAsync<AddExpenseResultModel>(cancellationToken: ct);
                    if (result is null)
                    {
                        return "Expense submitted but response could not be read.";
                    }

                    var created = result.Expense;
                    var budgetSummary = result.BudgetSummary;

                    var budgetNote = budgetSummary is null
                        ? "No budget defined for this month."
                        : budgetSummary.IsOverBudget
                            ? $"⚠️ You have exceeded your monthly budget by **{FormatTL(Math.Abs(budgetSummary.Remaining))}** (limit: **{FormatTL(budgetSummary.Limit)}**, total spent: **{FormatTL(budgetSummary.TotalSpent)}**)."
                            : $"Remaining budget: **{FormatTL(budgetSummary.Remaining)}** / **{FormatTL(budgetSummary.Limit)}** (spent this month: **{FormatTL(budgetSummary.TotalSpent)}**)";

                    return $"✅ **Expense added:** {created.Description} — **{FormatTL(created.Amount)}** / **{created.CategoryName}**\n\n{budgetNote}";
                }

            case "list_categories":
                {
                    var categories = await client.GetFromJsonAsync<List<CategoryModel>>("/api/categories", ct) ?? [];
                    if (categories.Count == 0)
                    {
                        return "No categories found.";
                    }

                    var list = string.Join(", ", categories.Select(c => $"**{c.Name}**"));
                    return $"Available categories: {list}";
                }

            case "check_budget":
                {
                    var now = DateTime.UtcNow;
                    var budget = await GetBudgetSafeAsync(client, now.Month, now.Year, ct);

                    return budget is null
                        ? "No budget defined for this month."
                        : $"**{now.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"))} budget:** {FormatTL(budget.Limit)}";
                }

            default:
                return "I can help you with expense tracking, category listing, or budget checking. For example: *'I spent 200 TL at the market'*";
        }
    }


    private static string FormatTL(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("tr-TR"));


    private static async Task<BudgetModel?> GetBudgetSafeAsync(HttpClient client, int month, int year, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/budgets/status?month={month}&year={year}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BudgetModel>(cancellationToken: ct);
    }

    private async Task<LlmAction> ExtractActionFromLlmAsync(SessionState session, string userMessage, CancellationToken ct)
    {
        string context;
        lock (session.SyncRoot)
        {
            context = string.Join("\n", session.Turns
                .TakeLast(6)
                .Select(t => $"{t.Role}: {t.Content}"));
        }

        var prompt = string.IsNullOrWhiteSpace(context)
            ? $"{ActionExtractionPrompt}\n\nUser message: {userMessage}"
            : $"{ActionExtractionPrompt}\n\nConversation history:\n{context}\n\nLast user message: {userMessage}";

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);

        return ParseLlmAction(response.Text ?? "{}");
    }

    private static LlmAction ParseLlmAction(string text)
    {
        try
        {
            var json = text.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    json = json[start..(end + 1)];
                }
            }

            JsonSerializerOptions jsonSerializerOptions = new()
            {
                PropertyNameCaseInsensitive = true
            };
            JsonSerializerOptions options = jsonSerializerOptions;
            return JsonSerializer.Deserialize<LlmAction>(json, options) ?? new LlmAction("unknown", null, null, null);
        }
        catch
        {
            return new LlmAction("unknown", null, null, null);
        }
    }


    private async Task<string> ProcessWithFoundryAgentAsync(string sessionId, string userMessage, CancellationToken cancellationToken)
    {
        var session = Sessions.GetOrAdd(sessionId, _ => new SessionState());
        string requestInput;

        lock (session.SyncRoot)
        {
            var context = string.Join("\n", session.Turns
                .TakeLast(6)
                .Select(turn => $"{turn.Role}: {turn.Content}"));

            if (!session.SystemPromptSent)
            {
                requestInput = string.IsNullOrWhiteSpace(context)
                    ? $"{SystemPrompt}\n\nUser: {userMessage}"
                    : $"{SystemPrompt}\n\n{context}\nUser: {userMessage}";
                session.SystemPromptSent = true;
            }
            else
            {
                requestInput = string.IsNullOrWhiteSpace(context)
                    ? $"User: {userMessage}"
                    : $"{context}\nUser: {userMessage}";
            }
        }

        var clientResponse = _responseClient.Value.CreateResponse(requestInput, cancellationToken: cancellationToken);

        var output = clientResponse.Value.GetOutputText() ?? "I'm sorry, but I couldn't generate a response.";

        lock (session.SyncRoot)
        {
            session.Turns.Add(new ConversationTurn("User", userMessage));
            session.Turns.Add(new ConversationTurn("Assistant", output));
            if (session.Turns.Count > 20)
            {
                session.Turns.RemoveRange(0, session.Turns.Count - 20);
            }
        }

        await Task.CompletedTask;
        return output;
    }
}

sealed class SessionState
{
    public bool SystemPromptSent { get; set; }
    public List<ConversationTurn> Turns { get; } = [];
    public object SyncRoot { get; } = new();
}

sealed record ConversationTurn(string Role, string Content);

sealed record LlmAction(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("amount")] decimal? Amount,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("category_hint")] string CategoryHint);
