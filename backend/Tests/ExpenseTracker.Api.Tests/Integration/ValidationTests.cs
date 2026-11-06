using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static ExpenseTracker.Api.Tests.Integration.Api;

namespace ExpenseTracker.Api.Tests.Integration;

// Every way a user can enter or change data is checked on the server: bad values get a 400 with a plain
// `error` message (never a 500), and references must exist, belong to the user and be of the right type.
[Collection("api")]
public class ValidationTests
{
    private readonly ApiFixture _fx;

    public ValidationTests(ApiFixture fx) => _fx = fx;

    // ---------- Helpers ----------

    private static async Task<string> Rejected(Task<JsonNode?> request)
    {
        var body = await request;
        var error = body?["error"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(error), $"expected an error message, got {body?.ToJsonString()}");
        return error!;
    }

    private static Task<string> BadPost(Api api, string url, object body) => Rejected(api.Post(url, body, HttpStatusCode.BadRequest));
    private static Task<string> BadPut(Api api, string url, object body) => Rejected(api.Put(url, body, HttpStatusCode.BadRequest));
    private static Task<string> BadGet(Api api, string url) => Rejected(api.Send(HttpMethod.Get, url, null, HttpStatusCode.BadRequest));

    private static async Task<(HttpStatusCode Status, JsonNode? Body)> Raw(Api api, HttpMethod method, string url, string? json)
    {
        var req = new HttpRequestMessage(method, url);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await api.Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? body = null;
        try
        {
            body = text.Length == 0 ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
        }
        return (res.StatusCode, body);
    }

    private static async Task<string> RawBad(Api api, HttpMethod method, string url, string? json)
    {
        var (status, body) = await Raw(api, method, url, json);
        Assert.True(status == HttpStatusCode.BadRequest, $"{method} {url} with {json}: expected 400, got {(int)status} {body?.ToJsonString()}");
        return body!["error"]!.GetValue<string>();
    }

