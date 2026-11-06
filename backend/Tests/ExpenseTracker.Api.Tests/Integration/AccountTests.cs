using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ExpenseTracker.Api.Data;
using static ExpenseTracker.Api.Tests.Integration.FakeEmailSender;

namespace ExpenseTracker.Api.Tests.Integration;

// Sign-up rules, emailed links, sessions, lockout and the profile endpoints.
[Collection("api")]
public class AccountTests
{
    private const string TooCommon = "This password is too common — pick something less guessable.";
    private const string LinkExpired = "This link has expired or was already used. Ask for a new one.";
    private const string WrongCurrent = "Your current password isn't right.";
    private const string LockedOut = "Too many failed attempts. Try again in 15 minutes or use Forgot password.";

    private readonly ApiFixture _fx;

    public AccountTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task NewPasswords_FollowThePolicy_ButSignInStillAcceptsOlderPasswords()
    {
        var email = NewEmail();
        async Task<string> Refused(string password) =>
            await ErrorOf(await Post("/api/auth/register", new { email, password }), HttpStatusCode.BadRequest);

        Assert.Equal("Use at least 8 characters.", await Refused("Ab1-xyz"));
        Assert.Equal("Spaces aren't allowed in passwords.", await Refused("Tr4ck my rupees"));
        Assert.Equal("Spaces aren't allowed in passwords.", await Refused("Tr4ck-my-rupees\t"));
        foreach (var common in new[]
                 {
                     "password", "12345678", "qwertyui", "iloveyou", "pakistan", "11111111", "PASSWORD", "P@ssw0rd", "Pakistan@123",
                     "monkey!!", "abcdefgh", "87654321", "1qaz2wsx", "abcabcabc", "my-expense-log"
                 })
            Assert.Equal(TooCommon, await Refused(common));
        Assert.Equal("Don't use your email name in your password.", await Refused(email.Split('@')[0] + "-x"));
        Assert.Equal("This password has appeared in a data breach. Choose a different one.", await Refused(FakeBreaches.Leaked));

        Assert.Equal("Enter a valid email address, like name@example.com.",
            await ErrorOf(await Post("/api/auth/register", new { email = "not-an-email", password = ApiFixture.Password }), HttpStatusCode.BadRequest));
        Assert.Equal("Keep your name to 100 characters or fewer.",
            await ErrorOf(await Post("/api/auth/register", new { email, password = ApiFixture.Password, displayName = new string('a', 101) }), HttpStatusCode.BadRequest));

        // Accepted, with the address stored trimmed and lower-case.
        var created = await Post("/api/auth/register", new { email = $"  {email.ToUpperInvariant()} ", password = "Kitten-Biryani-42", displayName = "  Ayesha " });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(email, (await created.Content.ReadFromJsonAsync<JsonObject>())!["email"]!.GetValue<string>());
        Assert.Equal("That email is already registered.",
            await ErrorOf(await Post("/api/auth/register", new { email, password = ApiFixture.Password }), HttpStatusCode.Conflict));

        // Sign-in never applies the rules: an account from before them, with a short password, still gets in.
        await WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Equal("Ayesha", user.DisplayName);
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret");
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Login(email, "secret")).StatusCode);
        Assert.True((await Client().GetFromJsonAsync<JsonObject>("/api/auth/options"))!["emailEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ForgotPassword_AnswersTheSameForUnknownEmails_AndOnlyMailsTheRegisteredAddress()
    {
        var api = await _fx.NewUserAsync();
        var unknown = NewEmail();

        var forUnknown = await Post("/api/auth/forgot-password", new { email = unknown });
        var forKnown = await Post("/api/auth/forgot-password", new { email = $" {api.Email.ToUpperInvariant()}" });
        Assert.Equal(HttpStatusCode.OK, forUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forKnown.StatusCode);
        var answer = await forUnknown.Content.ReadAsStringAsync();
        Assert.Equal(answer, await forKnown.Content.ReadAsStringAsync());
        Assert.Contains("If that email has an account, a reset link is on its way.", answer);

        var mail = await _fx.Emails.WaitForAsync(api.Email, "Reset your");
        // Emails go out first in, first out: one for the unknown address would have arrived before this one.
        Assert.Empty(_fx.Emails.To(unknown));
        Assert.StartsWith($"{ApiFixture.PublicUrl}/reset-password?token=", LinkIn(mail));
        Assert.Contains(LinkIn(mail), mail.Html);
        Assert.Contains("expires in 30 minutes", mail.Text);
        Assert.Contains("If you didn't ask for this, ignore this email.", mail.Text);
    }

    [Fact]
    public async Task ResetPassword_SetsANewPassword_SignsOutEveryDevice_AndTheLinkWorksOnce()
    {
        var api = await _fx.NewUserAsync();
        await Post("/api/auth/forgot-password", new { email = api.Email });
        var token = TokenIn(await _fx.Emails.WaitForAsync(api.Email, "Reset your"));

        // A refused password doesn't use up the link.
        Assert.Equal(TooCommon, await ErrorOf(await Post("/api/auth/reset-password", new { token, newPassword = "password1" }), HttpStatusCode.BadRequest));
        var fresh = await TokenOf(await Post("/api/auth/reset-password", new { token, newPassword = "Fresh-Start-2026" }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.GetAsync("/api/me")).StatusCode);
        var me = await Client(fresh).GetFromJsonAsync<JsonObject>("/api/me");
        Assert.Equal(api.Me["id"]!.GetValue<int>(), me!["id"]!.GetValue<int>());
        // Opening the link proved the inbox is theirs.
        Assert.True(me["emailVerified"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(api.Email, ApiFixture.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, "Fresh-Start-2026")).StatusCode);
        Assert.Equal(LinkExpired, await ErrorOf(await Post("/api/auth/reset-password", new { token, newPassword = "Other-Start-2026" }), HttpStatusCode.BadRequest));
        await _fx.Emails.WaitForAsync(api.Email, "password was changed");
    }

    [Fact]
    public async Task ResetLinks_ExpireAfter30Minutes_AreSentAtMostOnceAMinute_AndANewLinkReplacesTheOld()
    {
        var api = await _fx.NewUserAsync();
        var start = _fx.Clock.UtcNow;
        try
        {
            await Post("/api/auth/forgot-password", new { email = api.Email });
            var first = TokenIn(await _fx.Emails.WaitForAsync(api.Email, "Reset your"));

            // Asking again within the minute gets the same answer but sends nothing. The sign-in link queued after it
            // arriving shows nothing else is on its way.
            _fx.Clock.UtcNow = start.AddSeconds(30);
            Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/forgot-password", new { email = api.Email })).StatusCode);
            await Post("/api/auth/email-link", new { email = api.Email });
            await _fx.Emails.WaitForAsync(api.Email, "sign-in link");
            Assert.Single(_fx.Emails.To(api.Email), m => m.Subject.Contains("Reset your"));

            _fx.Clock.UtcNow = start.AddMinutes(2);
            await Post("/api/auth/forgot-password", new { email = api.Email });
            var second = TokenIn(await _fx.Emails.WaitForAsync(api.Email, "Reset your", count: 2));
            Assert.Equal(LinkExpired, await ErrorOf(await Post("/api/auth/reset-password", new { token = first, newPassword = "Fresh-Start-2026" }), HttpStatusCode.BadRequest));

            _fx.Clock.UtcNow = start.AddMinutes(2 + 31);
            Assert.Equal(LinkExpired, await ErrorOf(await Post("/api/auth/reset-password", new { token = second, newPassword = "Fresh-Start-2026" }), HttpStatusCode.BadRequest));
        }
        finally
        {
            _fx.Clock.UtcNow = start;
        }
        Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, ApiFixture.Password)).StatusCode);
    }

    [Fact]
    public async Task SignInLink_SignsInOnce_AndConfirmsTheEmail()
    {
        var api = await _fx.NewUserAsync();
        var forUnknown = await Post("/api/auth/email-link", new { email = NewEmail() });
        var forKnown = await Post("/api/auth/email-link", new { email = api.Email });
        Assert.Equal(await forUnknown.Content.ReadAsStringAsync(), await forKnown.Content.ReadAsStringAsync());

        var mail = await _fx.Emails.WaitForAsync(api.Email, "sign-in link");
        Assert.StartsWith($"{ApiFixture.PublicUrl}/sign-in-link?token=", LinkIn(mail));
        Assert.Contains("expires in 15 minutes", mail.Text);
        var token = TokenIn(mail);

        var signedIn = await TokenOf(await Post("/api/auth/redeem-link", new { token }));
        var me = await Client(signedIn).GetFromJsonAsync<JsonObject>("/api/me");
        Assert.Equal(api.Me["id"]!.GetValue<int>(), me!["id"]!.GetValue<int>());
        Assert.True(me["emailVerified"]!.GetValue<bool>());

        Assert.Equal(LinkExpired, await ErrorOf(await Post("/api/auth/redeem-link", new { token }), HttpStatusCode.BadRequest));
        Assert.Equal(LinkExpired, await ErrorOf(await Post("/api/auth/redeem-link", new { token = "made-up" }), HttpStatusCode.BadRequest));
        // Signing in by link doesn't sign anyone else out.
        Assert.Equal(HttpStatusCode.OK, (await api.Http.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task VerificationEmail_IsSentOnSignUp_AndCanBeResent()
    {
        var api = await _fx.NewUserAsync(displayName: "Bilal <b>");
        Assert.False(api.Me["emailVerified"]!.GetValue<bool>());
        var mail = await _fx.Emails.WaitForAsync(api.Email, "Confirm your email");
        Assert.StartsWith($"{ApiFixture.PublicUrl}/verify-email?token=", LinkIn(mail));
        Assert.Contains("Hi Bilal &lt;b&gt;,", mail.Html);
        Assert.Contains("Hi Bilal <b>,", mail.Text);

        var start = _fx.Clock.UtcNow;
        try
        {
            await api.Post("/api/me/verify-email/resend", new { }, HttpStatusCode.TooManyRequests);
            _fx.Clock.UtcNow = start.AddMinutes(2);
            await api.Post("/api/me/verify-email/resend", new { });
            var resent = TokenIn(await _fx.Emails.WaitForAsync(api.Email, "Confirm your email", count: 2));

            // The newer link replaced the first; it works without being signed in.
            Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/auth/verify-email", new { token = TokenIn(mail) })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/verify-email", new { token = resent })).StatusCode);
            Assert.True((await api.Get("/api/me"))["emailVerified"]!.GetValue<bool>());
            await api.Post("/api/me/verify-email/resend", new { }, HttpStatusCode.BadRequest);
        }
        finally
        {
            _fx.Clock.UtcNow = start;
        }
    }

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentOne_AndSignsOutOtherDevices()
    {
        var api = await _fx.NewUserAsync();
        var laptop = Client(await TokenOf(await Login(api.Email, ApiFixture.Password)));

        Assert.Equal(WrongCurrent, Error(await api.Post("/api/me/change-password", new { currentPassword = "Wrong-Guess-77", newPassword = "Mango-Season-88" }, HttpStatusCode.BadRequest)));
        Assert.Contains("different", Error(await api.Post("/api/me/change-password", new { currentPassword = ApiFixture.Password, newPassword = ApiFixture.Password }, HttpStatusCode.BadRequest)));
        Assert.Equal(TooCommon, Error(await api.Post("/api/me/change-password", new { currentPassword = ApiFixture.Password, newPassword = "qwerty123" }, HttpStatusCode.BadRequest)));
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/me")).StatusCode);

        var changed = await api.Post("/api/me/change-password", new { currentPassword = ApiFixture.Password, newPassword = "Mango-Season-88" });
        var fresh = changed!["token"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(fresh).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, "Mango-Season-88")).StatusCode);
        await _fx.Emails.WaitForAsync(api.Email, "password was changed");
    }

    [Fact]
    public async Task ChangeEmail_RefusesARegisteredAddress_AndAsksToConfirmTheNewOne()
    {
        var api = await _fx.NewUserAsync();
        var other = await _fx.NewUserAsync();

        Assert.Equal("That email is already registered.",
            Error(await api.Post("/api/me/change-email", new { newEmail = other.Email.ToUpperInvariant(), currentPassword = ApiFixture.Password }, HttpStatusCode.Conflict)));
        Assert.Equal(WrongCurrent, Error(await api.Post("/api/me/change-email", new { newEmail = NewEmail(), currentPassword = "Not-Mine-At-All" }, HttpStatusCode.BadRequest)));
        Assert.Equal("Enter a valid email address, like name@example.com.",
            Error(await api.Post("/api/me/change-email", new { newEmail = "two@@example.com", currentPassword = ApiFixture.Password }, HttpStatusCode.BadRequest)));

        var newEmail = NewEmail();
        var changed = await api.Post("/api/me/change-email", new { newEmail, currentPassword = ApiFixture.Password });
        Assert.Equal(newEmail, changed!["email"]!.GetValue<string>());
        var me = await Client(changed["token"]!.GetValue<string>()).GetFromJsonAsync<JsonObject>("/api/me");
        Assert.Equal(newEmail, me!["email"]!.GetValue<string>());
        Assert.False(me["emailVerified"]!.GetValue<bool>());

        var confirm = await _fx.Emails.WaitForAsync(newEmail, "Confirm your email");
        var notice = await _fx.Emails.WaitForAsync(api.Email, "email was changed");
        Assert.Contains(newEmail, notice.Text);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(api.Email, ApiFixture.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(newEmail, ApiFixture.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/verify-email", new { token = TokenIn(confirm) })).StatusCode);
    }

    [Fact]
    public async Task SignOutEverywhere_EndsEverySessionAndLiveConnection_ButKeepsThisDevice()
    {
        var api = await _fx.NewUserAsync();
        // Tokens from before session versions existed carry no "sv" claim; they keep working until a reset.
        var legacy = Client(LegacyToken(api.Me["id"]!.GetValue<int>(), api.Email));
        Assert.Equal(HttpStatusCode.OK, (await legacy.GetAsync("/api/me")).StatusCode);
        var phone = Client(await TokenOf(await Login(api.Email, ApiFixture.Password)));
        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsync("/hubs/ledger/negotiate?negotiateVersion=1", null)).StatusCode);

        var fresh = Client((await api.Post("/api/me/sign-out-everywhere", new { }))!["token"]!.GetValue<string>());

        foreach (var device in new[] { legacy, phone, api.Http })
            Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.PostAsync("/hubs/ledger/negotiate?negotiateVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsync("/hubs/ledger/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Fact]
    public async Task TenWrongPasswords_LockTheAccount_UntilItExpiresOrThePasswordIsReset()
    {
        var api = await _fx.NewUserAsync();
        // A successful sign-in starts the count again.
        for (var i = 0; i < 9; i++) await Login(api.Email, $"Wrong-Guess-{i}");
        Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, ApiFixture.Password)).StatusCode);

        for (var i = 0; i < 9; i++)
            Assert.Equal("Invalid email or password.", await ErrorOf(await Login(api.Email, $"Wrong-Guess-{i}"), HttpStatusCode.Unauthorized));
        Assert.Equal(LockedOut, await ErrorOf(await Login(api.Email, "Wrong-Guess-9"), HttpStatusCode.TooManyRequests));
        // While locked even the right password is refused.
        Assert.Equal(LockedOut, await ErrorOf(await Login(api.Email, ApiFixture.Password), HttpStatusCode.TooManyRequests));

        var start = _fx.Clock.UtcNow;
        try
        {
            _fx.Clock.UtcNow = start.AddMinutes(14);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(api.Email, ApiFixture.Password)).StatusCode);
            _fx.Clock.UtcNow = start.AddMinutes(16);
            Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, ApiFixture.Password)).StatusCode);
        }
        finally
        {
            _fx.Clock.UtcNow = start;
        }

        // Locked again; resetting the password unlocks it straight away.
        for (var i = 0; i < 10; i++) await Login(api.Email, $"Wrong-Guess-{i}");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(api.Email, ApiFixture.Password)).StatusCode);
        await Post("/api/auth/forgot-password", new { email = api.Email });
        var token = TokenIn(await _fx.Emails.WaitForAsync(api.Email, "Reset your"));
        await TokenOf(await Post("/api/auth/reset-password", new { token, newPassword = "Unlocked-Again-55" }));
        Assert.Equal(HttpStatusCode.OK, (await Login(api.Email, "Unlocked-Again-55")).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_NeedsThePassword_AndRemovesEverything()
    {
        var api = await _fx.NewUserAsync();
        var userId = api.Me["id"]!.GetValue<int>();
        // Something of every kind, including rows other rows point at (an expense with a refund, a salary payment).
        var food = await api.Expense("Food", 2_000m, "2026-11-03");
        await api.Post($"/api/expenses/{food["id"]}/refunds", new { clientRequestId = Guid.NewGuid(), date = "2026-11-04", amount = 500m });
        await api.Put("/api/income/plans", new { effectiveFrom = "2026-10", baseSalary = 55_000m, allowances = 0m });
        await api.Post("/api/income/statements/2026-10/payments", new { clientRequestId = Guid.NewGuid(), receivedDate = "2026-11-01", amount = 55_000m });
        await api.Post("/api/income/receipts", new { kind = "Other", categoryId = api.Categories["Other income"], status = "Received", receivedDate = "2026-11-02", amount = 1_000m });
        await api.Post("/api/recurring", new { kind = "Expense", description = "Internet", categoryId = api.Categories["Utilities"], amount = 3_000m, dayOfMonth = 20, start = "2026-11" });
        await api.Put($"/api/budgets/2026-11/{api.Categories["Food"]}", new { amount = 8_000m, scope = "future" });
        await api.Post("/api/columns", new { name = "Tip", type = "money" });
        await api.Put("/api/expenses/notes", new { date = "2026-11-03", text = "chai" });

        Assert.Equal(WrongCurrent, Error(await api.Send(HttpMethod.Delete, "/api/me", new { currentPassword = "Not-Mine-At-All" }, HttpStatusCode.BadRequest)));
        await api.Send(HttpMethod.Delete, "/api/me", new { currentPassword = ApiFixture.Password }, HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(api.Email, ApiFixture.Password)).StatusCode);
        await WithDb(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
            Assert.False(await db.Accounts.AnyAsync(a => a.UserId == userId));
            Assert.False(await db.Categories.AnyAsync(c => c.UserId == userId));
            Assert.False(await db.Transactions.AnyAsync(t => t.UserId == userId));
            Assert.False(await db.IncomeReceipts.AnyAsync(r => r.UserId == userId));
            Assert.False(await db.SalaryPlans.AnyAsync(s => s.UserId == userId));
            Assert.False(await db.SalaryStatements.AnyAsync(s => s.UserId == userId));
            Assert.False(await db.RecurringItems.AnyAsync(r => r.UserId == userId));
            Assert.False(await db.RecurringOccurrences.AnyAsync(o => o.UserId == userId));
            Assert.False(await db.CustomColumns.AnyAsync(c => c.UserId == userId));
            Assert.False(await db.DayNotes.AnyAsync(d => d.UserId == userId));
            Assert.False(await db.AuthTokens.AnyAsync(t => t.UserId == userId));
        });
    }

    [Fact]
    public async Task EmailedLinks_UseTheConfiguredAddress_WhateverHostTheRequestNames()
    {
        var api = await _fx.NewUserAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password") { Content = JsonContent.Create(new { email = api.Email }) };
        request.Headers.Host = "evil.example";
        request.Headers.Add("X-Forwarded-Host", "evil.example");
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(request)).StatusCode);

        var mail = await _fx.Emails.WaitForAsync(api.Email, "Reset your");
        Assert.StartsWith($"{ApiFixture.PublicUrl}/reset-password?token=", LinkIn(mail));
        Assert.DoesNotContain("evil.example", mail.Html + mail.Text);
    }

    [Fact]
    public async Task Profile_ShowsMembershipAndStats_AndRenames()
    {
        var api = await _fx.NewUserAsync(displayName: "Sara");
        Assert.Equal("Sara", api.Me["displayName"]!.GetValue<string>());
        // Signed up "today" on the test clock, in Karachi.
        Assert.Equal("2026-11-15", api.Me["memberSince"]!.GetValue<string>());

        var empty = await api.Get("/api/me/stats");
        Assert.Equal(0, empty["entriesLogged"]!.GetValue<int>());
        Assert.Equal(0, empty["monthsTracked"]!.GetValue<int>());

        await api.Expense("Food", 500m, "2026-09-10");
        await api.Expense("Food", 700m, "2026-11-02");
        await api.Post("/api/income/receipts", new { kind = "Other", categoryId = api.Categories["Other income"], status = "Received", receivedDate = "2026-10-02", amount = 10_000m });
        var stats = await api.Get("/api/me/stats");
        Assert.Equal(3, stats["entriesLogged"]!.GetValue<int>());
        Assert.Equal(3, stats["monthsTracked"]!.GetValue<int>());
        Assert.Equal("2026-09", stats["firstMonth"]!.GetValue<string>());

        await api.Put("/api/me/profile", new { displayName = "  Sara Khan " });
        Assert.Equal("Sara Khan", (await api.Get("/api/me"))["displayName"]!.GetValue<string>());
        Assert.Equal("Keep your name to 100 characters or fewer.", Error(await api.Put("/api/me/profile", new { displayName = new string('x', 101) }, HttpStatusCode.BadRequest)));
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@example.com";

    private HttpClient Client(string? token = null)
    {
        var client = _fx.Factory.CreateClient();
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<HttpResponseMessage> Post(string url, object body) => Client().PostAsJsonAsync(url, body);

    private Task<HttpResponseMessage> Login(string email, string password) => Post("/api/auth/login", new { email, password });

    private async Task WithDb(Func<AppDbContext, Task> work)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<string> TokenOf(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)res.StatusCode} {text}");
        return JsonNode.Parse(text)!["token"]!.GetValue<string>();
    }

    private static async Task<string> ErrorOf(HttpResponseMessage res, HttpStatusCode expected)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"expected {(int)expected}, got {(int)res.StatusCode} {text}");
        return JsonNode.Parse(text)!["error"]!.GetValue<string>();
    }

    private static string Error(JsonNode? body) => body!["error"]!.GetValue<string>();

    // A token as the app issued them before session versions existed: no "sv" claim.
    private static string LegacyToken(int userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFixture.JwtKey));
        var token = new JwtSecurityToken(
            issuer: "ExpenseTracker.Api",
            audience: "ExpenseTracker.Client",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Email, email) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
