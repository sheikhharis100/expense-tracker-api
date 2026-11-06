using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Tests;

public class SalaryCalculatorTests
{
    private static readonly SalaryCalculator.SalaryEntry[] History =
    {
        new(new YearMonth(2026, 6), 50_000m),
        new(new YearMonth(2026, 10), 55_000m),
    };

    [Fact]
    public void NovemberSpendsOctoberPay_WhenLagIsOneMonth()
    {
        var payFor = SalaryCalculator.PayMonthFor(new YearMonth(2026, 11), 1);

        Assert.Equal(new YearMonth(2026, 10), payFor);
        Assert.Equal(55_000m, SalaryCalculator.SalaryForPayMonth(History, payFor));
    }

    [Fact]
    public void JanuarySpendsPreviousDecember()
    {
        Assert.Equal(new YearMonth(2026, 12), SalaryCalculator.PayMonthFor(new YearMonth(2027, 1), 1));
    }

    [Fact]
    public void LaterMonthsFollowTheLatestRecord_UntilItChanges()
    {
        Assert.Equal(50_000m, SalaryCalculator.SalaryForPayMonth(History, new YearMonth(2026, 9)));
        Assert.Equal(55_000m, SalaryCalculator.SalaryForPayMonth(History, new YearMonth(2027, 8)));
    }

    [Fact]
    public void MonthsBeforeAnyRecord_HaveNoSalary()
    {
        Assert.Equal(0m, SalaryCalculator.SalaryForPayMonth(History, new YearMonth(2026, 5)));
    }
}

public class YearMonthTests
{
    [Theory]
    [InlineData("2026-11", 2026, 11)]
    [InlineData("2027-01", 2027, 1)]
    public void TryParse_ReadsYearDashMonth(string raw, int year, int month)
    {
        Assert.True(YearMonth.TryParse(raw, out var ym));
        Assert.Equal(new YearMonth(year, month), ym);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("Nov 2026")]
    [InlineData("")]
    public void TryParse_RejectsOtherFormats(string raw)
    {
        Assert.False(YearMonth.TryParse(raw, out _));
    }

    [Fact]
    public void AddMonths_CrossesYearBoundaries()
    {
        Assert.Equal(new YearMonth(2027, 2), new YearMonth(2026, 11).AddMonths(3));
        Assert.Equal(new YearMonth(2025, 12), new YearMonth(2026, 1).AddMonths(-1));
    }
}

public class SheetCsvParserTests
{
    // Shaped like File → Download → CSV of the "Daily Log" tab.
    private const string DailyLogCsv =
        "\"Type each day's spending in the yellow cells (PKR).\",,,,,,,,,,\n" +
        "Date,Day,Month,Bill / Rent,Food,Travel (InDrive),Travel (Bus),Other Expenses,Daily Total,Expense details (auto),Remarks\n" +
        "NOVEMBER 2026,,,\"Salary (Oct pay): 55,000    Income: 60,500    Spent: 30,495\",,,,,,,\n" +
        "01 Nov 2026,Sun,Nov 2026,\"14,920\",625,,,\"2,400\",\"17,945\",\"Bill / Rent: 14,920  |  Food: 625  |  Other Expenses: 2000 + 400\",2000 kameti\n" +
        "02 Nov 2026,Mon,Nov 2026,,,465,,515,980,Travel (InDrive): 465  |  Other Expenses: 515,learner license 515\n" +
        "04 Nov 2026,Wed,Nov 2026,,\"2,810\",200,,300,\"2,310\",Food: 1810 + 1000  |  Travel (InDrive): 200  |  Other Expenses: 300,spotify\n" +
        "06 Nov 2026,Fri,Nov 2026,,,,,,-,,\n";

    private static SheetCsvParser.ParseResult Parse(string csv) => SheetCsvParser.Parse(new StringReader(csv));

    [Fact]
    public void FindsTheSpendingColumnsBetweenMonthAndDailyTotal()
    {
        var result = Parse(DailyLogCsv);

        Assert.Equal(new[] { "Bill / Rent", "Food", "Travel (InDrive)", "Travel (Bus)", "Other Expenses" }, result.CategoryColumns);
    }

    [Fact]
    public void SplitsCellsIntoTheirSeparateAmounts_UsingExpenseDetails()
    {
        var day1 = Parse(DailyLogCsv).Days.Single(d => d.Date == new DateOnly(2026, 11, 1));

        Assert.Equal(new[] { 14_920m }, day1.Cells["Bill / Rent"]);
        Assert.Equal(new[] { 2000m, 400m }, day1.Cells["Other Expenses"]);
        Assert.Equal("2000 kameti", day1.Note);
    }

    [Fact]
    public void KeepsTheCellTotal_WhenDetailsDontAddUp()
    {
        // Food = 2,810 and its details "1810 + 1000" agree, so both amounts are kept;
        // a stale details line that no longer matches falls back to the single cell total.
        var day4 = Parse(DailyLogCsv).Days.Single(d => d.Date == new DateOnly(2026, 11, 4));
        Assert.Equal(new[] { 1810m, 1000m }, day4.Cells["Food"]);

        var stale = Parse(DailyLogCsv.Replace("Food: 1810 + 1000", "Food: 1810"));
        Assert.Equal(new[] { 2810m }, stale.Days.Single(d => d.Date == new DateOnly(2026, 11, 4)).Cells["Food"]);
    }

    [Fact]
    public void SkipsEmptyDays_AndCountsRowsRead()
    {
        var result = Parse(DailyLogCsv);

        Assert.Equal(4, result.RowsRead);
        Assert.Equal(3, result.Days.Count);
        Assert.DoesNotContain(result.Days, d => d.Date == new DateOnly(2026, 11, 6));
    }

    [Fact]
    public void ReadsTheSalaryLineOfEachMonthSection()
    {
        var salary = Assert.Single(Parse(DailyLogCsv).Salaries);

        Assert.Equal(new YearMonth(2026, 10), salary.PayMonth);
        Assert.Equal(55_000m, salary.Amount);
    }

    [Fact]
    public void DecemberPayInAJanuarySection_BelongsToThePreviousYear()
    {
        var csv = "Date,Day,Month,Food,Daily Total\nJANUARY 2027,Salary (Dec pay): 60000,,,\n";

        Assert.Equal(new YearMonth(2026, 12), Assert.Single(Parse(csv).Salaries).PayMonth);
    }

    [Fact]
    public void ReportsUnreadableCells_WithoutFailingTheImport()
    {
        var result = Parse(DailyLogCsv.Replace(",465,", ",abc,"));

        Assert.Equal(1, result.CellsSkipped);
        Assert.Contains(result.Warnings, w => w.Contains("Travel (InDrive)"));
        Assert.Equal(3, result.Days.Count);
    }

    [Fact]
    public void WarnsWhenTheFileIsNotTheDailyLog()
    {
        var result = Parse("Month (spending),Pay for,Salary received\nNov 2026,Oct,55000\n");

        Assert.Empty(result.Days);
        Assert.Contains(result.Warnings, w => w.Contains("header row"));
    }

    [Theory]
    [InlineData("14,920", 14920)]
    [InlineData("=100", 100)]
    [InlineData("Rs 1,045", 1045)]
    [InlineData(" 99.50 ", 99.5)]
    public void TryParseAmount_HandlesSheetFormatting(string raw, decimal expected)
    {
        Assert.True(SheetCsvParser.TryParseAmount(raw, out var amount));
        Assert.Equal(expected, amount);
    }
}