    private static async Task<(HttpStatusCode Status, JsonNode? Body)> Upload(Api api, string url, string fileName, byte[] content, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        var res = await api.Http.PostAsync(url, form);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private static object Entry(Api api, object amount, string date = "2026-11-05", string category = "Food", string status = "Paid", string description = "") => new
    {
        date,
        amount,
        kind = "Expense",
        status,
        categoryId = api.Categories[category],
        description
    };

    private static async Task<JsonNode> Category(Api api, string name, string type = "Expense") =>
        (await api.Post("/api/categories", new { name, type }))!;

    private static Task Archive(Api api, string name, string type = "Expense") =>
        api.Put($"/api/categories/{api.Categories[name]}", new { name, type, isArchived = true });

    // ---------- Amounts ----------

    [Fact]
    public async Task Amounts_MustBePositive_AtMostTwoDecimals_AndWithinTheLimit()
    {
        var api = await _fx.NewUserAsync();

        Assert.Equal("Amount must be greater than zero.", await BadPost(api, "/api/expenses", Entry(api, 0m)));
        Assert.Equal("Amount must be greater than zero.", await BadPost(api, "/api/expenses", Entry(api, -50m)));
        Assert.Equal("Amount can have at most 2 decimals.", await BadPost(api, "/api/expenses", Entry(api, 12.345m)));
        Assert.Equal("Amount can't be more than 999,999,999.99.", await BadPost(api, "/api/expenses", Entry(api, 1_000_000_000m)));
        await api.Post("/api/expenses", Entry(api, 999_999_999.99m));

        var food = await api.Expense("Food", 500m, "2026-11-04");
        Assert.Contains("greater than zero", await BadPost(api, $"/api/expenses/{food["id"]}/refunds", new { date = "2026-11-05", amount = 0m }));
        Assert.Contains("greater than zero", await Rejected(api.Patch($"/api/expenses/{food["id"]}", new { version = food["version"]!.GetValue<uint>(), amount = -1m }, HttpStatusCode.BadRequest)));

        Assert.Contains("greater than zero", await BadPost(api, "/api/income/receipts", new { kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 0m }));
        Assert.Contains("at most 2 decimals", await BadPost(api, "/api/recurring", new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 1.001m, dayOfMonth = 1, start = "2026-11" }));

        // Zero is fine where it means "nothing", but never negative or oversized.
        Assert.Equal("Budget can't be negative.", await BadPut(api, $"/api/budgets/2026-11/{api.Categories["Food"]}", new { amount = -1m, scope = "month" }));
        Assert.Contains("can't be more than", await BadPut(api, $"/api/budgets/2026-11/{api.Categories["Food"]}", new { amount = 5_000_000_000m, scope = "month" }));
        await api.Put($"/api/budgets/2026-11/{api.Categories["Food"]}", new { amount = 0m, scope = "month" });
        Assert.Equal("Base salary can't be negative.", await BadPut(api, "/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = -1m, allowances = 0m }));
        Assert.Contains("can't be more than", await BadPut(api, "/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 1_000_000_000_000m, allowances = 0m }));
        Assert.Equal("Reserved savings can't be negative.", await BadPut(api, "/api/months/2026-11/settings", new { reservedSavings = -5m }));
        // Used to overflow the database column and fail with a 500.
        Assert.Contains("can't be more than", await BadPut(api, "/api/months/2026-11/settings", new { reservedSavings = 100_000_000_000_000_000m }));
        Assert.Contains("can't be more than", await BadPut(api, "/api/months/2026-11/settings", new { openingBalance = -2_000_000_000m }));
        Assert.Contains("at most 2 decimals", await BadPut(api, "/api/income/statements/2026-10",
            new { funded = "2026-11", components = new[] { new { kind = "Base", label = "Base", amount = 1.234m } } }));
        Assert.Contains("can't be more than", await BadPost(api, "/api/accounts", new { name = "Bank", type = "Checking", currency = "PKR", openingBalance = 10_000_000_000m }));
    }

    // ---------- Malformed requests ----------

    [Fact]
    public async Task MalformedBodies_Return400WithAPlainMessage()
    {
        var api = await _fx.NewUserAsync();
        var food = api.Categories["Food"];

        Assert.Equal("Date must be a date like 2026-11-05.",
            await RawBad(api, HttpMethod.Post, "/api/expenses", $"{{\"date\":\"2026-13-45\",\"amount\":5,\"categoryId\":{food}}}"));
        Assert.Equal("Amount must be a number.",
            await RawBad(api, HttpMethod.Post, "/api/expenses", $"{{\"date\":\"2026-11-05\",\"amount\":\"lots\",\"categoryId\":{food}}}"));
        // NaN and Infinity aren't JSON; browsers send them as null.
        Assert.Equal("Amount must be a number.",
            await RawBad(api, HttpMethod.Post, "/api/expenses", $"{{\"date\":\"2026-11-05\",\"amount\":null,\"categoryId\":{food}}}"));
        Assert.Equal("Amount must be a number.",
            await RawBad(api, HttpMethod.Post, "/api/expenses", $"{{\"date\":\"2026-11-05\",\"amount\":1e40,\"categoryId\":{food}}}"));
        Assert.Equal("Category id must be a whole number.",
            await RawBad(api, HttpMethod.Post, "/api/expenses", "{\"date\":\"2026-11-05\",\"amount\":5,\"categoryId\":\"food\"}"));
        Assert.Equal("Effective from must be a month like 2026-11.",
            await RawBad(api, HttpMethod.Put, "/api/income/plans", "{\"effectiveFrom\":\"2026-13\",\"baseSalary\":1000}"));
        Assert.Equal("Start must be a month like 2026-11.",
            await RawBad(api, HttpMethod.Post, "/api/recurring", "{\"description\":\"Rent\",\"amount\":5,\"start\":202611}"));
        Assert.Equal("The request isn't valid JSON.", await RawBad(api, HttpMethod.Post, "/api/expenses", "{\"date\":"));
        Assert.Equal("The request body is missing.", await RawBad(api, HttpMethod.Post, "/api/expenses", ""));
        Assert.Equal("The request body is missing.", await RawBad(api, HttpMethod.Post, "/api/expenses", "null"));
        Assert.Equal("The request body has the wrong shape.", await RawBad(api, HttpMethod.Post, "/api/expenses", "[1,2]"));

        // Attribute checks read like the rest of the app, and the ProblemDetails shape is kept for tools.
        var (status, body) = await Raw(api, HttpMethod.Post, "/api/expenses", $"{{\"date\":\"2026-11-05\",\"amount\":5,\"categoryId\":{food},\"status\":\"Done\"}}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Status must be Paid or Planned.", body!["error"]!.GetValue<string>());
        Assert.Equal(400, body["status"]!.GetValue<int>());
        Assert.NotNull(body["errors"]!["Status"]);
    }

    [Fact]
    public async Task NoInputPath_AnswersWithA500()
    {
        var api = await _fx.NewUserAsync();
        var entry = await api.Expense("Food", 500m, "2026-11-04");
        var id = entry["id"]!.GetValue<int>();
        var category = api.Categories["Food"];
        var account = (await api.Get("/api/accounts")).AsArray()[0]!["id"]!.GetValue<int>();
        var item = await api.Post("/api/recurring", new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 100m, dayOfMonth = 1, start = "2026-11" });
        var occurrence = (await api.Get("/api/recurring/2026-11"))["occurrences"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var receipt = (await api.Post("/api/income/receipts", new { kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m }))!["id"]!.GetValue<int>();
        var column = (await api.Post("/api/columns", new { name = "Shop", type = "text" }))!["id"]!.GetValue<int>();

        var endpoints = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, "/api/expenses"), (HttpMethod.Put, $"/api/expenses/{id}"), (HttpMethod.Patch, $"/api/expenses/{id}"),
            (HttpMethod.Post, $"/api/expenses/{id}/duplicate"), (HttpMethod.Post, $"/api/expenses/{id}/refunds"), (HttpMethod.Put, "/api/expenses/notes"),
            (HttpMethod.Post, "/api/columns"), (HttpMethod.Put, $"/api/columns/{column}"), (HttpMethod.Post, "/api/columns/check"),
            (HttpMethod.Put, "/api/columns/layout/expenses/2026-11"), (HttpMethod.Post, "/api/recurring"), (HttpMethod.Put, $"/api/recurring/{item!["id"]}?from=2026-11"),
            (HttpMethod.Put, $"/api/recurring/occurrences/{occurrence}"), (HttpMethod.Post, $"/api/recurring/occurrences/{occurrence}/pay"),
            (HttpMethod.Put, "/api/income/plans"), (HttpMethod.Put, "/api/income/statements/2026-10"), (HttpMethod.Post, "/api/income/statements/2026-10/payments"),
            (HttpMethod.Post, "/api/income/receipts"), (HttpMethod.Put, $"/api/income/receipts/{receipt}"), (HttpMethod.Put, "/api/months/2026-11/settings"),
            (HttpMethod.Post, "/api/months/2026-11/copy"), (HttpMethod.Put, $"/api/budgets/2026-11/{category}"), (HttpMethod.Post, "/api/categories"),
            (HttpMethod.Put, $"/api/categories/{category}"), (HttpMethod.Put, "/api/categories/order"), (HttpMethod.Post, "/api/accounts"),
            (HttpMethod.Put, $"/api/accounts/{account}"),
        };
        // Not JSON, or not an object: always a 400 with a message.
        foreach (var (method, url) in endpoints)
        foreach (var json in new[] { "{", "[]", "null", "\"text\"", "42", "" })
        {
            var (status, body) = await Raw(api, method, url, json);
            Assert.True(status == HttpStatusCode.BadRequest, $"{method} {url} with {json}: expected 400, got {(int)status} {body?.ToJsonString()}");
            Assert.False(string.IsNullOrWhiteSpace(body?["error"]?.GetValue<string>()), $"{method} {url} with {json}: no error message");
        }

        // Wrong types, huge numbers and nulls: never a server error. (Fields an endpoint doesn't have are ignored,
        // so a few of these are valid requests for some endpoints.)
        var junk = new[]
        {
            "{\"amount\":\"x\",\"date\":\"x\",\"version\":-1,\"month\":\"x\",\"from\":\"x\",\"effectiveFrom\":\"x\",\"funded\":\"x\",\"start\":\"x\",\"orderedIds\":\"x\"}",
            "{\"amount\":1e300,\"baseSalary\":1e300,\"reservedSavings\":1e300,\"openingBalance\":1e300,\"dayOfMonth\":99999999999,\"categoryId\":1e10}",
            "{\"amount\":79228162514264337593543950335,\"baseSalary\":-79228162514264337593543950335,\"reservedSavings\":1e20,\"openingBalance\":-1e20}",
            "{\"name\":null,\"description\":null,\"note\":null,\"text\":null,\"custom\":[],\"components\":{},\"allocations\":\"x\",\"columns\":[{}],\"options\":null}",
            "{\"name\":\"\\u0000\",\"description\":\"\\u0000\",\"custom\":{\"c0\":{\"nested\":true}},\"components\":[{\"kind\":\"Base\",\"amount\":1e20}],\"allocations\":[{}]}",
        };
        foreach (var (method, url) in endpoints)
        foreach (var json in junk)
        {
            var (status, body) = await Raw(api, method, url, json);
            Assert.True((int)status < 500, $"{method} {url} with {json}: got {(int)status} {body?.ToJsonString()}");
            if ((int)status >= 400)
                Assert.False(string.IsNullOrWhiteSpace(body?["error"]?.GetValue<string>()), $"{method} {url} with {json}: no error message");
        }

        // Empty objects reach the domain checks, which refuse them. (Some endpoints have nothing required: duplicating,
        // paying at the defaults, clearing a budget, saving the default layout or month settings, checking a blank formula.)
        var emptyIsFine = new[] { "/duplicate", "/pay", "/settings", "/check", "/layout/", "/budgets/" };
        foreach (var (method, url) in endpoints.Where(e => !emptyIsFine.Any(e.Url.Contains)))
        {
            var (status, body) = await Raw(api, method, url, "{}");
            Assert.True(status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, $"{method} {url} with {{}}: got {(int)status} {body?.ToJsonString()}");
        }
    }

    // ---------- Dates and months ----------

    [Fact]
    public async Task Dates_MustBeRealAndInRange_AndPaidMoneyCantBeInTheFuture()
    {
        var api = await _fx.NewUserAsync();

        Assert.Equal("Date must be between 2000 and 2100.", await BadPost(api, "/api/expenses", Entry(api, 10m, "1999-12-31")));
        Assert.Equal("Date must be between 2000 and 2100.", await BadPost(api, "/api/expenses", Entry(api, 10m, "2101-01-01", status: "Planned")));
        Assert.Contains("can't be dated after today", await BadPost(api, "/api/expenses", Entry(api, 10m, "2026-11-16")));
        await api.Post("/api/expenses", Entry(api, 10m, "2026-11-16", status: "Planned"));
        Assert.Equal("Date is required.", await BadPost(api, "/api/expenses", new { amount = 10m, kind = "Expense", categoryId = api.Categories["Food"] }));

        // Remarks used to accept any date, including a missing one (0001-01-01).
        Assert.Equal("Date is required.", await BadPut(api, "/api/expenses/notes", new { text = "kameti" }));
        Assert.Equal("Date must be between 2000 and 2100.", await BadPut(api, "/api/expenses/notes", new { date = "1990-01-01", text = "kameti" }));
        await api.Put("/api/expenses/notes", new { date = "2026-11-01", text = "kameti" });

        var food = await api.Expense("Food", 500m, "2026-11-04");
        Assert.Contains("before the original", await BadPost(api, $"/api/expenses/{food["id"]}/refunds", new { date = "2026-11-03", amount = 10m }));
        Assert.Contains("after today", await BadPost(api, $"/api/expenses/{food["id"]}/refunds", new { date = "2026-11-20", amount = 10m }));

        Assert.Contains("after today", await BadPost(api, "/api/income/receipts", new { kind = "Other", status = "Received", receivedDate = "2026-11-16", amount = 10m }));
        Assert.Equal("Received date must be between 2000 and 2100.",
            await BadPost(api, "/api/income/receipts", new { kind = "Other", status = "Received", receivedDate = "1999-01-01", amount = 10m }));
        Assert.Equal("Expected date must be between 2000 and 2100.",
            await BadPost(api, "/api/income/receipts", new { kind = "Other", status = "Expected", expectedDate = "2200-01-01", amount = 10m }));
        Assert.Equal("Enter the date the money was received.", await BadPost(api, "/api/income/receipts", new { kind = "Other", status = "Received", amount = 10m }));

        await api.Post("/api/recurring", new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 100m, dayOfMonth = 20, start = "2026-11" });
        var occurrence = (await api.Get("/api/recurring/2026-11"))["occurrences"]!.AsArray().Single()!;
        Assert.Equal("Payment date must be between 2000 and 2100.", await BadPost(api, $"/api/recurring/occurrences/{occurrence["id"]}/pay", new { date = "1990-11-20" }));
        Assert.Contains("future", await BadPost(api, $"/api/recurring/occurrences/{occurrence["id"]}/pay", new { date = "2026-11-20" }));
        Assert.Contains("same month", await BadPut(api, $"/api/recurring/occurrences/{occurrence["id"]}",
            new { version = occurrence["version"]!.GetValue<uint>(), amount = 100m, dueDate = "2026-12-01", note = "" }));
    }

    [Fact]
    public async Task MonthAndYearParameters_AreValidatedEverywhere()
    {
        var api = await _fx.NewUserAsync();

        foreach (var url in new[]
        {
            "/api/expenses?month=2026-13", "/api/expenses?month=abc", "/api/expenses", "/api/expenses/matrix/2026-00", "/api/summary/1999-12",
            "/api/summary/year/1999", "/api/summary/year/2101", "/api/income/2026-1", "/api/budgets/2101-01", "/api/recurring/26-11",
            "/api/months/2026-11-01/settings", "/api/columns/layout/expenses/2026-13", "/api/exports/month/2026-13",
            // Used to fail with a 500.
            "/api/exports/year/99999", "/api/exports/year/0", "/api/analytics/by-category?month=13&year=2026",
            "/api/analytics/budget-status?month=0&year=0", "/api/analytics/trend?months=100000",
            "/api/exports/month/2026-11?format=csv&part=everything", "/api/exports/month/2026-11?format=pdf",
        })
            await BadGet(api, url);

        Assert.Contains("between 2000 and 2100", await BadGet(api, "/api/summary/year/3000"));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Http.GetAsync("/api/columns/layout/bogus/2026-11")).StatusCode);

