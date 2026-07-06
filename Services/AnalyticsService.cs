using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class AnalyticsService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly CurrencyConverter _currencyConverter;

    public AnalyticsService(AppDbContext context, IConfiguration config, CurrencyConverter currencyConverter)
    {
        _context = context;
        _config = config;
        _currencyConverter = currencyConverter;
    }

    public async Task<List<SpendingByCategoryDto>> GetSpendingByCategoryAsync(int userId, int month, int year)
    {
        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.TransactionDate.Month == month
                && t.TransactionDate.Year == year
                && t.Category.Type == "Expense")
            .Include(t => t.Category)
            .Include(t => t.Account)
            .ToListAsync();

        var records = transactions.Select(t => new AnalyticsCalculator.SpendingRecord(
            t.Category.Name,
            _currencyConverter.ToReportingCurrency(t.Amount, t.Account.Currency),
            t.TransactionDate));

        var results = AnalyticsCalculator.ComputeSpendingByCategory(records);
        foreach (var r in results)
            r.Currency = _currencyConverter.ReportingCurrency;

        return results;
    }

    public async Task<List<TrendDto>> GetSpendingTrendAsync(int userId, int months)
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-months);
        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.TransactionDate >= cutoffDate
                && t.Category.Type == "Expense")
            .Include(t => t.Account)
            .ToListAsync();

        var records = transactions.Select(t => new AnalyticsCalculator.SpendingRecord(
            string.Empty,
            _currencyConverter.ToReportingCurrency(t.Amount, t.Account.Currency),
            t.TransactionDate));

        var results = AnalyticsCalculator.ComputeTrend(records);
        foreach (var r in results)
            r.Currency = _currencyConverter.ReportingCurrency;

        return results;
    }

    public async Task<List<BudgetStatusDto>> GetBudgetStatusAsync(int userId, int month, int year)
    {
        var warningThreshold = _config.GetValue<int>("BudgetAlerts:WarningThresholdPercent", 80);

        var budgets = await _context.Budgets
            .Where(b => b.Category.UserId == userId && b.Month == month && b.Year == year)
            .Include(b => b.Category)
            .ToListAsync();

        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.TransactionDate.Month == month
                && t.TransactionDate.Year == year
                && t.Category.Type == "Expense")
            .Include(t => t.Account)
            .ToListAsync();

        var results = new List<BudgetStatusDto>();

        foreach (var budget in budgets)
        {
            var spent = transactions
                .Where(t => t.CategoryId == budget.CategoryId)
                .Sum(t => _currencyConverter.ToReportingCurrency(t.Amount, t.Account.Currency));

            var (percentUsed, isOverThreshold) = AnalyticsCalculator.ComputeBudgetStatus(budget.MonthlyLimit, spent, warningThreshold);

            results.Add(new BudgetStatusDto
            {
                BudgetId = budget.Id,
                Category = budget.Category.Name,
                Limit = budget.MonthlyLimit,
                Spent = spent,
                PercentUsed = percentUsed,
                IsOverThreshold = isOverThreshold,
                Currency = _currencyConverter.ReportingCurrency
            });
        }

        return results;
    }

    public async Task CheckBudgetAlertAsync(int userId, int categoryId, int month, int year)
    {
        var warningThreshold = _config.GetValue<int>("BudgetAlerts:WarningThresholdPercent", 80);

        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.Month == month && b.Year == year
                && b.Category.UserId == userId);

        if (budget == null)
            return;

        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.CategoryId == categoryId
                && t.TransactionDate.Month == month
                && t.TransactionDate.Year == year
                && t.Category.Type == "Expense")
            .Include(t => t.Account)
            .ToListAsync();

        var spent = transactions.Sum(t => _currencyConverter.ToReportingCurrency(t.Amount, t.Account.Currency));

        var (percentUsed, isOverThreshold) = AnalyticsCalculator.ComputeBudgetStatus(budget.MonthlyLimit, spent, warningThreshold);

        if (isOverThreshold)
        {
            await CreateBudgetAlertIfNeeded(budget.Id, budget.Category.Name, percentUsed);
        }
    }

    private async Task CreateBudgetAlertIfNeeded(int budgetId, string categoryName, decimal percentUsed)
    {
        var existingAlert = await _context.BudgetAlerts
            .FirstOrDefaultAsync(a => a.BudgetId == budgetId && !a.IsRead);

        if (existingAlert == null)
        {
            var alert = new BudgetAlert
            {
                BudgetId = budgetId,
                Message = $"Budget for '{categoryName}' has exceeded {percentUsed:F1}% of its limit",
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };
            _context.BudgetAlerts.Add(alert);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<BudgetAlertResponseDto>> GetBudgetAlertsAsync(int userId)
    {
        var alerts = await _context.BudgetAlerts
            .Where(a => a.Budget.Category.UserId == userId)
            .Include(a => a.Budget)
                .ThenInclude(b => b.Category)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return alerts.Select(a => new BudgetAlertResponseDto
        {
            Id = a.Id,
            BudgetId = a.BudgetId,
            Category = a.Budget.Category.Name,
            Message = a.Message,
            CreatedAt = a.CreatedAt,
            IsRead = a.IsRead
        }).ToList();
    }

    public async Task MarkAlertAsReadAsync(int userId, int alertId)
    {
        var alert = await _context.BudgetAlerts
            .Include(a => a.Budget)
                .ThenInclude(b => b.Category)
            .FirstOrDefaultAsync(a => a.Id == alertId && a.Budget.Category.UserId == userId);

        if (alert != null)
        {
            alert.IsRead = true;
            await _context.SaveChangesAsync();
        }
    }
}
