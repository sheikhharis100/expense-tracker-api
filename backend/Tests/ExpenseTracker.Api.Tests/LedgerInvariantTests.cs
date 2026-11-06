using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Tests;

// Hundreds of random months, each re-added by hand from the raw entries and compared with the calculator, so every
// headline figure is checked against plain arithmetic rather than a few chosen examples.
public class LedgerInvariantTests
{
    private static readonly YearMonth Oct = new(2026, 10);
    private static readonly YearMonth Nov = new(2026, 11);
    private static readonly YearMonth Dec = new(2026, 12);
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly LedgerCategory[] Categories =
    {
        new(1, "Bill / Rent", "Expense", "#2a78d6", null, false, 0),
        new(2, "Food", "Expense", "#eb6834", null, false, 1),
        new(3, "Travel — InDrive", "Expense", "#1baf7a", "Travel", false, 2),
        new(4, "Travel — Bus", "Expense", "#eda100", "Travel", false, 3),
        new(5, "Other Expenses", "Expense", "#898781", null, false, 4),
        new(10, "Pending / extra", "Income", "#eda100", null, false, 0),
        new(11, "Other income", "Income", "#1baf7a", null, false, 1),
    };

    private static decimal Amount(Random r) => Math.Round((decimal)r.Next(1, 2_000_000) / 100m, 2);

    private static LedgerData RandomMonth(Random r, out List<LedgerTxn> txns, out List<LedgerReceipt> receipts, out decimal expectedSalary)
    {
        var id = 1;
        txns = new List<LedgerTxn>();
        for (var i = 0; i < r.Next(0, 40); i++)
        {
            var month = r.Next(10) == 0 ? (r.Next(2) == 0 ? Oct : Dec) : Nov; // some noise outside November
            var day = r.Next(1, month.DaysInMonth + 1);
            var date = new DateOnly(month.Year, month.Month, day);
            var paid = date <= Today && r.Next(5) != 0;
            var kind = r.Next(8) == 0 ? TxKind.Refund : TxKind.Expense;
            txns.Add(new LedgerTxn(id++, date, Amount(r), r.Next(1, 6), kind, paid ? TxStatus.Paid : TxStatus.Planned, r.Next(4) == 0));
        }

        receipts = new List<LedgerReceipt>();
        expectedSalary = r.Next(3) == 0 ? 0m : Math.Round((decimal)r.Next(20_000, 300_000), 2);
        var statements = new List<LedgerStatement>();
        if (expectedSalary > 0)
        {
            statements.Add(new LedgerStatement(1, Oct, Nov, new[] { new LedgerComponent(SalaryComponentKind.Base, "Base salary", expectedSalary) }, false));
            // Nothing, part, all, or more than expected received.
            var received = r.Next(4) switch { 0 => 0m, 1 => Math.Round(expectedSalary / 2, 2), 2 => expectedSalary, _ => expectedSalary + 1_000m };
            if (received > 0)
                receipts.Add(new LedgerReceipt(id++, ReceiptKind.Salary, ReceiptStatus.Received, new DateOnly(2026, 11, 1), received, 1, null,
                    new[] { new LedgerAllocation(Nov, received) }));
        }
        for (var i = 0; i < r.Next(0, 5); i++)
        {
            var amount = Amount(r);
            var status = r.Next(4) == 0 ? ReceiptStatus.Expected : ReceiptStatus.Received;
            var month = r.Next(6) == 0 ? Dec : Nov;
            int? category = r.Next(3) switch { 0 => 10, 1 => 11, _ => null };
            receipts.Add(new LedgerReceipt(id++, ReceiptKind.Other, status, status == ReceiptStatus.Received ? new DateOnly(2026, 11, 3) : null, amount, null, category,
                new[] { new LedgerAllocation(month, amount) }));
        }

        return new LedgerData { Today = Today, Categories = Categories, Statements = statements, Receipts = receipts, Transactions = txns };
    }

