using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Tests;

public class LedgerCalculatorTests
{
    private static readonly YearMonth Oct = new(2026, 10);
    private static readonly YearMonth Nov = new(2026, 11);
    private static readonly YearMonth Dec = new(2026, 12);
    private static readonly DateOnly MidNov = new(2026, 11, 15);

    private const int Rent = 1, Food = 2, Travel = 3, Other = 4, Unbudgeted = 5;

    private static readonly LedgerCategory[] Categories =
    {
        new(Rent, "Bill / Rent", "Expense", "#2a78d6", null, false, 0),
        new(Food, "Food", "Expense", "#eb6834", null, false, 1),
        new(Travel, "Travel", "Expense", "#1baf7a", null, false, 2),
        new(Other, "Other Expenses", "Expense", "#e87ba4", null, false, 3),
        new(Unbudgeted, "Shopping", "Expense", "#4a3aa7", null, false, 4),
    };

    private static int _id = 100;

    private static LedgerTxn Paid(int category, decimal amount, DateOnly date, bool additional = false) =>
        new(_id++, date, amount, category, TxKind.Expense, TxStatus.Paid, additional);

    private static LedgerReceipt Salary(decimal amount, DateOnly received, int statementId, params (YearMonth Month, decimal Amount)[] allocations) =>
        new(_id++, ReceiptKind.Salary, ReceiptStatus.Received, received, amount, statementId, null,
            (allocations.Length == 0 ? new[] { (YearMonth.Of(received), amount) } : allocations).Select(a => new LedgerAllocation(a.Item1, a.Item2)).ToList());

    private static LedgerReceipt OtherIncome(decimal amount, DateOnly received, string status = ReceiptStatus.Received) =>
        new(_id++, ReceiptKind.Other, status, status == ReceiptStatus.Received ? received : null, amount, null, 9,
            new[] { new LedgerAllocation(YearMonth.Of(received), amount) });

    private static LedgerStatement Statement(int id, YearMonth earned, YearMonth funded, params (string Kind, decimal Amount)[] components) =>
        new(id, earned, funded, components.Select(c => new LedgerComponent(c.Kind, c.Kind, c.Amount)).ToList(), false);

