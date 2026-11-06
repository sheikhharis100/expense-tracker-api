using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// The original analytics endpoints, now answered by the same calculator as everything else.
public class AnalyticsService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly LedgerLoader _loader;

    public AnalyticsService(AppDbContext context, IConfiguration config, LedgerLoader loader)
    {
        _context = context;
        _config = config;
        _loader = loader;
    }

    private int Threshold => _config.GetValue<int>("BudgetAlerts:WarningThresholdPercent", 80);

    private async Task<(MonthFigures Figures, string Currency)> MonthAsync(int userId, YearMonth month)
    {
        var user = await _loader.UserAsync(userId);
        var data = await _loader.LoadAsync(userId, month, month);
        return (LedgerCalculator.Single(data, month), user.Currency);
    }

    public async Task<List<SpendingByCategoryDto>> GetSpendingByCategoryAsync(int userId, int month, int year)
    {
        var (figures, currency) = await MonthAsync(userId, new YearMonth(year, month));
        return figures.Categories.Where(c => c.Net != 0).OrderByDescending(c => c.Net)
            .Select(c => new SpendingByCategoryDto { Category = c.Name, TotalSpent = c.Net, Currency = currency })
            .ToList();
    }

    public async Task<List<TrendDto>> GetSpendingTrendAsync(int userId, int months)
    {
        var user = await _loader.UserAsync(userId);
        var current = YearMonth.Of(await _loader.TodayAsync(userId));
        var from = current.AddMonths(-Math.Clamp(months, 1, 120) + 1);
        var data = await _loader.LoadAsync(userId, from, current);
        return LedgerCalculator.Range(data, from, current)
            .Select(m => new TrendDto { Year = m.Month.Year, Month = m.Month.Month, TotalSpent = m.NetSpending, Currency = user.Currency })
            .ToList();
    }

    public async Task<List<BudgetStatusDto>> GetBudgetStatusAsync(int userId, int month, int year)
    {
        var (figures, currency) = await MonthAsync(userId, new YearMonth(year, month));
        return figures.Categories.Where(c => c.Budget.HasValue).Select(c =>
        {
            var (percent, over) = AnalyticsCalculator.ComputeBudgetStatus(c.Budget!.Value, c.Net, Threshold);
            return new BudgetStatusDto
            {
                BudgetId = c.CategoryId,
                Category = c.Name,
                Limit = c.Budget.Value,
                Spent = c.Net,
                PercentUsed = percent,
                IsOverThreshold = over,
                Currency = currency
            };
        }).ToList();
    }

    public async Task CheckBudgetAlertAsync(int userId, int categoryId, int month, int year)
    {
        var (figures, _) = await MonthAsync(userId, new YearMonth(year, month));
        var category = figures.Categories.FirstOrDefault(c => c.CategoryId == categoryId);
        if (category?.Budget is not decimal limit) return;

        var (percent, over) = AnalyticsCalculator.ComputeBudgetStatus(limit, category.Net, Threshold);
        if (!over) return;

        var exists = await _context.BudgetAlerts.AnyAsync(a => a.CategoryId == categoryId && a.Year == year && a.Month == month && !a.IsRead);
        if (exists) return;
        _context.BudgetAlerts.Add(new BudgetAlert
        {
            CategoryId = categoryId,
            Year = year,
            Month = month,
            Message = $"Budget for '{category.Name}' has reached {percent:F1}% of its limit",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task<List<BudgetAlertResponseDto>> GetBudgetAlertsAsync(int userId)
    {
        var alerts = await _context.BudgetAlerts.AsNoTracking()
            .Where(a => (a.Category != null && a.Category.UserId == userId) || (a.Budget != null && a.Budget.Category.UserId == userId))
            .Include(a => a.Category)
            .Include(a => a.Budget).ThenInclude(b => b!.Category)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return alerts.Select(a => new BudgetAlertResponseDto
        {
            Id = a.Id,
            BudgetId = a.BudgetId ?? 0,
            Category = a.Category?.Name ?? a.Budget?.Category.Name ?? string.Empty,
            Message = a.Message,
            CreatedAt = a.CreatedAt,
            IsRead = a.IsRead
        }).ToList();
    }

    public async Task MarkAlertAsReadAsync(int userId, int alertId)
    {
        var alert = await _context.BudgetAlerts
            .FirstOrDefaultAsync(a => a.Id == alertId
                && ((a.Category != null && a.Category.UserId == userId) || (a.Budget != null && a.Budget.Category.UserId == userId)));
        if (alert == null) return;
        alert.IsRead = true;
        await _context.SaveChangesAsync();
    }
}
