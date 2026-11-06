using System.Net;
using ExpenseTracker.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Api.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Tr4ck-my-rupees")]
    [InlineData("correcthorsebatterystaple")]
    [InlineData("imran90517386")]
    [InlineData("Lahore-Chai-At-5")]
    [InlineData("پاسورڈ-محفوظ-ہے")]
    public void GoodPasswords_Pass(string password) => Assert.Null(PasswordPolicy.Check(password, "sana.malik@example.com"));

    [Theory]
    [InlineData("", PasswordPolicy.TooShort)]
    [InlineData("Ab1-xyz", PasswordPolicy.TooShort)]
    [InlineData("two words here", PasswordPolicy.HasSpaces)]
    [InlineData("no break space", PasswordPolicy.HasSpaces)]
    [InlineData("password", PasswordPolicy.TooCommon)]
    [InlineData("Password123", PasswordPolicy.TooCommon)]
    [InlineData("p4$$w0rd", PasswordPolicy.TooCommon)]
    [InlineData("Pakistan@2024", PasswordPolicy.TooCommon)]
    [InlineData("love12345678", PasswordPolicy.TooCommon)]
    [InlineData("zzzzzzzzzz", PasswordPolicy.TooCommon)]
    [InlineData("12121212", PasswordPolicy.TooCommon)]
    [InlineData("passpasspass", PasswordPolicy.TooCommon)]
    [InlineData("98765432", PasswordPolicy.TooCommon)]
    [InlineData("zxcvbnm,", PasswordPolicy.TooCommon)]
    [InlineData("asdfghjkl", PasswordPolicy.TooCommon)]
    [InlineData("MyExpenses!", PasswordPolicy.TooCommon)]
    [InlineData("sana.malik-2026", PasswordPolicy.HasEmailName)]
    public void WeakPasswords_GetAPlainReason(string password, string reason) => Assert.Equal(reason, PasswordPolicy.Check(password, "sana.malik@example.com"));

    [Fact]
    public void LengthLimits_CountCharactersAsPeopleSeeThem()
    {
        Assert.Null(PasswordPolicy.Check(new string('x', 60) + "-Kq7" + new string('y', 64), null));
        Assert.Equal(PasswordPolicy.TooLong, PasswordPolicy.Check(new string('x', 60) + "-Kq7" + new string('y', 65), null));
        // Seven emoji are 14 UTF-16 units but still too short.
        Assert.Equal(PasswordPolicy.TooShort, PasswordPolicy.Check("🔑🏠🚗🍵🌙🌟🎉", null));
    }

    [Fact]
    public void ShortEmailNames_AreNotChecked() => Assert.Null(PasswordPolicy.Check("Ali-Lahore-Garden", "ali@example.com"));

    [Theory]
    [InlineData("name@example.com", null)]
    [InlineData("first.last+tag@sub.example.pk", null)]
    [InlineData("", "Enter your email address.")]
    [InlineData("name@example", "Enter a valid email address, like name@example.com.")]
    [InlineData("two@@example.com", "Enter a valid email address, like name@example.com.")]
    [InlineData("na me@example.com", "Enter a valid email address, like name@example.com.")]
    [InlineData("name@exa..mple.com", "Enter a valid email address, like name@example.com.")]
    [InlineData("Name <name@example.com>", "Enter a valid email address, like name@example.com.")]
    public void Emails_AreChecked(string email, string? problem) => Assert.Equal(problem, AccountRules.CheckEmail(email));

    [Fact]
    public void LongEmails_AreRefused() =>
        Assert.Equal("That email address is too long.", AccountRules.CheckEmail(new string('a', 64) + "@" + new string('b', 186) + ".com"));
}

public class PwnedPasswordsCheckerTests
{
    // SHA-1("password") = 5BAA6 1E4C9B93F3F0682250B6CF8331B7EE68FD8.
    private const string Suffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    [Fact]
    public async Task SendsOnlyTheHashPrefix_WithPadding_AndMatchesTheSuffixLocally()
    {
        var handler = new StubHandler(_ => Ok($"0018A45C4D1DEF81644B54AB7F969B88D65:21\r\n{Suffix.ToLowerInvariant()}:3861493\r\nFFFF0000000000000000000000000000000:0"));
        var checker = Checker(handler);

        Assert.True(await checker.IsBreachedAsync("password"));
        var request = handler.Requests.Single();
        Assert.Equal("https://api.pwnedpasswords.com/range/5BAA6", request.RequestUri!.ToString());
        Assert.Equal("true", request.Headers.GetValues("Add-Padding").Single());
        Assert.DoesNotContain(Suffix, request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaddingLines_DoNotCountAsBreaches()
    {
        var checker = Checker(new StubHandler(_ => Ok($"{Suffix}:0\r\n0018A45C4D1DEF81644B54AB7F969B88D65:21")));
        Assert.False(await checker.IsBreachedAsync("password"));
    }

    [Fact]
    public async Task WhenTheServiceIsDownOrSlow_ThePasswordIsAccepted()
    {
        Assert.False(await Checker(new StubHandler(_ => throw new HttpRequestException("offline"))).IsBreachedAsync("password"));
        Assert.False(await Checker(new StubHandler(_ => throw new TaskCanceledException("timeout"))).IsBreachedAsync("password"));
        Assert.False(await Checker(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).IsBreachedAsync("password"));
    }

    private static PwnedPasswordsChecker Checker(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(PwnedPasswordsChecker.BaseAddress) }, NullLogger<PwnedPasswordsChecker>.Instance);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}

public class EmailSetupTests
{
    [Theory]
    [InlineData(null, null, typeof(LogEmailSender))]
    [InlineData(null, "mailpit", typeof(SmtpEmailSender))]
    [InlineData("xkeysib-test", null, typeof(BrevoEmailSender))]
    [InlineData("xkeysib-test", "mailpit", typeof(BrevoEmailSender))]
    public void TheSenderFollowsConfiguration_BrevoThenSmtpThenTheLog(string? brevoKey, string? smtpHost, Type expected)
    {
        using var provider = Build("Production", new() { ["Email:BrevoApiKey"] = brevoKey, ["Email:SmtpHost"] = smtpHost, ["Email:FromAddress"] = "app@example.com" });
        var sender = provider.GetRequiredService<IEmailSender>();
        Assert.IsType(expected, sender);
        // Brevo and SMTP turn the email features on; the log doesn't, outside Development.
        Assert.Equal(expected != typeof(LogEmailSender), sender.CanDeliver);
    }

    [Fact]
    public void WithNoEmailService_EmailFeaturesAreOffExceptInDevelopment()
    {
        using var production = Build("Production", new());
        Assert.False(production.GetRequiredService<IEmailSender>().CanDeliver);
        using var development = Build("Development", new());
        Assert.True(development.GetRequiredService<IEmailSender>().CanDeliver);
    }

    [Fact]
    public void Smtp_DefaultsToMailpitsPort_AndBrevoNeedsAVerifiedSender()
    {
        using var smtp = Build("Production", new() { ["Email:SmtpHost"] = "mailpit" });
        var options = smtp.GetRequiredService<IOptions<EmailOptions>>().Value;
        Assert.Equal(1025, options.SmtpPort);
        Assert.False(options.SmtpTls);
        Assert.Equal("Expense Tracker", options.FromName);

        using var brevo = Build("Production", new() { ["Email:BrevoApiKey"] = "xkeysib-test" });
        Assert.False(brevo.GetRequiredService<IEmailSender>().CanDeliver);
    }

    private static ServiceProvider Build(string environment, Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        services.AddEmailSender(config);
        return services.BuildServiceProvider();
    }
}
