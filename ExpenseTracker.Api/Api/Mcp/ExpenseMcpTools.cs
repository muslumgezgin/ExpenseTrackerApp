using ExpenseTracker.Api.Application.DTOs;
using ExpenseTracker.Api.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ExpenseTracker.Api.Api.Mcp;


[McpServerToolType]
public sealed class ExpenseMcpTools
{
    [McpServerTool,
     Description("Create a new expense category. Use this when no matching category exists for the expense. Input: name of the category.")]
    public static async Task<CategoryDto> CreateCategoryAsync(
        string name,
        IExpenseApplicationService expenseService,
        CancellationToken cancellationToken)
    {
        return await expenseService.CreateCategoryAsync(new CreateCategoryRequest(name), cancellationToken);
    }

    [McpServerTool, Description("List all expense categories.")]
    public static async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(
        IExpenseApplicationService expenseService,
        CancellationToken cancellationToken)
    {
        return await expenseService.ListCategoriesAsync(cancellationToken);
    }

    [McpServerTool, Description("Get budget status by month and year.")]
    public static async Task<BudgetDto?> GetBudgetStatusAsync(
        int month,
        int year,
        IExpenseApplicationService expenseService,
        CancellationToken cancellationToken)
    {
        return await expenseService.GetBudgetStatusAsync(month, year, cancellationToken);
    }

    [McpServerTool,
     Description("Add a new expense. Inputs: amount, date (ISO), description, categoryId. Returns the created expense together with the current month's budget summary (limit, total spent, remaining, over-budget flag).")]
    public static async Task<AddExpenseResultDto> AddExpenseAsync(
        decimal amount,
        DateTime date,
        string description,
        int categoryId,
        IExpenseApplicationService expenseService,
        CancellationToken cancellationToken)
    {
        var request = new CreateExpenseRequest(amount, date, description, categoryId);
        return await expenseService.AddExpenseAsync(request, cancellationToken);
    }

    [McpServerTool,
     Description("Get the budget breakdown for a given month and year: overall limit, total spent, remaining budget, and a per-category spending list.")]
    public static async Task<BudgetBreakdownDto?> GetBudgetBreakdownAsync(
        int month,
        int year,
        IExpenseApplicationService expenseService,
        CancellationToken cancellationToken)
    {
        return await expenseService.GetBudgetBreakdownAsync(month, year, cancellationToken);
    }
}

