using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Ledger;

public sealed record CategoryFigures(
    int CategoryId,
    string Name,
    string Color,
    string? Group,
    bool IsArchived,
    decimal Gross,
    decimal Refunds,
    decimal Net,
    decimal? Budget,
    decimal? BudgetRemaining,
    decimal? PercentOfBudget,
    bool IsOverBudget);

public sealed record GroupFigures(string Name, decimal Net, decimal? Budget, IReadOnlyList<string> Members);

// One line of received other income for the month (e.g. "Pending / extra"); CategoryId is null for uncategorised receipts.
public sealed record IncomeSourceFigures(int? CategoryId, string Name, string Color, decimal Amount);

public sealed record DayFigures(DateOnly Date, decimal Net, IReadOnlyDictionary<int, decimal> ByCategory);

public sealed record SalaryFigures(
    int? StatementId,
    YearMonth Earned,
    YearMonth Funded,
    bool IsPlanned,
    decimal ExpectedNet,
    decimal Received,
    decimal Outstanding,
    decimal Overpaid,
    string Status,
    IReadOnlyList<LedgerComponent> Components);

// Budget-month basis: income counts in the month it is allocated to; spending in the month it is dated.
public sealed class MonthFigures
{
    public const string Basis = "budget-month";

    public required YearMonth Month { get; init; }
    public required string Timing { get; init; } // past | current | future

    public IReadOnlyList<SalaryFigures> FundingSalaries { get; init; } = Array.Empty<SalaryFigures>();
    public decimal ExpectedSalary { get; init; }
    public decimal SalaryReceivedTowardFunding { get; init; }
    public decimal SalaryOutstanding { get; init; }
    public decimal SalaryOverpaid { get; init; }

    public decimal SalaryIncome { get; init; }
    public decimal OtherIncome { get; init; }
    public IReadOnlyList<IncomeSourceFigures> OtherIncomeSources { get; init; } = Array.Empty<IncomeSourceFigures>();
    public decimal ReceivedIncome { get; init; }
    public decimal ExpectedOtherIncome { get; init; }

    public decimal OpeningBalance { get; init; }
    public string OpeningSource { get; init; } = "none"; // none | manual | carryover
    public decimal AvailableFunds { get; init; }

    public decimal GrossSpending { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetSpending { get; init; }
    public decimal AdditionalGross { get; init; }
    public decimal AdditionalNet { get; init; }
    public decimal PlannedSpending { get; init; }

    public decimal RemainingBalance { get; init; }
    public decimal Surplus { get; init; }
    public decimal Deficit { get; init; }

    public IReadOnlyList<CategoryFigures> Categories { get; init; } = Array.Empty<CategoryFigures>();
    public IReadOnlyList<GroupFigures> Groups { get; init; } = Array.Empty<GroupFigures>();
    public decimal? TotalBudget { get; init; }
    public decimal? OverallBudgetRemaining { get; init; }
    public decimal UnbudgetedSpending { get; init; }
    public IReadOnlyList<string> UnbudgetedCategories { get; init; } = Array.Empty<string>();
    public bool BudgetCoverageWarning { get; init; }

    public decimal? IncomeUsedPercent { get; init; }
    public int DaysInMonth { get; init; }
    public int? ElapsedDays { get; init; }
    public decimal? AverageDailySpending { get; init; }
    public int DaysWithSpending { get; init; }

    public int? RemainingDaysIncludingToday { get; init; }
    public decimal UnpaidCommitments { get; init; }
    public IReadOnlyList<LedgerCommitment> Commitments { get; init; } = Array.Empty<LedgerCommitment>();
    public decimal ReservedSavings { get; init; }
    public decimal? DailyAllowance { get; init; }

    // Plan view, clearly separate from actuals: what is still expected to come in and go out.
    public decimal ForecastIncome { get; init; }
    public decimal ForecastRemaining { get; init; }

    public IReadOnlyList<DayFigures> Days { get; init; } = Array.Empty<DayFigures>();
    public IReadOnlyList<CategoryFigures> TopCategories { get; init; } = Array.Empty<CategoryFigures>();
}

// Receipt-date basis: income counts on the day it was received, whatever month it funds. Never mixed with
// budget-month totals.
public sealed class CashFlowFigures
{
    public const string Basis = "cash-flow";

