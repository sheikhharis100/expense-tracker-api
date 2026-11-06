using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static ExpenseTracker.Api.Tests.Integration.Api;

namespace ExpenseTracker.Api.Tests.Integration;

[Collection("api")]
public class ImportAndProtectionTests
{
    private readonly ApiFixture _fx;

    public ImportAndProtectionTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task SheetImport_CreatesSeparateEntriesNotesAndSalary_AndImportingTwiceChangesNothing()
    {
        var api = await _fx.NewUserAsync();

        var result = await api.Upload("/api/expenses/import/sheet", "Daily Log.csv", SheetCsvParserTests.DailyLogCsv);
        Assert.Equal(3, result["daysImported"]!.GetValue<int>());
        Assert.Equal(10, result["entriesCreated"]!.GetValue<int>());
        Assert.Equal(3, result["notesImported"]!.GetValue<int>());
        Assert.Equal(1, result["salariesImported"]!.GetValue<int>());

        var rows = (await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray();
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Expense", r!["kind"]!.GetValue<string>());
            Assert.Equal("Paid", r["status"]!.GetValue<string>());
        });

        // "Food: 1810 + 1000" stays two entries in one cell.
        var matrix = await api.Get("/api/expenses/matrix/2026-11");
        var days = matrix["days"]!.AsArray().ToDictionary(d => d!["date"]!.GetValue<string>(), d => d!);
        var food = days["2026-11-04"]["cells"]![api.Categories["Food"].ToString()]!;
        Assert.Equal(new[] { 1_000m, 1_810m }, food["entries"]!.AsArray().Select(e => D(e!["amount"])).OrderBy(a => a));
        Assert.Equal(2_810m, D(food["net"]));
        Assert.Equal("2000 kameti", days["2026-11-01"]["note"]!.GetValue<string>());
        Assert.Equal("learner license 515", days["2026-11-02"]["note"]!.GetValue<string>());
        Assert.Equal("spotify", days["2026-11-04"]["note"]!.GetValue<string>());

        // "Salary (Oct pay): 55,000" becomes the October plan and a received payment funding November.
        var plan = (await api.Get("/api/income/plans")).AsArray().Single()!;
        Assert.Equal("2026-10", plan["effectiveFrom"]!.GetValue<string>());
        Assert.Equal(55_000m, D(plan["baseSalary"]));
        var nov = await api.Summary("2026-11");
        Assert.Equal(55_000m, D(nov["salaryIncome"]));
        Assert.Equal(22_235m, D(nov["netSpending"]));
        var payment = (await api.Get("/api/income/2026-11"))["funding"]!.AsArray().Single()!["payments"]!.AsArray().Single()!;
        Assert.Equal("Received", payment["status"]!.GetValue<string>());
        Assert.Equal(55_000m, D(payment["amount"]));

        var again = await api.Upload("/api/expenses/import/sheet", "Daily Log.csv", SheetCsvParserTests.DailyLogCsv);
        Assert.Equal(0, again["salariesImported"]!.GetValue<int>());
        var after = await api.Summary("2026-11");
        Assert.Equal(55_000m, D(after["salaryIncome"]));
        Assert.Equal(22_235m, D(after["netSpending"]));
        Assert.Equal(10, (await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray().Count);
        Assert.Single((await api.Get("/api/income/2026-11"))["funding"]!.AsArray().Single()!["payments"]!.AsArray());
        Assert.Single((await api.Get("/api/income/plans")).AsArray());
    }

