using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Services;
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

// Keeps what the app would have emailed. Emails leave through a background queue, so tests wait for them.
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool CanDeliver => true;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public List<EmailMessage> To(string address) => _sent.Where(m => m.To == address).ToList();

    // The `count`th email to `to` whose subject contains `subject`.
    public async Task<EmailMessage> WaitForAsync(string to, string subject, int count = 1)
    {
        for (var i = 0; i < 250; i++)
        {
            var found = To(to).Where(m => m.Subject.Contains(subject)).ToList();
            if (found.Count >= count) return found[count - 1];
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"No email \"{subject}\" (#{count}) reached {to}");
    }

    public static string LinkIn(EmailMessage message) => Regex.Match(message.Text, @"https?://\S+").Value;

    public static string TokenIn(EmailMessage message) =>
        Uri.UnescapeDataString(Regex.Match(message.Text, @"[?&]token=([^\s&]+)").Groups[1].Value);
}

public sealed class FakeBreaches : IBreachedPasswordChecker
{
    // Passes every other rule, so only the breach check can refuse it.
    public const string Leaked = "Leaked-Pa55word-2019";

    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default) => Task.FromResult(password == Leaked);
}

// One Postgres container and one app for the whole integration suite; every test signs up its own user.
public sealed class ApiFixture : IAsyncLifetime
{
    // Passes the password rules (8+ characters, no spaces, not common or breached).
    public const string Password = "Tr4ck-my-rupees";
    public const string PublicUrl = "https://expense-tracker.test";
    public const string JwtKey = "integration-test-signing-key-0123456789abcdefghij";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeClock Clock { get; } = new();
    public FakeEmailSender Emails { get; } = new();
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
            b.UseSetting("Jwt:Key", JwtKey);
            b.UseSetting("BackgroundJobs:Enabled", "false");
            b.UseSetting("RateLimiting:AuthPerMinute", authPerMinute.ToString());
            b.UseSetting("App:PublicUrl", PublicUrl);
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(Clock);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
                services.RemoveAll<IBreachedPasswordChecker>();
                services.AddSingleton<IBreachedPasswordChecker, FakeBreaches>();
            });
        });

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public async Task<Api> NewUserAsync(string? displayName = null)
    {
        var client = Factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName = displayName ?? "" });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var api = new Api(client) { Email = email };
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
    public string Email { get; set; } = string.Empty;
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