    public required YearMonth Month { get; init; }
    public decimal SalaryReceived { get; init; }
    public decimal OtherReceived { get; init; }
    public decimal TotalReceived { get; init; }
    public decimal NetSpending { get; init; }
    public decimal NetCashFlow { get; init; }
}

// The single source of every financial figure: dashboards, tables, reports and exports all call this.
public static class LedgerCalculator
{
    public static string Timing(YearMonth month, DateOnly today)
    {
        var current = YearMonth.Of(today);
        return month < current ? "past" : month == current ? "current" : "future";
    }

    // Computes consecutive months, carrying each closing balance into the next opening balance when enabled.
    public static IReadOnlyList<MonthFigures> Range(LedgerData data, YearMonth from, YearMonth to)
    {
        if (to < from) (from, to) = (to, from);

        var start = from;
        if (data.Carryover && data.ChainStart is YearMonth chainStart && chainStart < from)
            start = chainStart;

        var results = new List<MonthFigures>();
        decimal? previousClosing = null;
        for (var m = start; m <= to; m = m.AddMonths(1))
        {
            var (opening, source) = OpeningFor(data, m, previousClosing);
            var figures = Month(data, m, opening, source);
            previousClosing = figures.RemainingBalance;
            if (m >= from) results.Add(figures);
        }
        return results;
    }

    public static MonthFigures Single(LedgerData data, YearMonth month) => Range(data, month, month)[0];

    private static (decimal Opening, string Source) OpeningFor(LedgerData data, YearMonth month, decimal? previousClosing)
    {
        var manual = data.Settings.TryGetValue(month, out var s) ? s.OpeningBalance : null;
        if (data.Carryover && previousClosing is decimal carried && (data.ChainStart is null || month > data.ChainStart))
            return (carried, "carryover");
        return manual is decimal value ? (value, "manual") : (0m, "none");
    }

