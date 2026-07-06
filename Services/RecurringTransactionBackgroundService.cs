using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class RecurringTransactionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTransactionBackgroundService> _logger;

    public RecurringTransactionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RecurringTransactionBackgroundService> logger)
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
                await ProcessDueRecurringTransactionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing recurring transactions");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessDueRecurringTransactionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyticsService = scope.ServiceProvider.GetRequiredService<AnalyticsService>();

        var today = DateTime.UtcNow.Date;
        var due = await context.RecurringTransactions
            .Where(r => r.IsActive && r.NextRunDate <= today)
            .ToListAsync(stoppingToken);

        if (due.Count == 0)
            return;

        var generated = new List<(RecurringTransaction Recurring, DateTime TransactionDate)>();

        foreach (var recurring in due)
        {
            var transactionDate = recurring.NextRunDate;
            context.Transactions.Add(new Transaction
            {
                AccountId = recurring.AccountId,
                CategoryId = recurring.CategoryId,
                Amount = recurring.Amount,
                Description = recurring.Description,
                TransactionDate = transactionDate,
                CreatedAt = DateTime.UtcNow
            });
            recurring.NextRunDate = transactionDate.AddMonths(1);
            generated.Add((recurring, transactionDate));
        }

        await context.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Generated {Count} recurring transaction(s)", generated.Count);

        foreach (var (recurring, transactionDate) in generated)
        {
            await analyticsService.CheckBudgetAlertAsync(recurring.UserId, recurring.CategoryId, transactionDate.Month, transactionDate.Year);
        }
    }
}
