namespace ExpenseTracker.Agent.Models;

public record CategoryModel(int Id, string Name);
public record BudgetModel(int Id, int Month, int Year, decimal Limit);
public record BudgetSummaryModel(int Id, int Month, int Year, decimal Limit, decimal TotalSpent, decimal Remaining, bool IsOverBudget);
public record CategorySpendingModel(int CategoryId, string CategoryName, decimal TotalSpent);
public record BudgetBreakdownModel(BudgetSummaryModel Budget, List<CategorySpendingModel> Categories);
public record ExpenseModel(int Id, decimal Amount, DateTime Date, string Description, string CategoryName);
public record AddExpenseResultModel(ExpenseModel Expense, BudgetSummaryModel? BudgetSummary);

public record CreateCategoryRequest(string Name);
public record CreateBudgetRequest(int Month, int Year, decimal Limit);
public record CreateExpenseRequest(decimal Amount, DateTime Date, string Description, int CategoryId);