    [Fact]
    public void EveryHeadlineFigureMatchesPlainArithmetic()
    {
        var r = new Random(20261106);
        for (var run = 0; run < 400; run++)
        {
            var data = RandomMonth(r, out var txns, out var receipts, out var expectedSalary);
            var m = LedgerCalculator.Single(data, Nov);
            var context = $"run {run}";

            bool InNov(LedgerTxn t) => t.Date.Year == 2026 && t.Date.Month == 11;
            var paid = txns.Where(t => InNov(t) && t.Status == TxStatus.Paid).ToList();
            var gross = paid.Where(t => t.Kind == TxKind.Expense).Sum(t => t.Amount);
            var refunds = paid.Where(t => t.Kind == TxKind.Refund).Sum(t => t.Amount);
            var net = gross - refunds;
            decimal Allocated(IEnumerable<LedgerReceipt> rs) => rs.Where(x => x.Status == ReceiptStatus.Received).SelectMany(x => x.Allocations).Where(a => a.Month == Nov).Sum(a => a.Amount);
            var salary = Allocated(receipts.Where(x => x.Kind == ReceiptKind.Salary));
            var other = Allocated(receipts.Where(x => x.Kind == ReceiptKind.Other));
            var income = salary + other;
            var remaining = income - net;

            Assert.True(m.GrossSpending == gross, $"{context}: gross {m.GrossSpending} vs {gross}");
            Assert.True(m.Refunds == refunds, $"{context}: refunds");
            Assert.True(m.NetSpending == net, $"{context}: net {m.NetSpending} vs {net}");
            Assert.True(m.SalaryIncome == salary, $"{context}: salary income");
            Assert.True(m.OtherIncome == other, $"{context}: other income");
            Assert.True(m.ReceivedIncome == income, $"{context}: received income");
            Assert.True(m.AvailableFunds == income, $"{context}: available (no opening balance)");
            Assert.True(m.RemainingBalance == remaining, $"{context}: remaining {m.RemainingBalance} vs {remaining}");
            Assert.True(m.Surplus == income - net, $"{context}: surplus");
            Assert.True(m.Deficit == Math.Max(0, -remaining), $"{context}: deficit");
            Assert.True(m.IncomeUsedPercent == (income == 0 ? null : Math.Round(net / income * 100m, 1, MidpointRounding.AwayFromZero)), $"{context}: income used");

            // Expected salary is a plan: it is never money until received, and what's still due never goes negative.
            Assert.True(m.ExpectedSalary == expectedSalary, $"{context}: expected salary");
            var salaryReceived = receipts.Where(x => x.Kind == ReceiptKind.Salary).Sum(x => x.Amount);
            Assert.True(m.SalaryOutstanding == Math.Max(0, expectedSalary - salaryReceived), $"{context}: salary outstanding");
            Assert.True(m.SalaryOverpaid == Math.Max(0, salaryReceived - expectedSalary), $"{context}: salary overpaid");

            // Breakdowns add up to their totals.
            Assert.True(m.Categories.Sum(c => c.Net) == net, $"{context}: categories sum to net");
            Assert.True(m.OtherIncomeSources.Sum(s => s.Amount) == other, $"{context}: income sources sum to other income");
            var travel = paid.Where(t => t.CategoryId is 3 or 4).Sum(t => t.Kind == TxKind.Refund ? -t.Amount : t.Amount);
            Assert.True((m.Groups.SingleOrDefault(g => g.Name == "Travel")?.Net ?? 0) == travel, $"{context}: travel group");
            Assert.True(m.Days.Sum(d => d.Net) == net, $"{context}: days sum to net");

            // Per-day figures: 15 days have passed on 15 November.
            Assert.True(m.ElapsedDays == 15, $"{context}: elapsed days");
            Assert.True(m.AverageDailySpending == Money.Round(net / 15m), $"{context}: average per day");
            Assert.True(m.DaysWithSpending == paid.Where(t => t.Kind == TxKind.Expense).Select(t => t.Date).Distinct().Count(), $"{context}: days with spending");

            // Planned spending never counts as spent, and the allowance never goes below zero or above what's left.
            Assert.True(m.PlannedSpending == txns.Where(t => InNov(t) && t.Status == TxStatus.Planned && t.Kind == TxKind.Expense).Sum(t => t.Amount), $"{context}: planned");
            Assert.True(m.DailyAllowance is >= 0, $"{context}: allowance never negative");
            Assert.True(m.DailyAllowance * m.RemainingDaysIncludingToday <= Math.Max(0, m.RemainingBalance - m.UnpaidCommitments - m.ReservedSavings) + 0.01m * 31, $"{context}: allowance within what's free");
        }
    }

    [Fact]
    public void ChangingOneEntryMovesTheTotalsByExactlyThatAmount()
    {
        var r = new Random(7);
        for (var run = 0; run < 200; run++)
        {
            var data = RandomMonth(r, out var txns, out var receipts, out _);
            var before = LedgerCalculator.Single(data, Nov);
            var extra = Amount(r);
            var withExtra = new LedgerData
            {
                Today = data.Today, Categories = data.Categories, Statements = data.Statements, Receipts = data.Receipts,
                Transactions = txns.Append(new LedgerTxn(9_999, new DateOnly(2026, 11, 10), extra, 2, TxKind.Expense, TxStatus.Paid, false)).ToList(),
            };
            var after = LedgerCalculator.Single(withExtra, Nov);
            Assert.Equal(before.NetSpending + extra, after.NetSpending);
            Assert.Equal(before.RemainingBalance - extra, after.RemainingBalance);
            Assert.Equal(before.ReceivedIncome, after.ReceivedIncome);
            Assert.Equal(before.Categories.Single(c => c.CategoryId == 2).Net + extra, after.Categories.Single(c => c.CategoryId == 2).Net);
        }
    }
}