    public static MonthFigures Month(LedgerData data, YearMonth month, decimal opening, string openingSource)
    {
        var timing = Timing(month, data.Today);
        var first = DateOnly.FromDateTime(month.FirstDay);
        var last = first.AddDays(month.DaysInMonth - 1);

        // ---- Income (budget-month basis) ----
        var received = data.Receipts.Where(r => r.Status == ReceiptStatus.Received).ToList();
        decimal AllocatedTo(IEnumerable<LedgerReceipt> receipts) =>
            receipts.Sum(r => r.Allocations.Where(a => a.Month == month).Sum(a => a.Amount));

        var salaryIncome = AllocatedTo(received.Where(r => r.Kind == ReceiptKind.Salary));
        var otherIncome = AllocatedTo(received.Where(r => r.Kind == ReceiptKind.Other));
        var expectedOther = AllocatedTo(data.Receipts.Where(r => r.Kind == ReceiptKind.Other && r.Status == ReceiptStatus.Expected));
        var receivedIncome = salaryIncome + otherIncome;

        // Other income per category, so the statement can list each source the way the sheet does.
        var categoryById = data.Categories.ToDictionary(c => c.Id);
        var otherSources = received.Where(r => r.Kind == ReceiptKind.Other)
            .Select(r => (r.CategoryId, Amount: r.Allocations.Where(a => a.Month == month).Sum(a => a.Amount)))
            .Where(x => x.Amount != 0)
            .GroupBy(x => x.CategoryId is int id && categoryById.ContainsKey(id) ? id : (int?)null)
            .Select(g => g.Key is int id
                ? (Order: categoryById[id].SortOrder, Source: new IncomeSourceFigures(id, categoryById[id].Name, categoryById[id].Color, g.Sum(x => x.Amount)))
                : (Order: int.MaxValue, Source: new IncomeSourceFigures(null, "Other income", "#898781", g.Sum(x => x.Amount))))
            .OrderBy(x => x.Order).ThenBy(x => x.Source.Name)
            .Select(x => x.Source)
            .ToList();

        var funding = data.Statements
            .Where(s => s.Funded == month)
            .OrderBy(s => s.Earned)
            .Select(s =>
            {
                var expected = SalaryMath.ExpectedNet(s.Components);
                var paid = s.Id is int id
                    ? received.Where(r => r.Kind == ReceiptKind.Salary && r.StatementId == id).Sum(r => r.Amount)
                    : 0m;
                return new SalaryFigures(s.Id, s.Earned, s.Funded, s.IsPlanned, expected, paid,
                    Math.Max(0, expected - paid), Math.Max(0, paid - expected), SalaryMath.Status(expected, paid), s.Components);
            })
            .ToList();

        // ---- Spending ----
        var inMonth = data.Transactions
            .Where(t => t.Date >= first && t.Date <= last)
            .ToList();
        var paidExpenses = inMonth.Where(t => t.Kind == TxKind.Expense && t.Status == TxStatus.Paid).ToList();
        var paidRefunds = inMonth.Where(t => t.Kind == TxKind.Refund && t.Status == TxStatus.Paid).ToList();
        var planned = inMonth.Where(t => t.Kind == TxKind.Expense && t.Status == TxStatus.Planned).ToList();

        var gross = paidExpenses.Sum(t => t.Amount);
        var refunds = paidRefunds.Sum(t => t.Amount);
        var net = gross - refunds;
        var additionalGross = paidExpenses.Where(t => t.IsAdditional).Sum(t => t.Amount);
        var additionalNet = additionalGross - paidRefunds.Where(t => t.RefundOfIsAdditional).Sum(t => t.Amount);

        var available = opening + receivedIncome;
        var remaining = available - net;

        // ---- Categories & budgets ----
        var activeIds = paidExpenses.Concat(paidRefunds).Where(t => t.CategoryId.HasValue).Select(t => t.CategoryId!.Value).ToHashSet();
        var categories = data.Categories
            .Where(c => c.Type == "Expense" && (!c.IsArchived || activeIds.Contains(c.Id)))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c =>
            {
                var cg = paidExpenses.Where(t => t.CategoryId == c.Id).Sum(t => t.Amount);
                var cr = paidRefunds.Where(t => t.CategoryId == c.Id).Sum(t => t.Amount);
                var cn = cg - cr;
                decimal? budget = data.Budgets.TryGetValue((c.Id, month), out var b) ? b : null;
                return new CategoryFigures(c.Id, c.Name, c.Color, string.IsNullOrWhiteSpace(c.Group) ? null : c.Group.Trim(),
                    c.IsArchived, cg, cr, cn, budget, budget - cn,
                    budget is > 0 ? Money.Percent(cn, budget.Value) : null, budget.HasValue && cn > budget.Value);
            })
            .ToList();

        var budgeted = categories.Where(c => c.Budget.HasValue).ToList();
        decimal? totalBudget = budgeted.Count > 0 ? budgeted.Sum(c => c.Budget!.Value) : null;
        var unbudgeted = categories.Where(c => !c.Budget.HasValue && c.Net != 0).ToList();

        var groups = categories
            .Where(c => c.Group != null)
            .GroupBy(c => c.Group!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GroupFigures(g.First().Group!, g.Sum(c => c.Net),
                g.Any(c => c.Budget.HasValue) ? g.Sum(c => c.Budget ?? 0) : null, g.Select(c => c.Name).ToList()))
            .ToList();

        // ---- Days & pace ----
        int? elapsed = timing switch { "past" => month.DaysInMonth, "current" => data.Today.Day, _ => null };
        var days = Enumerable.Range(0, month.DaysInMonth)
            .Select(i => first.AddDays(i))
            .Select(d =>
            {
                var byCategory = new Dictionary<int, decimal>();
                foreach (var t in paidExpenses.Where(t => t.Date == d))
                    if (t.CategoryId is int id) byCategory[id] = byCategory.GetValueOrDefault(id) + t.Amount;
                foreach (var t in paidRefunds.Where(t => t.Date == d))
                    if (t.CategoryId is int id) byCategory[id] = byCategory.GetValueOrDefault(id) - t.Amount;
                var dayNet = paidExpenses.Where(t => t.Date == d).Sum(t => t.Amount) - paidRefunds.Where(t => t.Date == d).Sum(t => t.Amount);
                return new DayFigures(d, dayNet, byCategory);
            })
            .ToList();