    // The acceptance baseline: salary 55,000 and other income 5,000 received for November; rent 15,000,
    // food 8,000, travel 4,000 and an additional 3,000.
    private static LedgerData Baseline(params LedgerTxn[] extra) => new()
    {
        Today = MidNov,
        Categories = Categories,
        Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m)) },
        Receipts = new[] { Salary(55_000m, new DateOnly(2026, 11, 1), 1), OtherIncome(5_000m, new DateOnly(2026, 11, 3)) },
        Transactions = new[]
        {
            Paid(Rent, 15_000m, new DateOnly(2026, 11, 1)),
            Paid(Food, 8_000m, new DateOnly(2026, 11, 4)),
            Paid(Travel, 4_000m, new DateOnly(2026, 11, 5)),
            Paid(Other, 3_000m, new DateOnly(2026, 11, 6), additional: true),
        }.Concat(extra).ToList(),
    };

    [Fact]
    public void Baseline_TotalsMatchTheAcceptanceScenario()
    {
        var m = LedgerCalculator.Single(Baseline(), Nov);

        Assert.Equal(60_000m, m.ReceivedIncome);
        Assert.Equal(55_000m, m.SalaryIncome);
        Assert.Equal(5_000m, m.OtherIncome);
        Assert.Equal(30_000m, m.GrossSpending);
        Assert.Equal(30_000m, m.NetSpending);
        Assert.Equal(0m, m.OpeningBalance);
        Assert.Equal(60_000m, m.AvailableFunds);
        Assert.Equal(30_000m, m.RemainingBalance);
        Assert.Equal(30_000m, m.Surplus);
        Assert.Equal(50.0m, m.IncomeUsedPercent);
    }

    [Fact]
    public void OtherIncome_IsListedPerCategoryInCategoryOrder()
    {
        LedgerReceipt Received(int? category, decimal amount, YearMonth month) =>
            new(_id++, ReceiptKind.Other, ReceiptStatus.Received, new DateOnly(month.Year, month.Month, 2), amount, null, category,
                new[] { new LedgerAllocation(month, amount) });
        var data = new LedgerData
        {
            Today = MidNov,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m)) },
            Categories = Categories.Concat(new LedgerCategory[]
            {
                new(20, "Pending / extra", "Income", "#eda100", null, false, 0),
                new(21, "Freelance", "Income", "#1baf7a", null, false, 1),
            }).ToList(),
            Receipts = new[]
            {
                Salary(55_000m, new DateOnly(2026, 11, 1), 1),
                Received(21, 1_000m, Nov),
                Received(20, 5_000m, Nov),
                Received(20, 500m, Nov),
                Received(null, 250m, Nov),
                Received(21, 9_999m, Dec),
            },
        };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(6_750m, m.OtherIncome);
        Assert.Equal(
            new[] { ("Pending / extra", 5_500m), ("Freelance", 1_000m), ("Other income", 250m) },
            m.OtherIncomeSources.Select(s => (s.Name, s.Amount)).ToArray());
        Assert.Equal(m.OtherIncome, m.OtherIncomeSources.Sum(s => s.Amount));
    }

    [Fact]
    public void AdditionalExpenses_AreAlreadyInsideNetSpending_NotAddedAgain()
    {
        var m = LedgerCalculator.Single(Baseline(), Nov);

        Assert.Equal(3_000m, m.AdditionalNet);
        Assert.Equal(30_000m, m.NetSpending);
        Assert.Equal(m.NetSpending, m.Categories.Sum(c => c.Net));
    }

    [Fact]
    public void Refund_ReducesNetSpendingInTheMonthItIsReceived()
    {
        var food = Baseline().Transactions.Single(t => t.CategoryId == Food);
        var refund = new LedgerTxn(_id++, new DateOnly(2026, 11, 20), 1_000m, Food, TxKind.Refund, TxStatus.Paid, false, food.Id);
        var data = Baseline(refund);

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(30_000m, m.GrossSpending);
        Assert.Equal(1_000m, m.Refunds);
        Assert.Equal(29_000m, m.NetSpending);
        Assert.Equal(7_000m, m.Categories.Single(c => c.CategoryId == Food).Net);
    }

    [Fact]
    public void RefundReceivedNextMonth_LeavesTheOriginalMonthUnchanged()
    {
        var data = Baseline(new LedgerTxn(_id++, new DateOnly(2026, 12, 2), 1_000m, Food, TxKind.Refund, TxStatus.Paid, false));

        var range = LedgerCalculator.Range(data, Nov, Dec);

        Assert.Equal(30_000m, range[0].NetSpending);
        Assert.Equal(-1_000m, range[1].NetSpending);
    }

    [Fact]
    public void PendingSalary_DoesNotIncreaseAvailableFunds()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Categories = Categories,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m), (SalaryComponentKind.Arrears, 4_000m)) },
        };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(59_000m, m.ExpectedSalary);
        Assert.Equal(59_000m, m.SalaryOutstanding);
        Assert.Equal(0m, m.ReceivedIncome);
        Assert.Equal(0m, m.AvailableFunds);
        Assert.Null(m.IncomeUsedPercent);
        Assert.Equal(59_000m, m.ForecastIncome);
    }

    [Fact]
    public void PartialSalaryPayments_CountOnce()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Categories = Categories,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m)) },
            Receipts = new[] { Salary(30_000m, new DateOnly(2026, 11, 1), 1), Salary(20_000m, new DateOnly(2026, 11, 8), 1) },
        };

        var m = LedgerCalculator.Single(data, Nov);
        var salary = Assert.Single(m.FundingSalaries);

        Assert.Equal(50_000m, salary.Received);
        Assert.Equal(5_000m, salary.Outstanding);
        Assert.Equal("Partial", salary.Status);
        Assert.Equal(50_000m, m.ReceivedIncome);
    }

    [Fact]
    public void Overpayment_IsShownExplicitly()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m)) },
            Receipts = new[] { Salary(56_000m, new DateOnly(2026, 11, 1), 1) },
        };

        var salary = Assert.Single(LedgerCalculator.Single(data, Nov).FundingSalaries);

        Assert.Equal(0m, salary.Outstanding);
        Assert.Equal(1_000m, salary.Overpaid);
        Assert.Equal("Overpaid", salary.Status);
    }

    [Fact]
    public void PaymentSplitAcrossMonths_CountsInEachAllocatedMonthOnly()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 60_000m)) },
            Receipts = new[] { Salary(60_000m, new DateOnly(2026, 11, 1), 1, (Nov, 40_000m), (Dec, 20_000m)) },
        };

        var range = LedgerCalculator.Range(data, Nov, Dec);

        Assert.Equal(40_000m, range[0].ReceivedIncome);
        Assert.Equal(20_000m, range[1].ReceivedIncome);
        Assert.Equal(60_000m, range.Sum(m => m.ReceivedIncome));
    }

    [Fact]
    public void ExpectedNet_IncludesAllowancesBonusesAndSignedAdjustments_AndDeductionsNeverBecomeExpenses()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Categories = Categories,
            Statements = new[]
            {
                Statement(1, Oct, Nov,
                    (SalaryComponentKind.Base, 50_000m), (SalaryComponentKind.Allowance, 5_000m), (SalaryComponentKind.Bonus, 2_000m),
                    (SalaryComponentKind.Overtime, 1_500m), (SalaryComponentKind.Arrears, 1_000m),
                    (SalaryComponentKind.Adjustment, -500m), (SalaryComponentKind.Deduction, 3_000m))
            },
        };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(56_000m, m.ExpectedSalary);
        Assert.Equal(0m, m.NetSpending);
    }

    [Fact]
    public void Carryover_ForwardsDeficits_AndOpeningBalanceIsNotIncome()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Carryover = true,
            ChainStart = Oct,
            Categories = Categories,
            Receipts = new[] { OtherIncome(10_000m, new DateOnly(2026, 10, 2)), OtherIncome(20_000m, new DateOnly(2026, 11, 2)) },
            Transactions = new[] { Paid(Food, 15_000m, new DateOnly(2026, 10, 10)), Paid(Food, 5_000m, new DateOnly(2026, 11, 10)) },
        };

        var nov = LedgerCalculator.Single(data, Nov);

        Assert.Equal(-5_000m, nov.OpeningBalance);
        Assert.Equal("carryover", nov.OpeningSource);
        Assert.Equal(20_000m, nov.ReceivedIncome);
        Assert.Equal(15_000m, nov.AvailableFunds);
        Assert.Equal(10_000m, nov.RemainingBalance);
        Assert.Equal(15_000m, nov.Surplus);
    }

    [Fact]
    public void Carryover_BackdatedEditRecalculatesLaterMonths()
    {
        LedgerData Data(params LedgerTxn[] oct) => new()
        {
            Today = MidNov,
            Carryover = true,
            ChainStart = Oct,
            Categories = Categories,
            Receipts = new[] { OtherIncome(10_000m, new DateOnly(2026, 10, 2)) },
            Transactions = oct,
        };

        Assert.Equal(10_000m, LedgerCalculator.Single(Data(), Nov).OpeningBalance);
        Assert.Equal(7_500m, LedgerCalculator.Single(Data(Paid(Food, 2_500m, new DateOnly(2026, 10, 30))), Nov).OpeningBalance);
    }

    [Fact]
    public void WithoutCarryover_OpeningIsTheManualValueOrZero()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            ChainStart = Oct,
            Receipts = new[] { OtherIncome(10_000m, new DateOnly(2026, 10, 2)) },
            Settings = new Dictionary<YearMonth, LedgerMonthSetting> { [Dec] = new(Dec, 2_000m, 0) },
        };

        var range = LedgerCalculator.Range(data, Nov, Dec);

        Assert.Equal(0m, range[0].OpeningBalance);
        Assert.Equal(2_000m, range[1].OpeningBalance);
        Assert.Equal("manual", range[1].OpeningSource);
    }

    [Fact]
    public void NoBudgets_AreNotSet_NotZero()
    {
        var m = LedgerCalculator.Single(Baseline(), Nov);

        Assert.Null(m.TotalBudget);
        Assert.Null(m.OverallBudgetRemaining);
        Assert.False(m.BudgetCoverageWarning);
        Assert.All(m.Categories, c => Assert.Null(c.Budget));
    }

    [Fact]
    public void PartialBudgets_FlagUnbudgetedSpending()
    {
        var data = Baseline(Paid(Unbudgeted, 2_000m, new DateOnly(2026, 11, 9)));
        data = new LedgerData
        {
            Today = data.Today, Categories = data.Categories, Statements = data.Statements, Receipts = data.Receipts, Transactions = data.Transactions,
            Budgets = new Dictionary<(int, YearMonth), decimal> { [(Rent, Nov)] = 15_000m, [(Food, Nov)] = 6_000m, [(Travel, Nov)] = 5_000m, [(Other, Nov)] = 3_000m }
        };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(29_000m, m.TotalBudget);
        Assert.Equal(29_000m - 32_000m, m.OverallBudgetRemaining);
        Assert.Equal(2_000m, m.UnbudgetedSpending);
        Assert.Equal(new[] { "Shopping" }, m.UnbudgetedCategories);
        Assert.True(m.BudgetCoverageWarning);
        var food = m.Categories.Single(c => c.CategoryId == Food);
        Assert.Equal(-2_000m, food.BudgetRemaining);
        Assert.True(food.IsOverBudget);
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void PastMonths_AverageOverTheirRealCalendarLength(int year, int month, int days)
    {
        var ym = new YearMonth(year, month);
        var data = new LedgerData
        {
            Today = new DateOnly(2029, 1, 1),
            Categories = Categories,
            Transactions = new[] { Paid(Food, days * 100m, new DateOnly(year, month, 1)) },
        };

        var m = LedgerCalculator.Single(data, ym);

        Assert.Equal(days, m.DaysInMonth);
        Assert.Equal(days, m.ElapsedDays);
        Assert.Equal(100m, m.AverageDailySpending);
        Assert.Equal("past", m.Timing);
    }

    [Fact]
    public void CurrentMonth_AveragesOverDaysSoFar_AndFutureMonthsHaveNoAverage()
    {
        var data = Baseline();

        var nov = LedgerCalculator.Single(data, Nov);
        var dec = LedgerCalculator.Single(data, Dec);

        Assert.Equal(15, nov.ElapsedDays);
        Assert.Equal(2_000m, nov.AverageDailySpending);
        Assert.Equal(4, nov.DaysWithSpending);
        Assert.Equal("future", dec.Timing);
        Assert.Null(dec.ElapsedDays);
        Assert.Null(dec.AverageDailySpending);
        Assert.Null(dec.DailyAllowance);
    }

    [Fact]
    public void DailyAllowance_ReservesCommitmentsAndSavingsOnce()
    {
        var baseline = Baseline();
        var data = new LedgerData
        {
            Today = MidNov, Categories = Categories, Statements = baseline.Statements, Receipts = baseline.Receipts, Transactions = baseline.Transactions,
            Commitments = new[]
            {
                new LedgerCommitment("recurring", 1, new DateOnly(2026, 11, 20), 3_000m, Rent, "Internet"),
                new LedgerCommitment("recurring", 1, new DateOnly(2026, 11, 20), 3_000m, Rent, "Internet"),
                new LedgerCommitment("planned", 7, new DateOnly(2026, 11, 25), 1_000m, Food, "Dinner"),
                new LedgerCommitment("planned", 8, new DateOnly(2026, 12, 2), 9_999m, Food, "Next month"),
            },
            Settings = new Dictionary<YearMonth, LedgerMonthSetting> { [Nov] = new(Nov, null, 2_000m) },
        };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(16, m.RemainingDaysIncludingToday);
        Assert.Equal(4_000m, m.UnpaidCommitments);
        // (30,000 − 4,000 − 2,000) ÷ 16 days
        Assert.Equal(1_500m, m.DailyAllowance);
    }

    [Fact]
    public void DailyAllowance_IsZeroWhenInDeficit_AndTheDeficitIsShownSeparately()
    {
        var data = Baseline(Paid(Rent, 40_000m, new DateOnly(2026, 11, 14)));

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(-10_000m, m.RemainingBalance);
        Assert.Equal(10_000m, m.Deficit);
        Assert.Equal(0m, m.DailyAllowance);
    }

    [Fact]
    public void PlannedExpensesAndTransfers_NeverCountAsSpending()
    {
        var data = Baseline(
            new LedgerTxn(_id++, new DateOnly(2026, 11, 28), 7_000m, Food, TxKind.Expense, TxStatus.Planned, false),
            new LedgerTxn(_id++, new DateOnly(2026, 11, 8), 9_000m, null, TxKind.Transfer, TxStatus.Paid, false));

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(30_000m, m.NetSpending);
        Assert.Equal(7_000m, m.PlannedSpending);
    }

    [Fact]
    public void ExpectedOtherIncome_IsPlanningOnly()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Receipts = new[] { new LedgerReceipt(1, ReceiptKind.Other, ReceiptStatus.Expected, null, 8_000m, null, 9, new[] { new LedgerAllocation(Dec, 8_000m) }) },
        };

        var dec = LedgerCalculator.Single(data, Dec);

        Assert.Equal(0m, dec.ReceivedIncome);
        Assert.Equal(8_000m, dec.ExpectedOtherIncome);
        Assert.Equal(8_000m, dec.ForecastRemaining);
    }

    [Fact]
    public void CashFlowBasis_CountsReceiptsByReceivedDate_SeparatelyFromBudgetMonths()
    {
        var data = new LedgerData
        {
            Today = MidNov,
            Statements = new[] { Statement(1, Oct, Nov, (SalaryComponentKind.Base, 55_000m)) },
            // Paid early on 28 October, but it funds November.
            Receipts = new[] { Salary(55_000m, new DateOnly(2026, 10, 28), 1, (Nov, 55_000m)) },
        };

        Assert.Equal(0m, LedgerCalculator.Single(data, Oct).ReceivedIncome);
        Assert.Equal(55_000m, LedgerCalculator.Single(data, Nov).ReceivedIncome);
        Assert.Equal(55_000m, LedgerCalculator.CashFlow(data, Oct).TotalReceived);
        Assert.Equal(0m, LedgerCalculator.CashFlow(data, Nov).TotalReceived);
    }

    [Fact]
    public void ArchivedCategoriesKeepTheirHistoryInTotals()
    {
        var archived = Categories.Select(c => c.Id == Food ? c with { IsArchived = true } : c).ToArray();
        var baseline = Baseline();
        var data = new LedgerData { Today = MidNov, Categories = archived, Receipts = baseline.Receipts, Transactions = baseline.Transactions };

        var m = LedgerCalculator.Single(data, Nov);

        Assert.Equal(30_000m, m.NetSpending);
        Assert.Contains(m.Categories, c => c.CategoryId == Food && c.IsArchived && c.Net == 8_000m);
    }
}

