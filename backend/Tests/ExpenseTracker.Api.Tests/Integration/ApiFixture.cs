using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExpenseTracker.Api.Ledger;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace ExpenseTracker.Api.Tests.Integration;

public sealed class FakeClock : IClock
{
    // 15 November 2026, 12:00 in Karachi.
    public DateTime UtcNow { get; set; } = new(2026, 11, 15, 7, 0, 0, DateTimeKind.Utc);
}

// One Postgres container and one app for the whole integration suite; every test signs up its own user.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeClock Clock { get; } = new();
    public string ConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // Every test signs up from the same (test server) address, so sign-in limits must not get in the way.
        Factory = CreateFactory(authPerMinute: 100_000);
        _ = Factory.CreateClient();
    }

    // Another app over the same database, for tests that need different settings. The caller disposes it.
    public WebApplicationFactory<Program> CreateFactory(int authPerMinute, string? webRoot = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            if (webRoot != null) b.UseSetting(WebHostDefaults.WebRootKey, webRoot);
            b.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
            b.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789abcdefghij");
            b.UseSetting("BackgroundJobs:Enabled", "false");
            b.UseSetting("RateLimiting:AuthPerMinute", authPerMinute.ToString());
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(Clock);
            });
        });

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public async Task<Api> NewUserAsync()
    {
        var client = Factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new { email = $"u{Guid.NewGuid():N}@example.com", password = "secret123" });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var api = new Api(client);
        api.Me = await api.Get("/api/me");
        api.Categories = (await api.Get("/api/categories")).AsArray()
            .ToDictionary(c => c!["name"]!.GetValue<string>(), c => c!["id"]!.GetValue<int>());
        return api;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
}

public sealed class Api
{
    public HttpClient Http { get; }
    public JsonNode Me { get; set; } = null!;
    public Dictionary<string, int> Categories { get; set; } = new();

    public Api(HttpClient http) => Http = http;

    public async Task<JsonNode> Get(string url)
    {
        var res = await Http.GetAsync(url);
        await Ensure(res, url);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    }

    public async Task<JsonNode?> Send(HttpMethod method, string url, object? body = null, HttpStatusCode? expect = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body);
        var res = await Http.SendAsync(req);
        if (expect is HttpStatusCode code)
            Assert.True(res.StatusCode == code, $"{method} {url}: expected {(int)code}, got {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        else
            await Ensure(res, url);
        var text = await res.Content.ReadAsStringAsync();
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    public Task<JsonNode?> Post(string url, object body, HttpStatusCode? expect = null) => Send(HttpMethod.Post, url, body, expect);
    public Task<JsonNode?> Put(string url, object body, HttpStatusCode? expect = null) => Send(HttpMethod.Put, url, body, expect);
    public Task<JsonNode?> Patch(string url, object body, HttpStatusCode? expect = null) => Send(HttpMethod.Patch, url, body, expect);
    public Task<JsonNode?> Delete(string url, HttpStatusCode? expect = null) => Send(HttpMethod.Delete, url, null, expect);

    // Posts a file as the multipart "file" field, the way the import screens do.
    public async Task<JsonNode> Upload(string url, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(content), "file", fileName);
        var res = await Http.PostAsync(url, form);
        await Ensure(res, url);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    }

    private static async Task Ensure(HttpResponseMessage res, string url)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{url} → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }

    public async Task<JsonNode> Expense(string category, decimal amount, string date, bool additional = false, string status = "Paid", Guid? requestId = null) =>
        (await Post("/api/expenses", new
        {
            clientRequestId = requestId ?? Guid.NewGuid(),
            date,
            amount,
            kind = "Expense",
            status,
            categoryId = Categories[category],
            isAdditional = additional
        }))!;

    public async Task<JsonNode> Summary(string month) => (await Get($"/api/summary/{month}"))["figures"]!;

    public static decimal D(JsonNode? node) => node!.GetValue<decimal>();
}
