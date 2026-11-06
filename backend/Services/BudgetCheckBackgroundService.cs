using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// Once a day: plan the coming months' recurring occurrences and raise budget alerts for the current month.
// Nothing here ever creates a paid transaction.
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
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Daily planning check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var users = await db.Users.Select(u => new { u.Id, u.TimeZone }).ToListAsync(stoppingToken);

        foreach (var user in users)
        {
            stoppingToken.ThrowIfCancellationRequested();
            using var userScope = _scopeFactory.CreateScope();
            var recurring = userScope.ServiceProvider.GetRequiredService<RecurringService>();
            var analytics = userScope.ServiceProvider.GetRequiredService<AnalyticsService>();
            var current = YearMonth.Of(LocalCalendar.Today(clock, user.TimeZone));

            await recurring.EnsureOccurrencesAsync(user.Id, current, current.AddMonths(1));

            var userDb = userScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var categoryIds = await userDb.Categories.Where(c => c.UserId == user.Id && c.Type == "Expense").Select(c => c.Id).ToListAsync(stoppingToken);
            foreach (var categoryId in categoryIds)
                await analytics.CheckBudgetAlertAsync(user.Id, categoryId, current.Month, current.Year);
        }
        _logger.LogInformation("Planned recurring items and checked budgets for {Count} user(s)", users.Count);
    }
}
