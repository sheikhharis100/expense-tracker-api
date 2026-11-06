using System.Net;
using System.Text;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

// The app's public address, for links in emails. Always from configuration (App:PublicUrl), never from the request:
// a forged Host header would otherwise put an attacker's site in someone's reset link.
public sealed class AppLinks
{
    public const string DevelopmentUrl = "http://localhost:5173";

    public string? BaseUrl { get; }

    public AppLinks(IConfiguration config, IHostEnvironment env, ILogger<AppLinks> logger)
    {
        var raw = config["App:PublicUrl"];
        if (string.IsNullOrWhiteSpace(raw) && env.IsDevelopment()) raw = DevelopmentUrl;
        if (Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            BaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        else
            logger.LogWarning("App:PublicUrl isn't set (e.g. https://your-app.onrender.com), so emails with links are turned off");
    }

    public string Build(string page, string code) => $"{BaseUrl}/{page}?token={Uri.EscapeDataString(code)}";
}

// Account emails: links for password reset, sign-in and email verification, and notices after security changes.
public class AccountEmails
{
    private readonly IEmailSender _sender;
    private readonly EmailOutbox _outbox;
    private readonly AppLinks _links;
    private readonly OneTimeTokens _tokens;

    public AccountEmails(IEmailSender sender, EmailOutbox outbox, AppLinks links, OneTimeTokens tokens)
    {
        _sender = sender;
        _outbox = outbox;
        _links = links;
        _tokens = tokens;
    }

    public bool Enabled => _sender.CanDeliver && _links.BaseUrl != null;

    // Each Send…Async returns false when nothing went out: email is off, or the same kind was sent under a minute ago.

    public Task<bool> SendPasswordResetAsync(User user) => SendLinkAsync(user, AuthTokenPurpose.Reset, user.Email, "reset-password", link => Compose(
        user.Email,
        "Reset your Expense Tracker password",
        "Reset your password",
        $"Hi {Name(user)}, someone (hopefully you) asked to reset the password for {user.Email}. Tap the button to choose a new one.",
        ("Choose a new password", link),
        "This link works once and expires in 30 minutes. Using it signs you out on your other devices.",
        "If you didn't ask for this, ignore this email. Your password won't change."));

    public Task<bool> SendSignInLinkAsync(User user) => SendLinkAsync(user, AuthTokenPurpose.Login, user.Email, "sign-in-link", link => Compose(
        user.Email,
        "Your Expense Tracker sign-in link",
        "Sign in to Expense Tracker",
        $"Hi {Name(user)}, tap the button to sign in as {user.Email}. No password needed.",
        ("Sign in", link),
        "This link works once and expires in 15 minutes.",
        "If you didn't ask for this, ignore this email. Nobody can sign in without the link."));

    public Task<bool> SendVerificationAsync(User user) => SendLinkAsync(user, AuthTokenPurpose.Verify, user.Email, "verify-email", link => Compose(
        user.Email,
        "Confirm your email for Expense Tracker",
        "Confirm your email",
        $"Hi {Name(user)}, tap the button to confirm that {user.Email} is yours. Then you can reset your password or get sign-in links by email.",
        ("Confirm email", link),
        "This link works once and expires in 24 hours.",
        "If you didn't create an Expense Tracker account, ignore this email."));

    // OWASP ASVS: tell people when their credentials change, so a takeover doesn't go unnoticed.
    public void SendPasswordChangedNotice(User user)
    {
        if (!Enabled) return;
        _outbox.Queue(Compose(
            user.Email,
            "Your Expense Tracker password was changed",
            "Your password was changed",
            $"Hi {Name(user)}, the password for {user.Email} was just changed, and your other devices were signed out.",
            null,
            "If this was you, there's nothing to do.",
            "If it wasn't, open the app, tap Forgot password? on the sign-in screen and choose a new password right away."));
    }

    public void SendEmailChangedNotice(User user, string oldEmail)
    {
        if (!Enabled) return;
        _outbox.Queue(Compose(
            oldEmail,
            "Your Expense Tracker email was changed",
            "Your sign-in email was changed",
            $"Hi {Name(user)}, the email for your Expense Tracker account was changed from {oldEmail} to {user.Email}. From now on, sign in with {user.Email}.",
            null,
            "If this was you, there's nothing to do.",
            "If it wasn't, reply to this email right away."));
    }

    private async Task<bool> SendLinkAsync(User user, string purpose, string to, string page, Func<string, EmailMessage> compose)
    {
        if (!Enabled) return false;
        var code = await _tokens.IssueAsync(user, purpose, to);
        if (code == null) return false;
        _outbox.Queue(compose(_links.Build(page, code)));
        return true;
    }

    private static string Name(User user) => string.IsNullOrWhiteSpace(user.DisplayName) ? "there" : user.DisplayName.Trim();

    // A plain-text body plus a simple HTML one with inline styles (what email apps reliably show): one button, the
    // link again as text for apps that hide buttons, then the small print.
    private static EmailMessage Compose(string to, string subject, string heading, string intro, (string Label, string Url)? button, params string[] notes)
    {
        string E(string s) => WebUtility.HtmlEncode(s);

        var text = new StringBuilder();
        text.AppendLine(heading).AppendLine().AppendLine(intro).AppendLine();
        if (button is var (label, url))
            text.AppendLine($"{label}: {url}").AppendLine();
        foreach (var note in notes) text.AppendLine(note);

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#f3f7f6;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f7f6;padding:24px 12px;\"><tr><td align=\"center\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:480px;background:#ffffff;border-radius:16px;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#0b1220;\"><tr><td style=\"padding:28px;\">");
        html.Append("<p style=\"margin:0 0 18px;font-size:15px;font-weight:700;color:#047857;\">Expense Tracker</p>");
        html.Append($"<h1 style=\"margin:0 0 12px;font-size:21px;line-height:1.3;\">{E(heading)}</h1>");
        html.Append($"<p style=\"margin:0 0 22px;font-size:15px;line-height:1.55;color:#344054;\">{E(intro)}</p>");
        if (button is var (buttonLabel, buttonUrl))
        {
            html.Append($"<p style=\"margin:0 0 22px;\"><a href=\"{E(buttonUrl)}\" style=\"display:inline-block;background:#059669;color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:12px 22px;border-radius:12px;\">{E(buttonLabel)}</a></p>");
            html.Append("<p style=\"margin:0 0 4px;font-size:13px;color:#475467;\">Button not working? Copy this link into your browser:</p>");
            html.Append($"<p style=\"margin:0 0 22px;font-size:13px;word-break:break-all;\"><a href=\"{E(buttonUrl)}\" style=\"color:#047857;\">{E(buttonUrl)}</a></p>");
        }
        foreach (var note in notes)
            html.Append($"<p style=\"margin:0 0 8px;font-size:13px;line-height:1.5;color:#475467;\">{E(note)}</p>");
        html.Append("</td></tr></table></td></tr></table></body></html>");

        return new EmailMessage(to, subject, html.ToString(), text.ToString());
    }
}
