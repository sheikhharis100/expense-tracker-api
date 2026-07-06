using ExpenseTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class BudgetCheckBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BudgetCheckBackgroundService> _logger;

    public BudgetCheckBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BudgetCheckBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await CheckAllActiveBudgetsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking budgets for alerts");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAllActiveBudgetsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyticsService = scope.ServiceProvider.GetRequiredService<AnalyticsService>();

        var today = DateTime.UtcNow.Date;
        var budgets = await context.Budgets
            .Include(b => b.Category)
            .Where(b => b.Month == today.Month && b.Year == today.Year)
            .ToListAsync(stoppingToken);

        foreach (var budget in budgets)
        {
            await analyticsService.CheckBudgetAlertAsync(budget.Category.UserId, budget.CategoryId, budget.Month, budget.Year);
        }

        _logger.LogInformation("Checked {Count} active budget(s) for alerts", budgets.Count);
    }
}
