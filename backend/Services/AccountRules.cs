using System.Net.Mail;

namespace ExpenseTracker.Api.Services;

// Shared checks and messages for sign-up and profile changes.
public static class AccountRules
{
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 100;

    // OWASP: 10 wrong passwords in a row lock password sign-in for 15 minutes; a reset or sign-in link unlocks it.
    public const int MaxFailedLogins = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const string InvalidCredentials = "Invalid email or password.";
    public const string LockedOut = "Too many failed attempts. Try again in 15 minutes or use Forgot password.";
    public const string EmailTaken = "That email is already registered.";
    public const string WrongPassword = "That password isn't right.";

    // Addresses are compared and stored trimmed and lower-case.
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static string? CheckEmail(string email)
    {
        if (email.Length == 0) return "Enter your email address.";
        if (email.Length > MaxEmailLength) return "That email address is too long.";
        return IsValidEmail(email) ? null : "Enter a valid email address, like name@example.com.";
    }

    public static string NormalizeName(string? name) => (name ?? string.Empty).Trim();

    public static string? CheckName(string name) =>
        name.Length > MaxNameLength ? "Keep your name to 100 characters or fewer." : null;

    private static bool IsValidEmail(string email)
    {
        if (email.Any(char.IsWhiteSpace) || email.Count(c => c == '@') != 1) return false;
        var at = email.IndexOf('@');
        var local = email[..at];
        var domain = email[(at + 1)..];
        if (local.Length is 0 or > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        if (!domain.Contains('.') || domain.Contains("..") || domain.StartsWith('.') || domain.EndsWith('.')
            || domain.StartsWith('-') || domain.EndsWith('-'))
            return false;
        // MailAddress would also accept "Name <a@b.c>"; only a bare address that round-trips is allowed.
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
    }
}
