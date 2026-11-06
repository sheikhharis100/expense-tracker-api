namespace ExpenseTracker.Api.Services;

public static class SalaryCalculator
{
    public record SalaryEntry(YearMonth PayMonth, decimal Amount);

    // The salary earned for a pay month is the latest history entry at or before it.
    public static decimal SalaryForPayMonth(IEnumerable<SalaryEntry> history, YearMonth payMonth)
    {
        var entry = history
            .Where(s => s.PayMonth <= payMonth)
            .OrderByDescending(s => s.PayMonth.Index)
            .FirstOrDefault();
        return entry?.Amount ?? 0;
    }

    // With a lag of 1, the money spent in November is October's pay.
    public static YearMonth PayMonthFor(YearMonth spendingMonth, int lagMonths) => spendingMonth.AddMonths(-lagMonths);

    public static YearMonth ReceivedIn(YearMonth payMonth, int lagMonths) => payMonth.AddMonths(lagMonths);
}
