using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public static class AnalyticsCalculator
{
    public record SpendingRecord(string CategoryName, decimal Amount, DateTime Date);

    public static List<SpendingByCategoryDto> ComputeSpendingByCategory(IEnumerable<SpendingRecord> records)
    {
        return records
            .GroupBy(r => r.CategoryName)
            .Select(g => new SpendingByCategoryDto
            {
                Category = g.Key,
                TotalSpent = g.Sum(r => r.Amount)
            })
            .OrderByDescending(x => x.TotalSpent)
            .ToList();
    }

    public static List<TrendDto> ComputeTrend(IEnumerable<SpendingRecord> records)
    {
        return records
            .GroupBy(r => new { r.Date.Year, r.Date.Month })
            .Select(g => new TrendDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                TotalSpent = g.Sum(r => r.Amount)
            })
            .OrderBy(t => t.Year)
            .ThenBy(t => t.Month)
            .ToList();
    }

    public static (decimal PercentUsed, bool IsOverThreshold) ComputeBudgetStatus(decimal limit, decimal spent, int warningThresholdPercent)
    {
        var percentUsed = limit > 0 ? Math.Round((spent / limit) * 100, 2) : 0;
        var isOverThreshold = percentUsed >= warningThresholdPercent;
        return (percentUsed, isOverThreshold);
    }
}
