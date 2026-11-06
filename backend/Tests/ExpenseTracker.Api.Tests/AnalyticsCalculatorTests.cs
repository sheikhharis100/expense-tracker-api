using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Tests;

public class AnalyticsCalculatorTests
{
    [Fact]
    public void ComputeSpendingByCategory_GroupsAndSumsByCategory_OrderedDescending()
    {
        var records = new[]
        {
            new AnalyticsCalculator.SpendingRecord("Groceries", 50m, new DateTime(2026, 1, 5)),
            new AnalyticsCalculator.SpendingRecord("Groceries", 30m, new DateTime(2026, 1, 20)),
            new AnalyticsCalculator.SpendingRecord("Rent", 1000m, new DateTime(2026, 1, 1)),
        };

        var result = AnalyticsCalculator.ComputeSpendingByCategory(records);

        Assert.Equal(2, result.Count);
        Assert.Equal("Rent", result[0].Category);
        Assert.Equal(1000m, result[0].TotalSpent);
        Assert.Equal("Groceries", result[1].Category);
        Assert.Equal(80m, result[1].TotalSpent);
    }

    [Fact]
    public void ComputeTrend_DoesNotMergeSameMonthAcrossDifferentYears()
    {
        // Regression guard for the classic "group by month number only" bug that
        // merges January of one year with January of another.
        var records = new[]
        {
            new AnalyticsCalculator.SpendingRecord("Groceries", 100m, new DateTime(2025, 1, 15)),
            new AnalyticsCalculator.SpendingRecord("Groceries", 200m, new DateTime(2026, 1, 15)),
        };

        var result = AnalyticsCalculator.ComputeTrend(records);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Year == 2025 && r.Month == 1 && r.TotalSpent == 100m);
        Assert.Contains(result, r => r.Year == 2026 && r.Month == 1 && r.TotalSpent == 200m);
    }

    [Fact]
    public void ComputeTrend_OrdersChronologicallyAcrossYearBoundary()
    {
        var records = new[]
        {
            new AnalyticsCalculator.SpendingRecord("Groceries", 50m, new DateTime(2026, 1, 10)),
            new AnalyticsCalculator.SpendingRecord("Groceries", 50m, new DateTime(2025, 12, 10)),
        };

        var result = AnalyticsCalculator.ComputeTrend(records);

        Assert.Equal(2025, result[0].Year);
        Assert.Equal(12, result[0].Month);
        Assert.Equal(2026, result[1].Year);
        Assert.Equal(1, result[1].Month);
    }

    [Theory]
    [InlineData(1000, 800, 80, 80.0, true)]
    [InlineData(1000, 799, 80, 79.9, false)]
    [InlineData(1000, 1500, 80, 150.0, true)]
    [InlineData(0, 500, 80, 0.0, false)]
    public void ComputeBudgetStatus_ComputesPercentAndThresholdCorrectly(
        decimal limit, decimal spent, int warningThreshold, decimal expectedPercent, bool expectedOverThreshold)
    {
        var (percentUsed, isOverThreshold) = AnalyticsCalculator.ComputeBudgetStatus(limit, spent, warningThreshold);

        Assert.Equal(expectedPercent, percentUsed);
        Assert.Equal(expectedOverThreshold, isOverThreshold);
    }
}
