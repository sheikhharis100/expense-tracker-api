using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using CsvHelper;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExpenseTracker.Api.Tests.Integration.Api;

namespace ExpenseTracker.Api.Tests.Integration;

[Collection("api")]
public class LedgerApiTests
{
    private readonly ApiFixture _fx;

    public LedgerApiTests(ApiFixture fx) => _fx = fx;

    private static async Task<Api> WithBaseline(Api api)
    {
        await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 55_000m, allowances = 0m });
        await api.Post("/api/income/statements/2026-10/payments", new { clientRequestId = Guid.NewGuid(), receivedDate = "2026-11-01", amount = 55_000m });
        await api.Post("/api/income/receipts", new
        {
            clientRequestId = Guid.NewGuid(), kind = "Other", categoryId = api.Categories["Other income"], status = "Received",
            receivedDate = "2026-11-03", amount = 5_000m, description = "Freelance"
        });
        await api.Expense("Bill / Rent", 15_000m, "2026-11-01");
        await api.Expense("Food", 8_000m, "2026-11-04");
        await api.Expense("Travel — InDrive", 4_000m, "2026-11-05");
        await api.Expense("Other Expenses", 3_000m, "2026-11-06", additional: true);
        return api;
    }

    [Fact]
    public async Task Baseline_DashboardLedgerAnnualAndExportsReconcile()
    {
        var api = await WithBaseline(await _fx.NewUserAsync());

        var nov = await api.Summary("2026-11");
        Assert.Equal(60_000m, D(nov["receivedIncome"]));
        Assert.Equal(30_000m, D(nov["netSpending"]));
        Assert.Equal(30_000m, D(nov["remainingBalance"]));
        Assert.Equal(50.0m, D(nov["incomeUsedPercent"]));
        Assert.Equal(3_000m, D(nov["additionalNet"]));
        Assert.Equal("current", nov["timing"]!.GetValue<string>());

        var additional = await api.Get("/api/expenses?month=2026-11&additional=true");
        Assert.Single(additional["rows"]!.AsArray());
        Assert.Equal(3_000m, D(additional["filtered"]!["net"]));
        Assert.Equal(30_000m, D(additional["monthTotals"]!["net"]));
        Assert.True(additional["isFiltered"]!.GetValue<bool>());

        var annual = await api.Get("/api/summary/year/2026");
        var novRow = annual["rows"]!.AsArray().Single(r => r!["month"]!.GetValue<string>() == "2026-11")!;
        Assert.Equal(30_000m, D(novRow["netSpending"]));
        Assert.Equal(60_000m, D(novRow["receivedIncome"]));

        var csv = await api.Http.GetStringAsync("/api/exports/month/2026-11?format=csv&part=entries");
        using var reader = new CsvReader(new StringReader(csv), CultureInfo.InvariantCulture);
        var rows = new List<string[]>();
        while (reader.Read()) rows.Add(reader.Parser.Record!);
        Assert.Equal(new[] { "Reporting month", "Accounting basis" }, rows[0][..2]);
        var data = rows.Skip(1).ToList();
        Assert.All(data, r => Assert.Equal("2026-11", r[0]));
        Assert.All(data, r => Assert.Equal("budget-month", r[1]));
        var counted = data.Where(r => r[2] != "").Sum(r => decimal.Parse(r[9], CultureInfo.InvariantCulture));
        Assert.Equal(30_000m, counted);
        Assert.Equal("30000.00", data.Last()[9]);

        var xlsx = await api.Http.GetAsync("/api/exports/month/2026-11?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.True((await xlsx.Content.ReadAsByteArrayAsync()).Length > 1000);
        Assert.Equal(HttpStatusCode.OK, (await api.Http.GetAsync("/api/exports/year/2026?format=xlsx")).StatusCode);
    }

    [Fact]
    public async Task Refund_ReducesNetSpending_AndCannotExceedTheOriginal()
    {
        var api = await WithBaseline(await _fx.NewUserAsync());
        var food = (await api.Get("/api/expenses?month=2026-11&search=Food"))["rows"]!.AsArray().Single()!;

        await api.Post($"/api/expenses/{food["id"]}/refunds", new { clientRequestId = Guid.NewGuid(), date = "2026-11-10", amount = 1_000m });
        Assert.Equal(29_000m, D((await api.Summary("2026-11"))["netSpending"]));

        await api.Post($"/api/expenses/{food["id"]}/refunds", new { date = "2026-11-11", amount = 7_500m }, HttpStatusCode.BadRequest);
        await api.Delete($"/api/expenses/{food["id"]}", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PendingSalary_DoesNotIncreaseAvailableFunds_AndPartialPaymentsCountOnce()
    {
        var api = await _fx.NewUserAsync();
        await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 55_000m, allowances = 0m });

        var before = await api.Summary("2026-11");
        Assert.Equal(55_000m, D(before["expectedSalary"]));
        Assert.Equal(0m, D(before["receivedIncome"]));
        Assert.Equal(0m, D(before["availableFunds"]));
        Assert.Null(before["incomeUsedPercent"]);

        await api.Post("/api/income/statements/2026-10/payments", new { clientRequestId = Guid.NewGuid(), receivedDate = "2026-11-01", amount = 30_000m });
        await api.Post("/api/income/statements/2026-10/payments", new { clientRequestId = Guid.NewGuid(), receivedDate = "2026-11-09", amount = 25_000m });

        var income = await api.Get("/api/income/2026-11");
        var statement = income["funding"]!.AsArray().Single()!;
        Assert.Equal(55_000m, D(statement["received"]));
        Assert.Equal(0m, D(statement["outstanding"]));
        Assert.Equal("Received", statement["status"]!.GetValue<string>());
        Assert.Equal(55_000m, D(income["totalReceivedIncome"]));
        Assert.Equal(55_000m, D((await api.Summary("2026-11"))["availableFunds"]));

        // An allocation can't exceed what was received, and pending salary can't be recorded as a payment.
        await api.Post("/api/income/statements/2026-10/payments", new { receivedDate = "2026-11-09", amount = 1_000m, allocations = new[] { new { month = "2026-11", amount = 2_000m } } }, HttpStatusCode.BadRequest);
        await api.Post("/api/income/statements/2026-10/payments", new { receivedDate = "2026-11-20", amount = 1_000m }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SalaryPlanChange_DoesNotRewriteASavedStatement()
    {
        var api = await _fx.NewUserAsync();
        await api.Put("/api/income/plans", new { effectiveFrom = "2026-01", baseSalary = 50_000m, allowances = 0m });
        await api.Put("/api/income/statements/2026-10", new
        {
            funded = "2026-11",
            components = new object[] { new { kind = "Base", amount = 50_000m }, new { kind = "Deduction", label = "Tax", amount = 2_500m } }
        });

        await api.Put("/api/income/plans", new { effectiveFrom = "2026-09", baseSalary = 60_000m, allowances = 0m });

        Assert.Equal(47_500m, D((await api.Summary("2026-11"))["expectedSalary"]));
        Assert.Equal(60_000m, D((await api.Summary("2026-12"))["expectedSalary"]));
        Assert.Equal(0m, D((await api.Summary("2026-11"))["netSpending"]));
    }

    [Fact]
    public async Task EditingAndDeleting_UpdatesEverySummary()
    {
        var api = await WithBaseline(await _fx.NewUserAsync());
        var food = (await api.Get("/api/expenses?month=2026-11&search=Food"))["rows"]!.AsArray().Single()!;

        var edited = await api.Patch($"/api/expenses/{food["id"]}", new { version = food["version"]!.GetValue<uint>(), amount = 9_500m });
        Assert.Equal(31_500m, D((await api.Summary("2026-11"))["netSpending"]));
        Assert.Equal(9_500m, D((await api.Get("/api/expenses/matrix/2026-11"))["columnTotals"]![api.Categories["Food"].ToString()]));

        await api.Delete($"/api/expenses/{food["id"]}?version={edited!["version"]}");
        Assert.Equal(22_000m, D((await api.Summary("2026-11"))["netSpending"]));
        var annual = await api.Get("/api/summary/year/2026");
        Assert.Equal(22_000m, D(annual["rows"]!.AsArray()[10]!["netSpending"]));
    }

    [Fact]
    public async Task RepeatedSubmissionsAndStaleEdits_DoNotDuplicateOrOverwrite()
    {
        var api = await _fx.NewUserAsync();
        var requestId = Guid.NewGuid();
        var first = await api.Expense("Food", 500m, "2026-11-02", requestId: requestId);
        var second = await api.Expense("Food", 500m, "2026-11-02", requestId: requestId);
        Assert.Equal(first["id"]!.GetValue<int>(), second["id"]!.GetValue<int>());
        Assert.Single((await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray());

        var version = first["version"]!.GetValue<uint>();
        await api.Patch($"/api/expenses/{first["id"]}", new { version, amount = 600m });
        var conflict = await api.Patch($"/api/expenses/{first["id"]}", new { version, amount = 700m }, HttpStatusCode.Conflict);
        Assert.Equal(600m, D(conflict!["current"]!["amount"]));
        Assert.Equal(600m, D((await api.Summary("2026-11"))["netSpending"]));
    }

    [Fact]
    public async Task PaidEntriesCantBeFutureDated_PlannedOnesNeverCountAsSpending()
    {
        var api = await _fx.NewUserAsync();
        await api.Post("/api/expenses", new { date = "2026-11-20", amount = 100m, kind = "Expense", status = "Paid", categoryId = api.Categories["Food"] }, HttpStatusCode.BadRequest);
        await api.Expense("Food", 2_000m, "2026-11-25", status: "Planned");

        var nov = await api.Summary("2026-11");
        Assert.Equal(0m, D(nov["netSpending"]));
        Assert.Equal(2_000m, D(nov["unpaidCommitments"]));
    }

    [Fact]
    public async Task RecurringItems_GenerateOncePerMonth_AndPayingTwiceCreatesOneTransaction()
    {
        var api = await _fx.NewUserAsync();
        await api.Post("/api/recurring", new { kind = "Expense", description = "Internet", categoryId = api.Categories["Utilities"], amount = 3_000m, dayOfMonth = 20, start = "2026-11" });

        await api.Get("/api/recurring/2026-11");
        var month = await api.Get("/api/recurring/2026-11");
        var occurrence = month["occurrences"]!.AsArray().Single()!;
        Assert.Equal("Planned", occurrence["status"]!.GetValue<string>());
        Assert.Equal(3_000m, D((await api.Summary("2026-11"))["unpaidCommitments"]));
        Assert.Equal(0m, D((await api.Summary("2026-11"))["netSpending"]));

        var paid = await api.Post($"/api/recurring/occurrences/{occurrence["id"]}/pay", new { clientRequestId = Guid.NewGuid(), date = "2026-11-15" });
        var again = await api.Post($"/api/recurring/occurrences/{occurrence["id"]}/pay", new { clientRequestId = Guid.NewGuid(), date = "2026-11-15" });
        Assert.False(paid!["alreadyPaid"]!.GetValue<bool>());
        Assert.True(again!["alreadyPaid"]!.GetValue<bool>());

        var rows = (await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.True(rows[0]!["isRecurring"]!.GetValue<bool>());
        var nov = await api.Summary("2026-11");
        Assert.Equal(3_000m, D(nov["netSpending"]));
        Assert.Equal(0m, D(nov["unpaidCommitments"]));
        Assert.Single((await api.Get("/api/recurring/2026-12"))["occurrences"]!.AsArray());
    }

    [Fact]
    public async Task RecurringChange_ThisAndFuture_KeepsEarlierMonths()
    {
        var api = await _fx.NewUserAsync();
        var item = await api.Post("/api/recurring", new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 15_000m, dayOfMonth = 1, start = "2026-11" });
        await api.Get("/api/recurring/2026-11");
        var nov = (await api.Get("/api/recurring/2026-11"))["occurrences"]!.AsArray().Single()!;
        await api.Post($"/api/recurring/occurrences/{nov["id"]}/pay", new { date = "2026-11-01" });

        await api.Put($"/api/recurring/{item!["id"]}?from=2027-01", new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 17_000m, dayOfMonth = 1, start = "2026-11" });

        Assert.Equal(15_000m, D((await api.Get("/api/recurring/2026-12"))["occurrences"]!.AsArray().Single()!["amount"]));
        Assert.Equal(17_000m, D((await api.Get("/api/recurring/2027-01"))["occurrences"]!.AsArray().Single()!["amount"]));
        Assert.Equal(15_000m, D((await api.Summary("2026-11"))["netSpending"]));
    }

    [Fact]
    public async Task CustomColumns_PersistValues_AndFormulasSurviveRenames()
    {
        var api = await _fx.NewUserAsync();
        var tip = (await api.Post("/api/columns", new { name = "Tip", type = "money" }))!;
        var tipKey = tip["key"]!.GetValue<string>();
        var total = (await api.Post("/api/columns", new { name = "With tip", type = "formula", formula = "[Amount] + [Tip]" }))!;
        var totalKey = total["key"]!.GetValue<string>();
        var share = (await api.Post("/api/columns", new { name = "Food share", type = "formula", formula = "ROUND([Amount] / SUMIF([Category], \"Food\", [Amount]) * 100, 1)" }))!;

        var a = await api.Expense("Food", 600m, "2026-11-02");
        await api.Expense("Food", 400m, "2026-11-03");
        await api.Patch($"/api/expenses/{a["id"]}", new { version = a["version"]!.GetValue<uint>(), custom = new Dictionary<string, object> { [tipKey] = 50m } });

        var list = await api.Get("/api/expenses?month=2026-11&sort=amount&dir=desc");
        var row = list["rows"]!.AsArray()[0]!;
        Assert.Equal(50m, D(row["custom"]![tipKey]));
        Assert.Equal(650m, D(row["computed"]![totalKey]!["value"]));
        Assert.Equal(60m, D(row["computed"]![share["key"]!.GetValue<string>()]!["value"]));
        // Custom numbers never change the ledger totals.
        Assert.Equal(1_000m, D(list["monthTotals"]!["net"]));

        await api.Put($"/api/columns/{tip["id"]}", new { name = "Gratuity", type = "money", version = tip["version"]!.GetValue<uint>() });
        var columns = (await api.Get("/api/columns")).AsArray();
        Assert.Equal("[Amount] + [Gratuity]", columns.Single(c => c!["key"]!.GetValue<string>() == totalKey)!["formula"]!.GetValue<string>());
        var after = (await api.Get("/api/expenses?month=2026-11&sort=amount&dir=desc"))["rows"]!.AsArray()[0]!;
        Assert.Equal(650m, D(after["computed"]![totalKey]!["value"]));

        // Bad values and bad formulas are rejected with a reason.
        await api.Patch($"/api/expenses/{a["id"]}", new { version = after["version"]!.GetValue<uint>(), custom = new Dictionary<string, object> { [tipKey] = "lots" } }, HttpStatusCode.BadRequest);
        await api.Post("/api/columns", new { name = "Broken", type = "formula", formula = "[Amount] +" }, HttpStatusCode.BadRequest);
        await api.Post("/api/columns", new { name = "Unknown", type = "formula", formula = "VLOOKUP([Amount])" }, HttpStatusCode.BadRequest);
        var loopA = (await api.Post("/api/columns", new { name = "Loop A", type = "formula", formula = "[Amount] * 2" }))!;
        await api.Post("/api/columns", new { name = "Loop B", type = "formula", formula = "[Loop A] + 1" });
        await api.Put($"/api/columns/{loopA["id"]}", new { name = "Loop A", type = "formula", formula = "[Loop B] + 1" }, HttpStatusCode.BadRequest);
        var check = await api.Post("/api/columns/check", new { formula = "[Amount] / 0" });
        Assert.True(check!["ok"]!.GetValue<bool>());

        // Removing a column with data needs an explicit choice; deleting it turns dependents into #REF!, not 0.
        var impact = await api.Delete($"/api/columns/{tip["id"]}", HttpStatusCode.Conflict);
        Assert.Equal(1, impact!["current"]!["valueCount"]!.GetValue<int>());
        Assert.Contains("With tip", impact["current"]!["dependentColumns"]!.AsArray().Select(n => n!.GetValue<string>()));
        await api.Delete($"/api/columns/{tip["id"]}?mode=delete", HttpStatusCode.NoContent);
        var broken = (await api.Get("/api/expenses?month=2026-11&sort=amount&dir=desc"))["rows"]!.AsArray()[0]!;
        Assert.Equal("#REF!", broken["computed"]![totalKey]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Layouts_PersistPerMonth_OrAsATemplateForFutureMonths()
    {
        var api = await _fx.NewUserAsync();
        await api.Put("/api/columns/layout/expenses/2026-11", new { scope = "month", columns = new[] { new { key = "amount", width = (int?)140, hidden = false }, new { key = "date", width = (int?)null, hidden = false }, new { key = "method", width = (int?)null, hidden = true } } });

        var nov = await api.Get("/api/columns/layout/expenses/2026-11");
        Assert.Equal("month", nov["source"]!.GetValue<string>());
        Assert.Equal("amount", nov["columns"]![0]!["key"]!.GetValue<string>());
        Assert.True(nov["columns"]!.AsArray().Single(c => c!["key"]!.GetValue<string>() == "method")!["hidden"]!.GetValue<bool>());
        Assert.Equal("default", (await api.Get("/api/columns/layout/expenses/2026-12"))["source"]!.GetValue<string>());

        await api.Put("/api/columns/layout/expenses/2027-01", new { scope = "future", columns = new[] { new { key = "description", width = 300, hidden = false } } });
        Assert.Equal("template", (await api.Get("/api/columns/layout/expenses/2027-06"))["source"]!.GetValue<string>());
        Assert.Equal("default", (await api.Get("/api/columns/layout/expenses/2026-12"))["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task RenamingACategory_KeepsHistoricalTotals()
    {
        var api = await WithBaseline(await _fx.NewUserAsync());
        var food = (await api.Get("/api/categories")).AsArray().Single(c => c!["name"]!.GetValue<string>() == "Food")!;

        await api.Put($"/api/categories/{food["id"]}", new { name = "Meals", type = "Expense", color = food["color"]!.GetValue<string>(), isArchived = false });
        await api.Put($"/api/categories/{food["id"]}", new { name = "Meals", type = "Expense", color = food["color"]!.GetValue<string>(), isArchived = true });

        var nov = await api.Summary("2026-11");
        Assert.Equal(30_000m, D(nov["netSpending"]));
        Assert.Contains(nov["categories"]!.AsArray(), c => c!["name"]!.GetValue<string>() == "Meals" && D(c["net"]) == 8_000m);
        // Archived: no new entries, history intact.
        await api.Post("/api/expenses", new { date = "2026-11-12", amount = 100m, kind = "Expense", status = "Paid", categoryId = api.Categories["Food"] }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Carryover_ForwardsBalances_AndBackdatedEditsRecalculate()
    {
        var api = await _fx.NewUserAsync();
        await api.Put("/api/me", new { displayName = "", currency = "PKR", timeZone = "Asia/Karachi", salaryLagMonths = 1, carryoverEnabled = true });
        await api.Post("/api/income/receipts", new { kind = "Other", categoryId = api.Categories["Other income"], status = "Received", receivedDate = "2026-10-02", amount = 10_000m });
        await api.Expense("Food", 12_000m, "2026-10-05");

        var nov = await api.Summary("2026-11");
        Assert.Equal(-2_000m, D(nov["openingBalance"]));
        Assert.Equal(0m, D(nov["receivedIncome"]));

        await api.Expense("Food", 500m, "2026-10-28");
        Assert.Equal(-2_500m, D((await api.Summary("2026-11"))["openingBalance"]));
    }

    [Fact]
    public async Task ZeroIncomeAndMissingBudgets_ShowNotSetStates()
    {
        var api = await _fx.NewUserAsync();
        var nov = await api.Summary("2026-11");

        Assert.Null(nov["incomeUsedPercent"]);
        Assert.Null(nov["totalBudget"]);
        Assert.Null(nov["overallBudgetRemaining"]);
        Assert.Null((await api.Summary("2026-12"))["averageDailySpending"]);

        await api.Expense("Shopping", 1_000m, "2026-11-02");
        await api.Put($"/api/budgets/2026-11/{api.Categories["Food"]}", new { amount = 8_000m, scope = "future" });
        var budgets = await api.Get("/api/budgets/2026-11");
        Assert.True(budgets["coverageWarning"]!.GetValue<bool>());
        Assert.Equal(1_000m, D(budgets["unbudgetedSpending"]));
        Assert.Equal(8_000m, D((await api.Get("/api/budgets/2027-03"))["totalBudget"]));
        Assert.Null((await api.Get("/api/budgets/2026-10"))["totalBudget"]);
    }

    [Fact]
    public async Task OtherUsers_CannotReadOrChangeEntries()
    {
        var owner = await _fx.NewUserAsync();
        var stranger = await _fx.NewUserAsync();
        var entry = await owner.Expense("Food", 500m, "2026-11-02");

        await stranger.Send(HttpMethod.Get, $"/api/expenses/{entry["id"]}", null, HttpStatusCode.NotFound);
        await stranger.Patch($"/api/expenses/{entry["id"]}", new { version = entry["version"]!.GetValue<uint>(), amount = 1m }, HttpStatusCode.NotFound);
        await stranger.Delete($"/api/expenses/{entry["id"]}", HttpStatusCode.NotFound);
        Assert.Empty((await stranger.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray());
        Assert.Equal(500m, D((await owner.Summary("2026-11"))["netSpending"]));
    }

    [Fact]
    public async Task KarachiMidnight_MovesTodayIntoTheNextMonth()
    {
        var api = await _fx.NewUserAsync();
        var original = _fx.Clock.UtcNow;
        try
        {
            _fx.Clock.UtcNow = new DateTime(2026, 11, 30, 19, 30, 0, DateTimeKind.Utc);
            var me = await api.Get("/api/me");
            Assert.Equal("2026-12-01", me["today"]!.GetValue<string>());
            Assert.Equal("2026-12", me["currentMonth"]!.GetValue<string>());
            Assert.Equal("current", (await api.Summary("2026-12"))["timing"]!.GetValue<string>());
        }
        finally
        {
            _fx.Clock.UtcNow = original;
        }
    }

    [Fact]
    public async Task LegacyRecords_AreBackfilledOnce_WithTheSameTotals()
    {
        var api = await _fx.NewUserAsync();
        var userId = api.Me["id"]!.GetValue<int>();

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.DataVersion = 0;
            db.SalaryPlans.Add(new SalaryPlan { UserId = userId, Year = 2026, Month = 10, Amount = 55_000m });
            db.MonthAdjustments.Add(new MonthAdjustment { UserId = userId, Year = 2026, Month = 11, SalaryAdjust = 2_000m, Note = "Bonus" });
            var account = await db.Accounts.FirstAsync(a => a.UserId == userId);
            db.Transactions.Add(new Transaction
            {
                UserId = userId, AccountId = account.Id, CategoryId = api.Categories["Other income"], Amount = 5_500m,
                TransactionDate = new DateTime(2026, 11, 2), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<LegacyBackfillService>().RunAsync();
            await scope.ServiceProvider.GetRequiredService<LegacyBackfillService>().RunAsync();
        }

        var nov = await api.Summary("2026-11");
        Assert.Equal(57_000m, D(nov["salaryIncome"]));
        Assert.Equal(5_500m, D(nov["otherIncome"]));
        Assert.Equal(0m, D(nov["netSpending"]));
        Assert.Empty((await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray());
        Assert.Equal("Bonus", (await api.Get("/api/months/2026-11/settings"))["note"]!.GetValue<string>());
        Assert.Equal(0m, D((await api.Summary("2026-12"))["receivedIncome"]));
        Assert.Equal(55_000m, D((await api.Summary("2026-12"))["expectedSalary"]));
    }
}