        // Ledger filters: unknown values would quietly show nothing, so they're refused.
        await BadGet(api, "/api/expenses?month=2026-11&status=Done");
        await BadGet(api, "/api/expenses?month=2026-11&kind=Income");
        await BadGet(api, "/api/expenses?month=2026-11&sort=price");
        await BadGet(api, "/api/expenses?month=2026-11&dir=up");
        await BadGet(api, "/api/expenses?month=2026-11&from=2026-11-10&to=2026-11-01");
        await BadGet(api, "/api/expenses?month=2026-11&from=yesterday");
        await BadGet(api, $"/api/expenses?month=2026-11&search={new string('a', 201)}");
        await api.Get("/api/expenses?month=2026-11&status=Paid&kind=Expense&sort=Amount&dir=DESC&from=2026-11-01&to=2026-11-30");

        await BadPut(api, "/api/recurring/1?from=next", new { description = "Rent", amount = 5m, start = "2026-11" });
        Assert.Equal("The month to copy from is required.", await BadPost(api, "/api/months/2026-11/copy", new { layout = true }));
        await BadPost(api, "/api/months/2026-11/copy", new { from = "2026-13" });
        Assert.Equal("Each allocation's month is required.", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m, allocations = new[] { new { amount = 10m } } }));
    }

    // ---------- References ----------

    [Fact]
    public async Task References_MustExist_BelongToTheUser_AndBeTheRightType()
    {
        var api = await _fx.NewUserAsync();
        var other = await _fx.NewUserAsync();
        var theirFood = other.Categories["Food"];
        var theirAccount = (await other.Get("/api/accounts")).AsArray()[0]!["id"]!.GetValue<int>();
        var myAccount = (await api.Get("/api/accounts")).AsArray()[0]!["id"]!.GetValue<int>();

        // Categories.
        var spending = "Choose a spending category.";
        Assert.Equal(spending, await BadPost(api, "/api/expenses", new { date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = theirFood }));
        Assert.Equal(spending, await BadPost(api, "/api/expenses", new { date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = 99_999_999 }));
        Assert.Equal(spending, await BadPost(api, "/api/expenses", Entry(api, 5m, category: "Freelance")));
        Assert.Equal("Choose a category.", await BadPost(api, "/api/expenses", new { date = "2026-11-05", amount = 5m, kind = "Expense" }));
        Assert.Equal("Choose an income category.", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", categoryId = api.Categories["Food"], status = "Received", receivedDate = "2026-11-03", amount = 10m }));
        Assert.Equal("Choose an income category.", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", categoryId = other.Categories["Freelance"], status = "Received", receivedDate = "2026-11-03", amount = 10m }));
        Assert.Equal("Choose a spending category.", await BadPost(api, "/api/recurring",
            new { kind = "Expense", description = "Rent", categoryId = theirFood, amount = 5m, dayOfMonth = 1, start = "2026-11" }));
        Assert.Equal("Choose an income category.", await BadPost(api, "/api/recurring",
            new { kind = "Income", description = "Rent in", categoryId = api.Categories["Food"], amount = 5m, dayOfMonth = 1, start = "2026-11" }));
        Assert.Equal(spending, await BadPut(api, $"/api/budgets/2026-11/{theirFood}", new { amount = 5m, scope = "month" }));
        Assert.Equal(spending, await BadPut(api, $"/api/budgets/2026-11/{api.Categories["Freelance"]}", new { amount = 5m, scope = "month" }));

        // Accounts and transfers.
        Assert.Equal("Choose one of your accounts.", await BadPost(api, "/api/expenses",
            new { date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = api.Categories["Food"], accountId = theirAccount }));
        Assert.Contains("your accounts", await BadPost(api, "/api/expenses",
            new { date = "2026-11-05", amount = 5m, kind = "Transfer", accountId = myAccount, transferAccountId = theirAccount }));
        Assert.Contains("two different accounts", await BadPost(api, "/api/expenses",
            new { date = "2026-11-05", amount = 5m, kind = "Transfer", accountId = myAccount, transferAccountId = myAccount }));
        Assert.Contains("Choose the account", await BadPost(api, "/api/expenses", new { date = "2026-11-05", amount = 5m, kind = "Transfer" }));

        // Refunds link to the user's own paid expense.
        var theirs = await other.Expense("Food", 500m, "2026-11-04");
        Assert.Contains("paid expense", await BadPost(api, $"/api/expenses/{theirs["id"]}/refunds", new { date = "2026-11-05", amount = 5m }));
        var planned = await api.Expense("Food", 500m, "2026-11-20", status: "Planned");
        Assert.Contains("paid expense", await BadPost(api, $"/api/expenses/{planned["id"]}/refunds", new { date = "2026-11-05", amount = 5m }));
        Assert.Contains("paid expense", await BadPost(api, "/api/expenses", new { date = "2026-11-05", amount = 5m, kind = "Refund", refundOfId = 99_999_999 }));

        // Other users' rows are simply not found.
        await other.Post("/api/recurring", new { kind = "Expense", description = "Rent", categoryId = other.Categories["Bill / Rent"], amount = 5m, dayOfMonth = 1, start = "2026-11" });
        var theirOccurrence = (await other.Get("/api/recurring/2026-11"))["occurrences"]!.AsArray().Single()!["id"];
        await api.Post($"/api/recurring/occurrences/{theirOccurrence}/pay", new { date = "2026-11-05" }, HttpStatusCode.NotFound);
        await api.Put($"/api/categories/{theirFood}", new { name = "Mine now", type = "Expense" }, HttpStatusCode.NotFound);
        await api.Put($"/api/accounts/{theirAccount}", new { name = "Mine now", type = "Cash", currency = "PKR" }, HttpStatusCode.NotFound);

        // Custom values only go into the user's own columns.
        var theirColumn = (await other.Post("/api/columns", new { name = "Shop", type = "text" }))!["key"]!.GetValue<string>();
        Assert.Contains("Unknown column", await BadPost(api, "/api/expenses",
            new { date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = api.Categories["Food"], custom = new Dictionary<string, object> { [theirColumn] = "Daraz" } }));
    }

    [Fact]
    public async Task ArchivedCategories_RefuseNewEntries_ButOldOnesStayEditable()
    {
        var api = await _fx.NewUserAsync();
        var old = await api.Expense("Food", 500m, "2026-11-02");
        await api.Post("/api/income/receipts", new { kind = "Other", categoryId = api.Categories["Gift"], status = "Received", receivedDate = "2026-11-03", amount = 10m });
        await Archive(api, "Food");
        await Archive(api, "Gift", "Income");

        Assert.Contains("archived", await BadPost(api, "/api/expenses", Entry(api, 5m)));
        Assert.Contains("archived", await BadPost(api, $"/api/expenses/{old["id"]}/duplicate", new { }));
        Assert.Contains("archived", await BadPost(api, "/api/recurring",
            new { kind = "Expense", description = "Lunch", categoryId = api.Categories["Food"], amount = 5m, dayOfMonth = 1, start = "2026-11" }));
        Assert.Contains("archived", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", categoryId = api.Categories["Gift"], status = "Received", receivedDate = "2026-11-03", amount = 10m }));

        // The old entry keeps its category and can still be corrected.
        var edited = await api.Patch($"/api/expenses/{old["id"]}", new { version = old["version"]!.GetValue<uint>(), amount = 450m, description = "corrected" });
        Assert.Equal(450m, D(edited!["amount"]));
        Assert.Equal(api.Categories["Food"], edited["categoryId"]!.GetValue<int>());
        // …but another entry can't be moved into it.
        var travel = await api.Expense("Groceries", 100m, "2026-11-03");
        Assert.Contains("archived", await Rejected(api.Patch($"/api/expenses/{travel["id"]}",
            new { version = travel["version"]!.GetValue<uint>(), categoryId = api.Categories["Food"] }, HttpStatusCode.BadRequest)));

        const string csv = "Date,Description,Amount,Category,Account\n2026-11-03,Lunch,750,Food,Cash\n";
        var result = await api.Upload("/api/expenses/import/csv", "transactions.csv", csv);
        Assert.Equal(0, result["importedSuccessfully"]!.GetValue<int>());
        Assert.Contains("archived", result["errors"]![0]!["reason"]!.GetValue<string>());
    }

    // ---------- Text ----------

    [Fact]
    public async Task Text_IsCleanedTrimmedAndLimited()
    {
        var api = await _fx.NewUserAsync();

        Assert.Equal("Description can be at most 500 characters.", await BadPost(api, "/api/expenses", Entry(api, 5m, description: new string('x', 501))));
        var saved = (await api.Post("/api/expenses", Entry(api, 5m, description: "  Lunch\u0000 at\tthe\ncafe\u001b  ")))!;
        Assert.Equal("Lunch  at the cafe", saved["description"]!.GetValue<string>());

        Assert.Equal("Remarks can be at most 1000 characters.", await BadPut(api, "/api/expenses/notes", new { date = "2026-11-01", text = new string('x', 1001) }));
        Assert.Equal("Notes can be at most 1000 characters.", await BadPut(api, "/api/months/2026-11/settings", new { note = new string('x', 1001) }));
        Assert.Equal("Description can be at most 500 characters.", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m, description = new string('x', 501) }));
        Assert.Equal("Notes can be at most 200 characters.", await BadPut(api, "/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 5m, note = new string('x', 201) }));
        Assert.Equal("Labels can be at most 100 characters.", await BadPut(api, "/api/income/statements/2026-10",
            new { funded = "2026-11", components = new[] { new { kind = "Base", label = new string('x', 101), amount = 5m } } }));

        // Names that are only spaces or control characters used to be saved as empty names.
        Assert.Equal("Give the category a name.", await BadPost(api, "/api/categories", new { name = "   ", type = "Expense" }));
        Assert.Equal("Name is required.", await BadPost(api, "/api/categories", new { name = "\u0007\u0000", type = "Expense" }));
        Assert.Equal("Category names can be at most 100 characters.", await BadPost(api, "/api/categories", new { name = new string('x', 101), type = "Expense" }));
        Assert.Equal("Give it a name, e.g. Rent.", await BadPost(api, "/api/recurring",
            new { kind = "Expense", description = "   ", categoryId = api.Categories["Food"], amount = 5m, dayOfMonth = 1, start = "2026-11" }));
        Assert.Equal("Give the column a name.", await BadPost(api, "/api/columns", new { name = " ", type = "text" }));
        Assert.Equal("Give the account a name.", await BadPost(api, "/api/accounts", new { name = "  ", type = "Cash", currency = "PKR" }));

        var trimmed = await Category(api, "  Pets\u0007 ");
        Assert.Equal("Pets", trimmed["name"]!.GetValue<string>());
        var account = (await api.Post("/api/accounts", new { name = " Meezan Bank ", type = "Checking", currency = "pkr" }))!;
        Assert.Equal("Meezan Bank", account["name"]!.GetValue<string>());
        Assert.Equal("PKR", account["currency"]!.GetValue<string>());
        Assert.Contains("Currency must be one of", await BadPost(api, "/api/accounts", new { name = "Wise", type = "Checking", currency = "XYZ" }));
    }

    [Fact]
    public async Task Names_AreUniquePerUser_IgnoringCase()
    {
        var api = await _fx.NewUserAsync();
        var other = await _fx.NewUserAsync();

        Assert.Equal("There's already a category called “food”.", await BadPost(api, "/api/categories", new { name = "food", type = "Expense" }));
        Assert.Contains("already a category", await BadPost(api, "/api/categories", new { name = "FREELANCE", type = "Expense" }));
        Assert.Contains("already a category", await BadPut(api, $"/api/categories/{api.Categories["Food"]}", new { name = "groceries", type = "Expense" }));
        await api.Put($"/api/categories/{api.Categories["Food"]}", new { name = "FOOD", type = "Expense" });
        await Category(api, "Pets");
        await Category(other, "pets");

        await api.Post("/api/columns", new { name = "Shop", type = "text" });
        Assert.Contains("already a column", await BadPost(api, "/api/columns", new { name = "shop", type = "text" }));
        Assert.Contains("already a column", await BadPost(api, "/api/columns", new { name = "amount", type = "number" }));
        await other.Post("/api/columns", new { name = "Shop", type = "text" });

        await api.Post("/api/accounts", new { name = "Meezan", type = "Checking", currency = "PKR" });
        Assert.Contains("already an account", await BadPost(api, "/api/accounts", new { name = "MEEZAN", type = "Savings", currency = "PKR" }));
        await other.Post("/api/accounts", new { name = "Meezan", type = "Checking", currency = "PKR" });

        // A category with income can't silently turn into a spending one.
        await api.Post("/api/income/receipts", new { kind = "Other", categoryId = api.Categories["Gift"], status = "Received", receivedDate = "2026-11-03", amount = 10m });
        Assert.Contains("Can't switch", await BadPut(api, $"/api/categories/{api.Categories["Gift"]}", new { name = "Gift", type = "Expense" }));
    }

    // ---------- Custom columns ----------

    [Fact]
    public async Task CustomColumns_CheckOptionsValuesAndTypeChanges()
    {
        var api = await _fx.NewUserAsync();

        Assert.Equal("A dropdown needs at least one option.", await BadPost(api, "/api/columns", new { name = "Shop", type = "dropdown", options = new[] { " ", "" } }));
        Assert.Contains("listed twice", await BadPost(api, "/api/columns", new { name = "Shop", type = "dropdown", options = new[] { "Daraz", "daraz " } }));
        Assert.Contains("at most 60 characters", await BadPost(api, "/api/columns", new { name = "Shop", type = "dropdown", options = new[] { new string('x', 61) } }));
        Assert.Contains("can't contain [", await BadPost(api, "/api/columns", new { name = "Tip [PKR]", type = "money" }));
        Assert.Equal("Decimals must be between 0 and 6.", await BadPost(api, "/api/columns", new { name = "Qty", type = "number", decimals = 9 }));
        Assert.Contains("Column type must be", await BadPost(api, "/api/columns", new { name = "Qty", type = "integer" }));

        var shop = (await api.Post("/api/columns", new { name = "Shop", type = "dropdown", options = new[] { "Daraz", "Imtiaz" } }))!["key"]!.GetValue<string>();
        var tip = (await api.Post("/api/columns", new { name = "Tip", type = "money" }))!["key"]!.GetValue<string>();
        var qty = (await api.Post("/api/columns", new { name = "Qty", type = "number" }))!["key"]!.GetValue<string>();
        var when = (await api.Post("/api/columns", new { name = "Warranty", type = "date" }))!["key"]!.GetValue<string>();
        var done = (await api.Post("/api/columns", new { name = "Done", type = "checkbox" }))!["key"]!.GetValue<string>();
        var note = (await api.Post("/api/columns", new { name = "Ref", type = "text" }));

        async Task<string> BadValue(string key, object value) => await BadPost(api, "/api/expenses",
            new { date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = api.Categories["Food"], custom = new Dictionary<string, object> { [key] = value } });

        Assert.Contains("isn't one of the options", await BadValue(shop, "Hyperstar"));
        Assert.Contains("at most 2 decimals", await BadValue(tip, 1.234m));
        Assert.Contains("too large", await BadValue(tip, 5_000_000_000m));
        Assert.Contains("needs a number", await BadValue(qty, "lots"));
        Assert.Contains("needs a date", await BadValue(when, "2026-02-30"));
        Assert.Contains("between 1900 and 2100", await BadValue(when, "1850-01-01"));
        Assert.Contains("checkbox", await BadValue(done, "maybe"));
        Assert.Contains("limited to 500", await BadValue(note!["key"]!.GetValue<string>(), new string('x', 501)));

        var row = (await api.Post("/api/expenses", new
        {
            date = "2026-11-05", amount = 5m, kind = "Expense", categoryId = api.Categories["Food"],
            custom = new Dictionary<string, object> { [shop] = "daraz", [qty] = "1,200", [when] = "1990-05-01", [note["key"]!.GetValue<string>()] = "A-12\nB" }
        }))!;
        Assert.Equal("Daraz", row["custom"]![shop]!.GetValue<string>());
        Assert.Equal(1200m, D(row["custom"]![qty]));
        Assert.Equal("A-12 B", row["custom"]![note["key"]!.GetValue<string>()]!.GetValue<string>());

        // Saved values must still fit after a type change.
        var noteId = note["id"]!.GetValue<int>();
        Assert.Contains("can't become number", await BadPut(api, $"/api/columns/{noteId}", new { name = "Ref", type = "number", version = note["version"]!.GetValue<uint>() }));
        var qtyColumn = (await api.Get("/api/columns")).AsArray().Single(c => c!["key"]!.GetValue<string>() == qty)!;
        await api.Put($"/api/columns/{qtyColumn["id"]}", new { name = "Qty", type = "text", version = qtyColumn["version"]!.GetValue<uint>() });

        // Formulas are checked by the formula engine.
        Assert.StartsWith("#", await BadPost(api, "/api/columns", new { name = "Broken", type = "formula", formula = "[Amount] *" }));
        Assert.Contains("[#nowhere]", await BadPost(api, "/api/columns", new { name = "Missing", type = "formula", formula = "[#nowhere] + 1" }));
        Assert.Equal("Enter a formula, e.g. [Amount] * 0.1", await BadPost(api, "/api/columns", new { name = "Empty", type = "formula", formula = " " }));
    }

    // ---------- Recurring, salary and income rules ----------

    [Fact]
    public async Task RecurringSalaryAndIncome_FieldsMustBeConsistent()
    {
        var api = await _fx.NewUserAsync();
        object Item(string start, string? end = null, int day = 1) => new
        {
            kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 5m, dayOfMonth = day, start, end
        };

        Assert.Equal("Start month is required.", await BadPost(api, "/api/recurring",
            new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 5m, dayOfMonth = 1 }));
        Assert.Contains("before the start month", await BadPost(api, "/api/recurring", Item("2026-11", "2026-10")));
        Assert.Equal("Day of month must be between 1 and 31.", await BadPost(api, "/api/recurring", Item("2026-11", day: 0)));
        Assert.Equal("Day of month must be between 1 and 31.", await BadPost(api, "/api/recurring", Item("2026-11", day: 32)));
        Assert.Contains("Payment method must be one of", await BadPost(api, "/api/recurring",
            new { kind = "Expense", description = "Rent", categoryId = api.Categories["Bill / Rent"], amount = 5m, dayOfMonth = 1, start = "2026-11", paymentMethod = "Gold" }));

        Assert.Equal("The month the plan starts is required.", await BadPut(api, "/api/income/plans", new { baseSalary = 5m }));
        Assert.Contains("before it was earned", await BadPut(api, "/api/income/statements/2026-10", new { funded = "2026-09", components = Array.Empty<object>() }));
        Assert.Contains("at most 12 months", await BadPut(api, "/api/income/statements/2026-10", new { funded = "2028-01", components = Array.Empty<object>() }));
        Assert.Equal("The month it funds is required.", await BadPut(api, "/api/income/statements/2026-10", new { components = Array.Empty<object>() }));
        Assert.Contains("Deductions can't be more than the salary", await BadPut(api, "/api/income/statements/2026-10", new
        {
            funded = "2026-11",
            components = new[] { new { kind = "Base", label = "Base", amount = 1_000m }, new { kind = "Deduction", label = "Tax", amount = 1_500m } }
        }));
        Assert.Contains("use Adjustment", await BadPut(api, "/api/income/statements/2026-10",
            new { funded = "2026-11", components = new[] { new { kind = "Bonus", label = "", amount = -5m } } }));
        await api.Put("/api/income/statements/2026-10", new { funded = "2026-11", components = new[] { new { kind = "Base", label = "Base", amount = 1_000m }, new { kind = "Adjustment", label = "Fix", amount = -200m } } });

        Assert.Contains("Payment method must be one of", await BadPost(api, "/api/income/receipts",
            new { kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m, paymentMethod = "Gold" }));
        Assert.Equal("Allocate to each month once.", await BadPost(api, "/api/income/receipts", new
        {
            kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m,
            allocations = new[] { new { month = "2026-11", amount = 5m }, new { month = "2026-11", amount = 5m } }
        }));
        Assert.Contains("can't exceed", await BadPost(api, "/api/income/receipts", new
        {
            kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 10m, allocations = new[] { new { month = "2026-11", amount = 15m } }
        }));
        Assert.Equal("Split across at most 24 months.", await BadPost(api, "/api/income/receipts", new
        {
            kind = "Other", status = "Received", receivedDate = "2026-11-03", amount = 100m,
            allocations = Enumerable.Range(0, 25).Select(i => new { month = $"{2025 + i / 12}-{i % 12 + 1:D2}", amount = 1m }).ToArray()
        }));
        Assert.Equal("Enter the date the money was received.", await BadPost(api, "/api/income/statements/2026-10/payments",
            new { status = "Expected", expectedDate = "2026-11-20", amount = 10m }));
    }

    // ---------- Imports ----------

    [Fact]
    public async Task Imports_RefuseWrongFilesAndOversizeUploads_AndReportBadRows()
    {
        var api = await _fx.NewUserAsync();
        foreach (var url in new[] { "/api/expenses/import/csv", "/api/expenses/import/sheet" })
        {
            var (status, body) = await Upload(api, url, "Budget.xlsx", "PK\u0003\u0004"u8.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("Upload a .csv file", body!["error"]!.GetValue<string>());

            (status, body) = await Upload(api, url, "renamed.csv", new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x00, 0x00, 0x08 }, "text/csv");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("isn't a CSV", body!["error"]!.GetValue<string>());

            (status, body) = await Upload(api, url, "empty.csv", Array.Empty<byte>(), "text/csv");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("Choose a .csv file", body!["error"]!.GetValue<string>());

            // Over 5 MB: refused with a message, never a 500.
            var big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("2026-11-03,Lunch,750,Food,Cash\n", 200_000)));
            (status, body) = await Upload(api, url, "huge.csv", big, "text/csv");
            Assert.True(status is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"{url}: got {(int)status}");
            Assert.Contains("too large", body!["error"]!.GetValue<string>());

            var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };
            var missing = await api.Http.PostAsync(url, form);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }

        var (headerStatus, headerBody) = await Upload(api, "/api/expenses/import/csv", "t.csv", "When,What,How much\n2026-11-03,Lunch,750\n"u8.ToArray(), "text/csv");
        Assert.Equal(HttpStatusCode.BadRequest, headerStatus);
        Assert.Contains("missing: Date, Amount, Category, Account", headerBody!["error"]!.GetValue<string>());

        var csv =
            "date,description,amount,category,account\n" +
            "2026-11-03,Lunch,750,Food,Cash\n" +
            "2026-11-20,Dinner,500,Food,Cash\n" +
            "1999-05-01,Old,500,Food,Cash\n" +
            "2026-11-04,Free,0,Food,Cash\n" +
            "2026-11-04,Precise,10.555,Food,Cash\n" +
            $"2026-11-04,{new string('x', 501)},10,Food,Cash\n" +
            "someday,Chips,120,Food,Cash\n" +
            "2026-11-05,Bus,abc,Food,Cash\n";
        var result = await api.Upload("/api/expenses/import/csv", "transactions.csv", csv);
        Assert.Equal(8, result["totalRows"]!.GetValue<int>());
        Assert.Equal(1, result["importedSuccessfully"]!.GetValue<int>());
        var reasons = result["errors"]!.AsArray().ToDictionary(e => e!["row"]!.GetValue<int>(), e => e!["reason"]!.GetValue<string>());
        Assert.Contains("after today", reasons[2]);
        Assert.Contains("between 2000 and 2100", reasons[3]);
        Assert.Contains("greater than zero", reasons[4]);
        Assert.Contains("2 decimals", reasons[5]);
        Assert.Contains("at most 500", reasons[6]);
        Assert.Contains("Date “someday” isn't valid", reasons[7]);
        Assert.Contains("Amount “abc” isn't valid", reasons[8]);
        Assert.Single((await api.Get("/api/expenses?month=2026-11"))["rows"]!.AsArray());

        // The Daily Log: future days and impossible amounts are skipped with a note, the rest is imported.
        const string sheet =
            "Date,Day,Month,Food,Other Expenses,Daily Total,Expense details (auto),Remarks\n" +
            "01 Nov 2026,Sun,Nov 2026,625,1000000000000,,,ok\n" +
            "02 Nov 2026,Mon,Nov 2026,12.345,,,,\n" +
            "20 Nov 2026,Fri,Nov 2026,300,,,,dinner plan\n";
        var imported = await api.Upload("/api/expenses/import/sheet", "Daily Log.csv", sheet);
        Assert.Equal(1, imported["entriesCreated"]!.GetValue<int>());
        Assert.Equal(3, imported["cellsSkipped"]!.GetValue<int>());
        var warnings = imported["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
        Assert.Contains(warnings, w => w.Contains("20 Nov 2026 is after today"));
        Assert.Contains(warnings, w => w.Contains("couldn't read \"12.345\""));
        Assert.Equal(2, imported["notesImported"]!.GetValue<int>());
    }
}
