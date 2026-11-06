using static ExpenseTracker.Api.Tests.Integration.Api;

namespace ExpenseTracker.Api.Tests.Integration;

// The situation a user reported: a salary saved as expected but never marked received shows the month as overspent.
[Collection("api")]
public class SalaryNotRecordedTests
{
    private readonly ApiFixture _fx;

    public SalaryNotRecordedTests(ApiFixture fx) => _fx = fx;

    private static async Task<Api> WithExpectedSalaryAndRent(Api api)
    {
        await api.Put("/api/income/statements/2026-10", new
        {
            funded = "2026-11",
            components = new[] { new { kind = "Base", label = "Base salary", amount = 60_500m } }
        });
        await api.Expense("Bill / Rent", 14_920m, "2026-11-01");
        return api;
    }

    [Fact]
    public async Task ExpectedSalaryIsNotMoneyUntilReceived_ThenCountsOnce()
    {
        var api = await WithExpectedSalaryAndRent(await _fx.NewUserAsync());

        var before = await api.Summary("2026-11");
        Assert.Equal(0m, D(before["receivedIncome"]));
        Assert.Equal(60_500m, D(before["expectedSalary"]));
        Assert.Equal(60_500m, D(before["salaryOutstanding"]));
        Assert.Equal(-14_920m, D(before["remainingBalance"]));
        Assert.Equal(14_920m, D(before["deficit"]));
        Assert.Equal(0m, D(before["dailyAllowance"]));

        // "Mark as received" records the outstanding amount as a payment towards that salary.
        await api.Post("/api/income/statements/2026-10/payments", new { clientRequestId = Guid.NewGuid(), receivedDate = "2026-11-01", amount = 60_500m });

        var after = await api.Summary("2026-11");
        Assert.Equal(60_500m, D(after["receivedIncome"]));
        Assert.Equal(0m, D(after["salaryOutstanding"]));
        Assert.Equal(45_580m, D(after["remainingBalance"]));
        Assert.Equal(0m, D(after["deficit"]));
        Assert.Equal(24.7m, D(after["incomeUsedPercent"]));
        // FakeClock: 15 November, so 16 days left including today: 45,580 / 16 = 2,848.75.
        Assert.Equal(2_848.75m, D(after["dailyAllowance"]));
    }

    [Fact]
    public async Task SheetImport_CorrectsAnUnpaidSalaryToTheSheet_AndBringsTheExtraIncome()
    {
        var api = await WithExpectedSalaryAndRent(await _fx.NewUserAsync());

        await api.Upload("/api/expenses/import/sheet", "Daily Log.csv", SheetCsvParserTests.DailyLogCsv);

        var nov = await api.Summary("2026-11");
        // The sheet says Oct pay 55,000 and Income 60,500: the unpaid 60,500 salary becomes 55,000, paid, plus 5,500 other.
        Assert.Equal(55_000m, D(nov["expectedSalary"]));
        Assert.Equal(55_000m, D(nov["salaryIncome"]));
        Assert.Equal(0m, D(nov["salaryOutstanding"]));
        Assert.Equal(5_500m, D(nov["otherIncome"]));
        Assert.Equal(60_500m, D(nov["receivedIncome"]));
        // The rent typed on 1 Nov is the same cell as the sheet's, so it's replaced, not doubled.
        Assert.Equal(22_235m, D(nov["netSpending"]));
    }
}
