using ExpenseTracker.Api.Application.DTOs;

namespace ExpenseTracker.Api.Application.Interfaces;

public interface IExpenseApplicationService
{
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken);
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken);
    Task<BudgetDto?> GetBudgetStatusAsync(int month, int year, CancellationToken cancellationToken);
    Task<BudgetDto> CreateBudgetAsync(CreateBudgetRequest request, CancellationToken cancellationToken);
    Task<AddExpenseResultDto> AddExpenseAsync(CreateExpenseRequest request, CancellationToken cancellationToken);
    Task<BudgetBreakdownDto?> GetBudgetBreakdownAsync(int month, int year, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseDto>> GetExpensesAsync(int month, int year, CancellationToken cancellationToken);
}