        // ---- Commitments: unpaid planned items in this month, each counted once ----
        var commitments = data.Commitments
            .Where(c => c.DueDate >= first && c.DueDate <= last)
            .GroupBy(c => (c.Source, c.Id))
            .Select(g => g.First())
            .OrderBy(c => c.DueDate)
            .ToList();
        var commitmentTotal = commitments.Sum(c => c.Amount);
        var reserved = data.Settings.TryGetValue(month, out var setting) ? setting.ReservedSavings : 0m;

        int? remainingDays = timing == "current" ? month.DaysInMonth - data.Today.Day + 1 : null;
        decimal? allowance = remainingDays is int rd
            ? Money.RoundDown(Math.Max(0, remaining - commitmentTotal - reserved) / rd)
            : null;

        var expectedSalary = funding.Sum(f => f.ExpectedNet);
        var outstanding = funding.Sum(f => f.Outstanding);
        var forecastIncome = outstanding + expectedOther;

        return new MonthFigures
        {
            Month = month,
            Timing = timing,
            FundingSalaries = funding,
            ExpectedSalary = expectedSalary,
            SalaryReceivedTowardFunding = funding.Sum(f => f.Received),
            SalaryOutstanding = outstanding,
            SalaryOverpaid = funding.Sum(f => f.Overpaid),
            SalaryIncome = salaryIncome,
            OtherIncome = otherIncome,
            OtherIncomeSources = otherSources,
            ReceivedIncome = receivedIncome,
            ExpectedOtherIncome = expectedOther,
            OpeningBalance = opening,
            OpeningSource = openingSource,
            AvailableFunds = available,
            GrossSpending = gross,
            Refunds = refunds,
            NetSpending = net,
            AdditionalGross = additionalGross,
            AdditionalNet = additionalNet,
            PlannedSpending = planned.Sum(t => t.Amount),
            RemainingBalance = remaining,
            Surplus = receivedIncome - net,
            Deficit = remaining < 0 ? -remaining : 0,
            Categories = categories,
            Groups = groups,
            TotalBudget = totalBudget,
            OverallBudgetRemaining = totalBudget - net,
            UnbudgetedSpending = unbudgeted.Sum(c => c.Net),
            UnbudgetedCategories = unbudgeted.Select(c => c.Name).ToList(),
            BudgetCoverageWarning = totalBudget.HasValue && unbudgeted.Count > 0,
            IncomeUsedPercent = receivedIncome > 0 ? Money.Percent(net, receivedIncome) : null,
            DaysInMonth = month.DaysInMonth,
            ElapsedDays = elapsed,
            AverageDailySpending = elapsed is > 0 ? Money.Round(net / elapsed.Value) : null,
            DaysWithSpending = paidExpenses.Select(t => t.Date).Distinct().Count(),
            RemainingDaysIncludingToday = remainingDays,
            UnpaidCommitments = commitmentTotal,
            Commitments = commitments,
            ReservedSavings = reserved,
            DailyAllowance = allowance,
            ForecastIncome = forecastIncome,
            ForecastRemaining = remaining + forecastIncome - commitmentTotal,
            Days = days,
            TopCategories = categories.Where(c => c.Net > 0).OrderByDescending(c => c.Net).Take(3).ToList()
        };
    }

    public static CashFlowFigures CashFlow(LedgerData data, YearMonth month)
    {
        var first = DateOnly.FromDateTime(month.FirstDay);
        var last = first.AddDays(month.DaysInMonth - 1);
        var receivedInMonth = data.Receipts
            .Where(r => r.Status == ReceiptStatus.Received && r.ReceivedDate is DateOnly d && d >= first && d <= last)
            .ToList();
        var salary = receivedInMonth.Where(r => r.Kind == ReceiptKind.Salary).Sum(r => r.Amount);
        var other = receivedInMonth.Where(r => r.Kind == ReceiptKind.Other).Sum(r => r.Amount);

        var txns = data.Transactions.Where(t => t.Date >= first && t.Date <= last && t.Status == TxStatus.Paid).ToList();
        var net = txns.Where(t => t.Kind == TxKind.Expense).Sum(t => t.Amount) - txns.Where(t => t.Kind == TxKind.Refund).Sum(t => t.Amount);

        return new CashFlowFigures
        {
            Month = month,
            SalaryReceived = salary,
            OtherReceived = other,
            TotalReceived = salary + other,
            NetSpending = net,
            NetCashFlow = salary + other - net
        };
    }
}
