using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Ledger;

public static class SalaryMath
{
    // Expected net = base + allowances + bonuses + overtime + arrears + signed adjustments − deductions.
    public static decimal ExpectedNet(IEnumerable<LedgerComponent> components)
    {
        var total = 0m;
        foreach (var c in components)
        {
            total += c.Kind switch
            {
                SalaryComponentKind.Deduction => -Math.Abs(c.Amount),
                SalaryComponentKind.Adjustment => c.Amount,
                _ => Math.Abs(c.Amount)
            };
        }
        return Money.Round(total);
    }

    public static decimal SumOfKind(IEnumerable<LedgerComponent> components, string kind) =>
        components.Where(c => c.Kind == kind).Sum(c => c.Amount);

    // Expected / Partial / Received / Overpaid, from expected net and receipts linked to the salary.
    public static string Status(decimal expectedNet, decimal received) => received switch
    {
        0 when expectedNet > 0 => "Expected",
        _ when received < expectedNet => "Partial",
        _ when received == expectedNet => "Received",
        _ => expectedNet <= 0 && received == 0 ? "Expected" : "Overpaid"
    };
}
