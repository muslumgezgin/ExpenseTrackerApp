using ExpenseTracker.Api.Application.DTOs;
using ExpenseTracker.Api.Application.Interfaces;
using ExpenseTracker.Api.Domain.Entities;
using ExpenseTracker.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Application.Services;

public sealed class ExpenseApplicationService(ExpenseDbContext dbContext) : IExpenseApplicationService
{
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Categories
            .FirstOrDefaultAsync(c => c.Name == request.Name, cancellationToken);

        if (existing is not null)
        {
            return new CategoryDto(existing.Id, existing.Name);
        }

        var category = new Category { Name = request.Name };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CategoryDto(category.Id, category.Name);
    }

    public async Task<BudgetDto?> GetBudgetStatusAsync(int month, int year, CancellationToken cancellationToken)
    {
        var budget = await dbContext.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Month == month && x.Year == year, cancellationToken);

        return budget is null
            ? null
            : new BudgetDto(budget.Id, budget.Month, budget.Year, budget.Limit);
    }

    public async Task<BudgetDto> CreateBudgetAsync(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var budget = new Budget
        {
            Month = request.Month,
            Year = request.Year,
            Limit = request.Limit
        };

        dbContext.Budgets.Add(budget);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BudgetDto(budget.Id, budget.Month, budget.Year, budget.Limit);
    }

    public async Task<AddExpenseResultDto> AddExpenseAsync(CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        var categoryExists = await dbContext.Categories
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            throw new ArgumentException($"Category '{request.CategoryId}' was not found.");
        }

        var expense = new Expense
        {
            Amount = request.Amount,
            Date = request.Date,
            Description = request.Description,
            CategoryId = request.CategoryId
        };

        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .FirstAsync(e => e.Id == expense.Id, cancellationToken);

        var expenseDto = new ExpenseDto(
            created.Id,
            created.Amount,
            created.Date,
            created.Description,
            created.Category.Name);

        var budgetSummary = await BuildBudgetSummaryAsync(request.Date.Month, request.Date.Year, cancellationToken);

        return new AddExpenseResultDto(expenseDto, budgetSummary);
    }

    public async Task<BudgetBreakdownDto?> GetBudgetBreakdownAsync(int month, int year, CancellationToken cancellationToken)
    {
        var summary = await BuildBudgetSummaryAsync(month, year, cancellationToken);
        if (summary is null)
            return null;

        var categorySpending = await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Where(e => e.Date.Month == month && e.Date.Year == year)
            .GroupBy(e => new { e.CategoryId, e.Category.Name })
            .Select(g => new CategorySpendingDto(g.Key.CategoryId, g.Key.Name, g.Sum(e => e.Amount)))
            .ToListAsync(cancellationToken);

        return new BudgetBreakdownDto(summary, categorySpending);
    }

    private async Task<BudgetSummaryDto?> BuildBudgetSummaryAsync(int month, int year, CancellationToken cancellationToken)
    {
        var budget = await dbContext.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Month == month && b.Year == year, cancellationToken);

        if (budget is null)
            return null;

        var totalSpent = await dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.Date.Month == month && e.Date.Year == year)
            .SumAsync(e => e.Amount, cancellationToken);

        var remaining = budget.Limit - totalSpent;
        return new BudgetSummaryDto(budget.Id, budget.Month, budget.Year, budget.Limit, totalSpent, remaining, remaining < 0);
    }

    public async Task<IReadOnlyList<ExpenseDto>> GetExpensesAsync(int month, int year, CancellationToken cancellationToken)
    {
        return await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Where(e => e.Date.Month == month && e.Date.Year == year)
            .OrderByDescending(e => e.Date)
            .Select(e => new ExpenseDto(e.Id, e.Amount, e.Date, e.Description, e.Category.Name))
            .ToListAsync(cancellationToken);
    }
}