public class CalendarTests
{
    [Theory]
    [InlineData("2026-11-30T18:59:59Z", 2026, 11, 30)]
    [InlineData("2026-11-30T19:00:00Z", 2026, 12, 1)]
    [InlineData("2026-12-31T19:30:00Z", 2027, 1, 1)]
    [InlineData("2028-02-28T19:00:00Z", 2028, 2, 29)]
    public void KarachiMidnight_DecidesTodayAndTheMonth(string utc, int year, int month, int day)
    {
        var instant = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        Assert.Equal(new DateOnly(year, month, day), LocalCalendar.ToLocalDate(instant, "Asia/Karachi"));
    }

    [Fact]
    public void DecemberSalary_FundsJanuaryOfTheNextYear()
    {
        var plans = new[] { new SalaryPlan { Year = 2026, Month = 1, Amount = 55_000m } };

        var statement = LedgerLoader.PlannedStatement(plans, new YearMonth(2026, 12), new YearMonth(2026, 12).AddMonths(1));

        Assert.NotNull(statement);
        Assert.Equal(new YearMonth(2027, 1), statement!.Funded);
        Assert.Equal(55_000m, SalaryMath.ExpectedNet(statement.Components));
    }

    [Fact]
    public void SalaryPlanChanges_ApplyFromTheirEffectiveMonthOnly()
    {
        var plans = new[]
        {
            new SalaryPlan { Year = 2026, Month = 1, Amount = 50_000m },
            new SalaryPlan { Year = 2026, Month = 10, Amount = 55_000m, Allowances = 2_000m },
        };

        Assert.Equal(50_000m, SalaryMath.ExpectedNet(LedgerLoader.PlannedStatement(plans, new YearMonth(2026, 9), new YearMonth(2026, 10))!.Components));
        Assert.Equal(57_000m, SalaryMath.ExpectedNet(LedgerLoader.PlannedStatement(plans, new YearMonth(2026, 10), new YearMonth(2026, 11))!.Components));
        Assert.Null(LedgerLoader.PlannedStatement(plans, new YearMonth(2025, 12), new YearMonth(2026, 1)));
    }

    [Theory]
    [InlineData(2026, 2, 31, 28)]
    [InlineData(2028, 2, 31, 29)]
    [InlineData(2026, 4, 31, 30)]
    [InlineData(2026, 11, 5, 5)]
    public void RecurringDueDates_ClampToTheMonthLength(int year, int month, int day, int expected)
    {
        Assert.Equal(expected, RecurringService.DueDate(new YearMonth(year, month), day).Day);
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("10.25", true)]
    [InlineData("10.255", false)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    public void MoneyAmounts_AllowAtMostTwoDecimals(string value, bool valid)
    {
        Assert.Equal(valid, Money.IsValidAmount(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void MoneyArithmetic_IsExact()
    {
        Assert.Equal(0.3m, 0.1m + 0.2m);
        Assert.Equal(33.33m, Money.Round(100m / 3m));
        Assert.Equal(2.01m, Money.Round(2.005m));
        Assert.Equal(1_154.03m, Money.RoundDown(30_005m / 26m));
    }
}
