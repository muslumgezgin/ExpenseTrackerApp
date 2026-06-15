namespace ExpenseTracker.Api.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } =  string.Empty;
    public ICollection<Expense> Expenses { get; set; } = [];
}
