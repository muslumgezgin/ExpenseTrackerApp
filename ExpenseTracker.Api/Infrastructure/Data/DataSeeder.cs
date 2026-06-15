using ExpenseTracker.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Infrastructure.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db= scope.ServiceProvider.GetRequiredService<ExpenseDbContext>();

        await db.Database.EnsureCreatedAsync();

        await SeedCurrentMonthBadgetAsync(db);

    }

    public static async Task SeedCurrentMonthBadgetAsync(ExpenseDbContext db)
    {
        var current = DateTime.UtcNow;

        var existingBudget = await db.Budgets
            .AnyAsync(b => b.Month == current.Month && b.Year == current.Year);

        if(existingBudget)
        {
            return; // Budget for the current month already exists, no need to seed
        }

        var newBudget = new Budget
        {
            Month = current.Month,
            Year = current.Year,
            Limit = 500m // Initial limit, can be updated later
        };

        await db.Budgets.AddAsync(newBudget);
        await db.SaveChangesAsync();
    }
}
