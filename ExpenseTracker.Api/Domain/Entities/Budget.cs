namespace ExpenseTracker.Api.Domain.Entities;

public class Budget
{
    public int Id { get; set; }

    public int Month { get; set; }

    public int Year { get; set; }

    public decimal Limit { get; set; }
}