    [Fact]
    public async Task CsvImport_IncomeRowsBecomeReceivedIncome_AndBadRowsAreSkipped()
    {
        var api = await _fx.NewUserAsync();
        const string csv =
            "Date,Description,Amount,Category,Account\n" +
            "2026-11-03,Lunch,750,Food,Cash\n" +
            "2026-11-05,Logo design,4000,Other income,Cash\n" +
            "2026-11-06,Chips,120,Snacks,Cash\n";

        var result = await api.Upload("/api/expenses/import/csv", "transactions.csv", csv);
        Assert.Equal(3, result["totalRows"]!.GetValue<int>());
        Assert.Equal(2, result["importedSuccessfully"]!.GetValue<int>());
        Assert.Equal(1, result["skipped"]!.GetValue<int>());
        var error = result["errors"]!.AsArray().Single()!;
        Assert.Equal(3, error["row"]!.GetValue<int>());
        Assert.Contains("Snacks", error["reason"]!.GetValue<string>());

        var expense = (await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray().Single()!;
        Assert.Equal("Lunch", expense["description"]!.GetValue<string>());
        Assert.Equal(750m, D(expense["amount"]));

        var income = (await api.Get("/api/income/2026-11"))["otherIncome"]!.AsArray().Single()!;
        Assert.Equal("Received", income["status"]!.GetValue<string>());
        Assert.Equal("Other income", income["categoryName"]!.GetValue<string>());
        Assert.Equal(4_000m, D(income["amount"]));

        var nov = await api.Summary("2026-11");
        Assert.Equal(750m, D(nov["netSpending"]));
        Assert.Equal(4_000m, D(nov["otherIncome"]));
        Assert.Equal(4_000m, D(nov["receivedIncome"]));
    }

    [Fact]
    public async Task StaleCategoryEdit_Returns409WithTheCurrentCategory()
    {
        var api = await _fx.NewUserAsync();
        var url = $"/api/categories/{api.Categories["Food"]}";
        var version = (await api.Get(url))["version"]!.GetValue<uint>();

        await api.Put(url, new { name = "Meals", type = "Expense", version });
        var conflict = await api.Put(url, new { name = "Lunch", type = "Expense", version }, HttpStatusCode.Conflict);

        Assert.Contains("another device", conflict!["error"]!.GetValue<string>());
        Assert.Equal("Meals", conflict["current"]!["name"]!.GetValue<string>());
        Assert.NotEqual(version, conflict["current"]!["version"]!.GetValue<uint>());
        Assert.Equal("Meals", (await api.Get(url))["name"]!.GetValue<string>());

        // Clients that don't send a version still save as before.
        await api.Put(url, new { name = "Lunch", type = "Expense" });
        Assert.Equal("Lunch", (await api.Get(url))["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task StaleSalaryPlanEdit_Returns409WithTheCurrentPlan()
    {
        var api = await _fx.NewUserAsync();
        var created = await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 55_000m, allowances = 0m });
        var version = created!["version"]!.GetValue<uint>();

        var updated = await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 60_000m, allowances = 0m, version });
        Assert.NotEqual(version, updated!["version"]!.GetValue<uint>());

        var conflict = await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 65_000m, allowances = 0m, version }, HttpStatusCode.Conflict);
        Assert.Equal(60_000m, D(conflict!["current"]!["baseSalary"]));
        Assert.Equal(updated["version"]!.GetValue<uint>(), conflict["current"]!["version"]!.GetValue<uint>());
        Assert.Equal(60_000m, D((await api.Summary("2026-11"))["expectedSalary"]));
    }

    [Fact]
    public async Task SignInAttempts_AreLimitedPerClientIp_BehindRendersProxies()
    {
        await using var factory = _fx.CreateFactory(authPerMinute: 3);
        var client = factory.CreateClient();

        // Requests reach the app on Render as "<whatever the client sent>, <client>, <Cloudflare edge>".
        Task<HttpResponseMessage> Send(string url, object body, string forwardedFor)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            req.Headers.Add("X-Forwarded-For", forwardedFor);
            return client.SendAsync(req);
        }
        var login = new { email = "nobody@example.com", password = "wrong-password" };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send("/api/auth/login", login, $"203.0.113.{i}, 198.51.100.7, 192.0.2.10")).StatusCode);

        // The fourth attempt in the minute is refused, whatever the client puts at the front of the header.
        var limited = await Send("/api/auth/login", login, "203.0.113.99, 198.51.100.7, 192.0.2.10");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many attempts. Wait a minute and try again.", (await limited.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>());

        // Registration counts toward the same limit; another client behind the same edge has its own.
        var register = new { email = $"u{Guid.NewGuid():N}@example.com", password = "secret123" };
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send("/api/auth/register", register, "198.51.100.7, 192.0.2.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send("/api/auth/login", login, "198.51.100.8, 192.0.2.10")).StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_AreSentOnApiResponsesAndTheHealthCheck()
    {
        var api = await _fx.NewUserAsync();

        AssertSecurityHeaders(await api.Http.GetAsync("/api/me"));
        AssertSecurityHeaders(await _fx.Factory.CreateClient().GetAsync("/healthz"));
    }

    private static void AssertSecurityHeaders(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", res.Headers.GetValues("Permissions-Policy").Single());
    }
}
