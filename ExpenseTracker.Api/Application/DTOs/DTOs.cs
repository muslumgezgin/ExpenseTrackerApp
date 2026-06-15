namespace ExpenseTracker.Api.Application.DTOs;

public record CategoryDto(int Id, string Name);
public record BudgetDto(int Id, int Month, int Year, decimal Limit);
public record BudgetSummaryDto(int Id, int Month, int Year, decimal Limit, decimal TotalSpent, decimal Remaining, bool IsOverBudget);
public record CategorySpendingDto(int CategoryId, string CategoryName, decimal TotalSpent);
public record BudgetBreakdownDto(BudgetSummaryDto Budget, IReadOnlyList<CategorySpendingDto> Categories);
public record ExpenseDto(int Id, decimal Amount, DateTime Date, string Description, string CategoryName);
public record AddExpenseResultDto(ExpenseDto Expense, BudgetSummaryDto? BudgetSummary);

public record CreateCategoryRequest(string Name);
public record CreateBudgetRequest(int Month, int Year, decimal Limit);
public record CreateExpenseRequest(decimal Amount, DateTime Date, string Description, int CategoryId);
public record ChatRequest(string Message, string? SessionId);
public record ChatResponse(string Response, string SessionId);
