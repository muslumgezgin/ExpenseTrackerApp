using System.Net.Http.Json;
using ExpenseTracker.Agent.Models;

namespace ExpenseTracker.Web.Services;

public sealed class ExpenseApiService(IHttpClientFactory httpClientFactory, ILogger<ExpenseApiService> logger)
{
    private HttpClient CreateClient() => httpClientFactory.CreateClient("Api");

    public async Task<BudgetBreakdownModel?> GetBudgetBreakdownAsync(int month, int year, CancellationToken ct = default)
    {
        try
        {
            var response = await CreateClient().GetAsync($"/api/budgets/breakdown?month={month}&year={year}", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<BudgetBreakdownModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch budget breakdown for {Month}/{Year}.", month, year);
            return null;
        }
    }

    public async Task<List<ExpenseModel>> GetExpensesAsync(int month, int year, CancellationToken ct = default)
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<List<ExpenseModel>>(
                $"/api/expenses?month={month}&year={year}", ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch expenses for {Month}/{Year}.", month, year);
            return [];
        }
    }
}
