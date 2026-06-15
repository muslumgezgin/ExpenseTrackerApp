using ExpenseTracker.Agent.Interfaces;
using ExpenseTracker.Api.Application.DTOs;
using ExpenseTracker.Api.Application.Interfaces;

namespace ExpenseTracker.Api.Api.Endpoints;

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // POST /api/chat
        group.MapPost("/chat", async (ChatRequest request, IAiOrchestrator aiOrchestrator, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest("Message is required.");
            }

            var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
                ? Guid.NewGuid().ToString("N")
                : request.SessionId;

            var response = await aiOrchestrator.ProcessUserRequestAsync(request.Message, sessionId, ct);

            return Results.Ok(new ChatResponse(response, sessionId));
        });

        // POST /api/categories
        group.MapPost("/categories", async (CreateCategoryRequest request, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Category name is required.");
            }

            var category = await expenseService.CreateCategoryAsync(request, ct);
            return Results.Created($"/api/categories/{category.Id}", category);
        });

        // GET /api/categories
        group.MapGet("/categories", async (IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var categories = await expenseService.ListCategoriesAsync(ct);

            return Results.Ok(categories);
        });

        // GET /api/budgets/status?month={m}&year={y}
        group.MapGet("/budgets/status", async (int month, int year, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var budget = await expenseService.GetBudgetStatusAsync(month, year, ct);

            if (budget == null)
                return Results.NoContent();

            return Results.Ok(budget);
        });

        // POST /api/budgets
        group.MapPost("/budgets", async (CreateBudgetRequest request, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var budget = await expenseService.CreateBudgetAsync(request, ct);

            return Results.Created($"/api/budgets/status?month={budget.Month}&year={budget.Year}",
                budget);
        });

        // POST /api/expenses
        group.MapPost("/expenses", async (CreateExpenseRequest request, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var result = await expenseService.AddExpenseAsync(request, ct);

            return Results.Created($"/api/expenses/{result.Expense.Id}", result);
        });

        // GET /api/budgets/breakdown?month={m}&year={y}
        group.MapGet("/budgets/breakdown", async (int month, int year, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var breakdown = await expenseService.GetBudgetBreakdownAsync(month, year, ct);

            if (breakdown is null)
                return Results.NoContent();

            return Results.Ok(breakdown);
        });

        // GET /api/expenses?month={m}&year={y}
        group.MapGet("/expenses", async (int month, int year, IExpenseApplicationService expenseService, CancellationToken ct) =>
        {
            var expenses = await expenseService.GetExpensesAsync(month, year, ct);
            return Results.Ok(expenses);
        });
    }
}
